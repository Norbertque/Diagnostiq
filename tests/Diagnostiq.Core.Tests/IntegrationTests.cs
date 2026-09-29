using Diagnostiq.Core.Probing;
using Diagnostiq.Core.Sensors;

namespace Diagnostiq.Core.Tests;

/// <summary>Runs the real probes on the build machine: no crash, bounded time, every step reported.</summary>
[Trait("Category", "Integration")]
public class IntegrationTests
{
    [Fact]
    public async Task Snapshot_completes_and_reports_every_step()
    {
        var steps = new System.Collections.Concurrent.ConcurrentDictionary<string, ProbeStatus>();
        var progress = new SyncProgress<ProbeStep>(s => { if (s.Status is { } st) steps[s.Id] = st; });

        var snap = await SnapshotBuilder.BuildAsync(progress).WaitAsync(TimeSpan.FromSeconds(45));

        Assert.True(snap.Cpu.IsOk, snap.Cpu.Message);
        Assert.True(snap.Memory.IsOk, snap.Memory.Message);
        Assert.True(snap.Identity.IsOk, snap.Identity.Message);
        await Task.Delay(200); // let the last step reports land
        Assert.Equal(SnapshotBuilder.Steps.Select(s => s.Id).OrderBy(x => x), steps.Keys.OrderBy(x => x));
        snap.Sensors.Value?.Dispose();
    }

    [Fact]
    public void Sensors_report_cpu_load_without_admin()
    {
        using var sensors = new SensorService();
        sensors.Open();
        Thread.Sleep(1100);
        var reading = sensors.Read();
        Assert.NotNull(reading.CpuLoadPercent);
        Assert.NotNull(reading.CpuClockMHz);
    }

    // Progress<T> posts to the thread pool asynchronously; this reports inline.
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
