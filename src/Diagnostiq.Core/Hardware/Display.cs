using System.Management;
using Diagnostiq.Core.Probing;

namespace Diagnostiq.Core.Hardware;

/// <param name="WidthPx">Native (EDID preferred) resolution, not the current scaled one.</param>
public sealed record DisplayPanel(string? Name, string? ManufacturerCode, bool IsInternal, int? WidthPx, int? HeightPx, int? WidthCm, int? HeightCm)
{
    public double? DiagonalInches => WidthCm is > 0 && HeightCm is > 0
        ? Math.Sqrt(WidthCm.Value * WidthCm.Value + HeightCm.Value * HeightCm.Value) / 2.54
        : null;
}

public static class DisplayProbe
{
    // D3DKMDT_VIDEO_OUTPUT_TECHNOLOGY values that mean "built into the laptop".
    private const uint VotLvds = 6, VotEmbeddedDisplayPort = 11, VotEmbeddedUdi = 13, VotInternal = 0x80000000;

    /// <summary>Active monitors from their EDID (root\WMI), laptop panel first.</summary>
    public static IReadOnlyList<DisplayPanel> Read()
    {
        var basics = Wmi.Query("SELECT InstanceName, Active, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams", Wmi.RootWmi)
            .Where(m => m.Bool("Active") != false).ToList();
        if (basics.Count == 0) return [];

        var connections = ByInstance("SELECT InstanceName, VideoOutputTechnology FROM WmiMonitorConnectionParams");
        var ids = ByInstance("SELECT InstanceName, UserFriendlyName, ManufacturerName FROM WmiMonitorID");
        var modes = ByInstance("SELECT InstanceName, MonitorSourceModes, PreferredMonitorSourceModeIndex FROM WmiMonitorListedSupportedSourceModes");

        var panels = new List<DisplayPanel>();
        foreach (var b in basics)
        {
            var instance = b.Str("InstanceName") ?? "";
            uint? vot = connections.GetValueOrDefault(instance)?["VideoOutputTechnology"] is { } v ? Convert.ToUInt32(v) : null;
            var id = ids.GetValueOrDefault(instance);
            var (w, h) = PreferredMode(modes.GetValueOrDefault(instance));
            panels.Add(new DisplayPanel(
                Name: id is null ? null : EdidString(id["UserFriendlyName"]),
                ManufacturerCode: id is null ? null : EdidString(id["ManufacturerName"]),
                IsInternal: vot is VotLvds or VotEmbeddedDisplayPort or VotEmbeddedUdi or VotInternal,
                WidthPx: w, HeightPx: h,
                WidthCm: b.Int("MaxHorizontalImageSize") is > 0 and var wc ? wc : null,
                HeightCm: b.Int("MaxVerticalImageSize") is > 0 and var hc ? hc : null));
        }
        return panels.OrderByDescending(p => p.IsInternal).ToList();
    }

    private static Dictionary<string, ManagementBaseObject> ByInstance(string wql)
    {
        try
        {
            return Wmi.Query(wql, Wmi.RootWmi)
                .Where(o => o.Str("InstanceName") is not null)
                .GroupBy(o => o.Str("InstanceName")!)
                .ToDictionary(g => g.Key, g => g.First());
        }
        catch (ManagementException) { return []; }  // optional detail; basic params are enough
    }

    private static (int?, int?) PreferredMode(ManagementBaseObject? modes)
    {
        if (modes?["MonitorSourceModes"] is not ManagementBaseObject[] list || list.Length == 0) return (null, null);
        int index = modes.Int("PreferredMonitorSourceModeIndex") is { } i && i >= 0 && i < list.Length ? i : 0;
        var mode = list[index];
        return (mode.Int("HorizontalActivePixels"), mode.Int("VerticalActivePixels"));
    }

    /// <summary>EDID strings arrive as zero-padded UInt16 arrays.</summary>
    internal static string? EdidString(object? value)
    {
        if (value is not ushort[] chars) return null;
        var s = new string(chars.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim();
        return s.Length == 0 ? null : s;
    }
}
