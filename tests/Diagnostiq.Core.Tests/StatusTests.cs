using Diagnostiq.Core.Network;
using Diagnostiq.Core.Os;
using Diagnostiq.Core.Storage;

namespace Diagnostiq.Core.Tests;

public class StatusTests
{
    private static DriveHealth Drive(DriveHealthStatus health = DriveHealthStatus.Healthy, ReliabilityCounters? counters = null, params string[] op) =>
        new(0, "CT500P5PSSD8", "SSD", "NVMe", 500_000_000_000, health, op.Length > 0 ? op : ["OK"], counters, false);

    private static ReliabilityCounters Counters(int? wear = 3, long uncorrected = 0, int? temp = 38) =>
        new(temp, wear, 4200, uncorrected, 0, 900);

    [Fact]
    public void Healthy_nvme_passes() => Assert.Equal(CheckState.Pass, StorageHealth.Evaluate(Drive(counters: Counters())).State);

    [Fact]
    public void Unhealthy_drive_fails() => Assert.Equal(CheckState.Fail, StorageHealth.Evaluate(Drive(DriveHealthStatus.Unhealthy)).State);

    [Fact]
    public void Predictive_failure_fails() =>
        Assert.Equal(CheckState.Fail, StorageHealth.Evaluate(Drive(op: "Predictive failure")).State);

    [Theory]
    [InlineData(85, CheckState.Warn)]
    [InlineData(100, CheckState.Fail)]
    public void Worn_ssd(int wear, CheckState expected) =>
        Assert.Equal(expected, StorageHealth.Evaluate(Drive(counters: Counters(wear: wear))).State);

    [Fact]
    public void Uncorrected_errors_warn()
    {
        var v = StorageHealth.Evaluate(Drive(counters: Counters(uncorrected: 3)));
        Assert.Equal(CheckState.Warn, v.State);
        Assert.Contains(v.Reasons, r => r.Contains("uncorrected"));
    }

    [Fact]
    public void Silent_drive_is_unknown() =>
        Assert.Equal(CheckState.Unknown, StorageHealth.Evaluate(Drive(DriveHealthStatus.Unknown)).State);

    [Theory]
    [InlineData("Windows 10 Pro", 26100, "Windows 11 Pro")]
    [InlineData("Windows 10 Pro", 19045, "Windows 10 Pro")]
    public void Windows_11_name_fix(string registry, int build, string expected) =>
        Assert.Equal(expected, WindowsStatus.ProductName(registry, build));

    [Theory]
    [InlineData("[4.0] Professional OEM:DM", "Pro")]
    [InlineData("[4.0] Core OEM:DM", "Home")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Firmware_key_edition(string? description, string? expected) =>
        Assert.Equal(expected, WindowsStatus.FirmwareKeyEdition(description));

    [Theory]
    [InlineData(1, LicenseState.Licensed)]
    [InlineData(0, LicenseState.Unlicensed)]
    [InlineData(5, LicenseState.Notification)]
    [InlineData(3, LicenseState.Grace)]
    public void License_states(int status, LicenseState expected) => Assert.Equal(expected, WindowsStatus.LicenseStateFrom(status));

    [Theory]
    [InlineData(0x61100, true, true)]    // Defender on, up to date
    [InlineData(0x60100, false, true)]   // off (third-party AV took over)
    [InlineData(0x61110, true, false)]   // on, signatures out of date
    public void Antivirus_product_state(int state, bool enabled, bool upToDate)
    {
        var a = WindowsStatus.Decode("Microsoft Defender Antivirus", state);
        Assert.Equal(enabled, a.Enabled);
        Assert.Equal(upToDate, a.UpToDate);
    }

    [Theory]
    [InlineData(2.437f, "2.4 GHz")]
    [InlineData(5.18f, "5 GHz")]
    [InlineData(5.955f, "6 GHz")]
    public void Wifi_bands(float ghz, string expected) => Assert.Equal(expected, WifiProbe.BandName(ghz));

    [Fact]
    public void Wifi_standard_names() => Assert.Equal("Wi-Fi 6 (802.11ax)", WifiProbe.StandardName(ManagedNativeWifi.PhyType.He));
}
