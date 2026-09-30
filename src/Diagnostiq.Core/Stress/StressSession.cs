using System.Diagnostics;
using Diagnostiq.Core.Hardware;
using Diagnostiq.Core.Sensors;
using Diagnostiq.Core.Storage;

namespace Diagnostiq.Core.Stress;

/// <summary>One telemetry point, taken every second during the stress run.</summary>
public sealed record StressSample(
    TimeSpan At,
    double? CpuLoadPercent,
    double? CpuTempC,
    bool TempLimited,
    double? ClockMHz,
    double? PerformancePercent,
    int? FanRpm,
    bool? OnAcPower,
    int? BatteryPercent,
    double? DischargeWatts);

public sealed record StressProgress(TimeSpan Elapsed, TimeSpan Duration, StressSample Latest, MemoryTestProgress? Memory, ScanProgress? Scan);

/// <param name="SustainedPerformancePercent">Median clock as % of base clock after warm-up, under load, on AC.</param>
/// <param name="Reason">"thermal" (hot while slowing down) or "power" (slow while cool: power limit, weak charger, VRM).</param>
public sealed record ThrottleVerdict(bool Throttled, string? Reason, double? SustainedPerformancePercent);

/// <param name="EstimatedRuntime">How long a full battery would last at this load.</param>
public sealed record DrainResult(double AverageWatts, int PercentDrop, TimeSpan Measured, TimeSpan? EstimatedRuntime);

public sealed record StressReport(
    TimeSpan Duration,
    bool Cancelled,
    IReadOnlyList<StressSample> Samples,
    double? MaxTempC,
    double? AverageTempC,
    bool TempLimited,
    ThrottleVerdict Throttle,
    MemoryTestResult? Memory,
    string? MemorySkipped,
    SurfaceScanResult? Scan,
    string? ScanSkipped,
    DrainResult? Drain,
    StressParts Parts = StressParts.All);

/// <summary>Which parts of the burn-in run. Automatic mode runs all three together.</summary>
[Flags]
public enum StressParts { Cpu = 1, Memory = 2, DiskScan = 4, All = Cpu | Memory | DiskScan }

/// <summary>
/// The burn-in: CPU stress, memory pattern test and disk surface scan run at the same time
/// for a fixed duration, because faults tend to show under combined heat and load.
/// Telemetry is sampled every second for the live dashboard and throttling analysis. If
/// the charger is pulled during the run, the unplugged stretch doubles as a battery drain test.
/// </summary>
public sealed class StressSession(SensorService? sensors, int? scanDisk, TimeSpan duration, StressParts parts = StressParts.All)
{
    private static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(1);

    private volatile MemoryTestProgress? _memory;
    private volatile ScanProgress? _scan;

    /// <summary>About once a second, on a background thread.</summary>
    public event Action<StressProgress>? Progress;

    public TimeSpan Duration => duration;

    public async Task<StressReport> RunAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var samples = new List<StressSample>();
        using var cpu = new CpuStress();
        if (parts.HasFlag(StressParts.Cpu)) cpu.Start();

        var memoryTask = parts.HasFlag(StressParts.Memory)
            ? MemoryPatternTest.RunAsync(duration, progress: new Callback<MemoryTestProgress>(p => _memory = p), ct: ct)
            : null;
        Task<SurfaceScanResult>? scanTask = null;
        string? scanSkipped = null;
        if (parts.HasFlag(StressParts.DiskScan))
        {
            if (scanDisk is { } disk) scanTask = SurfaceScan.RunAsync(disk, duration, new Callback<ScanProgress>(p => _scan = p), ct);
            else scanSkipped = "No internal system disk to scan.";
        }

