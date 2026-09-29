using Diagnostiq.Core.Hardware;
using Diagnostiq.Core.Network;
using Diagnostiq.Core.Probing;
using Diagnostiq.Core.Sensors;
using Diagnostiq.Core.Storage;

namespace Diagnostiq.Core;

/// <summary>Everything read about the machine at startup. Each part carries its own probe status.</summary>
public sealed class SystemSnapshot
{
    public required ProbeResult<MachineIdentity> Identity { get; init; }
    public required ProbeResult<CpuInfo> Cpu { get; init; }
    public required ProbeResult<MemoryInfo> Memory { get; init; }
    public required ProbeResult<IReadOnlyList<DiskInfo>> Disks { get; init; }
    public required ProbeResult<int?> SystemDiskNumber { get; init; }
    public required ProbeResult<IReadOnlyList<SmartDrive>> Smart { get; init; }
    public required ProbeResult<BatteryInfo> Battery { get; init; }
    public required ProbeResult<TpmInfo> Tpm { get; init; }
    public required ProbeResult<IReadOnlyList<NetworkAdapterInfo>> Network { get; init; }
    public required ProbeResult<BluetoothInfo> Bluetooth { get; init; }
    public required ProbeResult<SensorService> Sensors { get; init; }
    public required bool IsAdmin { get; init; }
    public required TimeSpan Elapsed { get; init; }
}

/// <summary>One line on the loading screen.</summary>
public sealed record ProbeStep(string Id, string Label, ProbeStatus? Status);

public static class SnapshotBuilder
{
    // Loading-screen steps, in display order. Labels are user-facing.
    public static readonly IReadOnlyList<(string Id, string Label)> Steps =
    [
        ("identity", "Reading BIOS and firmware"),
        ("cpu", "Detecting processor and memory"),
        ("storage", "Scanning storage"),
        ("battery", "Checking battery"),
        ("tpm", "Checking TPM security chip"),
        ("network", "Finding network adapters"),
        ("sensors", "Starting temperature sensors"),
    ];

    /// <summary>
    /// Runs every probe in parallel; each reports on <paramref name="progress"/> as it finishes,
    /// so the loading screen ticks steps off in whatever order the hardware answers.
    /// </summary>
    public static async Task<SystemSnapshot> BuildAsync(IProgress<ProbeStep>? progress = null, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (id, label) in Steps) progress?.Report(new ProbeStep(id, label, null));

        Task<ProbeResult<T>> Run<T>(Func<T?> query, TimeSpan? timeout = null) => Probe.RunAsync(query, timeout, ct);

        // Report a step once all of its probes finished; the status shown is the worst of them.
        void When(string id, params Task<ProbeStatus>[] probes) =>
            Task.WhenAll(probes).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                    progress?.Report(new ProbeStep(id, Steps.First(s => s.Id == id).Label, t.Result.Max()));
            }, TaskScheduler.Default);
        static Task<ProbeStatus> S<T>(Task<ProbeResult<T>> probe) =>
            probe.ContinueWith(p => p.IsCompletedSuccessfully ? p.Result.Status : ProbeStatus.Error, TaskScheduler.Default);

        var identity = Run(IdentityProbe.Read);
        var cpu = Run(ProcessorProbe.ReadCpu);
        var memory = Run(ProcessorProbe.ReadMemory);
        var disks = Run(DiskProbe.Read);
        var sysDisk = Run(DiskProbe.SystemDiskNumber);
        var smart = Run(AtaSmart.Read, TimeSpan.FromSeconds(12));
        var battery = Run(BatteryProbe.Read);
        var tpm = Run(TpmProbe.Read);
        var network = Run(NetworkProbe.ReadAdapters);
        var bluetooth = Run(NetworkProbe.ReadBluetooth);
        var sensors = Run(() => { var s = new SensorService(); s.Open(); return s; }, TimeSpan.FromSeconds(20));

        When("identity", S(identity));
        When("cpu", S(cpu), S(memory));
        When("storage", S(disks), S(sysDisk));   // SMART is optional detail; it doesn't hold the step
        When("battery", S(battery));
        When("tpm", S(tpm));
        When("network", S(network), S(bluetooth));
        When("sensors", S(sensors));

        await Task.WhenAll(identity, cpu, memory, disks, sysDisk, smart, battery, tpm, network, bluetooth, sensors).ConfigureAwait(false);

        return new SystemSnapshot
        {
            Identity = identity.Result,
            Cpu = cpu.Result,
            Memory = memory.Result,
            Disks = disks.Result,
            SystemDiskNumber = sysDisk.Result,
            Smart = smart.Result,
            Battery = battery.Result,
            Tpm = tpm.Result,
            Network = network.Result,
            Bluetooth = bluetooth.Result,
            Sensors = sensors.Result,
            IsAdmin = Probe.IsAdmin,
            Elapsed = sw.Elapsed,
        };
    }

}
