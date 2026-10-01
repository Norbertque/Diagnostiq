using Diagnostiq.Core.Network;
using Diagnostiq.Core.Storage;
using Diagnostiq.Core.Stress;

namespace Diagnostiq.Core.Testing;

/// <summary>Turns raw measurements into test results with plain-language details.</summary>
public static class Evaluate
{
    /// <summary>Sequential reads below this are slow even for a hard drive in good shape.</summary>
    public const double SlowReadMBps = 100;

    /// <summary>
    /// Results only for the parts that were selected, so a CPU-only run doesn't overwrite an earlier memory result.
    /// A run stopped early (Skip or Exit) still reports the faults it found, but a clean partial run is Skipped, not Pass.
    /// </summary>
    public static IEnumerable<TestResult> Stress(StressReport r)
    {
        if (r.Parts.HasFlag(StressParts.Cpu)) yield return Cpu(r, ran: r.Samples.Count >= 10);
        if (r.Parts.HasFlag(StressParts.Memory)) yield return Memory(r);
        if (r.Parts.HasFlag(StressParts.DiskScan)) yield return Scan(r);
        if (r.Drain is { } d)
            yield return new(TestIds.BatteryDrain, "Battery under load", TestOutcome.Pass,
                $"{d.AverageWatts:0.0} W under full load" + (d.EstimatedRuntime is { } t ? $", about {Hours(t)} on a full charge." : "."));
    }

    private static TestResult Memory(StressReport r)
    {
        const string title = "Memory";
        if (r.Memory is not { TestedBytes: > 0 } m)
            return new(TestIds.Memory, title, TestOutcome.Skipped, r.MemorySkipped ?? "Didn't run.");
        string gb = $"{m.TestedBytes / (double)(1L << 30):0.#} GB";
        if (m.Errors > 0)
            return new(TestIds.Memory, title, TestOutcome.Fail,
                $"{m.Errors:N0} error{(m.Errors == 1 ? "" : "s")} found in {gb} tested. The memory is faulty and needs replacing.");
        if (m.Passes == 0)
            return new(TestIds.Memory, title, TestOutcome.Skipped, "Stopped before any memory was checked.");
        if (r.Cancelled)   // a part stopped after the full time has done its job
            return new(TestIds.Memory, title, TestOutcome.Skipped, $"Stopped early: {gb} checked, no errors so far.");
        return new(TestIds.Memory, title, TestOutcome.Pass, $"{gb} tested over {m.Passes} {(m.Passes == 1 ? "pass" : "passes")}, no errors.");
    }

    private static TestResult Scan(StressReport r)
    {
        const string title = "Disk surface scan";
        if (r.Scan is not { ChunksRead: > 0 } s)
            return new(TestIds.SurfaceScan, title, TestOutcome.Skipped, r.ScanSkipped ?? "Didn't run.");
        if (s.Errors > 0)
            return new(TestIds.SurfaceScan, title, TestOutcome.Fail,
                $"{s.Errors:N0} unreadable area{(s.Errors == 1 ? "" : "s")} out of {s.ChunksRead:N0} read. " +
                "The drive has bad sectors: back up your data and replace the drive.");
        if (r.Cancelled)
            return new(TestIds.SurfaceScan, title, TestOutcome.Skipped, $"Stopped early: {s.ChunksRead:N0} areas read, no errors so far.");
        return new(TestIds.SurfaceScan, title, TestOutcome.Pass, $"{s.ChunksRead:N0} areas read across the whole disk, no errors ({s.AvgMBps:0} MB/s average).");
    }

