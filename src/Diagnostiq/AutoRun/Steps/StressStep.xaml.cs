using Diagnostiq.Core;
using Diagnostiq.Core.Formatting;
using Diagnostiq.Core.Stress;
using Diagnostiq.Core.Testing;
using Diagnostiq.Presentation;

namespace Diagnostiq.AutoRun.Steps;

/// <summary>Live dashboard for the combined CPU + memory + disk burn-in.</summary>
public partial class StressStep : StepView
{
    private readonly StressParts _parts;
    private readonly TimeSpan? _duration;
    private double? _maxTemp;
    private bool _batteryPresent;

    /// <param name="parts">Automatic mode runs everything together; Manual mode can pick.</param>
    /// <param name="duration">Defaults to the run's preset.</param>
    public StressStep(StressParts parts = StressParts.All, TimeSpan? duration = null)
    {
        InitializeComponent();
        _parts = parts;
        _duration = duration;
    }

    public override string Id => _parts.HasFlag(StressParts.Cpu) ? TestIds.Cpu : _parts.HasFlag(StressParts.Memory) ? TestIds.Memory : TestIds.SurfaceScan;

    public override string Title => _parts switch
    {
        StressParts.Cpu => "Processor stress test",
        StressParts.Memory => "Memory test",
        StressParts.DiskScan => "Disk surface scan",
        _ => "Stress test",
    };

    public override StepMode Mode => StepMode.Automatic;

    public override IReadOnlyList<string> ResultIds =>
        new[] { (StressParts.Cpu, TestIds.Cpu), (StressParts.Memory, TestIds.Memory), (StressParts.DiskScan, TestIds.SurfaceScan) }
            .Where(p => _parts.HasFlag(p.Item1)).Select(p => p.Item2).ToList();

    protected override async Task OnRunAsync(CancellationToken ct)
    {
        var duration = _duration ?? Ctx.Preset.Duration();
        Heading.Text = Title;
        Intro.Text = _parts == StressParts.All
            ? $"The processor, memory and disk run flat out together for {Format.Minutes(duration)}, because faults tend to show " +
              "under heat and load. You don't need to do anything; the laptop may get warm and loud."
            : $"Runs for {Format.Minutes(duration)}. You don't need to do anything.";
        MemoryCard.Visibility = _parts.HasFlag(StressParts.Memory) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ScanCard.Visibility = _parts.HasFlag(StressParts.DiskScan) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        Remaining.Text = Clock(duration);
        foreach (var card in new[] { TempCard, ClockCard, LoadCard, FanCard })
        {
            card.Spark.AnchorLeft = true;   // the chart is the whole run, filling left to right
            card.Spark.Capacity = Math.Max((int)duration.TotalSeconds, 10);
        }
        TempCard.Spark.Minimum = 30; TempCard.Spark.Maximum = 105;
        LoadCard.Spark.Minimum = 0; LoadCard.Spark.Maximum = 100;
        ClockCard.Spark.Minimum = 0;
        FanCard.Spark.Minimum = 0;
        _batteryPresent = Ctx.Snapshot.Battery.Value?.Present == true;
        BatteryCard.Visibility = _batteryPresent ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        // Raw reads need admin; USB system disks (Windows To Go) aren't the laptop's own drive.
        int? disk = Ctx.Snapshot.IsAdmin && Ctx.Snapshot.SystemDisk is { IsUsb: false } d ? d.Number : null;
        if (disk is null)
        {
            ScanIcon.Outcome = TestOutcome.Skipped;
            ScanText.Text = Ctx.Snapshot.IsAdmin ? "Skipped: no internal system disk." : "Skipped: needs administrator rights.";
        }

        var session = new StressSession(Ctx.Sensors, disk, duration, _parts);
        session.Progress += p => Dispatcher.InvokeAsync(() => Update(p));
        var report = await session.RunAsync(ct);
        if (disk is null && !Ctx.Snapshot.IsAdmin && _parts.HasFlag(StressParts.DiskScan)) report = report with { ScanSkipped = "Needs administrator rights." };

        Ctx.Run.Stress = report;
        foreach (var result in Evaluate.Stress(report)) Ctx.Run.Record(result);
        ShowFinal();
        ct.ThrowIfCancellationRequested();
        await Task.Delay(TimeSpan.FromSeconds(2), ct);
    }

