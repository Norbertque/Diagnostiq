using Diagnostiq.Core.Storage;
using Diagnostiq.Core.Stress;

namespace Diagnostiq.Core.Tests;

public class StressAndStorageTests
{
    [Fact]
    public async Task Disk_benchmark_measures_and_cleans_up()
    {
        var dir = Directory.CreateTempSubdirectory("diag-bench").FullName;
        try
        {
            var r = await DiskBenchmark.RunAsync(dir, sizeMB: 32);
            Assert.True(r.SeqWriteMBps > 0);
            Assert.True(r.SeqReadMBps > 0);
            Assert.True(r.RandomRead4kIops > 0);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Disk_benchmark_honours_cancellation_and_cleans_up()
    {
        var dir = Directory.CreateTempSubdirectory("diag-bench").FullName;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DiskBenchmark.RunAsync(dir, 512, ct: cts.Token));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Memory_test_finds_no_errors_in_good_ram()
    {
        var r = await MemoryPatternTest.RunAsync(TimeSpan.FromMilliseconds(400), maxBytes: 128L << 20);
        Assert.Equal(0, r.Errors);
        Assert.True(r.Passes >= 1);
        Assert.True(r.TestedBytes >= 64L << 20);
    }

    [Fact]
    public void Memory_test_keeps_ram_free_for_windows()
    {
        long avail = (long)Interop.Native.AvailablePhysicalMemory();
        Assert.True(MemoryPatternTest.SafeTestBytes() <= Math.Max(avail - (1536L << 20), 64L << 20));
    }

    [Fact]
    public async Task Surface_scan_without_admin_reports_access_denied()
    {
        if (Probing.Probe.IsAdmin) return; // elevated runs would really scan; covered on test machines
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => SurfaceScan.RunAsync(0));
    }

    [Fact]
    public void Cpu_stress_starts_and_stops()
    {
        using var stress = new CpuStress();
        stress.Start(2);
        Assert.True(stress.IsRunning);
        Thread.Sleep(200);
        stress.Stop();
        Assert.False(stress.IsRunning);
    }
}
