using Diagnostiq.Core.Network;
using Diagnostiq.Core.Storage;
using Diagnostiq.Core.Stress;

namespace Diagnostiq.Core.Testing;

/// <summary>Turns raw measurements into test results with plain-language details.</summary>
public static class Evaluate
{
    /// <summary>Sequential reads below this are slow even for a hard drive in good shape.</summary>
    public const double SlowReadMBps = 100;

    public static IEnumerable<TestResult> Stress(StressReport r)
    {
        bool ran = r.Samples.Count >= 10;
        yield return Cpu(r, ran);

        if (r.Memory is { } m && m.TestedBytes > 0)
        {
            string gb = $"{m.TestedBytes / (double)(1L << 30):0.#} GB";
            yield return m.Errors > 0
                ? new(TestIds.Memory, "Memory", TestOutcome.Fail, $"{m.Errors:N0} errors found in {gb} tested. A memory module is faulty.")
                : new(TestIds.Memory, "Memory", TestOutcome.Pass, $"{gb} tested over {m.Passes} {(m.Passes == 1 ? "pass" : "passes")}, no errors.");
        }
        else
            yield return new(TestIds.Memory, "Memory", TestOutcome.Skipped, r.MemorySkipped ?? "Didn't run.");

        if (r.Scan is { } s && s.ChunksRead > 0)
            yield return s.Errors > 0
                ? new(TestIds.SurfaceScan, "Disk surface scan", TestOutcome.Fail, $"{s.Errors} unreadable areas out of {s.ChunksRead:N0} read. The drive has bad sectors.")
                : new(TestIds.SurfaceScan, "Disk surface scan", TestOutcome.Pass, $"{s.ChunksRead:N0} areas read across the whole disk, no errors ({s.AvgMBps:0} MB/s average).");
        else
            yield return new(TestIds.SurfaceScan, "Disk surface scan", TestOutcome.Skipped, r.ScanSkipped ?? "Didn't run.");

        if (r.Drain is { } d)
            yield return new(TestIds.BatteryDrain, "Battery under load", TestOutcome.Pass,
                $"{d.AverageWatts:0.0} W under full load" + (d.EstimatedRuntime is { } t ? $", about {Hours(t)} on a full charge." : "."));
    }

    private static TestResult Cpu(StressReport r, bool ran)
    {
        const string title = "Processor under load";
        if (!ran) return new(TestIds.Cpu, title, TestOutcome.Skipped, "Stopped before enough data was collected.");

        string temp = r.MaxTempC is { } max ? $"{max:0} °C max{(r.TempLimited ? " (approximate sensor)" : "")}" : "temperature not available";
        string clock = r.Throttle.SustainedPerformancePercent is { } p ? $"{p:0}% of base clock sustained" : "clock not measured";

        if (r.Throttle is { Throttled: true, Reason: "thermal" })
            return new(TestIds.Cpu, title, TestOutcome.Fail, $"Thermal throttling: {clock} at {temp}. Clean the fan and renew the thermal paste.");
        if (r.MaxTempC >= 100 && !r.TempLimited)
            return new(TestIds.Cpu, title, TestOutcome.Fail, $"Overheating: reached {r.MaxTempC:0} °C.");
        if (r.Throttle is { Throttled: true })
            return new(TestIds.Cpu, title, TestOutcome.Warn, $"Held only {clock} while cool ({temp}). Check the charger and power settings.");
        return new(TestIds.Cpu, title, TestOutcome.Pass, $"Stable. {char.ToUpperInvariant(clock[0])}{clock[1..]}, {temp}.");
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
