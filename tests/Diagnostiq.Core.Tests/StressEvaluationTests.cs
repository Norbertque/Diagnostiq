using Diagnostiq.Core.Network;
using Diagnostiq.Core.Storage;
using Diagnostiq.Core.Stress;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.Core.Tests;

public class StressEvaluationTests
{
    /// <summary>A run of n seconds: turbo at 160 % for the first fifth, then <paramref name="perf"/>.</summary>
    private static List<StressSample> Run(int n, double perf, double temp, bool? onAc = true, double? watts = null) =>
        Enumerable.Range(0, n).Select(i => new StressSample(TimeSpan.FromSeconds(i), 100, temp, false, null,
            i < n / 5 ? 160 : perf, 3000, onAc, 90 - i / 30, watts)).ToList();

    [Fact]
    public void Holding_base_clock_is_not_throttling()
    {
        var v = StressSession.AnalyzeThrottle(Run(120, 130, 85));
        Assert.False(v.Throttled);
        Assert.Equal(130, v.SustainedPerformancePercent);
    }

    [Fact]
    public void Hot_and_slow_is_thermal_throttling() =>
        Assert.Equal("thermal", StressSession.AnalyzeThrottle(Run(120, 60, 99)).Reason);

    [Fact]
    public void Cool_and_slow_is_power_limiting() =>
        Assert.Equal("power", StressSession.AnalyzeThrottle(Run(120, 60, 70)).Reason);

    [Fact]
    public void Turbo_warmup_is_ignored()
    {
        // Median over the whole run would include the 160 % turbo burst.
        Assert.True(StressSession.AnalyzeThrottle(Run(120, 70, 95)).Throttled);
    }

    [Fact]
    public void Running_on_battery_is_not_judged_as_throttling() =>
        Assert.False(StressSession.AnalyzeThrottle(Run(120, 50, 95, onAc: false)).Throttled);

    [Fact]
    public void Drain_needs_an_unplugged_stretch()
    {
        Assert.Null(StressSession.AnalyzeDrain(Run(120, 130, 80), null));
        var d = StressSession.AnalyzeDrain(Run(120, 100, 80, onAc: false, watts: 30), null);
        Assert.NotNull(d);
        Assert.Equal(30, d.AverageWatts, 1);
        Assert.Equal(3, d.PercentDrop);
    }

    private static StressReport Report(ThrottleVerdict throttle, double? maxTemp = 85, long memErrors = 0, int scanErrors = 0,
        bool cancelled = false, int memPasses = 3) =>
        new(TimeSpan.FromMinutes(5), cancelled, Run(300, 120, 80), maxTemp, 75, false, throttle,
            new MemoryTestResult(8L << 30, memPasses, memErrors, TimeSpan.FromMinutes(5), cancelled), null,
            new SurfaceScanResult(0, 500_000_000_000, 4000, scanErrors, [], 1500, 900, TimeSpan.FromMinutes(5), cancelled), null, null);

    [Fact]
    public void Healthy_run_passes_everything()
    {
        var results = Evaluate.Stress(Report(new(false, null, 120))).ToList();
        Assert.All(results, r => Assert.Equal(TestOutcome.Pass, r.Outcome));
        Assert.Equal("Stable, 85 °C max. Held 120% of its base speed.", results.Single(r => r.Id == TestIds.Cpu).Detail);
    }

    [Fact]
    public void Faults_fail_the_right_tests()
    {
        var results = Evaluate.Stress(Report(new(true, "thermal", 55), 99, memErrors: 12, scanErrors: 2)).ToDictionary(r => r.Id);
        Assert.Equal(TestOutcome.Fail, results[TestIds.Cpu].Outcome);
        Assert.Equal(TestOutcome.Fail, results[TestIds.Memory].Outcome);
        Assert.Equal(TestOutcome.Fail, results[TestIds.SurfaceScan].Outcome);
        Assert.Contains("Overheating slowed the processor to 55% of its base speed (thermal throttling, 99 °C max)", results[TestIds.Cpu].Detail);
        Assert.StartsWith("12 errors found in 8 GB tested.", results[TestIds.Memory].Detail);
        Assert.StartsWith("2 unreadable areas out of", results[TestIds.SurfaceScan].Detail);
    }

    [Fact]
    public void A_single_fault_is_singular()
    {
        var results = Evaluate.Stress(Report(new(false, null, 120), memErrors: 1, scanErrors: 1)).ToDictionary(r => r.Id);
        Assert.StartsWith("1 error found", results[TestIds.Memory].Detail);
        Assert.StartsWith("1 unreadable area out of", results[TestIds.SurfaceScan].Detail);
    }

