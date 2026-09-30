using System.Runtime.InteropServices;
using Diagnostiq.Core.Interop;
using Diagnostiq.Core.Probing;

namespace Diagnostiq.Core.Hardware;

/// <param name="Version">1 = TPM 1.2, 2 = TPM 2.0 (from TPM Base Services).</param>
/// <param name="SpecVersion">Win32_Tpm spec list ("2.0, 0, 1.38"); admin only.</param>
public sealed record TpmInfo(bool Present, int? Version, string? SpecVersion, bool? Enabled, bool? Activated, string? Manufacturer)
{
    /// <summary>Highest TPM spec the chip supports (2.0 or 1.2).</summary>
    public double? MajorVersion => Version switch
    {
        2 => 2.0,
        1 => 1.2,
        _ => SpecVersion?.Split(',')[0].Trim() is { } v &&
             double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null,
    };
}

public static class TpmProbe
{
    /// <summary>
    /// TPM Base Services answers "is there a TPM, which version" without admin. "Not found"
    /// often means a firmware TPM (Intel PTT / AMD fTPM) switched off in BIOS rather than
    /// missing hardware. Manufacturer and enabled/activated flags come from Win32_Tpm when elevated.
    /// </summary>
    public static TpmInfo Read()
    {
        uint rc = Native.Tbsi_GetDeviceInfo((uint)Marshal.SizeOf<Native.TpmDeviceInfo>(), out var info);
        if (rc == Native.TbsTpmNotFound) return new TpmInfo(false, null, null, null, null, null);
        int? version = rc == Native.TbsSuccess ? (int)info.TpmVersion : null;

        // TBS gave an answer: only ask WMI for details when it won't be refused.
        if (version is not null && !Probe.IsAdmin) return new TpmInfo(true, version, null, null, null, null);

        var tpm = Wmi.First("SELECT SpecVersion, IsEnabled_InitialValue, IsActivated_InitialValue, ManufacturerIdTxt FROM Win32_Tpm", Wmi.Tpm);
        if (tpm is null) return new TpmInfo(version is not null, version, null, null, null, null);
        return new TpmInfo(true, version, tpm.Str("SpecVersion"), tpm.Bool("IsEnabled_InitialValue"),
            tpm.Bool("IsActivated_InitialValue"), tpm.Str("ManufacturerIdTxt"));
    }
}
