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

    private static StressReport Report(ThrottleVerdict throttle, double? maxTemp = 85, long memErrors = 0, int scanErrors = 0) =>
        new(TimeSpan.FromMinutes(5), false, Run(300, 120, 80), maxTemp, 75, false, throttle,
            new MemoryTestResult(8L << 30, 3, memErrors, TimeSpan.FromMinutes(5), false), null,
            new SurfaceScanResult(0, 500_000_000_000, 4000, scanErrors, [], 1500, 900, TimeSpan.FromMinutes(5), false), null, null);

    [Fact]
    public void Healthy_run_passes_everything()
    {
        var results = Evaluate.Stress(Report(new(false, null, 120))).ToList();
        Assert.All(results, r => Assert.Equal(TestOutcome.Pass, r.Outcome));
        Assert.Contains("120% of base clock", results.Single(r => r.Id == TestIds.Cpu).Detail);
    }

    [Fact]
    public void Faults_fail_the_right_tests()
    {
        var results = Evaluate.Stress(Report(new(true, "thermal", 55), 99, memErrors: 12, scanErrors: 2)).ToDictionary(r => r.Id);
        Assert.Equal(TestOutcome.Fail, results[TestIds.Cpu].Outcome);
        Assert.Equal(TestOutcome.Fail, results[TestIds.Memory].Outcome);
        Assert.Equal(TestOutcome.Fail, results[TestIds.SurfaceScan].Outcome);
    }

    [Fact]
    public void Power_limited_cpu_is_a_warning() =>
        Assert.Equal(TestOutcome.Warn, Evaluate.Stress(Report(new(true, "power", 70))).First().Outcome);

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
