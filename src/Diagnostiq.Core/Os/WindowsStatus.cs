using Diagnostiq.Core.Probing;
using Microsoft.Win32;

namespace Diagnostiq.Core.Os;

/// <param name="Name">"Windows 11 Pro" (the registry still says "Windows 10" on Windows 11).</param>
/// <param name="InstallDate">When this Windows installation was set up; a fresh install on a used laptop shows here.</param>
public sealed record OsInfo(string Name, string? DisplayVersion, int Build, int Revision, string? Edition, DateTime? InstallDate)
{
    public bool IsWindows11 => Build >= Win11.Win11Readiness.FirstWindows11Build;
    public string BuildString => $"{Build}.{Revision}";
}

public enum LicenseState { Licensed, Unlicensed, Grace, Notification, Unknown }

/// <param name="Channel">"OEM", "Retail" or "Volume".</param>
/// <param name="FirmwareKeyEdition">Edition of the product key embedded in the firmware (OEM machines), e.g. "Professional".</param>
public sealed record ActivationInfo(LicenseState State, string? Channel, string? FirmwareKeyEdition)
{
    public bool IsActivated => State == LicenseState.Licensed;
    public bool HasFirmwareKey => FirmwareKeyEdition is not null;
}

public sealed record BitLockerInfo(string Drive, bool IsProtected, string Conversion);

public sealed record DeviceProblem(string Name, string? DeviceClass, int Code, string Meaning);

public sealed record AntivirusInfo(string Name, bool Enabled, bool UpToDate);

public static class WindowsStatus
{
    private const string WindowsAppId = "55c92734-d682-4d71-983e-d6ec3f16059f";

