using Diagnostiq.Core;
using Diagnostiq.Core.Formatting;
using Diagnostiq.Core.Hardware;
using Diagnostiq.Core.Os;
using Diagnostiq.Core.Storage;
using Diagnostiq.Core.Win11;
using Wpf.Ui.Controls;

namespace Diagnostiq.Presentation;

public sealed record SpecTile(string Label, SymbolRegular Icon, string Value, string? Detail, string? Pill = null, CheckState? PillState = null)
{
    /// <summary>What a screen reader says for the tile (the list reads items by their ToString).</summary>
    public override string ToString() =>
        string.Join(". ", new[] { $"{Label}: {Value}", Detail, Pill }.Where(s => !string.IsNullOrEmpty(s))) + ".";
}

public sealed record Win11Summary(CheckState State, string Headline, string Detail, string? Note);

/// <summary>Everything the Home screen shows, as display strings built from the startup snapshot.</summary>
public sealed class HomeModel
{
    public required string DeviceName { get; init; }
    public required string Subtitle { get; init; }
    public string? Serial { get; init; }
    public required Win11Summary Win11 { get; init; }
    public required IReadOnlyList<Finding> Findings { get; init; }
    public required IReadOnlyList<SpecTile> Tiles { get; init; }

    public static HomeModel From(SystemSnapshot s)
    {
        var device = s.Device;
        var id = s.Identity.Value;
        return new HomeModel
        {
            DeviceName = device?.DisplayName ?? "This computer",
            Serial = device?.Serial,
            Subtitle = Format.Join(
                device?.Serial is { } serial ? $"{device.SerialLabel} {serial}" : null,
                device?.ModelNumber is { } mt ? $"Type {mt}" : null,
                id?.BiosVersion is { } bios ? $"BIOS {bios}" + (id.BiosDate is { } d ? $" ({Format.Date(d)})" : "") : null,
                device?.IsVirtualMachine == true ? "Virtual machine" : null),
            Win11 = Summarize(s.Win11, s.Os.Value, s.IsAdmin),
            Findings = Core.Findings.From(s),
            Tiles = [Cpu(s), Memory(s), Storage(s), Graphics(s), Display(s), Battery(s), Network(s), Windows(s), Security(s)],
        };
    }

    internal static Win11Summary Summarize(Win11Report r, OsInfo? os, bool isAdmin)
    {
        string? note = r.RunningWindows11 ? $"Already running {os?.Name ?? "Windows 11"}{(os?.DisplayVersion is { } v ? " " + v : "")}." : null;
        var failed = r.Checks.Where(c => c.State == CheckState.Fail).ToList();
        switch (r.Verdict)
        {
            case Win11Verdict.Ready:
                // Informative checks (display, memory, …) may be unreadable without changing the verdict; say so.
                var warns = r.Checks.Where(c => c.State == CheckState.Warn).Select(c => $"{c.Title}: {c.Detail}").ToList();
                int unread = r.Checks.Count(c => c.State == CheckState.Unknown);
                string unreadNote = $"{unread} requirement{(unread == 1 ? "" : "s")} couldn't be checked.";
                string detail = warns.Count > 0 ? string.Join(" ", warns) + (unread > 0 ? " " + unreadNote : "")
                              : unread == 0 ? $"Meets all {r.Checks.Count} requirements."
                              : $"Meets {r.Checks.Count - unread} of {r.Checks.Count} requirements; {unread} couldn't be checked.";
                return new(CheckState.Pass, "Ready for Windows 11", detail, note);

            case Win11Verdict.ReadyAfterChanges:
                string after = failed.All(c => c.Fix == FixKind.BiosSetting) ? "after BIOS changes"
                             : failed.All(c => c.Fix == FixKind.Driver) ? "after a driver install"
                             : "after a few changes";
                return new(CheckState.Warn, $"Ready for Windows 11 {after}",
                    "To change: " + string.Join(", ", failed.Select(c => c.Title)) + ".", note);

            case Win11Verdict.NotSupported:
                var blockers = failed.Where(c => c.Fix == FixKind.Hardware).Select(c => $"{c.Title}: {c.Detail}");
                return new(CheckState.Fail, "Doesn't meet Windows 11 requirements", string.Join(" ", blockers), note);

            default:
                var unknown = r.Checks.Where(c => c.State == CheckState.Unknown).Select(c => c.Title);
                return new(CheckState.Unknown, "Couldn't check every Windows 11 requirement",
                    "Couldn't read: " + string.Join(", ", unknown) + "." + (isAdmin ? "" : " Restart as administrator for full results."), note);
        }
    }