    [Fact]
    public void Power_limited_cpu_is_a_warning()
    {
        var cpu = Evaluate.Stress(Report(new(true, "power", 70))).First();
        Assert.Equal(TestOutcome.Warn, cpu.Outcome);
        Assert.StartsWith("Held only 70% of its base speed while cool (85 °C max).", cpu.Detail);
    }

    [Fact]
    public void Stopping_early_skips_instead_of_passing()
    {
        // Skip or Exit a few seconds in: nothing was proven either way.
        var results = Evaluate.Stress(Report(new(false, null, 120), cancelled: true, memPasses: 1)).ToDictionary(r => r.Id);
        Assert.All(results.Values, r => Assert.Equal(TestOutcome.Skipped, r.Outcome));
        Assert.All(results.Values, r => Assert.StartsWith("Stopped early", r.Detail));
    }

    [Fact]
    public void Stopping_early_still_reports_the_faults_found()
    {
        var results = Evaluate.Stress(Report(new(true, "thermal", 60), 97, memErrors: 3, scanErrors: 1, cancelled: true)).ToDictionary(r => r.Id);
        Assert.All(results.Values, r => Assert.Equal(TestOutcome.Fail, r.Outcome));
    }

    [Fact]
    public void Memory_stopped_during_allocation_is_skipped()
    {
        var memory = Evaluate.Stress(Report(new(false, null, 120), memPasses: 0)).Single(r => r.Id == TestIds.Memory);
        Assert.Equal(TestOutcome.Skipped, memory.Outcome);
    }

    [Fact]
    public void Speed_check_on_battery_says_why_it_was_skipped()
    {
        var onBattery = Report(new(false, null, null)) with { Samples = Run(300, 120, 80, onAc: false) };
        var cpu = Evaluate.Stress(onBattery).Single(r => r.Id == TestIds.Cpu);
        Assert.Equal(TestOutcome.Pass, cpu.Outcome);
        Assert.Equal("Stable, 85 °C max. Speed not checked because it ran on battery.", cpu.Detail);
    }

    [Fact]
    public void Skipped_scan_keeps_its_reason()
    {
        var r = Report(new(false, null, 110)) with { Scan = null, ScanSkipped = "Needs administrator rights." };
        var scan = Evaluate.Stress(r).Single(x => x.Id == TestIds.SurfaceScan);
        Assert.Equal(TestOutcome.Skipped, scan.Outcome);
        Assert.Equal("Needs administrator rights.", scan.Detail);
    }

    [Theory]
    [InlineData(3500, TestOutcome.Pass)]
    [InlineData(80, TestOutcome.Warn)]
    public void Disk_speed(double read, TestOutcome expected) =>
        Assert.Equal(expected, Evaluate.DiskSpeed(new DiskBenchmarkResult(1000, read, 20000, 1L << 30)).Outcome);

    private static WifiInfo Wifi(bool connected = true, int visible = 5, bool? radio = true) =>
        new(true, "Intel AX211", radio, true, connected, connected ? "Home" : null, 80, "Wi-Fi 6 (802.11ax)", 866, visible, ["5 GHz"], false);

    private static PingResult Ping(bool internet) =>
        new([new PingTarget("Gateway", "192.168.1.1", internet, 2, null), new PingTarget("Google DNS", "8.8.8.8", internet, 14, null)]);

    [Fact]
    public void Online_over_wifi_passes()
    {
        var r = Evaluate.Network(Ping(true), Wifi(), false);
        Assert.Equal(TestOutcome.Pass, r.Outcome);
        Assert.Contains("Home", r.Detail);
    }

    [Fact]
    public void Offline_but_seeing_networks_is_a_warning() =>
        Assert.Equal(TestOutcome.Warn, Evaluate.Network(Ping(false), Wifi(connected: false), false).Outcome);

    [Fact]
    public void Deaf_wifi_fails() =>
        Assert.Equal(TestOutcome.Fail, Evaluate.Network(Ping(false), Wifi(connected: false, visible: 0), false).Outcome);

    [Fact]
    public void Missing_adapter_fails() =>
        Assert.Equal(TestOutcome.Fail, Evaluate.Network(Ping(false), null, false).Outcome);

    [Fact]
    public void Test_run_replaces_a_rerun()
    {
        var run = new TestRun();
        run.Record(new(TestIds.Keyboard, "Keyboard", TestOutcome.Fail));
        run.Record(new(TestIds.Keyboard, "Keyboard", TestOutcome.Pass));
        Assert.Single(run.Results);
        Assert.Equal(TestOutcome.Pass, run[TestIds.Keyboard]!.Outcome);
    }
}

