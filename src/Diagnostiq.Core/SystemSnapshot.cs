using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Diagnostiq.Core.Hardware;
using Diagnostiq.Core.Network;
using Diagnostiq.Core.Os;
using Diagnostiq.Core.Probing;
using Diagnostiq.Core.Sensors;
using Diagnostiq.Core.Storage;
using Diagnostiq.Core.Win11;

namespace Diagnostiq.Core;

/// <summary>Everything read about the machine at startup. Each part carries its own probe status.</summary>
public sealed class SystemSnapshot
{
    public required ProbeResult<MachineIdentity> Identity { get; init; }
    public required ProbeResult<FirmwareInfo> Firmware { get; init; }
    public required ProbeResult<CpuInfo> Cpu { get; init; }
    public required ProbeResult<MemoryInfo> Memory { get; init; }
    public required ProbeResult<IReadOnlyList<GpuInfo>> Gpus { get; init; }
    public required ProbeResult<bool> DirectX12 { get; init; }
    public required ProbeResult<IReadOnlyList<DisplayPanel>> Displays { get; init; }
    public required ProbeResult<IReadOnlyList<DiskInfo>> Disks { get; init; }
    public required ProbeResult<int?> SystemDiskNumber { get; init; }
    public required ProbeResult<IReadOnlyList<DriveHealth>> DriveHealth { get; init; }
    public required ProbeResult<IReadOnlyList<SmartDrive>> Smart { get; init; }
    public required ProbeResult<BatteryInfo> Battery { get; init; }
    public required ProbeResult<BatteryLive> BatteryLive { get; init; }
    public required ProbeResult<TpmInfo> Tpm { get; init; }
    public required ProbeResult<IReadOnlyList<NetworkAdapterInfo>> Network { get; init; }
    public required ProbeResult<WifiInfo> Wifi { get; init; }
    public required ProbeResult<BluetoothInfo> Bluetooth { get; init; }
    public required ProbeResult<OsInfo> Os { get; init; }
    public required ProbeResult<ActivationInfo> Activation { get; init; }
    public required ProbeResult<BitLockerInfo> BitLocker { get; init; }
    public required ProbeResult<IReadOnlyList<DeviceProblem>> DeviceProblems { get; init; }
    public required ProbeResult<IReadOnlyList<AntivirusInfo>> Antivirus { get; init; }
    public required ProbeResult<SensorService> Sensors { get; init; }
    public required Win11Report Win11 { get; init; }
    public required bool IsAdmin { get; init; }
    public required TimeSpan Elapsed { get; init; }

    /// <summary>Vendor-normalized name, serial and BIOS hints.</summary>
    public DeviceIdentity? Device => Identity.Value is { } id ? VendorCatalog.Normalize(id) : null;

    public DiskInfo? SystemDisk => Disks.Value?.FirstOrDefault(d => d.Number == SystemDiskNumber.Value);

    /// <summary>The laptop's own screen when there is one, otherwise the first active monitor.</summary>
    public DisplayPanel? MainDisplay => Displays.Value?.FirstOrDefault();
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
        ("graphics", "Checking graphics and display"),
        ("storage", "Checking storage health"),
        ("battery", "Checking battery"),
        ("tpm", "Checking TPM security chip"),
        ("network", "Finding Wi-Fi and network adapters"),
        ("windows", "Checking Windows and security"),
        ("sensors", "Starting temperature sensors"),
        ("win11", "Checking Windows 11 requirements"),
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
        var firmware = Run(FirmwareProbe.Read);
        var cpu = Run(ProcessorProbe.ReadCpu);
        var memory = Run(ProcessorProbe.ReadMemory);
        var gpus = Run(GraphicsProbe.Read);
        var dx12 = Run(GraphicsProbe.SupportsDirectX12);
        var displays = Run(DisplayProbe.Read);
        var disks = Run(DiskProbe.Read);
        var sysDisk = Run(DiskProbe.SystemDiskNumber);
        var driveHealth = Run(StorageHealth.Read, TimeSpan.FromSeconds(12));
        var smart = Run(AtaSmart.Read, TimeSpan.FromSeconds(12));
        var battery = Run(BatteryProbe.Read);
        var batteryLive = Run(BatteryLiveProbe.Read);
        var tpm = Run(TpmProbe.Read);
        var network = Run(NetworkProbe.ReadAdapters);
        var wifi = Run(WifiProbe.Read);
        var bluetooth = Run(NetworkProbe.ReadBluetooth);
        var os = Run(WindowsStatus.ReadOs);
        var activation = Run(WindowsStatus.ReadActivation, TimeSpan.FromSeconds(20));   // licensing service is slow
        var bitLocker = Run(WindowsStatus.ReadBitLocker);
        var problems = Run(WindowsStatus.ReadDeviceProblems);
        var antivirus = Run(WindowsStatus.ReadAntivirus);
        var sensors = Run(() => { var s = new SensorService(); s.Open(); return s; }, TimeSpan.FromSeconds(20));

