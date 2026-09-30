using Diagnostiq.Core.Hardware;

namespace Diagnostiq.Core.Tests;

public class VendorTests
{
    private static DeviceIdentity N(string manufacturer, string model, string? version = null, string? serial = null) =>
        VendorCatalog.Normalize(new MachineIdentity(manufacturer, model, version, null, serial, null, null, null));

    [Fact]
    public void Dell_keeps_model_and_uses_service_tag()
    {
        var d = N("Dell Inc.", "Latitude 7430", serial: "52C2YT3");
        Assert.Equal("Dell", d.Vendor);
        Assert.Equal("Dell Latitude 7430", d.DisplayName);
        Assert.Equal("Service Tag", d.SerialLabel);
        Assert.Equal("52C2YT3", d.Serial);
        Assert.Equal("F2", d.Hints.SetupKey);
    }

    [Fact]
    public void Lenovo_uses_marketing_name_from_version_and_keeps_machine_type()
    {
        var d = N("LENOVO", "20XW0026GE", "ThinkPad X1 Carbon Gen 9");
        Assert.Equal("Lenovo", d.Vendor);
        Assert.Equal("ThinkPad X1 Carbon Gen 9", d.Model);
        Assert.Equal("20XW0026GE", d.ModelNumber);
        Assert.StartsWith("F1", d.Hints.SetupKey);
        Assert.Equal("Serial number", d.SerialLabel);
    }

    [Fact]
    public void Lenovo_consumer_models_get_novo_hint()
    {
        var d = N("LENOVO", "82KU", "Lenovo IdeaPad 5 14ALC05");
        Assert.Equal("IdeaPad 5 14ALC05", d.Model);
        Assert.Contains("Novo", d.Hints.SetupKey);
    }

    [Theory]
    [InlineData("HP", "HP EliteBook 840 G5 Notebook PC", "EliteBook 840 G5")]
    [InlineData("Hewlett-Packard", "HP ProBook 450 G8", "ProBook 450 G8")]
    public void Hp_strips_brand_and_suffix(string manufacturer, string model, string expected)
    {
        var d = N(manufacturer, model, "SBKPF");
        Assert.Equal("HP", d.Vendor);
        Assert.Equal(expected, d.Model);
    }

    [Theory]
    [InlineData("ASUS TUF Gaming F15 FX506HC_FX506HC", "TUF Gaming F15 FX506HC")]
    [InlineData("VivoBook_ASUSLaptop X512FA_X512FA", "VivoBook X512FA")]
    public void Asus_model_cleanup(string model, string expected)
    {
        var d = N("ASUSTeK COMPUTER INC.", model, "1.0");
        Assert.Equal("ASUS", d.Vendor);
        Assert.Equal(expected, d.Model);
    }

    [Fact]
    public void Msi_code_model_prefers_version_name()
    {
        var d = N("Micro-Star International Co., Ltd.", "MS-16R3", "GF63 Thin 9SC");
        Assert.Equal("MSI", d.Vendor);
        Assert.Equal("GF63 Thin 9SC", d.Model);
    }

    [Theory]
    [InlineData("To Be Filled By O.E.M.")]
    [InlineData("Default string")]
    [InlineData("00000000")]
    [InlineData("System Serial Number")]
    public void Placeholder_serials_are_dropped(string serial) => Assert.Null(N("Acer", "Aspire A515-54", serial: serial).Serial);

    [Fact]
    public void Hyper_v_is_a_virtual_machine() => Assert.True(N("Microsoft Corporation", "Virtual Machine").IsVirtualMachine);

    [Fact]
    public void Unknown_vendor_is_tidied() => Assert.Equal("Chuwi", VendorCatalog.NormalizeVendor("CHUWI Inc."));

    [Fact]
    public void Acer_notes_supervisor_password() => Assert.NotNull(N("Acer", "Aspire A515-54").Hints.Note);
}
