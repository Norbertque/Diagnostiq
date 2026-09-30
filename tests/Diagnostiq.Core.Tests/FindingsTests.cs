using Diagnostiq.Core.Hardware;
using Diagnostiq.Core.Os;
using Diagnostiq.Core.Reporting;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.Core.Tests;

public class FindingsTests
{
    [Fact]
    public void Unchecked_activation_isnt_reported_as_not_activated()
    {
        // The licensing service didn't answer: no warning, and the report says it couldn't check.
        var s = TestSnapshots.Create(new ActivationInfo(LicenseState.Unknown, null, "Pro"));
        Assert.DoesNotContain(Findings.From(s), f => f.Area == "Windows");
        Assert.Equal("Couldn't check", ActivationRow(s).Value);
        Assert.Null(ActivationRow(s).Status);
    }

    [Fact]
    public void Unlicensed_windows_is_worth_a_look()
    {
        var s = TestSnapshots.Create(new ActivationInfo(LicenseState.Unlicensed, null, "Pro"));
        var finding = Assert.Single(Findings.From(s), f => f.Area == "Windows");
        Assert.StartsWith("Windows isn't activated. The BIOS holds a Windows Pro key", finding.Text);
        Assert.Equal(new ReportRow("Activation", "Not activated", "Warn"), ActivationRow(s));
    }

    [Fact]
    public void Activated_windows_shows_its_channel() =>
        Assert.Equal(new ReportRow("Activation", "Activated (OEM)", "Pass"),
            ActivationRow(TestSnapshots.Create(new ActivationInfo(LicenseState.Licensed, "OEM", null))));

    [Fact]
    public void Worn_battery_is_described_as_capacity()
    {
        var s = TestSnapshots.Create(battery: new BatteryInfo(true, 1, 80, true, 50_000, 17_500, 900));
        var finding = Assert.Single(Findings.From(s));
        Assert.Equal("Battery holds only 35% of its original capacity. Replace it.", finding.Text);
    }

    [Fact]
    public void Finding_reads_as_a_sentence_for_screen_readers() =>
        Assert.Equal("Storage: Running hot.", new Finding(CheckState.Warn, "Storage", "Running hot.").ToString());

    private static ReportRow ActivationRow(SystemSnapshot s) =>
        ReportModel.Build(s, new TestRun()).WindowsAndSecurity.SelectMany(x => x.Rows).Single(r => r.Label == "Activation");
}
