using Diagnostiq.Core.Hardware;

namespace Diagnostiq.Core.Win11;

/// <summary>What it takes to fix a failed requirement.</summary>
public enum FixKind { None, BiosSetting, Driver, Hardware }

public sealed record Win11Check(string Id, string Title, CheckState State, string Detail, FixKind Fix = FixKind.None, string? Hint = null);

public enum Win11Verdict
{
    Ready,
    /// <summary>Only BIOS settings or a driver stand in the way.</summary>
    ReadyAfterChanges,
    NotSupported,
    /// <summary>A key requirement couldn't be read (e.g. no admin rights for TPM details).</summary>
    Incomplete,
}

public sealed record Win11Report(Win11Verdict Verdict, bool RunningWindows11, IReadOnlyList<Win11Check> Checks);

/// <summary>Raw facts, gathered by the snapshot; <see cref="Win11Readiness.Evaluate"/> is pure so tests can drive it.</summary>
public sealed record Win11Inputs(
    FirmwareInfo? Firmware,
    TpmInfo? Tpm,
    string? CpuName,
    int? CpuCores,
    int? CpuMaxClockMHz,
    bool Sse42,
    bool Popcnt,
    bool IsArm,
    long? RamBytes,
    long? SystemDiskBytes,
    bool? DirectX12,
    bool BasicDisplayDriver,
    DisplayPanel? Panel,
    int OsBuild,
    VendorHints? Hints = null);

/// <summary>
/// Windows 11 minimum requirements (learn.microsoft.com/windows/whats-new/windows11-requirements):
/// 1 GHz 2-core 64-bit CPU on the supported list, 4 GB RAM, 64 GB storage, UEFI with Secure Boot
/// capability, TPM 2.0, DirectX 12 / WDDM 2.0 graphics, 720p display larger than 9" diagonal.
/// Version 24H2 also needs the SSE4.2 and POPCNT instructions.
/// </summary>
public static class Win11Readiness
{
    public const int FirstWindows11Build = 22000;

    // Checks whose "Unknown" makes the verdict Incomplete; the rest are informative.
    private static readonly HashSet<string> Essential = ["cpu", "instructions", "tpm", "uefi"];

    public static Win11Report Evaluate(Win11Inputs x)
    {
        var setup = x.Hints is { } h ? $"BIOS setup ({h.SetupKey})" : "BIOS setup";
        var checks = new List<Win11Check>
        {
            Cpu(x),
            Instructions(x),
            Tpm(x, setup),
            Uefi(x, setup),
            SecureBoot(x, setup),
            Ram(x),
            Storage(x),
            Graphics(x),
            Display(x),
        };

        Win11Verdict verdict =
            checks.Any(c => c.State == CheckState.Fail && c.Fix == FixKind.Hardware) ? Win11Verdict.NotSupported
            : checks.Any(c => c.State == CheckState.Fail) ? Win11Verdict.ReadyAfterChanges
            : checks.Any(c => c.State == CheckState.Unknown && Essential.Contains(c.Id)) ? Win11Verdict.Incomplete
            : Win11Verdict.Ready;

        return new Win11Report(verdict, x.OsBuild >= FirstWindows11Build, checks);
    }

    private static Win11Check Cpu(Win11Inputs x)
    {
        const string title = "Processor";
        if (x.CpuName is null) return new("cpu", title, CheckState.Unknown, "Couldn't read the processor.");
        if (x.CpuCores is < 2) return new("cpu", title, CheckState.Fail, $"{x.CpuCores} core; at least 2 needed.", FixKind.Hardware);
        if (x.CpuMaxClockMHz is < 1000) return new("cpu", title, CheckState.Fail, $"{x.CpuMaxClockMHz} MHz; at least 1 GHz needed.", FixKind.Hardware);

        var r = SupportedCpus.Check(x.CpuName);
        return r.Support switch
        {
            CpuSupport.Supported => new("cpu", title, CheckState.Pass, $"{r.Family} is on Microsoft's supported list."),
            CpuSupport.LikelySupported => new("cpu", title, CheckState.Warn,
                $"{r.Family} is newer than the lists this app ships with; newer families are supported."),
            CpuSupport.NotSupported => new("cpu", title, CheckState.Fail,
                $"{r.Family} isn't on Microsoft's supported list.", FixKind.Hardware),
            _ => new("cpu", title, CheckState.Unknown, $"\"{x.CpuName}\" isn't a recognised processor name."),
        };
    }

    private static Win11Check Instructions(Win11Inputs x)
    {
        const string title = "SSE4.2 and POPCNT";
        if (x.IsArm) return new("instructions", title, CheckState.Pass, "Not needed on ARM processors.");
        return x.Sse42 && x.Popcnt
            ? new("instructions", title, CheckState.Pass, "Supported (required since Windows 11 24H2).")
            : new("instructions", title, CheckState.Fail, "Missing; Windows 11 24H2 and later won't run on this processor.", FixKind.Hardware);
    }

