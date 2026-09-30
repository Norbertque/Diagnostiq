using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Diagnostiq.Controls;
using Diagnostiq.Core;
using Diagnostiq.Core.Formatting;
using Diagnostiq.Core.Os;
using Diagnostiq.Core.Win11;
using Diagnostiq.Presentation;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Diagnostiq.Views.Manual;

/// <summary>Windows version, activation, security features and devices with driver problems.</summary>
public sealed class WindowsPage : UserControl
{
    public WindowsPage(SystemSnapshot s)
    {
        var page = new StackPanel { Margin = new Thickness(32, 24, 32, 32) };
        page.SetResourceReference(MaxWidthProperty, "Diag.Page.MaxWidth");
        var title = new TextBlock { Text = "Windows & security", Margin = new Thickness(0, 0, 0, 20) };
        title.SetResourceReference(StyleProperty, "Diag.Text.Title");
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        page.Children.Add(title);

        var os = s.Os.Value;
        page.Children.Add(new DetailSection("Windows")
            .Row("Edition", os?.Name ?? Outcomes.Unavailable(s.Os))
            .Row("Version", os?.DisplayVersion)
            .Row("Build", os?.BuildString)
            .Row("Installed", os?.InstallDate is { } d ? Format.Date(d) : null));

        var act = s.Activation.Value;
        // Unknown means the licensing service didn't answer: say so, without a "Not activated" pill.
        var (pill, pillState) = act switch
        {
            null or { State: LicenseState.Unknown } => ((string?)null, (CheckState?)null),
            { IsActivated: true } => ("Activated", CheckState.Pass),
            _ => ("Not activated", CheckState.Warn),
        };
        page.Children.Add(new DetailSection("Activation")
            .Row("Status", act is null ? Outcomes.Unavailable(s.Activation) : act.IsActivated ? null : act.State switch
            {
                LicenseState.Grace => "Grace period (not activated yet)",
                LicenseState.Notification => "Not activated (notification mode)",
                LicenseState.Unlicensed => "Not activated",
                _ => "Couldn't check",
            }, pill, pillState)
            .Row("Licence type", act?.Channel)
            .Row("Key in BIOS", act is null ? null : act.HasFirmwareKey ? $"Windows {act.FirmwareKeyEdition}" : "None"));

        var tpm = s.Tpm.Value;
        var bl = s.BitLocker.Value;
        var security = new DetailSection("Security")
            .Row("TPM", tpm switch
            {
                { Present: false } => "Not found (may be off in BIOS)",
                { MajorVersion: { } v } => $"TPM {v:0.0}{(tpm.Manufacturer is { } m ? $" ({m})" : "")}",
                _ => Outcomes.Unavailable(s.Tpm),
            })
            .Row("Secure Boot", s.Firmware.Value?.SecureBoot switch
            {
                SecureBootState.On => "On",
                SecureBootState.Off => "Supported, switched off",
                SecureBootState.NotAvailable => "Not available in Legacy boot mode",
                _ => "Unknown",
            })
            .Row("BitLocker (system drive)", bl is null ? Outcomes.Unavailable(s.BitLocker) : $"{(bl.IsProtected ? "On" : "Off")}, {bl.Conversion.ToLowerInvariant()}");
        foreach (var av in s.Antivirus.Value ?? [])
            security.Row("Antivirus", av.Name, av.Enabled ? (av.UpToDate ? "On" : "Out of date") : "Off",
                av.Enabled && av.UpToDate ? CheckState.Pass : CheckState.Warn);
        page.Children.Add(security);

        var drivers = new DetailSection("Devices with driver problems");
        var problems = s.DeviceProblems.Value;
        if (problems is null) drivers.Note(Outcomes.Unavailable(s.DeviceProblems));
        else if (problems.Count == 0) drivers.Note("None. Every device has a working driver.");
        else foreach (var p in problems) drivers.Row(p.Name, $"{p.Meaning} (code {p.Code})");
        page.Children.Add(drivers);

        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
