using System.Diagnostics;
using System.Runtime.InteropServices;
using Diagnostiq.Core.Interop;

namespace Diagnostiq.Core.Storage;

public sealed record SurfaceScanResult(int DiskNumber, long DiskBytes, int ChunksRead, int Errors,
    IReadOnlyList<long> ErrorOffsets, double AvgMBps, double MinMBps, TimeSpan Duration, bool Cancelled);

public sealed record ScanProgress(int ChunksRead, int Errors, double Fraction, double CurrentMBps);

/// <summary>
/// Read-only surface scan of a physical disk: 1 MiB reads spread over the whole drive.
/// The disk is split into equal zones and each pass reads one random spot per zone, so
/// even a short scan touches every region; later passes fill in more spots. Read errors
/// are bad sectors (or a failing controller). Needs admin.
/// </summary>
public static class SurfaceScan
{
    private const int Chunk = 1 << 20;
    private const int Zones = 512;

    /// <param name="duration">Time-boxed mode (stress run). Null = one pass over all zones.</param>
    public static Task<SurfaceScanResult> RunAsync(int diskNumber, TimeSpan? duration = null,
        IProgress<ScanProgress>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Run(diskNumber, duration, progress, ct), CancellationToken.None);

    private static unsafe SurfaceScanResult Run(int diskNumber, TimeSpan? duration, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        using var disk = Native.CreateFile($@"\\.\PhysicalDrive{diskNumber}", Native.GenericRead,
            Native.FileShareRead | Native.FileShareWrite, IntPtr.Zero, Native.OpenExisting, Native.FileFlagNoBuffering, IntPtr.Zero);
        if (disk.IsInvalid)
        {
            int err = Marshal.GetLastPInvokeError();
            if (err == 5) throw new UnauthorizedAccessException("Raw disk access needs admin.");
            throw new IOException($"Cannot open disk {diskNumber} (Win32 error {err}).");
        }
        if (!Native.DeviceIoControl(disk, Native.IoctlDiskGetLengthInfo, IntPtr.Zero, 0, out long size, sizeof(long), out _, IntPtr.Zero))
            throw new IOException($"Cannot read the size of disk {diskNumber}.");

        long zoneBytes = size / Zones / 4096 * 4096;
        long maxInZone = Math.Max(zoneBytes - Chunk, 0);
        byte* buffer = (byte*)NativeMemory.AlignedAlloc(Chunk, 4096);
        var errors = new List<long>();
        var rng = new Random();
        int chunks = 0;
        double minMBps = double.MaxValue;
        var sw = Stopwatch.StartNew();
        bool cancelled = false;
        try
        {
            for (int pass = 0; ; pass++)
            {
                for (int z = 0; z < Zones; z++)
                {
                    if (ct.IsCancellationRequested) { cancelled = true; goto done; }
                    if (duration is { } d && sw.Elapsed >= d) goto done;

                    long offset = z * zoneBytes + rng.NextInt64(maxInZone / 4096 + 1) * 4096;
                    var t0 = sw.Elapsed;
                    try
                    {
                        int n = RandomAccess.Read(disk, new Span<byte>(buffer, Chunk), offset);
                        if (n < Chunk) errors.Add(offset);
                    }
                    catch (IOException) { errors.Add(offset); }
                    chunks++;
                    double mbps = Chunk / 1e6 / Math.Max((sw.Elapsed - t0).TotalSeconds, 1e-6);
                    minMBps = Math.Min(minMBps, mbps);

                    if (chunks % 8 == 0)
                    {
                        double fraction = duration is { } dd ? sw.Elapsed / dd : (double)chunks / Zones;
                        progress?.Report(new ScanProgress(chunks, errors.Count, Math.Min(fraction, 1), mbps));
                    }
                }
                if (duration is null) break; // single pass
            }
        done:;
        }
        finally { NativeMemory.AlignedFree(buffer); }

        double avg = chunks * (double)Chunk / 1e6 / Math.Max(sw.Elapsed.TotalSeconds, 1e-6);
        return new SurfaceScanResult(diskNumber, size, chunks, errors.Count, errors, avg,
            chunks > 0 ? minMBps : 0, sw.Elapsed, cancelled);
    }
}
