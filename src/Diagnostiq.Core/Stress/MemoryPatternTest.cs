using System.Diagnostics;
using Diagnostiq.Core.Interop;

namespace Diagnostiq.Core.Stress;

public sealed record MemoryTestResult(long TestedBytes, int Passes, long Errors, TimeSpan Duration, bool Cancelled);

public sealed record MemoryTestProgress(string Pattern, int Pass, long TestedBytes, long Errors, double Fraction);

/// <summary>
/// Fills as much free RAM as is safe with fixed and pseudo-random patterns and verifies
/// every byte, repeating until the time is up. Not a pre-boot memtest (Windows and the app
/// occupy some RAM), but it catches most faulty modules, especially under the heat of the
/// concurrent CPU stress.
/// </summary>
public static class MemoryPatternTest
{
    private const long BlockBytes = 64L << 20;               // 64 MiB blocks
    private const long KeepFreeBytes = 1536L << 20;          // leave 1.5 GB for Windows
    private static readonly byte[] Fixed = [0x00, 0xFF, 0xAA, 0x55];

    /// <summary>How much this machine can safely test right now.</summary>
    public static long SafeTestBytes(long? cap = null)
    {
        long avail = (long)Native.AvailablePhysicalMemory();
        long target = Math.Min((long)(avail * 0.7), avail - KeepFreeBytes);
        if (cap is { } c) target = Math.Min(target, c);
        return Math.Max(target, BlockBytes) / BlockBytes * BlockBytes;
    }

    public static Task<MemoryTestResult> RunAsync(TimeSpan duration, long? maxBytes = null,
        IProgress<MemoryTestProgress>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Run(duration, maxBytes, progress, ct), CancellationToken.None);

    private static MemoryTestResult Run(TimeSpan duration, long? maxBytes, IProgress<MemoryTestProgress>? progress, CancellationToken ct)
    {
        long want = SafeTestBytes(maxBytes);
        long blockBytes = Math.Min(BlockBytes, want);
        var blocks = new List<byte[]>();
        var sw = Stopwatch.StartNew();
        try
        {
            for (long got = 0; got < want && !ct.IsCancellationRequested; got += blockBytes)
            {
                try { blocks.Add(GC.AllocateUninitializedArray<byte>((int)blockBytes, pinned: true)); }
                catch (OutOfMemoryException) { break; } // take what we could get
            }
            long tested = blocks.Count * blockBytes;
            long errors = 0;
            int pass = 0;
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), CancellationToken = CancellationToken.None };

            while (!ct.IsCancellationRequested && sw.Elapsed < duration && blocks.Count > 0)
            {
                pass++;
                // Four fixed patterns, then a per-block pseudo-random pattern.
                for (int p = 0; p <= Fixed.Length && !ct.IsCancellationRequested && sw.Elapsed < duration; p++)
                {
                    string name = p < Fixed.Length ? $"0x{Fixed[p]:X2}" : "random";
                    long patternErrors = 0;
                    Parallel.For(0, blocks.Count, parallel, i =>
                    {
                        var span = blocks[i].AsSpan();
                        if (p < Fixed.Length) span.Fill(Fixed[p]); else FillRandom(span, (uint)(i * 2654435761u + (uint)pass));
                    });
                    Parallel.For(0, blocks.Count, parallel, i =>
                    {
                        long e = p < Fixed.Length ? CountMismatches(blocks[i], Fixed[p]) : VerifyRandom(blocks[i], (uint)(i * 2654435761u + (uint)pass));
                        if (e > 0) Interlocked.Add(ref patternErrors, e);
                    });
                    errors += patternErrors;
                    progress?.Report(new MemoryTestProgress(name, pass, tested, errors, Math.Min(sw.Elapsed / duration, 1)));
                }
            }
            return new MemoryTestResult(tested, pass, errors, sw.Elapsed, ct.IsCancellationRequested);
        }
        finally
        {
            blocks.Clear();
            GC.Collect();
        }
    }

    private static long CountMismatches(byte[] block, byte value)
    {
        var span = block.AsSpan();
        long errors = 0;
        int idx;
        while ((idx = span.IndexOfAnyExcept(value)) >= 0)   // vectorized scan
        {
            errors++;
            span = span[(idx + 1)..];
        }
        return errors;
    }

    // xorshift32: fast, reproducible per block, so verification regenerates instead of storing.
    private static void FillRandom(Span<byte> span, uint seed)
    {
        uint x = seed | 1;
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(span);
        for (int i = 0; i < words.Length; i++) { x ^= x << 13; x ^= x >> 17; x ^= x << 5; words[i] = x; }
    }

    private static long VerifyRandom(byte[] block, uint seed)
    {
        uint x = seed | 1;
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(block.AsSpan());
        long errors = 0;
        for (int i = 0; i < words.Length; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            if (words[i] != x) errors++;
        }
        return errors;
    }
}
