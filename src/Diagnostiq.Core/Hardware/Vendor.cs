using System.Globalization;
using System.Text.RegularExpressions;

namespace Diagnostiq.Core.Hardware;

/// <summary>
/// Where things are in a vendor's firmware setup. Menu names differ between models and BIOS
/// versions, so the UI presents these as "usually".
/// </summary>
public sealed record VendorHints(string SetupKey, string BootMenuKey, string TpmLocation, string SecureBootLocation, string? Note = null);

/// <param name="Model">Marketing name without the vendor ("Latitude 7430", "ThinkPad T14 Gen 2").</param>
/// <param name="ModelNumber">Vendor's type/part number when it differs from the name (Lenovo machine type).</param>
public sealed record DeviceIdentity(
    string Vendor,
    string Model,
    string? ModelNumber,
    string? Serial,
    string SerialLabel,
    bool IsVirtualMachine,
    VendorHints Hints)
{
    public string DisplayName => Model.StartsWith(Vendor, StringComparison.OrdinalIgnoreCase) ? Model : $"{Vendor} {Model}";
}

public static partial class VendorCatalog
{
    private static readonly VendorHints Generic = new(
        SetupKey: "F2, F1, F10 or Del (the key is usually shown briefly at power-on)",
        BootMenuKey: "F12, F11, F9 or Esc",
        TpmLocation: "Security or Advanced: look for TPM, Intel PTT or AMD fTPM",
        SecureBootLocation: "Boot or Security → Secure Boot");

    // Manufacturer prefix (lower case) → canonical vendor name.
    private static readonly (string Prefix, string Vendor)[] Vendors =
    [
        ("dell", "Dell"), ("alienware", "Alienware"), ("lenovo", "Lenovo"),
        ("hewlett-packard", "HP"), ("hewlett packard", "HP"), ("hp", "HP"),
        ("asustek", "ASUS"), ("asus", "ASUS"), ("acer", "Acer"),
        ("micro-star", "MSI"), ("msi", "MSI"), ("microsoft", "Microsoft"),
        ("samsung", "Samsung"), ("toshiba", "Toshiba"), ("dynabook", "Dynabook"),
        ("fujitsu", "Fujitsu"), ("huawei", "Huawei"), ("honor", "Honor"), ("razer", "Razer"),
        ("gigabyte", "Gigabyte"), ("framework", "Framework"), ("panasonic", "Panasonic"),
        ("lg electronics", "LG"), ("medion", "Medion"), ("timi", "Xiaomi"), ("xiaomi", "Xiaomi"),
        ("apple", "Apple"), ("google", "Google"), ("getac", "Getac"), ("sony", "Sony"), ("vaio", "VAIO"),
        ("vmware", "VMware"), ("innotek", "VirtualBox"), ("qemu", "QEMU"), ("parallels", "Parallels"), ("xen", "Xen"),
    ];

