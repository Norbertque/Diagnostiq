using System.Reflection;
using Diagnostiq.Core.Formatting;
using Diagnostiq.Core.Hardware;
using Diagnostiq.Core.Os;
using Diagnostiq.Core.Scoring;
using Diagnostiq.Core.Storage;
using Diagnostiq.Core.Testing;
using Diagnostiq.Core.Win11;

namespace Diagnostiq.Core.Reporting;

public sealed record ReportRow(string Label, string Value, string? Status = null);

public sealed record ReportSection(string Title, IReadOnlyList<ReportRow> Rows);

public sealed record ReportDevice(string Name, string? Vendor, string? Model, string? ModelNumber, string SerialLabel, string? Serial,
    string? Bios, string? BiosDate);

public sealed record ReportHealth(int Score, string Verdict, IReadOnlyList<Deduction> Deductions, IReadOnlyList<string> Skipped, IReadOnlyList<string> NotTested)
{
    /// <summary>See <see cref="HealthReport.NothingTested"/>.</summary>
    public bool NothingTested { get; init; }
}

public sealed record ReportWin11Check(string Title, string State, string Detail, string? Hint);

public sealed record ReportWin11(string Verdict, bool RunningWindows11, IReadOnlyList<ReportWin11Check> Checks);

public sealed record ReportTest(string Id, string Title, string Outcome, string? Detail);

/// <param name="Series">Downsampled to at most ~120 points: seconds, °C, MHz, load %.</param>
public sealed record ReportStress(double Minutes, double? MaxTempC, double? AverageTempC, bool TempApproximate,
    double? SustainedClockPercent, string? Throttling, IReadOnlyList<ReportSample> Series);

public sealed record ReportSample(int Second, double? TempC, double? ClockMHz, double? LoadPercent);

