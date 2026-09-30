using System.Diagnostics;
using Diagnostiq.Core.Interop;
using Microsoft.Win32;

namespace Diagnostiq.Core.Win11;

public enum SecureBootState { On, Off, NotAvailable, Unknown }

/// <param name="Uefi">Booted in UEFI mode (false = Legacy BIOS / CSM).</param>
public sealed record FirmwareInfo(bool? Uefi, SecureBootState SecureBoot);

public static class FirmwareProbe
{
    public static FirmwareInfo Read()
    {
        bool? uefi = Native.GetFirmwareType(out int type) ? type == Native.FirmwareTypeUefi : null;
        if (uefi == false) return new FirmwareInfo(false, SecureBootState.NotAvailable);

        // Present on UEFI systems that support Secure Boot; 1 = enforced, 0 = supported but off.
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
        var state = key?.GetValue("UEFISecureBootEnabled") switch
        {
            1 => SecureBootState.On,
            0 => SecureBootState.Off,
            _ => SecureBootState.Unknown,
        };
        return new FirmwareInfo(uefi, state);
    }

    /// <summary>
    /// Restarts straight into the UEFI setup screen (same as Settings → Recovery → Advanced
    /// startup → UEFI Firmware Settings). UEFI only; needs admin. The caller must confirm first.
    /// </summary>
    public static void RestartToFirmwareSetup()
    {
        var shutdown = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
        using var p = Process.Start(new ProcessStartInfo(shutdown, "/r /fw /t 0") { UseShellExecute = false, CreateNoWindow = true });
    }
}
