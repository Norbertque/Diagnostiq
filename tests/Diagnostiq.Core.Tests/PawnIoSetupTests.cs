using System.Security.Cryptography;
using Diagnostiq.Core.Sensors;

namespace Diagnostiq.Core.Tests;

public class PawnIoSetupTests
{
    [Fact]
    public void Bundled_setup_is_embedded_unmodified()
    {
        using var stream = PawnIoSetup.OpenBundled();
        Assert.Equal(PawnIoSetup.BundledSha256, Convert.ToHexStringLower(SHA256.HashData(stream)));
    }

    [Theory]
    [InlineData(null, PawnIoState.NotInstalled)]
    [InlineData("1.9.0.0", PawnIoState.Outdated)]
    [InlineData("2.0.0.0", PawnIoState.Installed)]
    [InlineData("2.2.0.0", PawnIoState.Installed)]
    public void Installed_version_is_classified(string? version, PawnIoState expected)
    {
        Assert.Equal(expected, PawnIoSetup.Classify(version is null ? null : Version.Parse(version)));
    }

    [Theory]
    [InlineData(@"""C:\Program Files\PawnIO\uninstall.exe"" -uninstall -silent", @"C:\Program Files\PawnIO\uninstall.exe", "-uninstall -silent")]
    [InlineData(@"C:\PawnIO\uninstall.exe -uninstall", @"C:\PawnIO\uninstall.exe", "-uninstall")]
    [InlineData(@"C:\PawnIO\uninstall.exe", @"C:\PawnIO\uninstall.exe", "")]
    public void Uninstall_command_is_split(string command, string file, string args)
    {
        Assert.Equal((file, args), PawnIoSetup.SplitCommand(command));
    }

    [Fact]
    public void Unterminated_quote_is_rejected()
    {
        Assert.Null(PawnIoSetup.SplitCommand(@"""C:\PawnIO\uninstall.exe -uninstall"));
    }

    // InstallAsync and UninstallAsync turn these into a failed result instead of throwing.
    [Fact]
    public void Setup_failures_become_messages()
    {
        Assert.True(PawnIoSetup.IsSetupFailure(new System.ComponentModel.Win32Exception(5)));
        Assert.True(PawnIoSetup.IsSetupFailure(new InvalidDataException()));
        Assert.True(PawnIoSetup.IsSetupFailure(new IOException("There is not enough space on the disk.")));
        Assert.True(PawnIoSetup.IsSetupFailure(new UnauthorizedAccessException()));
        Assert.False(PawnIoSetup.IsSetupFailure(new NullReferenceException()));
    }

    [Fact]
    public void Blocked_setup_points_at_antivirus()
    {
        var text = PawnIoSetup.Explain(new System.ComponentModel.Win32Exception(5, "An error occurred trying to start process 'C:\\x\\PawnIO_setup.exe'."));
        Assert.StartsWith("Windows couldn't start the setup (", text);
        Assert.DoesNotContain("PawnIO_setup.exe", text);
        Assert.EndsWith("Check that antivirus isn't blocking Diagnostiq.", text);
    }

    [Fact]
    public void Corrupted_setup_says_to_download_again() =>
        Assert.Contains("Download Diagnostiq again.", PawnIoSetup.Explain(new InvalidDataException("The bundled PawnIO setup is corrupted.")));

    [Fact]
    public void Other_failures_keep_their_reason() =>
        Assert.Equal("There is not enough space on the disk.", PawnIoSetup.Explain(new IOException("There is not enough space on the disk.")));
}