/// <summary>Everything the HTML and JSON reports contain, built once from the snapshot and the session.</summary>
public sealed record ReportModel(
    string App,
    DateTimeOffset Created,
    ReportDevice Device,
    ReportHealth Health,
    ReportWin11 Windows11,
    IReadOnlyList<ReportTest> Tests,
    ReportStress? Stress,
    IReadOnlyList<ReportSection> Specs,
    IReadOnlyList<ReportSection> WindowsAndSecurity)
{
    public static ReportModel Build(SystemSnapshot s, TestRun run)
    {
        var health = HealthScore.Compute(s, run);
        var d = s.Device;
        var id = s.Identity.Value;
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "2";

        return new ReportModel(
            App: $"Diagnostiq {version}",
            Created: DateTimeOffset.Now,
            Device: new ReportDevice(d?.DisplayName ?? "Unknown computer", d?.Vendor, d?.Model, d?.ModelNumber, d?.SerialLabel ?? "Serial number",
                d?.Serial, id?.BiosVersion, id?.BiosDate is { } bd ? Format.Date(bd) : null),
            Health: new ReportHealth(health.Score, HealthScore.Label(health.Verdict), health.Deductions,
                health.Skipped.Select(r => r.Title).ToList(), health.NotTested) { NothingTested = health.NothingTested },
            Windows11: new ReportWin11(Win11Label(s.Win11.Verdict), s.Win11.RunningWindows11,
                s.Win11.Checks.Select(c => new ReportWin11Check(c.Title, c.State.ToString(), c.Detail, c.Hint)).ToList()),
            Tests: run.Results.Select(r => new ReportTest(r.Id, r.Title, r.Outcome.ToString(), r.Detail)).ToList(),
            Stress: run.Stress is { } st ? BuildStress(st) : null,
            Specs: BuildSpecs(s, run),
            WindowsAndSecurity: BuildSecurity(s));
    }

    private static string Win11Label(Win11Verdict v) => v switch
    {
        Win11Verdict.Ready => "Ready",
        Win11Verdict.ReadyAfterChanges => "Ready after changes",
        Win11Verdict.NotSupported => "Not supported",
        _ => "Couldn't check everything",
    };

    private static ReportStress BuildStress(Stress.StressReport r)
    {
        int step = Math.Max(1, r.Samples.Count / 120);
        var series = r.Samples.Where((_, i) => i % step == 0)
            .Select(x => new ReportSample((int)x.At.TotalSeconds, x.CpuTempC, x.ClockMHz, x.CpuLoadPercent)).ToList();
        return new ReportStress(Math.Round(r.Duration.TotalMinutes, 1), r.MaxTempC, r.AverageTempC, r.TempLimited,
            r.Throttle.SustainedPerformancePercent,
            r.Throttle.Throttled ? (r.Throttle.Reason == "thermal" ? "Thermal throttling" : "Power limited") : null,
            series);
    }

    private static List<ReportSection> BuildSpecs(SystemSnapshot s, TestRun run)
    {
        var list = new List<ReportSection>();
        void Add(string title, params (string Label, string? Value)[] rows)
        {
            var kept = rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).Select(r => new ReportRow(r.Label, r.Value!)).ToList();
            if (kept.Count > 0) list.Add(new ReportSection(title, kept));
        }

        var c = s.Cpu.Value;
        Add("Processor",
            ("Name", c is null ? null : Names.Clean(c.Name)),
            ("Cores / threads", c is null ? null : $"{c.Cores} / {c.Threads}"),
            ("Base clock", c is { MaxClockMHz: > 0 } ? $"{c.MaxClockMHz / 1000.0:0.0#} GHz" : null));

        var m = s.Memory.Value;
        Add("Memory",
            ("Installed", m is null ? null : Format.Memory(m.InstalledBytes > 0 ? m.InstalledBytes : m.VisibleBytes)),
            ("Modules", m is null || m.Modules.Count == 0 ? null
                : string.Join(", ", m.Modules.Select(x => $"{Format.Memory(x.CapacityBytes)}{(x.SpeedMHz is > 0 ? $" {x.SpeedMHz} MT/s" : "")}"))));

        var health = s.DriveHealth.Value ?? [];
        foreach (var disk in s.Disks.Value ?? [])
        {
            var h = health.FirstOrDefault(x => x.DiskNumber == disk.Number);
            var v = h is null ? null : StorageHealth.Evaluate(h);
            Add($"Storage: {Names.Clean(disk.Model)}",
                ("Size", Format.Disk(disk.SizeBytes)),
                ("Type", Format.Join(h?.BusType ?? disk.InterfaceType, h?.MediaType is "SSD" or "HDD" ? h.MediaType : null)),
                ("Health", v is null ? null : v.State == CheckState.Pass ? "Healthy" : string.Join(" ", v.Reasons)),
                ("Wear", h?.Counters?.WearPercent is { } w ? $"{w}%" : null),
                ("Powered on", h?.Counters?.PowerOnHours is { } poh ? $"{Format.Number(poh)} h" : null));
        }
        if (run.Benchmark is { } b)
            Add("Disk speed", ("Sequential read", $"{b.SeqReadMBps:N0} MB/s"), ("Sequential write", $"{b.SeqWriteMBps:N0} MB/s"),
                ("Random 4K reads", $"{b.RandomRead4kIops:N0} per second"));

        foreach (var g in s.Gpus.Value ?? [])
            Add($"Graphics: {Names.Clean(g.Name)}",
                ("Video memory", g.DedicatedMemoryBytes is > 0 and var vram ? Format.Memory(vram) : null),
                ("Driver", Format.Join(g.DriverVersion, g.DriverDate is { } dd ? Format.Date(dd) : null)));

        if (s.MainDisplay is { } p)
            Add("Display",
                ("Panel", p.Name ?? (p.ManufacturerCode is { } mc ? $"{mc} panel" : null)),
                ("Resolution", p.WidthPx is { } pw && p.HeightPx is { } ph ? $"{pw} × {ph}" : null),
                ("Size", p.DiagonalInches is { } di ? $"{di:0.0}\"" : null));

        if (s.Battery.Value is { Present: true } bat)
            Add("Battery",
                ("Health", bat.HealthPercent is { } hp ? $"{hp:0}% of original capacity" : null),
                ("Capacity", bat.FullChargeCapacityMWh is { } fc && bat.DesignCapacityMWh is { } dc ? $"{fc / 1000.0:0.#} of {dc / 1000.0:0.#} Wh" : null),
                ("Charge cycles", bat.CycleCount?.ToString()),
                ("Under full load", run.Stress?.Drain is { } drain
                    ? $"{drain.AverageWatts:0.0} W" + (drain.EstimatedRuntime is { } rt ? $", about {Evaluate.Hours(rt)} per charge" : "")
                    : null));

        var wifi = s.Wifi.Value;
        Add("Wireless",
            ("Wi-Fi", wifi is { AdapterPresent: true } ? Names.Clean(wifi.AdapterName ?? "Wi-Fi") : "Not found"),
            ("Bluetooth", s.Bluetooth.Value?.RadioPresent == true ? Names.Clean(s.Bluetooth.Value.RadioName ?? "Present") : "Not found"));
        return list;
    }

    private static List<ReportSection> BuildSecurity(SystemSnapshot s)
    {
        var os = s.Os.Value;
        var act = s.Activation.Value;
        var rows = new List<ReportRow>();
        void Row(string label, string? value, string? status = null) { if (!string.IsNullOrWhiteSpace(value)) rows.Add(new(label, value, status)); }

        Row("Windows", os is null ? null : Format.Join(os.Name, os.DisplayVersion, $"build {os.BuildString}"));
        Row("Installed", os?.InstallDate is { } d ? Format.Date(d) : null);
        (string? Value, string? Status) activation = act?.State switch
        {
            null => (null, null),
            LicenseState.Licensed => ($"Activated ({act.Channel ?? "unknown licence"})", "Pass"),
            // The licensing service didn't answer (it can be slow to start); that isn't a missing licence.
            LicenseState.Unknown => ("Couldn't check", null),
            _ => ("Not activated", "Warn"),
        };
        Row("Activation", activation.Value, activation.Status);
        Row("Key in BIOS", act?.HasFirmwareKey == true ? $"Windows {act.FirmwareKeyEdition}" : act is null ? null : "None");
        Row("TPM", s.Tpm.Value switch { { Present: false } => "Not found", { MajorVersion: { } v } => $"TPM {v:0.0}", _ => null });
        Row("Secure Boot", s.Firmware.Value?.SecureBoot switch
        {
            SecureBootState.On => "On", SecureBootState.Off => "Off (supported)", SecureBootState.NotAvailable => "Legacy boot", _ => null,
        });
        Row("BitLocker", s.BitLocker.Value is { } bl ? (bl.IsProtected ? "On" : "Off") : null);
        foreach (var av in s.Antivirus.Value ?? [])
            Row("Antivirus", $"{av.Name}: {(av.Enabled ? "on" : "off")}", av.Enabled ? "Pass" : "Warn");
        var problems = s.DeviceProblems.Value;
        Row("Driver problems", problems is null ? null : problems.Count == 0 ? "None"
            : string.Join("; ", problems.Select(p => $"{p.Name} ({p.Meaning.ToLowerInvariant()})")), problems is { Count: > 0 } ? "Warn" : null);
        return [new ReportSection("Windows & security", rows)];
    }
}
