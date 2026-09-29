using Diagnostiq.Core.Probing;

namespace Diagnostiq.Core.Hardware;

public sealed record TpmInfo(bool Present, string? SpecVersion, bool? Enabled, bool? Activated, string? Manufacturer)
{
    /// <summary>Highest TPM spec the chip supports ("2.0, 0, 1.38" → 2.0).</summary>
    public double? MajorVersion =>
        SpecVersion?.Split(',')[0].Trim() is { } v &&
        double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}

public static class TpmProbe
{
    /// <summary>
    /// No Win32_Tpm instance means Windows sees no TPM at all. On many laptops that's a
    /// firmware TPM (Intel PTT / AMD fTPM) switched off in BIOS, not missing hardware.
    /// Needs admin; without it <see cref="Probe"/> reports NeedsAdmin.
    /// </summary>
    public static TpmInfo Read()
    {
        var tpm = Wmi.First("SELECT SpecVersion, IsEnabled_InitialValue, IsActivated_InitialValue, ManufacturerIdTxt FROM Win32_Tpm", Wmi.Tpm);
        if (tpm is null) return new TpmInfo(false, null, null, null, null);
        return new TpmInfo(true, tpm.Str("SpecVersion"), tpm.Bool("IsEnabled_InitialValue"),
            tpm.Bool("IsActivated_InitialValue"), tpm.Str("ManufacturerIdTxt"));
    }
}
