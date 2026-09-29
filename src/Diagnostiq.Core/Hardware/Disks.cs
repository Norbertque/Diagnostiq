using Diagnostiq.Core.Probing;

namespace Diagnostiq.Core.Hardware;

/// <summary>Physical disk as Windows enumerates it. Health details come from StorageHealth.</summary>
public sealed record DiskInfo(int Number, string Model, long SizeBytes, string? InterfaceType, string? SerialNumber, string? PnpDeviceId)
{
    public double SizeGB => SizeBytes / 1e9; // drives are marketed in decimal GB
    public bool IsUsb => string.Equals(InterfaceType, "USB", StringComparison.OrdinalIgnoreCase)
                         || (PnpDeviceId?.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase) ?? false);
}

public static class DiskProbe
{
    public static IReadOnlyList<DiskInfo>? Read()
    {
        var disks = Wmi.Query("SELECT Index, Model, Size, InterfaceType, SerialNumber, PNPDeviceID FROM Win32_DiskDrive")
            .Select(d => new DiskInfo(
                Number: d.Int("Index") ?? -1,
                Model: d.Str("Model") ?? "Unknown disk",
                SizeBytes: d.Long("Size") ?? 0,
                InterfaceType: d.Str("InterfaceType"),
                SerialNumber: d.Str("SerialNumber"),
                PnpDeviceId: d.Str("PNPDeviceID")))
            .OrderBy(d => d.Number)
            .ToList();
        return disks.Count > 0 ? disks : null;
    }

    /// <summary>Number of the physical disk that holds the Windows partition, or null if unknown.</summary>
    public static int? SystemDiskNumber()
    {
        var sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        foreach (var part in Wmi.Query($"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{sysDrive}'}} WHERE AssocClass=Win32_LogicalDiskToPartition"))
        {
            if (part.Int("DiskIndex") is { } idx) return idx;
        }
        return null;
    }
}
