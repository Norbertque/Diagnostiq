using Diagnostiq.Core.Storage;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.Core.Scoring;

public enum HealthVerdict { Excellent, Ok, NeedsRepair }

public enum Severity { Critical, Major, Minor }

public sealed record Deduction(string Area, string Reason, Severity Severity, int Points);

/// <param name="NotTested">Tests that were never run, so a high score based on a few tests isn't mistaken for a full check.</param>
public sealed record HealthReport(int Score, HealthVerdict Verdict, IReadOnlyList<Deduction> Deductions,
    IReadOnlyList<TestResult> Skipped, IReadOnlyList<string> NotTested)
{
    public bool HasCritical => Deductions.Any(d => d.Severity == Severity.Critical);

    /// <summary>No test has a result yet (none run, or all skipped): the score then only reflects what the startup scan found (battery, drives, drivers, Bluetooth).</summary>
    public bool NothingTested { get; init; }
}

/// <summary>Facts the score needs, so <see cref="HealthScore.Compute(HealthInputs)"/> stays pure and testable.</summary>
public sealed record HealthInputs(
    IReadOnlyList<TestResult> Results,
    double? BatteryHealthPercent,
    CheckState? StorageHealth,
    string? StorageProblem,
    int DriverProblems,
    bool BluetoothMissing)
{
    public static HealthInputs From(SystemSnapshot s, TestRun run)
    {
        // Worst internal drive: Windows' own health plus the ATA SMART fallback.
        var verdicts = (s.DriveHealth.Value ?? []).Where(d => !d.IsUsb).Select(Storage.StorageHealth.Evaluate).ToList();
        var worst = verdicts.OrderBy(v => v.State switch { CheckState.Fail => 0, CheckState.Warn => 1, _ => 2 }).FirstOrDefault();
        var smartBad = (s.Smart.Value ?? []).FirstOrDefault(d => !d.Healthy);
        CheckState? storage = smartBad is not null ? CheckState.Fail : worst?.State;
        string? problem = smartBad?.Problems.FirstOrDefault() ?? worst?.Reasons.FirstOrDefault();

        return new HealthInputs(run.Results,
            s.Battery.Value is { Present: true } b ? b.HealthPercent : null,
            storage, problem,
            s.DeviceProblems.Value?.Count ?? 0,
            s.Bluetooth.IsOk && s.Bluetooth.Value?.RadioPresent == false);
    }
}

/// <summary>
/// 0–100 overall condition. Critical faults (−30) mean the laptop needs repair whatever the
/// total; major faults cost 15, minor ones and warnings 5. Skipped and untested items are
/// listed, not penalised. Windows 11 readiness is reported separately and doesn't count.
/// </summary>
public static class HealthScore
{
    public const int Critical = 30, Major = 15, Minor = 5;

    /// <summary>What a complete check covers; anything missing is listed as "not tested".</summary>
    public static readonly (string Id, string Title)[] Expected =
    [
        (TestIds.Network, "Wi-Fi and internet"), (TestIds.Cpu, "Processor under load"), (TestIds.Memory, "Memory"),
        (TestIds.SurfaceScan, "Disk surface scan"), (TestIds.DiskSpeed, "Disk speed"), (TestIds.Display, "Screen"),
        (TestIds.Brightness, "Brightness"), (TestIds.Keyboard, "Keyboard"), (TestIds.Touchpad, "Touchpad"),
        (TestIds.Speakers, "Speakers"), (TestIds.Headphones, "Headphone jack"), (TestIds.Microphone, "Microphone"),
        (TestIds.Webcam, "Camera"), (TestIds.Usb, "USB ports"), (TestIds.Charger, "Charger"),
    ];

    private static readonly HashSet<string> CriticalWhenFailed = [TestIds.SurfaceScan, TestIds.Memory, TestIds.Display, TestIds.Keyboard, TestIds.Cpu];
    private static readonly HashSet<string> MajorWhenFailed = [TestIds.Speakers, TestIds.Touchpad, TestIds.Webcam, TestIds.Microphone, TestIds.Usb, TestIds.Charger, TestIds.Network];

    public static HealthReport Compute(SystemSnapshot snapshot, TestRun run) => Compute(HealthInputs.From(snapshot, run));

    public static HealthReport Compute(HealthInputs x)
    {
        var deductions = new List<Deduction>();

        foreach (var r in x.Results)
        {
            switch (r.Outcome)
            {
                case TestOutcome.Fail when CriticalWhenFailed.Contains(r.Id):
                    deductions.Add(new(r.Title, r.Detail ?? "Failed.", Severity.Critical, Critical));
                    break;
                case TestOutcome.Fail when MajorWhenFailed.Contains(r.Id):
                    deductions.Add(new(r.Title, r.Detail ?? "Failed.", Severity.Major, Major));
                    break;
                case TestOutcome.Fail or TestOutcome.Warn:
                    deductions.Add(new(r.Title, r.Detail ?? (r.Outcome == TestOutcome.Fail ? "Failed." : "Warning."), Severity.Minor, Minor));
                    break;
            }
        }

        switch (x.StorageHealth)
        {
            case CheckState.Fail: deductions.Add(new("Storage", x.StorageProblem ?? "The drive reports a failure.", Severity.Critical, Critical)); break;
            // Running hot, 80 % worn, a logged error: worth knowing, but the drive still works.
            case CheckState.Warn: deductions.Add(new("Storage", x.StorageProblem ?? "The drive reports a warning.", Severity.Minor, Minor)); break;
        }

        switch (x.BatteryHealthPercent)
        {
            case < 40 and var h: deductions.Add(new("Battery", $"Holds only {h:0}% of its original capacity. Replace it.", Severity.Critical, Critical)); break;
            case < 60 and var h: deductions.Add(new("Battery", $"Holds {h:0}% of its original capacity.", Severity.Major, Major)); break;
        }

        if (x.DriverProblems > 0)
            deductions.Add(new("Drivers", $"{x.DriverProblems} device{(x.DriverProblems == 1 ? " has" : "s have")} a driver problem.", Severity.Minor, Minor));
        if (x.BluetoothMissing)
            deductions.Add(new("Bluetooth", "No Bluetooth radio found.", Severity.Minor, Minor));

        int score = Math.Max(0, 100 - deductions.Sum(d => d.Points));
        bool critical = deductions.Any(d => d.Severity == Severity.Critical);
        var verdict = critical || score < 60 ? HealthVerdict.NeedsRepair : score >= 85 ? HealthVerdict.Excellent : HealthVerdict.Ok;

        var ran = x.Results.Select(r => r.Id).ToHashSet();
        return new HealthReport(score, verdict,
            deductions.OrderBy(d => d.Severity).ToList(),
            x.Results.Where(r => r.Outcome == TestOutcome.Skipped).ToList(),
            Expected.Where(e => !ran.Contains(e.Id)).Select(e => e.Title).ToList())
        {
            NothingTested = x.Results.All(r => r.Outcome == TestOutcome.Skipped),
        };
    }

    public static string Label(HealthVerdict v) => v switch
    {
        HealthVerdict.Excellent => "Excellent",
        HealthVerdict.Ok => "OK",
        _ => "Needs repair",
    };
}