    private static SpecTile Cpu(SystemSnapshot s)
    {
        if (s.Cpu.Value is not { } c) return new("Processor", SymbolRegular.DeveloperBoard24, "Not detected", Outcomes.Unavailable(s.Cpu));
        return new("Processor", SymbolRegular.DeveloperBoard24, Names.Clean(c.Name),
            Format.Join($"{c.Cores} cores", c.Threads != c.Cores ? $"{c.Threads} threads" : null,
                c.MaxClockMHz > 0 ? $"base {c.MaxClockMHz / 1000.0:0.0#} GHz" : null));
    }

    private static SpecTile Memory(SystemSnapshot s)
    {
        if (s.Memory.Value is not { } m) return new("Memory", SymbolRegular.Memory16, "Not detected", Outcomes.Unavailable(s.Memory));
        var modules = m.Modules.GroupBy(x => x.CapacityBytes).Select(g => $"{g.Count()} × {Format.Memory(g.Key)}");
        int? speed = m.Modules.Max(x => x.SpeedMHz);
        return new("Memory", SymbolRegular.Memory16, Format.Memory(m.InstalledBytes > 0 ? m.InstalledBytes : m.VisibleBytes),
            m.Modules.Count == 0 ? "Soldered or not reported" : Format.Join(string.Join(" + ", modules), speed is > 0 ? $"{speed} MT/s" : null));
    }

    private static SpecTile Storage(SystemSnapshot s)
    {
        var disk = s.SystemDisk;
        if (disk is null) return new("Storage", SymbolRegular.Storage24, "Not detected", s.Disks.IsOk ? null : Outcomes.Unavailable(s.Disks));
        var health = s.DriveHealth.Value?.FirstOrDefault(d => d.DiskNumber == disk.Number);
        var verdict = health is null ? null : StorageHealth.Evaluate(health);
        string kind = Format.Join(health?.BusType is { } b && b != "Other" ? b : disk.InterfaceType, health?.MediaType is "SSD" or "HDD" ? health.MediaType : null).Replace(" · ", " ");
        int others = (s.Disks.Value?.Count(d => d.Number != disk.Number && !d.IsUsb)) ?? 0;

        string? usage = health?.Counters is { } c
            ? Format.Join(c.PowerOnHours is { } h ? $"{Format.Number(h)} h powered on" : null, c.WearPercent is { } w ? $"{w}% worn" : null)
            : null;
        return new("Storage", SymbolRegular.Storage24, $"{Format.Disk(disk.SizeBytes)} {kind}".Trim(),
            Format.Join(Names.Clean(disk.Model), usage, others > 0 ? $"+{others} more drive{(others > 1 ? "s" : "")}" : null),
            verdict?.State switch
            {
                CheckState.Pass => "Healthy",
                CheckState.Warn => "Check drive",
                CheckState.Fail => "Failing",
                _ => health is null ? null : "Health not reported",
            },
            verdict?.State);
    }

    private static SpecTile Graphics(SystemSnapshot s)
    {
        var gpus = s.Gpus.Value ?? [];
        if (gpus.Count == 0) return new("Graphics", SymbolRegular.Games24, "Not detected", s.Gpus.IsOk ? null : Outcomes.Unavailable(s.Gpus));
        var main = gpus[0];
        return new("Graphics", SymbolRegular.Games24, Names.Clean(main.Name),
            Format.Join(main.DedicatedMemoryBytes is > 0 and var v ? $"{Format.Memory(v)} video memory" : null,
                main.DriverDate is { } d ? $"driver {Format.MonthYear(d)}" : null,
                gpus.Count > 1 ? "+ " + string.Join(", ", gpus.Skip(1).Select(g => Names.Clean(g.Name))) : null),
            main.IsBasicDisplayDriver ? "No driver" : main.HasProblem ? "Driver problem" : null,
            main.IsBasicDisplayDriver || main.HasProblem ? CheckState.Warn : null);
    }

    private static SpecTile Display(SystemSnapshot s)
    {
        if (s.MainDisplay is not { } p) return new("Display", SymbolRegular.Desktop24, "Not detected", s.Displays.IsOk ? null : Outcomes.Unavailable(s.Displays));
        var value = Format.Join(p.DiagonalInches is { } d ? $"{d:0.0}\"" : null, p.WidthPx is { } w && p.HeightPx is { } h ? $"{w} × {h}" : null);
        return new("Display", SymbolRegular.Desktop24, value.Length > 0 ? value.Replace(" · ", " ") : "Size not reported",
            Format.Join(p.IsInternal ? "Built-in" : "External", p.Name ?? (p.ManufacturerCode is { } m ? $"{m} panel" : null)));
    }