    private static TestResult Cpu(StressReport r, bool ran)
    {
        const string title = "Processor under load";
        if (!ran) return new(TestIds.Cpu, title, TestOutcome.Skipped, "Stopped before enough data was collected.");

        string temp = r.MaxTempC is { } max ? $"{max:0} °C max{(r.TempLimited ? ", approximate sensor" : "")}" : "temperature not available";
        double? held = r.Throttle.SustainedPerformancePercent;

        if (r.Throttle is { Throttled: true, Reason: "thermal" })
            return new(TestIds.Cpu, title, TestOutcome.Fail,
                $"Overheating slowed the processor to {held:0}% of its base speed (thermal throttling, {temp}). " +
                "Clean the fan and vents and replace the thermal paste.");
        if (r.MaxTempC >= 100 && !r.TempLimited)
            return new(TestIds.Cpu, title, TestOutcome.Fail,
                $"Overheated, reaching {r.MaxTempC:0} °C. Clean the fan and vents and replace the thermal paste.");
        if (r.Throttle is { Throttled: true })
            return new(TestIds.Cpu, title, TestOutcome.Warn,
                $"Held only {held:0}% of its base speed while cool ({temp}). " +
                "Check that the laptop's own charger is plugged in and Windows isn't set to a power-saving mode.");
        if (r.Cancelled)
            return new(TestIds.Cpu, title, TestOutcome.Skipped, $"Stopped early, stable so far ({temp}).");

        // The speed check only uses samples on AC, so a run on battery has no verdict on speed.
        string speed = held is { } p ? $"Held {p:0}% of its base speed."
            : r.Samples.Any(s => s.OnAcPower == false && s.PerformancePercent is not null) ? "Speed not checked because it ran on battery."
            : "Speed couldn't be measured.";
        return new(TestIds.Cpu, title, TestOutcome.Pass, $"Stable, {temp}. {speed}");
    }

    public static TestResult DiskSpeed(DiskBenchmarkResult b)
    {
        string detail = $"Read {b.SeqReadMBps:0} MB/s, write {b.SeqWriteMBps:0} MB/s, {b.RandomRead4kIops:N0} random reads per second.";
        return b.SeqReadMBps < SlowReadMBps
            ? new(TestIds.DiskSpeed, "Disk speed", TestOutcome.Warn, "Slow. " + detail)
            : new(TestIds.DiskSpeed, "Disk speed", TestOutcome.Pass, detail);
    }

    public static TestResult Network(PingResult ping, WifiInfo? wifi, bool ethernetConnected)
    {
        const string title = "Wi-Fi and internet";
        long? rtt = ping.Targets.Where(t => t.Reachable && t.Label != "Gateway").Min(t => t.RoundTripMs);
        string seen = wifi is { NetworksVisible: > 0 } w ? $" The Wi-Fi adapter sees {w.NetworksVisible} network{(w.NetworksVisible == 1 ? "" : "s")}." : "";

        if (wifi is not { AdapterPresent: true })
            return ping.InternetReachable && ethernetConnected
                ? new(TestIds.Network, title, TestOutcome.Warn, "Online through Ethernet, but no Wi-Fi adapter was found.")
                : new(TestIds.Network, title, TestOutcome.Fail, "No Wi-Fi adapter found.");
        if (wifi.RadioOn == false)
            return new(TestIds.Network, title, TestOutcome.Warn, "Wi-Fi is switched off" + (wifi.HardwareSwitchOn == false ? " at a hardware switch or Fn key." : "."));
        if (ping.InternetReachable)
            return new(TestIds.Network, title, TestOutcome.Pass,
                $"Online{(wifi.Connected && wifi.Ssid is { } ssid ? $" through {ssid}" : ethernetConnected ? " through Ethernet" : "")}" +
                $"{(rtt is { } ms ? $", {ms} ms" : "")}.{seen}");
        if (wifi.NetworksVisible > 0 || wifi.NamesHidden)
            return new(TestIds.Network, title, TestOutcome.Warn, "Not connected to the internet, but the Wi-Fi radio works." + seen);
        return new(TestIds.Network, title, TestOutcome.Fail, "The Wi-Fi adapter doesn't see any networks.");
    }

    public static string Hours(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : $"{Math.Max(1, (int)t.TotalMinutes)} min";
}
