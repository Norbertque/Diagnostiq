using System.Management;
using Diagnostiq.Core.Probing;

namespace Diagnostiq.Core.Hardware;

/// <summary>
/// Built-in panel brightness through the WMI monitor classes (the same interface the
/// Windows brightness slider uses). External monitors and most desktops don't expose it.
/// </summary>
public static class BrightnessControl
{
    /// <summary>Current level 0–100, or null when there's no controllable panel.</summary>
    public static int? Current()
    {
        try
        {
            return Wmi.Query("SELECT CurrentBrightness, Active FROM WmiMonitorBrightness", Wmi.RootWmi)
                .FirstOrDefault(m => m.Bool("Active") != false)?.Int("CurrentBrightness");
        }
        catch (Exception ex) when (IsWmiError(ex)) { return null; }
    }

    /// <summary>
    /// Sets every active panel (hybrid-graphics laptops also list an inactive one). Never throws:
    /// firmware can refuse with "Generic failure".
    /// </summary>
    /// <returns>True when at least one panel took the new level.</returns>
    public static bool Set(int percent)
    {
        byte level = (byte)Math.Clamp(percent, 0, 100);
        List<ManagementObject> panels;
        try
        {
            panels = Wmi.Query("SELECT * FROM WmiMonitorBrightnessMethods", Wmi.RootWmi).OfType<ManagementObject>()
                .Where(m => m.Bool("Active") != false).ToList();
        }
        catch (Exception ex) when (IsWmiError(ex)) { return false; }

        bool set = false;
        foreach (var m in panels)
        {
            try
            {
                m.InvokeMethod("WmiSetBrightness", [(uint)1, level]);   // timeout (s), level
                set = true;
            }
            catch (Exception ex) when (IsWmiError(ex)) { }
        }
        return set;
    }

    private static bool IsWmiError(Exception ex) => ex is ManagementException or System.Runtime.InteropServices.COMException;
}
