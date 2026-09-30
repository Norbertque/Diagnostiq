using System.Windows;
using System.Windows.Controls;
using Diagnostiq.AutoRun;
using Diagnostiq.AutoRun.Steps;
using Diagnostiq.Core;
using Diagnostiq.Core.Stress;
using Diagnostiq.Core.Testing;
using Diagnostiq.Presentation;
using Wpf.Ui.Controls;

namespace Diagnostiq.Views.Manual;

/// <summary>Every test as a card; each opens the same fullscreen slide as the Automatic run, for that test alone.</summary>
public partial class TestsPage : UserControl
{
    private readonly MainWindow _window;
    private readonly List<TestCard> _cards = [];

    public TestsPage(MainWindow window)
    {
        InitializeComponent();
        _window = window;

        var disk = new TestCard(TestIds.DiskSpeed, "Disk speed", "Sequential read and write, random reads. 1 GB test file, deleted afterwards.",
            SymbolRegular.Storage24, () => _window.RunSingle(new DiskSpeedStep()));
        _cards.Add(disk);
        HandsOn.Children.Add(disk);

        (string Id, string Title, string Description, SymbolRegular Icon, Func<StepView> Step)[] tests =
        [
            (TestIds.Network, "Wi-Fi and internet", "Internet connection and a fresh Wi-Fi scan.", SymbolRegular.Wifi124, () => new ChecksStep()),
            (TestIds.Display, "Screen", "Solid colours for dead pixels, lines and backlight bleed.", SymbolRegular.Desktop24, () => new DisplayStep()),
            (TestIds.Brightness, "Brightness", "Automatic sweep and the brightness keys.", SymbolRegular.BrightnessHigh24, () => new BrightnessStep()),
            (TestIds.Keyboard, "Keyboard", "Every key, including Esc, Windows and Alt.", SymbolRegular.Keyboard24, () => new KeyboardStep()),
            (TestIds.Touchpad, "Touchpad", "Coverage, both buttons and scrolling.", SymbolRegular.CursorClick24, () => new TouchpadStep()),
            (TestIds.Speakers, "Speakers", "Left, right and a sweep for rattles.", SymbolRegular.Speaker224, () => new SpeakersStep()),
            (TestIds.Headphones, "Headphone jack", "Plug detection and left/right.", SymbolRegular.Headphones24, () => new HeadphonesStep()),
            (TestIds.Microphone, "Microphone", "Level meter and a short recording.", SymbolRegular.Mic24, () => new MicrophoneStep()),
            (TestIds.Webcam, "Camera", "Live preview.", SymbolRegular.Camera24, () => new WebcamStep()),
            (TestIds.Usb, "USB ports", "Counts each port that recognises a device.", SymbolRegular.UsbStick24, () => new UsbStep()),
            (TestIds.Charger, "Charger", "Unplug, plug back in, confirm charging.", SymbolRegular.PlugConnected24, () => new ChargerStep()),
        ];
        foreach (var t in tests)
        {
            var card = new TestCard(t.Id, t.Title, t.Description, t.Icon, () => _window.RunSingle(t.Step()));
            _cards.Add(card);
            HandsOn.Children.Add(card);
        }

        Loaded += (_, _) => { _window.SessionChanged += Refresh; Refresh(); };
        Unloaded += (_, _) => _window.SessionChanged -= Refresh;
        SizeChanged += (_, e) => HandsOn.Columns = e.NewSize.Width >= 1000 ? 3 : 2;
    }

    private void Refresh()
    {
        foreach (var card in _cards) card.Refresh(_window.Session);
        StressResults.ItemsSource = new[] { (TestIds.Cpu, "Processor"), (TestIds.Memory, "Memory"), (TestIds.SurfaceScan, "Disk surface scan") }
            .Select(p => _window.Session[p.Item1] is { } r
                ? new StressRow(Outcomes.Label(r.Outcome), Outcomes.State(r.Outcome), $"{p.Item2}: {r.Detail}")
                : new StressRow("Not run", null, p.Item2))
            .ToList();
    }

    private void Stress_Click(object sender, RoutedEventArgs e)
    {
        var parts = (CpuCheck.IsChecked == true ? StressParts.Cpu : 0)
                  | (MemoryCheck.IsChecked == true ? StressParts.Memory : 0)
                  | (ScanCheck.IsChecked == true ? StressParts.DiskScan : 0);
        if (parts == 0) { _window.Notify("Nothing selected", "Choose at least one part to stress."); return; }
        var preset = QuickRadio.IsChecked == true ? StressPreset.Quick : ExtendedRadio.IsChecked == true ? StressPreset.Extended : StressPreset.Standard;
        _window.RunSingle(new StressStep(parts, preset.Duration()), preset);
    }

    private void RunAll_Click(object sender, RoutedEventArgs e) => _window.StartAutomatic(StressPreset.Standard);

    private sealed record StressRow(string Label, CheckState? State, string Text);
}
