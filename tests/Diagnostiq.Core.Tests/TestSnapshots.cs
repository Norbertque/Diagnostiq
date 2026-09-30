using Diagnostiq.Core.Hardware;
using Diagnostiq.Core.Network;
using Diagnostiq.Core.Os;
using Diagnostiq.Core.Probing;
using Diagnostiq.Core.Sensors;
using Diagnostiq.Core.Storage;
using Diagnostiq.Core.Win11;

namespace Diagnostiq.Core.Tests;

/// <summary>Snapshots built from data instead of hardware: every probe "not available" unless given.</summary>
internal static class TestSnapshots
{
    private static ProbeResult<T> Na<T>() => ProbeResult<T>.NotAvailable();

    public static SystemSnapshot Create(ActivationInfo? activation = null, BatteryInfo? battery = null) => new()
    {
        Identity = Na<MachineIdentity>(),
        Firmware = Na<FirmwareInfo>(),
        Cpu = Na<CpuInfo>(),
        Memory = Na<MemoryInfo>(),
        Gpus = Na<IReadOnlyList<GpuInfo>>(),
        DirectX12 = Na<bool>(),
        Displays = Na<IReadOnlyList<DisplayPanel>>(),
        Disks = Na<IReadOnlyList<DiskInfo>>(),
        SystemDiskNumber = Na<int?>(),
        DriveHealth = Na<IReadOnlyList<DriveHealth>>(),
        Smart = Na<IReadOnlyList<SmartDrive>>(),
        Battery = battery is null ? Na<BatteryInfo>() : ProbeResult<BatteryInfo>.Ok(battery),
        BatteryLive = Na<BatteryLive>(),
        Tpm = Na<TpmInfo>(),
        Network = Na<IReadOnlyList<NetworkAdapterInfo>>(),
        Wifi = Na<WifiInfo>(),
        Bluetooth = Na<BluetoothInfo>(),
        Os = Na<OsInfo>(),
        Activation = activation is null ? Na<ActivationInfo>() : ProbeResult<ActivationInfo>.Ok(activation),
        BitLocker = Na<BitLockerInfo>(),
        DeviceProblems = Na<IReadOnlyList<DeviceProblem>>(),
        Antivirus = Na<IReadOnlyList<AntivirusInfo>>(),
        Sensors = Na<SensorService>(),
        Win11 = Win11Readiness.Evaluate(new Win11Inputs(null, null, null, null, null, true, true, false, null, null, null, false, null, 26100)),
        IsAdmin = false,
        Elapsed = TimeSpan.FromSeconds(3),
    };
}
