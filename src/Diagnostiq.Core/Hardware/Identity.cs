using System.Management;
using Diagnostiq.Core.Probing;

namespace Diagnostiq.Core.Hardware;

/// <summary>Raw firmware/SMBIOS identity. Vendor-friendly naming is applied by IdentityService.</summary>
public sealed record MachineIdentity(
    string Manufacturer,
    string Model,
    string? ProductVersion,   // Lenovo puts the marketing name here ("ThinkPad T14 Gen 2")
    string? SystemSku,
    string? BiosSerial,
    string? ProductSerial,
    string? BiosVersion,
    DateTime? BiosDate);

public static class IdentityProbe
{
    public static MachineIdentity? Read()
    {
        var cs = Wmi.First("SELECT Manufacturer, Model, SystemSKUNumber FROM Win32_ComputerSystem");
        if (cs is null) return null;
        var product = Wmi.First("SELECT Version, IdentifyingNumber FROM Win32_ComputerSystemProduct");
        var bios = Wmi.First("SELECT SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS");

        DateTime? biosDate = null;
        if (bios?.Str("ReleaseDate") is { } raw)
        {
            try { biosDate = ManagementDateTimeConverter.ToDateTime(raw).Date; } catch (ArgumentOutOfRangeException) { }
        }

        return new MachineIdentity(
            Manufacturer: cs.Str("Manufacturer") ?? "Unknown",
            Model: cs.Str("Model") ?? "Unknown",
            ProductVersion: product?.Str("Version"),
            SystemSku: cs.Str("SystemSKUNumber"),
            BiosSerial: bios?.Str("SerialNumber"),
            ProductSerial: product?.Str("IdentifyingNumber"),
            BiosVersion: bios?.Str("SMBIOSBIOSVersion"),
            BiosDate: biosDate);
    }
}
