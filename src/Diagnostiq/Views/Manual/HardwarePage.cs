using Diagnostiq.Core.Formatting;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Diagnostiq.Controls;
using Diagnostiq.Core;
using Diagnostiq.Core.Hardware;
using Diagnostiq.Core.Storage;
using Diagnostiq.Core.Win11;
using Diagnostiq.Presentation;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Diagnostiq.Views.Manual;

/// <summary>Everything read about the hardware, in detail, plus live sensor readings.</summary>
public sealed class HardwarePage : UserControl
{
    private readonly MainWindow _window;
    private readonly DetailSection _live = new("Live sensors");
    private readonly TextBlock _liveText;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public HardwarePage(SystemSnapshot s, MainWindow window)
    {
        _window = window;
        var page = new StackPanel { Margin = new Thickness(32, 24, 32, 32), MaxWidth = 1000 };
        var title = new TextBlock { Text = "Hardware", Margin = new Thickness(0, 0, 0, 20) };
        title.SetResourceReference(StyleProperty, "Diag.Text.Title");
        page.Children.Add(title);

        page.Children.Add(Device(s));
        page.Children.Add(Processor(s));
        page.Children.Add(_live);
        page.Children.Add(Memory(s));
        page.Children.Add(Storage(s));
        page.Children.Add(Graphics(s));
        page.Children.Add(Displays(s));
        page.Children.Add(Battery(s));
        page.Children.Add(Network(s));


        _liveText = _live.NoteBlock("Reading…");
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _timer.Tick += async (_, _) => await RefreshLiveAsync();
        Loaded += async (_, _) => { _timer.Start(); await RefreshLiveAsync(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task RefreshLiveAsync()
    {
        var sensors = _window.Sensors;
        if (sensors is null || !_window.IsEnabled) return;   // a test window is using the sensors
        var r = await Task.Run(sensors.Read);
        string temp = r.CpuTempC is { } t ? $"{t:0} °C{(r.CpuTempLimited ? " (approximate)" : "")}" : "not available";
        string fans = r.Fans.Count > 0 ? string.Join(", ", r.Fans.Select(f => $"{f.Rpm:N0} rpm")) : "not reported";
        string text = Format.Join($"CPU {temp}", r.CpuClockMHz is { } mhz ? $"{mhz / 1000:0.00} GHz" : null,
            r.CpuLoadPercent is { } load ? $"{load:0}% load" : null, r.GpuTempC is { } g ? $"GPU {g:0} °C" : null, $"fans {fans}");
        _liveText.Text = text;
    }

    private static DetailSection Device(SystemSnapshot s)
    {
        var d = s.Device;
        var id = s.Identity.Value;
        return new DetailSection("Device")
            .Row("Manufacturer", d?.Vendor)
            .Row("Model", d?.Model)
            .Row("Type / SKU", d?.ModelNumber ?? id?.SystemSku)
            .Row(d?.SerialLabel ?? "Serial number", d?.Serial)
            .Row("BIOS", id?.BiosVersion is { } v ? $"{v}{(id.BiosDate is { } date ? $", {Format.Date(date)}" : "")}" : null)
            .Row("Boot mode", s.Firmware.Value?.Uefi switch { true => "UEFI", false => "Legacy BIOS (CSM)", _ => null })
            .Row("Virtual machine", d?.IsVirtualMachine == true ? "Yes" : null);
    }

    private static DetailSection Processor(SystemSnapshot s)
    {
        var c = s.Cpu.Value;
        var support = c is null ? null : SupportedCpus.Check(c.Name);
        return new DetailSection("Processor")
            .Row("Name", c is null ? s.Cpu.Message : Names.Clean(c.Name))
            .Row("Cores / threads", c is null ? null : $"{c.Cores} / {c.Threads}")
            .Row("Base clock", c is { MaxClockMHz: > 0 } ? $"{c.MaxClockMHz / 1000.0:0.0#} GHz" : null)
            .Row("Windows 11 list", support?.Family, support?.Support switch
            {
                CpuSupport.Supported => "Supported",
                CpuSupport.LikelySupported => "Likely supported",
                CpuSupport.NotSupported => "Not supported",
                _ => null,
            }, support?.Support switch { CpuSupport.Supported or CpuSupport.LikelySupported => CheckState.Pass, CpuSupport.NotSupported => CheckState.Fail, _ => null });
    }

    private static DetailSection Memory(SystemSnapshot s)
    {
        var m = s.Memory.Value;
        var section = new DetailSection("Memory")
            .Row("Installed", m is null ? s.Memory.Message : Format.Memory(m.InstalledBytes))
            .Row("Usable by Windows", m is null ? null : Format.Memory(m.VisibleBytes));
        if (m is { Modules.Count: 0 }) section.Note("Individual modules aren't reported (usually soldered memory).");
        foreach (var (mod, i) in (m?.Modules ?? []).Select((x, i) => (x, i)))
            section.Group($"Module {i + 1}")
                .Row("Slot", mod.Slot)
                .Row("Size", Format.Memory(mod.CapacityBytes))
                .Row("Speed", mod.SpeedMHz is > 0 ? $"{mod.SpeedMHz} MT/s" : null)
                .Row("Manufacturer", mod.Manufacturer)
                .Row("Part number", mod.PartNumber);
        return section;
    }

    private static DetailSection Storage(SystemSnapshot s)
    {
        var section = new DetailSection("Storage");
        var health = s.DriveHealth.Value ?? [];
        foreach (var d in s.Disks.Value ?? [])
        {
            var h = health.FirstOrDefault(x => x.DiskNumber == d.Number);
            var verdict = h is null ? null : StorageHealth.Evaluate(h);
            section.Group($"{Names.Clean(d.Model)}{(d.Number == s.SystemDiskNumber.Value ? " (system disk)" : "")}")
                .Row("Size", Format.Disk(d.SizeBytes))
                .Row("Type", Format.Join(h?.BusType ?? d.InterfaceType, h?.MediaType is "SSD" or "HDD" ? h.MediaType : null))
                .Row("Health", verdict?.Reasons.FirstOrDefault(), verdict?.State switch
                {
                    CheckState.Pass => "Healthy",
                    CheckState.Warn => "Check drive",
                    CheckState.Fail => "Failing",
                    _ => h is null ? null : "Not reported",
                }, verdict?.State)
                .Row("Wear", h?.Counters?.WearPercent is { } w ? $"{w}% of rated endurance used" : null)
                .Row("Powered on", h?.Counters?.PowerOnHours is { } poh ? $"{Format.Number(poh)} hours" : null)
                .Row("Temperature", h?.Counters?.TemperatureC is { } t ? $"{t} °C" : null)
                .Row("Uncorrected errors", h?.Counters is { } c ? Format.Number((c.ReadErrorsUncorrected ?? 0) + (c.WriteErrorsUncorrected ?? 0)) : null)
                .Row("Serial number", d.SerialNumber?.TrimEnd('.'));
            if (h is { CountersNeedAdmin: true }) section.Note("Wear, hours and error counters need administrator rights.");
        }
        foreach (var smart in s.Smart.Value ?? [])
            if (!smart.Healthy) section.Row("SMART", string.Join(" ", smart.Problems), "Problem", CheckState.Fail);
        return section;
    }

    private static DetailSection Graphics(SystemSnapshot s)
    {
        var section = new DetailSection("Graphics");
        foreach (var g in s.Gpus.Value ?? [])
            section.Group(Names.Clean(g.Name))
                .Row("Video memory", g.DedicatedMemoryBytes is > 0 and var b ? Format.Memory(b) : null)
                .Row("Driver", Format.Join(g.DriverVersion, g.DriverDate is { } date ? Format.Date(date) : null),
                    g.IsBasicDisplayDriver ? "No driver" : g.HasProblem ? "Problem" : null,
                    g.IsBasicDisplayDriver || g.HasProblem ? CheckState.Warn : null);
        section.Row("DirectX 12", s.DirectX12.IsOk ? (s.DirectX12.Value ? "Supported" : "Not supported") : null);
        return section;
    }

    private static DetailSection Displays(SystemSnapshot s)
    {
        var section = new DetailSection("Displays");
        foreach (var p in s.Displays.Value ?? [])
            section.Group(p.Name ?? (p.ManufacturerCode is { } m ? $"{m} panel" : "Display"))
                .Row("Connection", p.IsInternal ? "Built-in" : "External")
                .Row("Resolution", p.WidthPx is { } w && p.HeightPx is { } h ? $"{w} × {h}" : null)
                .Row("Size", p.DiagonalInches is { } d ? $"{d:0.0}\" ({p.WidthCm} × {p.HeightCm} cm)" : null);
        return section;
    }

    private static DetailSection Battery(SystemSnapshot s)
    {
        var b = s.Battery.Value;
        var live = s.BatteryLive.Value;
        var section = new DetailSection("Battery");
        if (b is not { Present: true }) return section.Note("No battery found.");
        return section
            .Row("Charge", b.ChargePercent is { } c ? $"{c}%" : null)
            .Row("Health", b.HealthPercent is { } h ? $"{h:0}% of original capacity" : null,
                b.HealthPercent switch { < 40 => "Replace", < 60 => "Worn", not null => "Good", _ => null },
                b.HealthPercent switch { < 40 => CheckState.Fail, < 60 => CheckState.Warn, not null => CheckState.Pass, _ => null })
            .Row("Design capacity", b.DesignCapacityMWh is { } dc ? $"{dc / 1000.0:0.#} Wh" : null)
            .Row("Full charge capacity", b.FullChargeCapacityMWh is { } fc ? $"{fc / 1000.0:0.#} Wh" : null)
            .Row("Charge cycles", b.CycleCount?.ToString())
            .Row("Power", live?.Charging == true ? $"Charging{(live.ChargeRateMw is > 0 and var r ? $" at {r / 1000.0:0.#} W" : "")}"
                        : b.OnAcPower == true ? "On AC" : live?.DischargeWatts is { } w ? $"On battery, drawing {w:0.0} W" : "On battery")
            .Row("Batteries", b.BatteryCount > 1 ? b.BatteryCount.ToString() : null);
    }

    private static DetailSection Network(SystemSnapshot s)
    {
        var section = new DetailSection("Network");
        if (s.Wifi.Value is { AdapterPresent: true } w)
            section.Group("Wi-Fi")
                .Row("Adapter", Names.Clean(w.AdapterName ?? ""))
                .Row("Radio", w.RadioOn switch { true => "On", false => w.HardwareSwitchOn == false ? "Off (hardware switch)" : "Off", _ => null })
                .Row("Connection", w.Connected ? Format.Join(w.Ssid, w.Standard, w.SignalPercent is { } sig ? $"{sig}% signal" : null, w.LinkMbps is > 0 ? $"{w.LinkMbps} Mbit/s" : null) : "Not connected")
                .Row("Bands seen", w.BandsSeen.Count > 0 ? string.Join(", ", w.BandsSeen) : null);
        else section.Group("Wi-Fi").Note("No Wi-Fi adapter found.");

        foreach (var e in (s.Network.Value ?? []).Where(a => a.Kind == Core.Network.AdapterKind.Ethernet))
            section.Group(e.IsExternal ? "Ethernet (USB adapter)" : "Ethernet")
                .Row("Adapter", Names.Clean(e.Description))
                .Row("Status", e.IsConnected ? $"Connected{(e.SpeedBps > 0 ? $", {e.SpeedBps / 1_000_000} Mbit/s" : "")}" : "Not connected");

        var bt = s.Bluetooth.Value;
        section.Group("Bluetooth").Row("Radio", bt?.RadioPresent == true ? Names.Clean(bt.RadioName ?? "Present") : "Not found",
            bt?.Working == false ? "Driver problem" : null, bt?.Working == false ? CheckState.Warn : null);
        return section;
    }
}