    public static OsInfo ReadOs()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")
            ?? throw new InvalidOperationException("Windows version key missing");
        int build = int.TryParse(k.GetValue("CurrentBuildNumber") as string ?? k.GetValue("CurrentBuild") as string, out var b) ? b : Environment.OSVersion.Version.Build;
        int ubr = k.GetValue("UBR") is int u ? u : 0;
        DateTime? installed = k.GetValue("InstallDate") is int secs && secs > 0 ? DateTimeOffset.FromUnixTimeSeconds((uint)secs).LocalDateTime : null;
        return new OsInfo(
            Name: ProductName(k.GetValue("ProductName") as string ?? "Windows", build),
            DisplayVersion: k.GetValue("DisplayVersion") as string ?? k.GetValue("ReleaseId") as string,
            Build: build, Revision: ubr,
            Edition: k.GetValue("EditionID") as string,
            InstallDate: installed);
    }

    internal static string ProductName(string registryName, int build) =>
        build >= Win11.Win11Readiness.FirstWindows11Build ? registryName.Replace("Windows 10", "Windows 11", StringComparison.Ordinal) : registryName;

    /// <summary>Slow (licensing service takes a few seconds). Never reads the product key itself.</summary>
    public static ActivationInfo ReadActivation()
    {
        var p = Wmi.First("SELECT LicenseStatus, ProductKeyChannel FROM SoftwareLicensingProduct " +
                          $"WHERE ApplicationID='{WindowsAppId}' AND PartialProductKey IS NOT NULL");
        string? firmware = null;
        try
        {
            // "[4.0] Professional OEM:DM" when the laptop carries a Windows key in its firmware.
            var desc = Wmi.First("SELECT OA3xOriginalProductKeyDescription FROM SoftwareLicensingService")?.Str("OA3xOriginalProductKeyDescription");
            firmware = FirmwareKeyEdition(desc);
        }
        catch (System.Management.ManagementException) { }

        if (p is null) return new ActivationInfo(LicenseState.Unlicensed, null, firmware);
        return new ActivationInfo(LicenseStateFrom(p.Int("LicenseStatus")), Channel(p.Str("ProductKeyChannel")), firmware);
    }

    internal static LicenseState LicenseStateFrom(int? status) => status switch
    {
        0 => LicenseState.Unlicensed,
        1 => LicenseState.Licensed,
        2 or 3 or 4 or 6 => LicenseState.Grace,
        5 => LicenseState.Notification,
        _ => LicenseState.Unknown,
    };

    internal static string? Channel(string? productKeyChannel) => productKeyChannel switch
    {
        null => null,
        var c when c.StartsWith("OEM", StringComparison.OrdinalIgnoreCase) => "OEM",
        var c when c.StartsWith("Retail", StringComparison.OrdinalIgnoreCase) => "Retail",
        var c when c.StartsWith("Volume", StringComparison.OrdinalIgnoreCase) => "Volume",
        var c => c,
    };

    /// <summary>"[4.0] Professional OEM:DM" → "Professional".</summary>
    internal static string? FirmwareKeyEdition(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var parts = description.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var edition = parts.FirstOrDefault(p => !p.StartsWith('[') && !p.Contains(':'));
        return edition ?? description.Trim();
    }

    /// <summary>Needs admin (the BitLocker WMI namespace refuses standard users).</summary>
    public static BitLockerInfo? ReadBitLocker()
    {
        var drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var v = Wmi.First($"SELECT DriveLetter, ProtectionStatus, ConversionStatus FROM Win32_EncryptableVolume WHERE DriveLetter='{drive}'",
            @"root\CIMV2\Security\MicrosoftVolumeEncryption");
        if (v is null) return null;
        string conversion = v.Int("ConversionStatus") switch
        {
            0 => "Not encrypted",
            1 => "Encrypted",
            2 => "Encrypting",
            3 => "Decrypting",
            4 => "Encryption paused",
            5 => "Decryption paused",
            _ => "Unknown",
        };
        return new BitLockerInfo(drive, v.Int("ProtectionStatus") == 1, conversion);
    }

    /// <summary>Devices Device Manager flags with a yellow triangle (ignores disabled and unplugged ones).</summary>
    public static IReadOnlyList<DeviceProblem> ReadDeviceProblems()
    {
        var list = new List<DeviceProblem>();
        foreach (var d in Wmi.Query("SELECT Name, PNPClass, ConfigManagerErrorCode, DeviceID, Present FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
        {
            int code = d.Int("ConfigManagerErrorCode") ?? 0;
            if (code is 0 or 22 or 45 || d.Bool("Present") == false) continue;   // 22 disabled by user, 45 not connected
            var name = d.Str("Name") ?? d.Str("DeviceID")?.Split('\\').FirstOrDefault() ?? "Unknown device";
            list.Add(new DeviceProblem(name, d.Str("PNPClass"), code, ProblemMeaning(code)));
        }
        return list;
    }

    internal static string ProblemMeaning(int code) => code switch
    {
        1 => "Not configured correctly",
        3 => "Driver may be corrupted",
        10 => "Can't start",
        12 => "Not enough free resources",
        14 => "Needs a restart",
        18 => "Drivers need reinstalling",
        19 or 40 => "Registry entry is damaged",
        24 => "Missing or not working",
        28 => "Driver not installed",
        29 => "Disabled in firmware",
        31 => "Not working properly",
        32 => "Driver disabled",
        37 => "Driver failed to start",
        39 => "Driver missing or corrupted",
        41 => "Hardware not found",
        43 => "Stopped after reporting a problem",
        48 => "Driver blocked as incompatible",
        52 => "Driver signature can't be verified",
        _ => $"Error code {code}",
    };

    /// <summary>Antivirus products registered with Windows Security Center (not available on Server).</summary>
    public static IReadOnlyList<AntivirusInfo> ReadAntivirus() =>
        Wmi.Query("SELECT displayName, productState FROM AntiVirusProduct", @"root\SecurityCenter2")
            .Select(a => Decode(a.Str("displayName") ?? "Unknown antivirus", a.Int("productState") ?? 0))
            .ToList();

    /// <summary>productState: bit 12 = real-time protection on, bit 4 = signatures out of date.</summary>
    internal static AntivirusInfo Decode(string name, int productState) =>
        new(name, Enabled: (productState & 0x1000) != 0, UpToDate: (productState & 0x10) == 0);
}