    private static Win11Check Tpm(Win11Inputs x, string setup)
    {
        const string title = "TPM 2.0 security chip";
        var where = x.Hints?.TpmLocation ?? "Security or Advanced: TPM, Intel PTT or AMD fTPM";
        if (x.Tpm is not { } t) return new("tpm", title, CheckState.Unknown, "Couldn't read the TPM.");
        if (!t.Present)
            return new("tpm", title, CheckState.Fail, "Windows sees no TPM security chip. Most laptops from 2016 on have one; it may be switched off.",
                FixKind.BiosSetting, $"In {setup}: {where}");
        if (t.Enabled == false || t.Activated == false)
            return new("tpm", title, CheckState.Fail, "TPM is present but switched off.", FixKind.BiosSetting, $"In {setup}: {where}");
        return t.MajorVersion switch
        {
            >= 2.0 => new("tpm", title, CheckState.Pass, t.Manufacturer is { } m ? $"TPM 2.0 ({m}), switched on." : "TPM 2.0, switched on."),
            { } v => new("tpm", title, CheckState.Fail, $"TPM {v:0.0} only.", FixKind.Hardware,
                "Some business laptops (Dell, HP, Lenovo) can switch TPM 1.2 to 2.0 with a firmware update from the vendor."),
            null => new("tpm", title, CheckState.Unknown, "A TPM is present but its version couldn't be read."),
        };
    }

    private static Win11Check Uefi(Win11Inputs x, string setup) => x.Firmware?.Uefi switch
    {
        true => new("uefi", "UEFI boot", CheckState.Pass, "Booted in UEFI mode."),
        false => new("uefi", "UEFI boot", CheckState.Fail,
            "Starts in the older Legacy BIOS (CSM) mode; Windows 11 needs UEFI, the modern startup mode.", FixKind.BiosSetting,
            $"First convert the Windows disk to GPT (mbr2gpt /convert /allowFullOS), or Windows won't start. Then set Boot mode to UEFI in {setup}."),
        null => new("uefi", "UEFI boot", CheckState.Unknown, "Couldn't read the firmware type."),
    };

    private static Win11Check SecureBoot(Win11Inputs x, string setup)
    {
        var where = x.Hints?.SecureBootLocation ?? "Boot or Security → Secure Boot";
        return x.Firmware?.SecureBoot switch
        {
            SecureBootState.On => new("secureboot", "Secure Boot", CheckState.Pass, "On."),
            SecureBootState.Off => new("secureboot", "Secure Boot", CheckState.Warn,
                "Supported but switched off. Windows 11 only needs support; turning it on is recommended.", FixKind.BiosSetting, $"In {setup}: {where}"),
            SecureBootState.NotAvailable => new("secureboot", "Secure Boot", CheckState.Unknown, "Only available after switching to UEFI boot."),
            _ => new("secureboot", "Secure Boot", CheckState.Unknown, "Couldn't read the Secure Boot state."),
        };
    }

    private static Win11Check Ram(Win11Inputs x)
    {
        if (x.RamBytes is not { } b) return new("ram", "Memory (4 GB)", CheckState.Unknown, "Couldn't read installed memory.");
        double gb = b / (double)(1L << 30);
        // A 4 GB machine can report a little less when firmware reserves memory.
        return gb >= 3.9
            ? new("ram", "Memory (4 GB)", CheckState.Pass, $"{gb:0.#} GB installed.")
            : new("ram", "Memory (4 GB)", CheckState.Fail, $"{gb:0.#} GB installed.", FixKind.Hardware,
                "Upgradeable only if the laptop has a free or replaceable memory slot.");
    }

    private static Win11Check Storage(Win11Inputs x)
    {
        if (x.SystemDiskBytes is not { } b) return new("storage", "Storage (64 GB)", CheckState.Unknown, "Couldn't read the system disk.");
        double gb = b / 1e9;
        // "64 GB" eMMC modules report about 62 GB.
        return gb >= 60
            ? new("storage", "Storage (64 GB)", CheckState.Pass, $"{gb:0} GB system disk.")
            : new("storage", "Storage (64 GB)", CheckState.Fail, $"{gb:0} GB system disk.", FixKind.Hardware);
    }

    private static Win11Check Graphics(Win11Inputs x) => x.DirectX12 switch
    {
        true => new("graphics", "DirectX 12 graphics", CheckState.Pass, "DirectX 12 with a WDDM 2 driver."),
        false when x.BasicDisplayDriver => new("graphics", "DirectX 12 graphics", CheckState.Fail,
            "No graphics driver installed (Microsoft Basic Display Adapter).", FixKind.Driver,
            "Install the graphics driver from the laptop maker's support site or Windows Update, then check again."),
        false => new("graphics", "DirectX 12 graphics", CheckState.Fail, "The graphics chip or its driver doesn't support DirectX 12.", FixKind.Hardware),
        null => new("graphics", "DirectX 12 graphics", CheckState.Unknown, "Couldn't test DirectX 12."),
    };

    private static Win11Check Display(Win11Inputs x)
    {
        const string title = "HD display over 9\"";
        if (x.Panel is not { } p) return new("display", title, CheckState.Unknown, "No built-in display found.");
        bool? hd = p.WidthPx is { } w && p.HeightPx is { } h ? Math.Min(w, h) >= 720 && Math.Max(w, h) >= 1280 : null;
        bool? big = p.DiagonalInches is { } d ? d > 9 : null;
        var desc = string.Join(", ", new[]
        {
            p.WidthPx is { } pw && p.HeightPx is { } ph ? $"{pw}×{ph}" : null,
            p.DiagonalInches is { } di ? $"{di:0.#}\"" : null,
        }.Where(s => s is not null));

        if (hd == false || big == false) return new("display", title, CheckState.Fail, desc + ".", FixKind.Hardware);
        if (hd == true && big == true) return new("display", title, CheckState.Pass, desc + ".");
        return new("display", title, CheckState.Unknown, desc.Length > 0 ? desc + " (size or resolution not reported)." : "Size and resolution not reported.");
    }
}
