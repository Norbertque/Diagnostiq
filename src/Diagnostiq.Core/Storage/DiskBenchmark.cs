using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Diagnostiq.Core.Storage;

public sealed record DiskBenchmarkResult(double SeqWriteMBps, double SeqReadMBps, double RandomRead4kIops, long TestBytes);

public sealed record BenchmarkProgress(string Phase, double Fraction);

/// <summary>
/// Sequential write/read and random 4K reads on a temp file, all unbuffered so the
/// Windows file cache can't inflate the numbers (v1 read its file straight from cache).
/// </summary>
public static class DiskBenchmark
{
    private const int Block = 1 << 20;   // 1 MiB sequential blocks
    private const int Small = 4096;      // random-read size and alignment
    private const FileOptions NoBuffering = (FileOptions)0x20000000;
    private static readonly TimeSpan RandomPhase = TimeSpan.FromSeconds(3);

    public static Task<DiskBenchmarkResult> RunAsync(string directory, int sizeMB = 1024,
        IProgress<BenchmarkProgress>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Run(directory, sizeMB, progress, ct), ct);

    private static unsafe DiskBenchmarkResult Run(string directory, int sizeMB, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
    {
        // Never fill the disk: use at most a quarter of free space.
        long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace;
        int blocks = (int)Math.Clamp(Math.Min(sizeMB, free / 4 / Block), 16, sizeMB);
        long bytes = (long)blocks * Block;
        var path = Path.Combine(directory, $"diagnostiq-bench-{Guid.NewGuid():N}.tmp");

        byte* buffer = (byte*)NativeMemory.AlignedAlloc(Block, Small);
        try
        {
            Random.Shared.NextBytes(new Span<byte>(buffer, Block)); // incompressible data

            var sw = Stopwatch.StartNew();
            using (var h = File.OpenHandle(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       FileOptions.WriteThrough | NoBuffering, preallocationSize: bytes))
            {
                for (int i = 0; i < blocks; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    RandomAccess.Write(h, new ReadOnlySpan<byte>(buffer, Block), (long)i * Block);
                    if (i % 16 == 0) progress?.Report(new("Writing", (double)i / blocks / 3));
                }
            }
            double write = bytes / 1e6 / sw.Elapsed.TotalSeconds;

            double read, iops;
            using (var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.None, NoBuffering))
            {
                sw.Restart();
                for (int i = 0; i < blocks; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    RandomAccess.Read(h, new Span<byte>(buffer, Block), (long)i * Block);
                    if (i % 16 == 0) progress?.Report(new("Reading", 1.0 / 3 + (double)i / blocks / 3));
                }
                read = bytes / 1e6 / sw.Elapsed.TotalSeconds;

                long slots = bytes / Small, ops = 0;
                var rng = new Random(1);
                sw.Restart();
                while (sw.Elapsed < RandomPhase)
                {
                    ct.ThrowIfCancellationRequested();
                    RandomAccess.Read(h, new Span<byte>(buffer, Small), rng.NextInt64(slots) * Small);
                    if (++ops % 256 == 0) progress?.Report(new("Random reads", 2.0 / 3 + sw.Elapsed / RandomPhase / 3));
                }
                iops = ops / sw.Elapsed.TotalSeconds;
            }

            progress?.Report(new("Done", 1));
            return new DiskBenchmarkResult(write, read, iops, bytes);
        }
        finally
        {
            NativeMemory.AlignedFree(buffer);
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
