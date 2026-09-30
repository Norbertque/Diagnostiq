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
}
