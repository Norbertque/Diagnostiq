using Diagnostiq.Core.Interop;
using LibreHardwareMonitor.Hardware;

namespace Diagnostiq.Core.Sensors;

public sealed record FanReading(string Name, int Rpm);

/// <param name="CpuTempLimited">Temperature comes from ACPI thermal zones (often a chassis sensor, not the CPU die).</param>
/// <param name="CpuPerformancePercent">Current performance vs. nominal clock; above 100 with turbo, a drop under load means throttling.</param>
public sealed record SensorReading(
    double? CpuTempC,
    bool CpuTempLimited,
    double? GpuTempC,
    IReadOnlyList<FanReading> Fans,
    double? CpuLoadPercent,
    double? CpuClockMHz,
    double? CpuPerformancePercent);

/// <summary>
/// Live sensors. With the PawnIO driver installed, LibreHardwareMonitor reads the CPU die
/// temperature and embedded-controller fans. Without it, temperature falls back to ACPI
/// thermal zones (labelled "limited"). Load and clock always come from Windows performance
/// counters, which need no driver, so throttling is detectable on every machine.
/// </summary>
public sealed class SensorService : IDisposable
{
    private Computer? _lhm;
    private PdhQuery? _pdh;
    private IntPtr? _load, _perf, _freq, _zones;

    public bool UsingPawnIo { get; private set; }

    /// <summary>Slow (loads drivers/vendor libraries): call from a background thread.</summary>
    public void Open()
    {
        _pdh = new PdhQuery();
        _load = _pdh.Add(@"\Processor Information(_Total)\% Processor Utility") ?? _pdh.Add(@"\Processor Information(_Total)\% Processor Time");
        _perf = _pdh.Add(@"\Processor Information(_Total)\% Processor Performance");
        _freq = _pdh.Add(@"\Processor Information(_Total)\Processor Frequency");
        _zones = _pdh.Add(@"\Thermal Zone Information(*)\High Precision Temperature");
        _pdh.Collect(); // rate counters need a first sample
        OpenHardware();
    }

    /// <summary>Re-attaches LibreHardwareMonitor, e.g. right after PawnIO was installed. Background thread.</summary>
    public void ReopenHardware()
    {
        try { _lhm?.Close(); } catch { }
        _lhm = null;
        OpenHardware();
    }

    private void OpenHardware()
    {
        try
        {
            _lhm = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true, IsControllerEnabled = true };
            _lhm.Open();
            UsingPawnIo = PawnIoSetup.State == PawnIoState.Installed;
        }
        catch (Exception)
        {
            _lhm = null; // no driver / blocked: counters + thermal zones still work
            UsingPawnIo = false;
        }
    }

    public SensorReading Read()
    {
        _pdh?.Collect();
        double? load = _load is { } l ? PdhQuery.Value(l) : null;
        double? perf = _perf is { } p ? PdhQuery.Value(p) : null;
        double? nominal = _freq is { } f ? PdhQuery.Value(f) : null;
        double? clock = perf is not null && nominal is > 0 ? nominal * perf / 100 : null;

        double? cpuTemp = null, gpuTemp = null;
        var fans = new List<FanReading>();
        if (_lhm is not null)
        {
            foreach (var hw in _lhm.Hardware)
            {
                hw.Update();
                foreach (var sub in hw.SubHardware) sub.Update();
                switch (hw.HardwareType)
                {
                    case HardwareType.Cpu:
                        cpuTemp ??= CpuPackage(hw);
                        break;
                    case HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel:
                        gpuTemp ??= hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value > 0)?.Value;
                        break;
                }
                foreach (var s in hw.Sensors.Concat(hw.SubHardware.SelectMany(x => x.Sensors)))
                    if (s.SensorType == SensorType.Fan && s.Value is > 0)
                        fans.Add(new FanReading(s.Name, (int)s.Value.Value));
            }
        }

        bool limited = false;
        if (cpuTemp is null && _zones is { } z)
        {
            // Tenths of Kelvin; the hottest zone is the best CPU proxy available without a driver.
            var zones = PdhQuery.Values(z).Select(v => v.Value / 10 - 273.15).Where(c => c is > 5 and < 125).ToList();
            if (zones.Count > 0) { cpuTemp = zones.Max(); limited = true; }
        }

        return new SensorReading(cpuTemp, limited, gpuTemp, fans, load, clock, perf);
    }

    private static double? CpuPackage(IHardware cpu)
    {
        var temps = cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0 and < 125).ToList();
        var package = temps.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
                                              || s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
                                              || s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase));
        return package?.Value ?? (temps.Count > 0 ? temps.Max(s => s.Value) : null);
    }

    public void Dispose()
    {
        try { _lhm?.Close(); } catch { }
        _lhm = null;
        _pdh?.Dispose();
        _pdh = null;
    }
}
