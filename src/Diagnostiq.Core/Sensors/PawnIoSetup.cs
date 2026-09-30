using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Diagnostiq.Core.Probing;
using Microsoft.Win32;

namespace Diagnostiq.Core.Sensors;

public enum PawnIoState { NotInstalled, Outdated, Installed }

public sealed record PawnIoSetupResult(bool Success, string Message);

/// <summary>
/// Installs and removes the PawnIO driver using the signed setup bundled in this assembly
/// (PawnIO_setup.exe 2.2.0 by namazso, GPL-2.0, https://github.com/namazso/PawnIO.Setup,
/// redistributed unmodified). The UI asks the user before calling either method.
/// </summary>
public static class PawnIoSetup
{
    public static readonly Version BundledVersion = new(2, 2, 0, 0);

    /// <summary>Oldest driver LibreHardwareMonitorLib 0.9.x works with.</summary>
    public static readonly Version MinimumVersion = new(2, 0, 0, 0);

    internal const string ResourceName = "PawnIO_setup.exe";
    internal const string BundledSha256 = "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromMinutes(2);

    /// <summary>True once this process installed the driver, so the app can offer to remove it on exit.</summary>
    public static bool InstalledByUs { get; private set; }

    /// <summary>Read fresh each time: LibreHardwareMonitor caches its own copy for the process lifetime.</summary>
    public static Version? InstalledVersion()
    {
        foreach (var view in (RegistryView[])[RegistryView.Registry64, RegistryView.Registry32])
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = hklm.OpenSubKey(UninstallKey);
            if (Version.TryParse(key?.GetValue("DisplayVersion") as string, out var v)) return v;
        }
        return null;
    }

    public static PawnIoState State => Classify(InstalledVersion());

    internal static PawnIoState Classify(Version? installed) => installed switch
    {
        null => PawnIoState.NotInstalled,
        _ when installed < MinimumVersion => PawnIoState.Outdated,
        _ => PawnIoState.Installed,
    };

    private const string RemoveByHand = "You can remove it in Settings › Apps › Installed apps.";

    /// <summary>
    /// Installs the bundled driver, replacing an outdated one. Takes a few seconds; call off the UI thread.
    /// Never throws: failures (antivirus blocking the setup, a full disk) come back as a message for the user.
    /// </summary>
    public static async Task<PawnIoSetupResult> InstallAsync()
    {
        try
        {
            if (State == PawnIoState.Installed) return new(true, "The PawnIO sensor driver is already installed.");
            if (!Probe.IsAdmin) return new(false, "Installing the sensor driver needs administrator rights.");

            // The setup refuses to run over an existing install, so an outdated driver goes first.
            if (State == PawnIoState.Outdated && !(await UninstallAsync().ConfigureAwait(false)).Success)
                return new(false, "Couldn't remove the outdated PawnIO sensor driver. Remove it in Settings › Apps › Installed apps, then try again.");

            var dir = CreatePrivateTempDirectory();
            try
            {
                var exe = Path.Combine(dir, ResourceName);
                await ExtractBundledAsync(exe).ConfigureAwait(false);
                var exit = await RunAsync(exe, "-install -silent").ConfigureAwait(false);
                if (State != PawnIoState.Installed)
                    return new(false, exit is null
                        ? "The sensor driver setup didn't finish in time. Try again."
                        : $"The sensor driver didn't install (setup error {exit}). Temperatures stay approximate; restart Windows and try again.");
                InstalledByUs = true;
                return new(true, "PawnIO sensor driver installed.");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (IsSetupFailure(ex))
        {
            return new(false, $"The sensor driver couldn't be installed. {Explain(ex)}");
        }
    }

    /// <summary>Removes the driver with its own registered uninstaller, falling back to the bundled setup. Never throws.</summary>
    public static async Task<PawnIoSetupResult> UninstallAsync()
    {
        try
        {
            if (State == PawnIoState.NotInstalled) return new(true, "The PawnIO sensor driver isn't installed.");
            if (!Probe.IsAdmin) return new(false, "Removing the sensor driver needs administrator rights.");

            if (ReadQuietUninstallCommand() is { } registered && File.Exists(registered.File))
                await RunAsync(registered.File, registered.Arguments).ConfigureAwait(false);

            if (State != PawnIoState.NotInstalled)
            {
                var dir = CreatePrivateTempDirectory();
                try
                {
                    var exe = Path.Combine(dir, ResourceName);
                    await ExtractBundledAsync(exe).ConfigureAwait(false);
                    await RunAsync(exe, "-uninstall -silent").ConfigureAwait(false);
                }
                finally
                {
                    try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }

            bool removed = State == PawnIoState.NotInstalled;
            if (removed) InstalledByUs = false;
            return new(removed, removed ? "PawnIO sensor driver removed." : $"Couldn't remove the PawnIO sensor driver. {RemoveByHand}");
        }
        catch (Exception ex) when (IsSetupFailure(ex))
        {
            return new(false, $"Couldn't remove the PawnIO sensor driver. {Explain(ex)} {RemoveByHand}");
        }
    }

    /// <summary>What can go wrong around the setup: extracting it, checking it, starting it.</summary>
    internal static bool IsSetupFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or Win32Exception;

    /// <summary>Why, in a sentence for the message the user sees, with what to do when there's something specific.</summary>
    internal static string Explain(Exception ex) => ex switch
    {
        // Process.Start's own message repeats the whole path; the system text alone is clearer.
        Win32Exception w => $"Windows couldn't start the setup ({new Win32Exception(w.NativeErrorCode).Message.TrimEnd('.')}). " +
                            "Check that antivirus isn't blocking Diagnostiq.",
        InvalidDataException => "The setup inside this copy of Diagnostiq is damaged. Download Diagnostiq again.",
        _ => $"{ex.Message.TrimEnd('.')}.",
    };

    internal static Stream OpenBundled() =>
        typeof(PawnIoSetup).Assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException("The bundled PawnIO setup is missing from this build.");

    private static async Task ExtractBundledAsync(string path)
    {
        await using (var source = OpenBundled())
        await using (var file = File.Create(path))
            await source.CopyToAsync(file).ConfigureAwait(false);

        await using var check = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(check).ConfigureAwait(false));
        if (hash != BundledSha256) throw new InvalidDataException("The bundled PawnIO setup is corrupted.");
    }

    /// <returns>The exit code, or null if the setup was killed after <see cref="SetupTimeout"/>.</returns>
    private static async Task<int?> RunAsync(string exe, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe),
        })!;
        using var timeout = new CancellationTokenSource(SetupTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            return null;
        }
    }

    private static (string File, string Arguments)? ReadQuietUninstallCommand()
    {
        foreach (var view in (RegistryView[])[RegistryView.Registry64, RegistryView.Registry32])
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = hklm.OpenSubKey(UninstallKey);
            if (key?.GetValue("QuietUninstallString") is string command && SplitCommand(command) is { } parsed) return parsed;
        }
        return null;
    }

    /// <summary>Splits <c>"C:\path with spaces\app.exe" -a -b</c> into the file and its arguments.</summary>
    internal static (string File, string Arguments)? SplitCommand(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return null;
        if (command[0] == '"')
        {
            int close = command.IndexOf('"', 1);
            return close < 0 ? null : (command[1..close], command[(close + 1)..].Trim());
        }
        int space = command.IndexOf(' ');
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..].Trim());
    }

    /// <summary>
    /// The setup runs elevated, so extract it where only Administrators and SYSTEM can write;
    /// otherwise a normal-user process could plant a DLL next to it.
    /// </summary>
    private static string CreatePrivateTempDirectory()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in (WellKnownSidType[])[WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid])
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));

        var dir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "Diagnostiq-PawnIO-" + Guid.NewGuid().ToString("N")));
        dir.Create(security);
        return dir.FullName;
    }
}