public class TestRunMergeTests
{
    private static StressReport Stress(bool cancelled) =>
        new(TimeSpan.FromMinutes(cancelled ? 0.1 : 5), cancelled, [], null, null, false, new(false, null, null), null, null, null, null, null);

    [Fact]
    public void Skipping_a_rerun_keeps_the_earlier_result()
    {
        var run = new TestRun();
        run.Record(new(TestIds.Keyboard, "Keyboard", TestOutcome.Fail, "Not working: F7."));
        run.Record(new(TestIds.Keyboard, "Keyboard", TestOutcome.Skipped));
        Assert.Equal(TestOutcome.Fail, run[TestIds.Keyboard]!.Outcome);

        run.Record(new(TestIds.Webcam, "Camera", TestOutcome.Skipped));
        run.Record(new(TestIds.Webcam, "Camera", TestOutcome.Skipped, "Skipped again."));
        Assert.Equal("Skipped again.", run[TestIds.Webcam]!.Detail);
    }

    [Fact]
    public void Automatic_run_skipping_a_step_doesnt_erase_the_manual_fault()
    {
        var session = new TestRun();
        session.Record(new(TestIds.Keyboard, "Keyboard", TestOutcome.Fail, "Not working: F7, Right Shift."));
        var auto = new TestRun();
        auto.Record(new(TestIds.Keyboard, "Keyboard", TestOutcome.Skipped));
        auto.Record(new(TestIds.Webcam, "Camera", TestOutcome.Pass));

        session.MergeFrom(auto);

        Assert.Equal(TestOutcome.Fail, session[TestIds.Keyboard]!.Outcome);
        Assert.Equal(TestOutcome.Pass, session[TestIds.Webcam]!.Outcome);
    }

    [Fact]
    public void Stopped_stress_run_doesnt_replace_a_complete_one()
    {
        var complete = Stress(cancelled: false);
        var session = new TestRun();
        session.RecordStress(complete);

        var auto = new TestRun();
        auto.RecordStress(Stress(cancelled: true));
        session.MergeFrom(auto);
        Assert.Same(complete, session.Stress);

        var newer = Stress(cancelled: false);
        session.RecordStress(newer);
        Assert.Same(newer, session.Stress);
    }

    [Fact]
    public void Stopped_stress_run_is_kept_when_there_is_nothing_better()
    {
        var run = new TestRun();
        var first = Stress(cancelled: true);
        run.RecordStress(first);
        Assert.Same(first, run.Stress);

        var second = Stress(cancelled: true);
        run.RecordStress(second);
        Assert.Same(second, run.Stress);
    }
}

public class StressPartsTests
{
    private static readonly List<StressSample> Samples =
        Enumerable.Range(0, 60).Select(i => new StressSample(TimeSpan.FromSeconds(i), 100, 80, false, null, 120, null, true, 90, null)).ToList();

    [Fact]
    public void Only_selected_parts_produce_results()
    {
        var report = new StressReport(TimeSpan.FromMinutes(1), false, Samples, 80, 78, false, new(false, null, 120),
            null, null, null, null, null, StressParts.Cpu);
        var ids = Evaluate.Stress(report).Select(r => r.Id).ToList();
        Assert.Equal([TestIds.Cpu], ids);
    }

    [Fact]
    public void Stopped_cpu_only_run_skips_only_the_cpu()
    {
        var report = new StressReport(TimeSpan.FromMinutes(1), true, Samples, 80, 78, false, new(false, null, 120),
            null, null, null, null, null, StressParts.Cpu);
        var result = Assert.Single(Evaluate.Stress(report));
        Assert.Equal(TestIds.Cpu, result.Id);
        Assert.Equal(TestOutcome.Skipped, result.Outcome);
    }
}

public class StressSessionTests
{
    // No parts selected: the session only samples, so these tests put no load on the machine.
    private const StressParts NoLoad = 0;

    [Fact]
    public async Task Reaching_the_end_of_the_time_is_not_cancelled()
    {
        var report = await new StressSession(null, null, TimeSpan.FromSeconds(1.5), NoLoad).RunAsync();
        Assert.False(report.Cancelled);
        Assert.NotEmpty(report.Samples);
    }

    [Fact]
    public async Task Skipping_marks_the_report_cancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        var report = await new StressSession(null, null, TimeSpan.FromMinutes(5), NoLoad).RunAsync(cts.Token);
        Assert.True(report.Cancelled);
        Assert.True(report.Duration < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task A_failure_while_sampling_is_passed_on()
    {
        var session = new StressSession(null, null, TimeSpan.FromMinutes(5), NoLoad);
        session.Progress += _ => throw new InvalidOperationException("boom");
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync());
    }
}
