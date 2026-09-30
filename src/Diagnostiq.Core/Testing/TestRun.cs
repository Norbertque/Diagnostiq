using Diagnostiq.Core.Network;
using Diagnostiq.Core.Storage;
using Diagnostiq.Core.Stress;

namespace Diagnostiq.Core.Testing;

public enum TestOutcome { Pass, Warn, Fail, Skipped }

public sealed record TestResult(string Id, string Title, TestOutcome Outcome, string? Detail = null);

/// <summary>Stable ids: scoring and the report key off these, the titles are for display.</summary>
public static class TestIds
{
    public const string Network = "network";
    public const string Cpu = "cpu";
    public const string Memory = "memory";
    public const string SurfaceScan = "surface";
    public const string DiskSpeed = "diskspeed";
    public const string BatteryDrain = "battery.drain";
    public const string Display = "display";
    public const string Brightness = "brightness";
    public const string Keyboard = "keyboard";
    public const string Touchpad = "touchpad";
    public const string Speakers = "speakers";
    public const string Headphones = "headphones";
    public const string Microphone = "microphone";
    public const string Webcam = "webcam";
    public const string Usb = "usb";
    public const string Charger = "charger";
}

/// <summary>Everything one Automatic or Manual session measured. Re-running a test replaces its result.</summary>
public sealed class TestRun
{
    private readonly List<TestResult> _results = [];
    private readonly object _lock = new();

    public DateTimeOffset Started { get; } = DateTimeOffset.Now;
    public StressReport? Stress { get; set; }
    public DiskBenchmarkResult? Benchmark { get; set; }
    public PingResult? Ping { get; set; }
    public WifiInfo? WifiScan { get; set; }

    public event Action<TestResult>? Recorded;

    public IReadOnlyList<TestResult> Results
    {
        get { lock (_lock) return _results.ToList(); }
    }

    public TestResult? this[string id]
    {
        get { lock (_lock) return _results.FirstOrDefault(r => r.Id == id); }
    }

    /// <summary>Takes over another run's results and measurements (an Automatic run joining the session).</summary>
    public void MergeFrom(TestRun other)
    {
        foreach (var r in other.Results) Record(r);
        Stress = other.Stress ?? Stress;
        Benchmark = other.Benchmark ?? Benchmark;
        Ping = other.Ping ?? Ping;
        WifiScan = other.WifiScan ?? WifiScan;
    }

    public void Record(TestResult result)
    {
        lock (_lock)
        {
            int i = _results.FindIndex(r => r.Id == result.Id);
            if (i >= 0) _results[i] = result; else _results.Add(result);
        }
        Recorded?.Invoke(result);
    }
}