    private static SpecTile Battery(SystemSnapshot s)
    {
        if (s.Battery.Value is not { Present: true } b)
            return new("Battery", SymbolRegular.Battery524, s.Battery.IsOk || s.Battery.Status == Core.Probing.ProbeStatus.NotAvailable ? "No battery" : "Not detected", null);
        var live = s.BatteryLive.Value;
        string power = live?.Charging == true ? "charging" : b.OnAcPower == true ? "plugged in" : live?.DischargeWatts is { } wt ? $"drawing {wt:0.0} W" : "on battery";
        string? capacity = b.FullChargeCapacityMWh is { } full && b.DesignCapacityMWh is { } design ? $"{full / 1000.0:0} of {design / 1000.0:0} Wh" : null;
        var detail = Format.Join(b.ChargePercent is { } c ? $"{c}% charged, {power}" : power, b.CycleCount is { } cyc ? $"{cyc} cycles" : null, capacity);

        if (b.HealthPercent is not { } health) return new("Battery", SymbolRegular.Battery524, "Health not reported", detail);
        var (pill, state) = health switch
        {
            < 40 => ("Replace", CheckState.Fail),
            < 60 => ("Worn", CheckState.Warn),
            < 80 => ("Fair", CheckState.Pass),
            _ => ("Good", CheckState.Pass),
        };
        return new("Battery", live?.Charging == true ? SymbolRegular.BatteryCharge24 : SymbolRegular.Battery524, $"{health:0}% of original capacity", detail, pill, state);
    }

    private static SpecTile Network(SystemSnapshot s)
    {
        var wifi = s.Wifi.Value;
        var ethernet = s.Network.Value?.Any(a => a.Kind == Core.Network.AdapterKind.Ethernet && !a.IsExternal) == true;
        var bt = s.Bluetooth.Value?.RadioPresent == true ? "Bluetooth" : "No Bluetooth";
        if (wifi is not { AdapterPresent: true })
            return new("Network", SymbolRegular.Wifi124, "No Wi-Fi adapter", Format.Join(ethernet ? "Ethernet port" : null, bt));

        string link = wifi.RadioOn == false ? "Wi-Fi off"
            : wifi.Connected ? Format.Join("Connected", ShortStandard(wifi.Standard), wifi.SignalPercent is { } sig ? $"{sig}% signal" : null)
            : "Not connected";
        return new("Network", SymbolRegular.Wifi124, Names.Clean(wifi.AdapterName ?? "Wi-Fi"), Format.Join(link, ethernet ? "Ethernet port" : null, bt));
    }

    /// <summary>"Wi-Fi 6 (802.11ax)" → "Wi-Fi 6".</summary>
    private static string? ShortStandard(string? standard) =>
        standard is not null && standard.IndexOf('(') is > 0 and var i ? standard[..i].Trim() : standard;

    private static SpecTile Windows(SystemSnapshot s)
    {
        if (s.Os.Value is not { } os) return new("Windows", SymbolRegular.Window24, "Not detected", Outcomes.Unavailable(s.Os));
        var act = s.Activation.Value;
        var (pill, state) = act switch
        {
            // An unknown licence state means the licensing service didn't answer, not that Windows isn't activated.
            null or { State: LicenseState.Unknown } => ((string?)null, (CheckState?)null),
            { IsActivated: true } => ("Activated", CheckState.Pass),
            _ => ("Not activated", CheckState.Warn),
        };
        return new("Windows", SymbolRegular.Window24, Format.Join(os.Name, os.DisplayVersion).Replace(" · ", " "),
            Format.Join($"Build {os.BuildString}", os.InstallDate is { } d ? $"installed {Format.Date(d)}" : null,
                act?.HasFirmwareKey == true ? $"Windows {act.FirmwareKeyEdition} key in BIOS" : null),
            pill, state);
    }

    private static SpecTile Security(SystemSnapshot s)
    {
        string tpm = s.Tpm.Value switch
        {
            { Present: false } => "No TPM",
            { MajorVersion: { } v } => $"TPM {v:0.0}",
            _ => "TPM unknown",
        };
        string boot = s.Firmware.Value?.SecureBoot switch
        {
            SecureBootState.On => "Secure Boot on",
            SecureBootState.Off => "Secure Boot off",
            SecureBootState.NotAvailable => "Legacy boot",
            _ => "Secure Boot unknown",
        };
        var av = s.Antivirus.Value;
        string? antivirus = av is null ? null
            : av.FirstOrDefault(a => a.Enabled) is { } on ? $"{on.Name} on"
            : av.Count > 0 ? "Antivirus off" : null;
        string? bitlocker = s.BitLocker.Value is not { } bl ? null
            : bl.IsProtected ? "BitLocker on"
            : bl.Conversion switch
            {
                "Not encrypted" => "BitLocker off",
                "Encrypted" => "BitLocker suspended",   // encrypted, but protection is paused
                var c => $"BitLocker {c.ToLowerInvariant()}",
            };
        return new("Security", SymbolRegular.ShieldKeyhole24, $"{tpm}, {boot}", Format.Join(antivirus, bitlocker));
    }
}
