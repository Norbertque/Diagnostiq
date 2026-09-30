using Diagnostiq.Core.Os;
using Diagnostiq.Core.Storage;

namespace Diagnostiq.Core;

/// <param name="Area">Short category shown next to the text ("Storage", "Battery", …).</param>
public sealed record Finding(CheckState State, string Area, string Text)
{
    /// <summary>What screen readers announce for a list item bound to this record.</summary>
    public override string ToString() => $"{Area}: {Text}";
}

/// <summary>
/// Things worth a look that the startup snapshot already reveals, before any test runs.
/// Windows 11 readiness has its own summary and isn't repeated here.
/// </summary>
public static class Findings
{
    public static IReadOnlyList<Finding> From(SystemSnapshot s)
    {
        var list = new List<Finding>();

        foreach (var drive in s.DriveHealth.Value ?? [])
        {
            if (drive.IsUsb) continue;
            var v = StorageHealth.Evaluate(drive);
            if (v.State is CheckState.Fail or CheckState.Warn)
                list.Add(new(v.State, "Storage", $"{drive.Model}: {v.Reasons[0]}"));
        }
        foreach (var smart in s.Smart.Value ?? [])
            if (!smart.Healthy)
                list.Add(new(CheckState.Fail, "Storage", $"SMART: {smart.Problems[0]}"));

        if (s.Battery.Value is { Present: true, HealthPercent: { } health })
        {
            if (health < 40) list.Add(new(CheckState.Fail, "Battery", $"Battery holds only {health:0}% of its original capacity. Replace it."));
            else if (health < 60) list.Add(new(CheckState.Warn, "Battery", $"Battery holds {health:0}% of its original capacity."));
        }

        if (s.Gpus.Value?.Any(g => g.IsBasicDisplayDriver) == true)
            list.Add(new(CheckState.Warn, "Graphics", "No graphics driver installed (Microsoft Basic Display Adapter)."));

        if (s.DeviceProblems.Value is { Count: > 0 } problems)
        {
            foreach (var p in problems.Take(3))
                list.Add(new(CheckState.Warn, "Drivers", $"{p.Name}: {p.Meaning.ToLowerInvariant()}."));
            if (problems.Count > 3)
                list.Add(new(CheckState.Warn, "Drivers", $"{problems.Count - 3} more devices have driver problems."));
        }

        if (s.Wifi.Value is { AdapterPresent: true, HardwareSwitchOn: false })
            list.Add(new(CheckState.Warn, "Wi-Fi", "Wi-Fi is off at a hardware switch or Fn key."));
        else if (s.Wifi.IsOk && s.Wifi.Value is { AdapterPresent: false } && s.Battery.Value?.Present == true)
            list.Add(new(CheckState.Warn, "Wi-Fi", "No Wi-Fi adapter found. It may be off in BIOS or missing a driver."));

        if (s.Activation.Value is { IsActivated: false, State: not LicenseState.Unknown } act)
            list.Add(new(CheckState.Warn, "Windows", act.HasFirmwareKey
                ? $"Windows isn't activated. The BIOS holds a Windows {act.FirmwareKeyEdition} key; connect to the internet to activate."
                : "Windows isn't activated."));

        if (s.Antivirus.Value is { Count: > 0 } av && !av.Any(a => a.Enabled))
            list.Add(new(CheckState.Warn, "Security", "No antivirus has real-time protection switched on."));

        return list.OrderBy(f => f.State == CheckState.Fail ? 0 : 1).ToList();
    }
}
