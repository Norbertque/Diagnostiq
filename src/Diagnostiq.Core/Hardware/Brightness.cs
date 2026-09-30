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
        catch (ManagementException) { return null; }
    }

    public static void Set(int percent)
    {
        byte level = (byte)Math.Clamp(percent, 0, 100);
        foreach (var m in Wmi.Query("SELECT * FROM WmiMonitorBrightnessMethods", Wmi.RootWmi).OfType<ManagementObject>())
            m.InvokeMethod("WmiSetBrightness", [(uint)1, level]);   // timeout (s), level
    }
}