        int tick = 0;
        BatteryLive? battery = BatteryLiveProbe.Read();
        try
        {
            while (sw.Elapsed < duration && !ct.IsCancellationRequested)
            {
                try { await Task.Delay(SampleEvery, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }

                if (tick++ % 2 == 0) battery = TryReadBattery() ?? battery;   // WMI; every 2 s is plenty
                var reading = sensors?.Read();
                var sample = new StressSample(sw.Elapsed,
                    reading?.CpuLoadPercent, reading?.CpuTempC, reading?.CpuTempLimited ?? false,
                    reading?.CpuClockMHz, reading?.CpuPerformancePercent,
                    reading?.Fans.Count > 0 ? reading.Fans.Max(f => f.Rpm) : null,
                    battery?.OnAcPower, battery?.ChargePercent, battery?.DischargeWatts);
                samples.Add(sample);
                Progress?.Invoke(new StressProgress(sw.Elapsed, duration, sample, _memory, _scan));
            }
        }
        finally
        {
            cpu.Stop();
        }

        MemoryTestResult? memory = null;
        string? memorySkipped = null;
        if (memoryTask is not null)
        {
            try { memory = await memoryTask.ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { memorySkipped = ex.Message; }
        }

        SurfaceScanResult? scan = null;
        if (scanTask is not null)
        {
            try { scan = await scanTask.ConfigureAwait(false); }
            catch (UnauthorizedAccessException) { scanSkipped = "Needs administrator rights."; }
            catch (IOException ex) { scanSkipped = ex.Message; }
        }

        var temps = samples.Where(s => s.CpuTempC is not null).Select(s => s.CpuTempC!.Value).ToList();
        return new StressReport(sw.Elapsed, ct.IsCancellationRequested, samples,
            temps.Count > 0 ? temps.Max() : null,
            temps.Count > 0 ? temps.Average() : null,
            samples.Any(s => s.TempLimited),
            AnalyzeThrottle(samples),
            memory, memorySkipped, scan, scanSkipped,
            AnalyzeDrain(samples, battery), parts);
    }

    private static BatteryLive? TryReadBattery()
    {
        try { return BatteryLiveProbe.Read(); }
        catch (Exception ex) when (ex is System.Management.ManagementException or System.Runtime.InteropServices.COMException) { return null; }
    }

    /// <summary>
    /// Turbo lifts clocks well above base for the first seconds, so the first 20 % is ignored.
    /// Sustained clocks below 90 % of base under full load on AC mean the laptop can't hold its
    /// rated speed: hot = thermal throttling (dried paste, clogged fan), cool = power limiting.
    /// </summary>
    public static ThrottleVerdict AnalyzeThrottle(IReadOnlyList<StressSample> samples)
    {
        if (samples.Count == 0) return new(false, null, null);
        var steady = samples.Skip(samples.Count / 5)
            .Where(s => s.CpuLoadPercent >= 80 && s.PerformancePercent is not null && s.OnAcPower != false)
            .ToList();
        if (steady.Count < 5) return new(false, null, null);

        var perf = steady.Select(s => s.PerformancePercent!.Value).Order().ToList();
        double median = perf[perf.Count / 2];
        if (median >= 90) return new(false, null, median);

        double? hottest = steady.Max(s => s.CpuTempC);
        return new(true, hottest >= 90 ? "thermal" : "power", median);
    }

    /// <summary>The unplugged stretch of the run: average draw, charge lost, runtime at this load.</summary>
    public static DrainResult? AnalyzeDrain(IReadOnlyList<StressSample> samples, BatteryLive? last)
    {
        var unplugged = samples.Where(s => s.OnAcPower == false && s.DischargeWatts > 0).ToList();
        if (unplugged.Count < 10) return null;   // under ~20 s of data isn't a measurement

        double watts = unplugged.Average(s => s.DischargeWatts!.Value);
        int drop = (unplugged[0].BatteryPercent ?? 0) - (unplugged[^1].BatteryPercent ?? 0);
        TimeSpan? runtime = last?.RemainingMWh is > 0 && last.ChargePercent is > 0
            ? TimeSpan.FromHours(last.RemainingMWh.Value / (last.ChargePercent.Value / 100.0) / 1000.0 / watts)
            : null;
        return new DrainResult(watts, Math.Max(drop, 0), unplugged[^1].At - unplugged[0].At, runtime);
    }

    // Progress<T> would post to the UI thread; these callbacks just store the latest value.
    private sealed class Callback<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