    private static readonly Dictionary<string, VendorHints> Hints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dell"] = new("F2", "F12", "Security → TPM 2.0 Security → TPM On", "Secure Boot → Secure Boot Enable"),
        ["Alienware"] = new("F2", "F12", "Security → TPM 2.0 Security → TPM On", "Secure Boot → Secure Boot Enable"),
        ["HP"] = new("F10 (or Esc for the startup menu)", "F9", "Security → TPM Embedded Security", "Advanced → Secure Boot Configuration"),
        ["ASUS"] = new("F2", "Esc", "Advanced → Trusted Computing, or Intel PTT / AMD fTPM", "Security → Secure Boot, or Boot → Secure Boot"),
        ["Acer"] = new("F2", "F12 (enable \"F12 Boot Menu\" in Main first)", "Security or Advanced: Intel PTT / AMD fTPM", "Boot → Secure Boot",
            Note: "On many Acer models Secure Boot stays greyed out until a supervisor password is set."),
        ["MSI"] = new("Del", "F11", "Security → Trusted Computing", "Security → Secure Boot"),
        ["Microsoft"] = new("Hold Volume Up, then press Power", "Hold Volume Down, then press Power", "Security → TPM", "Security → Secure Boot"),
        ["Samsung"] = new("F2", "F10", "Advanced: TPM, Intel PTT or AMD fTPM", "Boot → Secure Boot Control"),
        ["Toshiba"] = new("F2", "F12", "Security → TPM", "Security → Secure Boot"),
        ["Dynabook"] = new("F2", "F12", "Security → TPM", "Security → Secure Boot"),
        ["Fujitsu"] = new("F2", "F12", "Security → TPM (Security Chip) Setting", "Security → Secure Boot Configuration"),
        ["Framework"] = new("F2", "F12", "Security → Intel PTT / AMD fTPM", "Security → Secure Boot"),
    };

    private static readonly VendorHints ThinkPad = new("F1 (or Enter, then F1)", "F12", "Security → Security Chip", "Security → Secure Boot");
    private static readonly VendorHints LenovoConsumer = new("F2 (Fn+F2), or the Novo button", "F12 (Fn+F12)",
        "Security or Configuration: Intel PTT / AMD fTPM", "Security → Secure Boot");

    public static DeviceIdentity Normalize(MachineIdentity id)
    {
        var vendor = NormalizeVendor(id.Manufacturer);
        var (model, number) = NormalizeModel(vendor, id.Model, id.ProductVersion);
        var serial = CleanValue(id.BiosSerial) is { } s && !IsPlaceholderSerial(s) ? s
                   : CleanValue(id.ProductSerial) is { } p && !IsPlaceholderSerial(p) ? p : null;

        VendorHints hints = vendor == "Lenovo"
            ? (model.StartsWith("ThinkPad", StringComparison.OrdinalIgnoreCase) ? ThinkPad : LenovoConsumer)
            : Hints.GetValueOrDefault(vendor, Generic);

        return new DeviceIdentity(vendor, model, number, serial,
            SerialLabel: vendor is "Dell" or "Alienware" ? "Service Tag" : "Serial number",
            IsVirtualMachine: IsVirtual(vendor, model),
            Hints: hints);
    }

    internal static string NormalizeVendor(string manufacturer)
    {
        var m = manufacturer.Trim();
        foreach (var (prefix, vendor) in Vendors)
            if (m.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (m.Length == prefix.Length || !char.IsLetter(m[prefix.Length])))
                return vendor;

        // Unknown brand: drop legal suffixes, title-case SHOUTING names.
        var name = LegalSuffix().Replace(m, "").Trim(' ', ',', '.');
        if (name.Length == 0 || IsPlaceholder(name)) return "Unknown";
        return name.Length > 3 && name == name.ToUpperInvariant()
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant())
            : name;
    }

    internal static (string Model, string? Number) NormalizeModel(string vendor, string rawModel, string? productVersion)
    {
        var model = CleanValue(rawModel);
        var version = CleanValue(productVersion);
        if (model is not null && IsPlaceholder(model)) model = null;
        if (version is not null && (IsPlaceholder(version) || !LooksLikeName(version))) version = null;

        // Lenovo (and some MSI/Samsung) put a type code in Model and the marketing name in Version.
        if (version is not null && (model is null || LooksLikeCode(model)))
            return (StripVendor(vendor, version), model);
        if (model is null) return ("Unknown model", null);

        model = StripVendor(vendor, model);
        if (vendor == "ASUS") model = CleanAsus(model);
        if (vendor == "HP") model = HpSuffix().Replace(model, "");
        return (model.Trim(), null);
    }

    /// <summary>"VivoBook_ASUSLaptop X512FA_X512FA" → "VivoBook X512FA".</summary>
    private static string CleanAsus(string model)
    {
        var tokens = model.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !t.Equals("ASUSLaptop", StringComparison.OrdinalIgnoreCase));
        return string.Join(' ', tokens.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string StripVendor(string vendor, string model)
    {
        var s = model.Trim();
        if (s.StartsWith(vendor + " ", StringComparison.OrdinalIgnoreCase)) s = s[(vendor.Length + 1)..];
        return s.Trim();
    }

    private static bool IsVirtual(string vendor, string model) =>
        vendor is "VMware" or "VirtualBox" or "QEMU" or "Parallels" or "Xen"
        || model.Contains("Virtual Machine", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("Standard PC (", StringComparison.OrdinalIgnoreCase)   // QEMU/KVM
        || model.Equals("KVM", StringComparison.OrdinalIgnoreCase);

    private static string? CleanValue(string? s) => string.IsNullOrWhiteSpace(s) ? null : MultiSpace().Replace(s.Trim(), " ");

    internal static bool IsPlaceholder(string s)
    {
        string[] junk = ["To Be Filled By O.E.M.", "To be filled by O.E.M.", "System Product Name", "System Version", "Default string",
                         "Not Applicable", "Not Specified", "None", "N/A", "INVALID", "O.E.M.", "OEM", "Type1ProductConfigId",
                         "System manufacturer", "x.x", "0"];
        return junk.Any(j => s.Equals(j, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPlaceholderSerial(string s) =>
        IsPlaceholder(s) || s.Equals("System Serial Number", StringComparison.OrdinalIgnoreCase)
        || s.Equals("Chassis Serial Number", StringComparison.OrdinalIgnoreCase)
        || s is "123456789" or "0123456789" or "1234567890"
        || s.Distinct().Count() == 1;   // "00000000", "11111111"

    /// <summary>"20XW0026GE", "MS-16R3", "950XDA": one token of capitals and digits.</summary>
    private static bool LooksLikeCode(string s) => ModelCode().IsMatch(s);

    /// <summary>Marketing names have a word of letters; "1.0", "REV:1.0" or "V1.20" don't.</summary>
    private static bool LooksLikeName(string s) => s.Contains(' ') && Word().IsMatch(s) && !s.StartsWith("REV", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\s+")] private static partial Regex MultiSpace();
    [GeneratedRegex(@"^(?=.*\d)[A-Z0-9]+(-[A-Z0-9]+)?$")] private static partial Regex ModelCode();
    [GeneratedRegex(@"[A-Za-z]{3,}")] private static partial Regex Word();
    [GeneratedRegex(@"\s+(Notebook PC|Laptop PC|Notebook)$", RegexOptions.IgnoreCase)] private static partial Regex HpSuffix();
    [GeneratedRegex(@"[,\s]+(Inc\.?|Incorporated|Corporation|Corp\.?|Co\.,?\s*Ltd\.?|Ltd\.?|Limited|GmbH|AG|S\.A\.|Computer|Technology|International)\b\.?", RegexOptions.IgnoreCase)]
    private static partial Regex LegalSuffix();
}