        When("identity", S(identity), S(firmware));
        When("cpu", S(cpu), S(memory));
        When("graphics", S(gpus), S(dx12), S(displays));
        When("storage", S(disks), S(sysDisk), S(driveHealth));   // SMART is optional detail; it doesn't hold the step
        When("battery", S(battery));
        When("tpm", S(tpm));
        When("network", S(network), S(wifi), S(bluetooth));
        When("windows", S(os), S(activation), S(problems), S(antivirus));   // BitLocker needs admin; not worth a warning icon
        When("sensors", S(sensors));

        await Task.WhenAll(identity, firmware, cpu, memory, gpus, dx12, displays, disks, sysDisk, tpm, os).ConfigureAwait(false);
        var win11 = Win11Readiness.Evaluate(new Win11Inputs(
            Firmware: firmware.Result.Value,
            Tpm: tpm.Result.Value,
            CpuName: cpu.Result.Value?.Name,
            CpuCores: cpu.Result.Value?.Cores,
            CpuMaxClockMHz: cpu.Result.Value?.MaxClockMHz,
            Sse42: Sse42.IsSupported,
            Popcnt: Popcnt.IsSupported,
            IsArm: RuntimeInformation.OSArchitecture == Architecture.Arm64,
            RamBytes: memory.Result.Value is { } m ? (m.InstalledBytes > 0 ? m.InstalledBytes : m.VisibleBytes) : null,
            SystemDiskBytes: disks.Result.Value?.FirstOrDefault(d => d.Number == sysDisk.Result.Value)?.SizeBytes,
            DirectX12: dx12.Result.IsOk ? dx12.Result.Value : null,
            BasicDisplayDriver: gpus.Result.Value?.Any(g => g.IsBasicDisplayDriver) ?? false,
            Panel: displays.Result.Value?.FirstOrDefault(),
            OsBuild: os.Result.Value?.Build ?? Environment.OSVersion.Version.Build,
            Hints: identity.Result.Value is { } idv ? VendorCatalog.Normalize(idv).Hints : null));
        progress?.Report(new ProbeStep("win11", Steps.First(s => s.Id == "win11").Label,
            win11.Verdict == Win11Verdict.Incomplete ? ProbeStatus.NeedsAdmin : ProbeStatus.Ok));

        await Task.WhenAll(driveHealth, smart, battery, batteryLive, network, wifi, bluetooth,
            activation, bitLocker, problems, antivirus, sensors).ConfigureAwait(false);

        return new SystemSnapshot
        {
            Identity = identity.Result,
            Firmware = firmware.Result,
            Cpu = cpu.Result,
            Memory = memory.Result,
            Gpus = gpus.Result,
            DirectX12 = dx12.Result,
            Displays = displays.Result,
            Disks = disks.Result,
            SystemDiskNumber = sysDisk.Result,
            DriveHealth = driveHealth.Result,
            Smart = smart.Result,
            Battery = battery.Result,
            BatteryLive = batteryLive.Result,
            Tpm = tpm.Result,
            Network = network.Result,
            Wifi = wifi.Result,
            Bluetooth = bluetooth.Result,
            Os = os.Result,
            Activation = activation.Result,
            BitLocker = bitLocker.Result,
            DeviceProblems = problems.Result,
            Antivirus = antivirus.Result,
            Sensors = sensors.Result,
            Win11 = win11,
            IsAdmin = Probe.IsAdmin,
            Elapsed = sw.Elapsed,
        };
    }
}
