using Diagnostiq.Core.Interop;
using Diagnostiq.Core.Probing;

namespace Diagnostiq.Core.Hardware;

public sealed record BatteryInfo(
    bool Present,
    int BatteryCount,
    int? ChargePercent,
    bool? OnAcPower,
    long? DesignCapacityMWh,
    long? FullChargeCapacityMWh,
    int? CycleCount)
{
    /// <summary>Full-charge capacity as a share of design capacity (wear = 100 − this).</summary>
    public double? HealthPercent =>
        DesignCapacityMWh is > 0 && FullChargeCapacityMWh is > 0
            ? Math.Min(100.0 * FullChargeCapacityMWh.Value / DesignCapacityMWh.Value, 100.0)
            : null;
}

public static class BatteryProbe
{
    public static BatteryInfo Read()
    {
        Native.GetSystemPowerStatus(out var ps);
        bool? onAc = ps.ACLineStatus switch { 0 => false, 1 => true, _ => null };

        var batteries = Wmi.Query("SELECT EstimatedChargeRemaining FROM Win32_Battery");
        if (batteries.Count == 0 && ps.BatteryFlag == 128)
            return new BatteryInfo(false, 0, null, onAc, null, null, null);

        int? charge = batteries.Count > 0 ? batteries[0].Int("EstimatedChargeRemaining")
                    : ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : null;

        // root\WMI battery classes are per battery; sum them (dual-battery ThinkPads).
        long? design = SumOrNull("SELECT DesignedCapacity FROM BatteryStaticData", "DesignedCapacity");
        long? full = SumOrNull("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");
        long? cycles = MaxOrNull("SELECT CycleCount FROM BatteryCycleCount", "CycleCount");

        return new BatteryInfo(true, Math.Max(batteries.Count, 1), charge, onAc, design, full,
            cycles is > 0 ? (int)cycles.Value : null);
    }

    private static long? SumOrNull(string wql, string prop)
    {
        try
        {
            var values = Wmi.Query(wql, Wmi.RootWmi).Select(o => o.Long(prop)).Where(v => v is > 0).ToList();
            return values.Count > 0 ? values.Sum() : null;
        }
        catch (System.Management.ManagementException) { return null; } // class absent on this firmware
    }

    private static long? MaxOrNull(string wql, string prop)
    {
        try
        {
            var values = Wmi.Query(wql, Wmi.RootWmi).Select(o => o.Long(prop)).Where(v => v is not null).ToList();
            return values.Count > 0 ? values.Max() : null;
        }
        catch (System.Management.ManagementException) { return null; }
    }
}

/// <param name="DischargeRateMw">Power drawn from the battery right now (0 on AC).</param>
public sealed record BatteryLive(int? ChargePercent, bool? OnAcPower, bool? Charging, int? DischargeRateMw, int? ChargeRateMw, long? RemainingMWh)
{
    public double? DischargeWatts => DischargeRateMw is > 0 ? DischargeRateMw / 1000.0 : null;

    /// <summary>Time left at the current drain; only meaningful while unplugged.</summary>
    public TimeSpan? EstimatedRuntime => DischargeRateMw is > 0 && RemainingMWh is > 0
        ? TimeSpan.FromHours((double)RemainingMWh.Value / DischargeRateMw.Value)
        : null;
}

public static class BatteryLiveProbe
{
    /// <summary>Cheap enough to poll every second during the drain test.</summary>
    public static BatteryLive? Read()
    {
        Native.GetSystemPowerStatus(out var ps);
        if (ps.BatteryFlag == 128) return null;   // no system battery
        bool? onAc = ps.ACLineStatus switch { 0 => false, 1 => true, _ => null };
        int? percent = ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : null;

        List<System.Management.ManagementBaseObject> status;
        try { status = Wmi.Query("SELECT ChargeRate, DischargeRate, RemainingCapacity, Charging FROM BatteryStatus", Wmi.RootWmi); }
        catch (System.Management.ManagementException) { status = []; }
        if (status.Count == 0) return new BatteryLive(percent, onAc, null, null, null, null);

        // Dual-battery laptops report one instance per pack.
        int Sum(string p) => status.Sum(s => s.Int(p) is > 0 and var v ? v : 0);
        return new BatteryLive(percent, onAc,
            Charging: status.Any(s => s.Bool("Charging") == true),
            DischargeRateMw: Sum("DischargeRate"),
            ChargeRateMw: Sum("ChargeRate"),
            RemainingMWh: status.Sum(s => s.Long("RemainingCapacity") ?? 0));
    }
}

/// <summary>Raises <see cref="AcChanged"/> when the charger is plugged in or pulled out.</summary>
public sealed class ChargerWatcher : IDisposable
{
    private readonly Timer _timer;
    private bool? _last;

    /// <summary>True = on AC. Raised on a thread-pool thread.</summary>
    public event Action<bool>? AcChanged;

    public ChargerWatcher(TimeSpan? interval = null)
    {
        _last = OnAcPower();
        var every = interval ?? TimeSpan.FromMilliseconds(500);
        _timer = new Timer(_ => Tick(), null, every, every);
    }

    public static bool? OnAcPower()
    {
        Native.GetSystemPowerStatus(out var ps);
        return ps.ACLineStatus switch { 0 => false, 1 => true, _ => null };
    }

    private void Tick()
    {
        if (OnAcPower() is not { } now || now == _last) return;
        _last = now;
        AcChanged?.Invoke(now);
    }

    public void Dispose() => _timer.Dispose();
}
