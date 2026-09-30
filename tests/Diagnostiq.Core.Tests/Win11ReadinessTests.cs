using Diagnostiq.Core.Hardware;
using Diagnostiq.Core.Win11;

namespace Diagnostiq.Core.Tests;

public class Win11ReadinessTests
{
    // A typical supported laptop: Latitude 7430 class.
    private static readonly Win11Inputs Good = new(
        Firmware: new FirmwareInfo(true, SecureBootState.On),
        Tpm: new TpmInfo(true, 2, null, null, null, null),
        CpuName: "12th Gen Intel(R) Core(TM) i7-1265U",
        CpuCores: 10, CpuMaxClockMHz: 1800,
        Sse42: true, Popcnt: true, IsArm: false,
        RamBytes: 16L << 30,
        SystemDiskBytes: 500_000_000_000,
        DirectX12: true, BasicDisplayDriver: false,
        Panel: new DisplayPanel("LG Display", "LGD", true, 1920, 1080, 30, 17),
        OsBuild: 22631);

    private static Win11Check Check(Win11Report r, string id) => r.Checks.Single(c => c.Id == id);

    [Fact]
    public void Supported_laptop_is_ready()
    {
        var r = Win11Readiness.Evaluate(Good);
        Assert.Equal(Win11Verdict.Ready, r.Verdict);
        Assert.True(r.RunningWindows11);
        Assert.All(r.Checks, c => Assert.Equal(CheckState.Pass, c.State));
    }

    [Fact]
    public void Old_cpu_is_not_supported_even_with_everything_else()
    {
        var r = Win11Readiness.Evaluate(Good with { CpuName = "Intel(R) Core(TM) i7-7600U CPU @ 2.80GHz" });
        Assert.Equal(Win11Verdict.NotSupported, r.Verdict);
        Assert.Equal(FixKind.Hardware, Check(r, "cpu").Fix);
    }

    [Fact]
    public void Tpm_switched_off_in_bios_is_fixable_with_vendor_hint()
    {
        var hints = VendorCatalog.Normalize(new MachineIdentity("Dell Inc.", "Latitude 7430", null, null, null, null, null, null)).Hints;
        var r = Win11Readiness.Evaluate(Good with { Tpm = new TpmInfo(false, null, null, null, null, null), Hints = hints });
        Assert.Equal(Win11Verdict.ReadyAfterChanges, r.Verdict);
        var tpm = Check(r, "tpm");
        Assert.Equal(FixKind.BiosSetting, tpm.Fix);
        Assert.Contains("F2", tpm.Hint);
        Assert.Contains("TPM On", tpm.Hint);
    }

    [Fact]
    public void Legacy_boot_needs_uefi_switch_and_gpt()
    {
        var r = Win11Readiness.Evaluate(Good with { Firmware = new FirmwareInfo(false, SecureBootState.NotAvailable) });
        Assert.Equal(Win11Verdict.ReadyAfterChanges, r.Verdict);
        Assert.Contains("mbr2gpt", Check(r, "uefi").Hint);
        Assert.Contains("Windows 11 needs UEFI, the modern startup mode", Check(r, "uefi").Detail);
    }

    [Fact]
    public void Tpm_is_explained_as_a_security_chip()
    {
        var tpm = Check(Win11Readiness.Evaluate(Good), "tpm");
        Assert.Equal("TPM 2.0 security chip", tpm.Title);
        Assert.Equal("TPM 2.0, switched on.", tpm.Detail);

        var missing = Check(Win11Readiness.Evaluate(Good with { Tpm = new TpmInfo(false, null, null, null, null, null) }), "tpm");
        Assert.StartsWith("Windows sees no TPM security chip.", missing.Detail);
    }

    [Fact]
    public void Secure_boot_off_is_a_warning_not_a_blocker()
    {
        var r = Win11Readiness.Evaluate(Good with { Firmware = new FirmwareInfo(true, SecureBootState.Off) });
        Assert.Equal(Win11Verdict.Ready, r.Verdict);
        Assert.Equal(CheckState.Warn, Check(r, "secureboot").State);
    }

    [Fact]
    public void Tpm_1_2_is_not_supported()
    {
        var r = Win11Readiness.Evaluate(Good with { Tpm = new TpmInfo(true, 1, null, null, null, null) });
        Assert.Equal(Win11Verdict.NotSupported, r.Verdict);
    }

    [Fact]
    public void Missing_graphics_driver_is_fixable()
    {
        var r = Win11Readiness.Evaluate(Good with { DirectX12 = false, BasicDisplayDriver = true });
        Assert.Equal(Win11Verdict.ReadyAfterChanges, r.Verdict);
        Assert.Equal(FixKind.Driver, Check(r, "graphics").Fix);
    }

    [Fact]
    public void Unknown_tpm_makes_the_verdict_incomplete()
    {
        var r = Win11Readiness.Evaluate(Good with { Tpm = null });
        Assert.Equal(Win11Verdict.Incomplete, r.Verdict);
    }

    [Fact]
    public void Missing_popcnt_blocks_24H2()
    {
        var r = Win11Readiness.Evaluate(Good with { Popcnt = false });
        Assert.Equal(Win11Verdict.NotSupported, r.Verdict);
    }

    [Theory]
    [InlineData(4L << 30, CheckState.Pass)]
    [InlineData(4000L << 20, CheckState.Pass)]   // 4 GB with firmware reserve
    [InlineData(2L << 30, CheckState.Fail)]
    public void Memory_threshold(long bytes, CheckState expected) =>
        Assert.Equal(expected, Check(Win11Readiness.Evaluate(Good with { RamBytes = bytes }), "ram").State);

    [Theory]
    [InlineData(62_500_000_000L, CheckState.Pass)]   // "64 GB" eMMC
    [InlineData(32_000_000_000L, CheckState.Fail)]
    public void Storage_threshold(long bytes, CheckState expected) =>
        Assert.Equal(expected, Check(Win11Readiness.Evaluate(Good with { SystemDiskBytes = bytes }), "storage").State);

    [Theory]
    [InlineData(1366, 768, 30, 17, CheckState.Pass)]
    [InlineData(1024, 600, 22, 13, CheckState.Fail)]   // 10" netbook panel below 720p
    [InlineData(1280, 800, 17, 11, CheckState.Fail)]   // 8" tablet
    public void Display_requirement(int w, int h, int wcm, int hcm, CheckState expected) =>
        Assert.Equal(expected, Check(Win11Readiness.Evaluate(Good with { Panel = new DisplayPanel(null, null, true, w, h, wcm, hcm) }), "display").State);

    [Fact]
    public void Windows_10_build_is_not_running_11() =>
        Assert.False(Win11Readiness.Evaluate(Good with { OsBuild = 19045 }).RunningWindows11);
}
