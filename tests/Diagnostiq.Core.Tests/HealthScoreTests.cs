using Diagnostiq.Core.Scoring;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.Core.Tests;

public class HealthScoreTests
{
    private static TestResult R(string id, TestOutcome o) => new(id, id, o, $"{id} detail");

    private static HealthInputs Inputs(IEnumerable<TestResult>? results = null, double? battery = 95, CheckState? storage = CheckState.Pass,
        int drivers = 0, bool noBluetooth = false) =>
        new((results ?? HealthScore.Expected.Select(e => R(e.Id, TestOutcome.Pass))).ToList(), battery, storage, null, drivers, noBluetooth);

    [Fact]
    public void Perfect_laptop_is_excellent()
    {
        var h = HealthScore.Compute(Inputs());
        Assert.Equal(100, h.Score);
        Assert.Equal(HealthVerdict.Excellent, h.Verdict);
        Assert.Empty(h.NotTested);
    }

    [Theory]
    [InlineData(TestIds.Keyboard)]
    [InlineData(TestIds.Memory)]
    [InlineData(TestIds.SurfaceScan)]
    [InlineData(TestIds.Display)]
    [InlineData(TestIds.Cpu)]
    public void Critical_failure_means_needs_repair_even_with_a_high_score(string id)
    {
        var results = HealthScore.Expected.Select(e => R(e.Id, e.Id == id ? TestOutcome.Fail : TestOutcome.Pass));
        var h = HealthScore.Compute(Inputs(results));
        Assert.Equal(70, h.Score);
        Assert.Equal(HealthVerdict.NeedsRepair, h.Verdict);
        Assert.True(h.HasCritical);
    }

    [Fact]
    public void Major_failure_costs_fifteen()
    {
        var results = HealthScore.Expected.Select(e => R(e.Id, e.Id == TestIds.Webcam ? TestOutcome.Fail : TestOutcome.Pass));
        var h = HealthScore.Compute(Inputs(results));
        Assert.Equal(85, h.Score);
        Assert.Equal(HealthVerdict.Excellent, h.Verdict);
    }

    [Fact]
    public void Warnings_and_minor_issues_cost_five_each()
    {
        var results = HealthScore.Expected.Select(e => R(e.Id, e.Id is TestIds.DiskSpeed or TestIds.Brightness ? TestOutcome.Warn : TestOutcome.Pass));
        var h = HealthScore.Compute(Inputs(results, drivers: 2, noBluetooth: true));
        Assert.Equal(80, h.Score);
        Assert.Equal(HealthVerdict.Ok, h.Verdict);
        Assert.Equal(4, h.Deductions.Count);
    }

    [Theory]
    [InlineData(35.0, HealthVerdict.NeedsRepair, 70)]
    [InlineData(50.0, HealthVerdict.Excellent, 85)]
    [InlineData(75.0, HealthVerdict.Excellent, 100)]
    public void Battery_wear(double health, HealthVerdict verdict, int score)
    {
        var h = HealthScore.Compute(Inputs(battery: health));
        Assert.Equal(score, h.Score);
        Assert.Equal(verdict, h.Verdict);
    }

    [Fact]
    public void Battery_wear_is_described_as_capacity()
    {
        var h = HealthScore.Compute(Inputs(battery: 35));
        Assert.Equal("Holds only 35% of its original capacity. Replace it.", h.Deductions.Single().Reason);
    }

    [Fact]
    public void Failing_drive_is_critical() =>
        Assert.Equal(HealthVerdict.NeedsRepair, HealthScore.Compute(Inputs(storage: CheckState.Fail)).Verdict);

    [Fact]
    public void Drive_warning_is_minor()
    {
        // Running hot or 80 % worn: worth knowing, but the drive works.
        var h = HealthScore.Compute(Inputs(storage: CheckState.Warn));
        Assert.Equal(95, h.Score);
        Assert.Equal(HealthVerdict.Excellent, h.Verdict);
        var d = Assert.Single(h.Deductions);
        Assert.Equal(Severity.Minor, d.Severity);
        Assert.Equal(HealthScore.Minor, d.Points);
    }

    [Fact]
    public void Nothing_tested_is_flagged()
    {
        Assert.True(HealthScore.Compute(Inputs([])).NothingTested);
        Assert.True(HealthScore.Compute(Inputs([R(TestIds.Keyboard, TestOutcome.Skipped)])).NothingTested);
        Assert.False(HealthScore.Compute(Inputs([R(TestIds.Keyboard, TestOutcome.Pass)])).NothingTested);
        Assert.False(HealthScore.Compute(Inputs()).NothingTested);
    }

    [Fact]
    public void Many_majors_drop_below_sixty()
    {
        var failed = new[] { TestIds.Speakers, TestIds.Touchpad, TestIds.Webcam };
        var results = HealthScore.Expected.Select(e => R(e.Id, failed.Contains(e.Id) ? TestOutcome.Fail : TestOutcome.Pass));
        var h = HealthScore.Compute(Inputs(results, battery: 50));   // 3 × 15 + 15
        Assert.Equal(40, h.Score);
        Assert.Equal(HealthVerdict.NeedsRepair, h.Verdict);
    }

    [Fact]
    public void Skipped_and_untested_are_listed_not_penalised()
    {
        var results = new[] { R(TestIds.Keyboard, TestOutcome.Pass), R(TestIds.Webcam, TestOutcome.Skipped) };
        var h = HealthScore.Compute(Inputs(results));
        Assert.Equal(100, h.Score);
        Assert.Single(h.Skipped);
        Assert.Equal(HealthScore.Expected.Length - 2, h.NotTested.Count);
    }
}
