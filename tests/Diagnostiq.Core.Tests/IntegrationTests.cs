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

    [Fact]
    public async Task Phase3_probes_run_and_never_error()
    {
        var snap = await SnapshotBuilder.BuildAsync().WaitAsync(TimeSpan.FromSeconds(45));
        snap.Sensors.Value?.Dispose();

        var log = new System.Text.StringBuilder();
        void Show<T>(string name, ProbeResult<T> r) =>
            log.AppendLine($"{name}: {r.Status} {(r.Value is System.Collections.IEnumerable e and not string ? string.Join(" | ", e.Cast<object>()) : r.Value)} {r.Message}");
        Show("Firmware", snap.Firmware);
        Show("Tpm", snap.Tpm);
        Show("Gpus", snap.Gpus);
        Show("DirectX12", snap.DirectX12);
        Show("Displays", snap.Displays);
        Show("DriveHealth", snap.DriveHealth);
        Show("BatteryLive", snap.BatteryLive);
        Show("Wifi", snap.Wifi);
        Show("Os", snap.Os);
        Show("Activation", snap.Activation);
        Show("BitLocker", snap.BitLocker);
        Show("DeviceProblems", snap.DeviceProblems);
        Show("Antivirus", snap.Antivirus);
        log.AppendLine($"Device: {snap.Device}");
        log.AppendLine($"Win11: {snap.Win11.Verdict}, running 11: {snap.Win11.RunningWindows11}");
        foreach (var c in snap.Win11.Checks) log.AppendLine($"  {c.State,-7} {c.Title}: {c.Detail} {c.Hint}");
        log.AppendLine($"Elapsed: {snap.Elapsed.TotalSeconds:0.0} s");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "diagnostiq-phase3-probes.txt"), log.ToString());

        Assert.DoesNotContain(ProbeStatus.Error, new[]
        {
            snap.Firmware.Status, snap.Tpm.Status, snap.Gpus.Status, snap.DirectX12.Status, snap.Displays.Status,
            snap.DriveHealth.Status, snap.Wifi.Status, snap.Os.Status, snap.Activation.Status, snap.DeviceProblems.Status,
            snap.Antivirus.Status, snap.BatteryLive.Status,
        });
        Assert.NotEqual(Win11.Win11Verdict.NotSupported, snap.Win11.Verdict);   // the dev laptop is a supported Latitude 7430
    }

    // Progress<T> posts to the thread pool asynchronously; this reports inline.
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