    private void Update(StressProgress p)
    {
        var s = p.Latest;
        Remaining.Text = Clock(p.Duration - p.Elapsed);
        Bar.Value = Math.Min(p.Elapsed / p.Duration, 1);

        if (s.CpuTempC is { } t)
        {
            _maxTemp = Math.Max(_maxTemp ?? t, t);
            TempCard.Set($"{t:0} °C", $"Max {_maxTemp:0} °C" + (s.TempLimited ? " · approximate sensor" : ""));
            TempCard.Spark.Add(t);
        }
        else TempCard.Set("–", MissingSensorReason());

        if (s.ClockMHz is { } mhz)
        {
            ClockCard.Set($"{mhz / 1000:0.00} GHz", s.PerformancePercent is { } perf ? $"{perf:0}% of base clock" : null);
            ClockCard.Spark.Add(mhz);
        }
        else ClockCard.Set("–", "Not reported");

        if (s.CpuLoadPercent is { } load)
        {
            LoadCard.Set($"{load:0}%", $"{Environment.ProcessorCount} threads busy");
            LoadCard.Spark.Add(load);
        }

        if (s.FanRpm is { } rpm)
        {
            FanCard.Set($"{rpm:N0} rpm");
            FanCard.Spark.Add(rpm);
        }
        else FanCard.Set("–", MissingSensorReason());

        if (p.Memory is { } m)
        {
            MemoryIcon.Outcome = m.Errors > 0 ? TestOutcome.Fail : null;
            MemoryText.Text = $"Pass {m.Pass}, pattern {m.Pattern}";
            MemorySub.Text = $"{m.TestedBytes / (double)(1L << 30):0.#} GB tested · {m.Errors:N0} errors";
        }

        if (p.Scan is { } scan)
        {
            ScanIcon.Outcome = scan.Errors > 0 ? TestOutcome.Fail : null;
            ScanText.Text = $"{scan.ChunksRead:N0} areas read · {scan.CurrentMBps:0} MB/s";
            ScanSub.Text = $"{scan.Errors:N0} read errors";
        }

        if (_batteryPresent && s.OnAcPower == false)
        {
            BatteryTitle.Text = "On battery: measuring drain under load";
            BatteryText.Text = Format.Join(s.DischargeWatts is { } w ? $"Drawing {w:0.0} W" : null, s.BatteryPercent is { } pct ? $"{pct}% left" : null);
        }
        else if (_batteryPresent)
        {
            BatteryTitle.Text = "Optional: measure the battery under load";
            BatteryText.Text = "Unplug the charger now and the rest of the run doubles as a battery drain test. You'll be asked to plug it back in later.";
        }
    }

    private void ShowFinal()
    {
        Bar.Value = 1;
        Remaining.Text = "0:00";
        MemoryIcon.Outcome = Ctx.Run[TestIds.Memory]?.Outcome;
        ScanIcon.Outcome = Ctx.Run[TestIds.SurfaceScan]?.Outcome;
    }

    /// <summary>Why a temperature or fan reading is blank: rights, driver, or the laptop simply doesn't expose it.</summary>
    private string MissingSensorReason() =>
        !Ctx.Snapshot.IsAdmin ? "Needs administrator rights"
        : Core.Sensors.PawnIoSetup.State != Core.Sensors.PawnIoState.Installed ? "Needs the sensor driver (install it on Home)"
        : "Not reported by this laptop";

    private static string Clock(TimeSpan t) => t < TimeSpan.Zero ? "0:00" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}
