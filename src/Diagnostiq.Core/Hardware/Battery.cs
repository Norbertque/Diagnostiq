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
