using System.Collections.ObjectModel;
using System.Windows;
using Diagnostiq.Core;
using Diagnostiq.Core.Hardware;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.AutoRun.Steps;

/// <summary>Counts the distinct laptop ports a device was plugged into. The user decides when every port has been tried.</summary>
public partial class UsbStep : StepView
{
    private readonly ObservableCollection<Row> _log = [];
    private UsbPortWatcher? _watcher;

    public UsbStep()
    {
        InitializeComponent();
        Log.ItemsSource = _log;
    }

    public override string Id => TestIds.Usb;
    public override string Title => "USB ports";

    protected override async Task OnStartAsync(CancellationToken ct)
    {
        if (!Ctx.LiveDevices) return;
        // Listing the devices already plugged in is a SetupAPI walk: keep it off the UI thread.
        var watcher = await Task.Run(() => new UsbPortWatcher(), CancellationToken.None);
        if (ct.IsCancellationRequested) { watcher.Dispose(); return; }   // skipped meanwhile
        _watcher = watcher;
        _watcher.Arrived += a => Dispatcher.BeginInvoke(() => OnArrived(a));
    }

    private void OnArrived(UsbArrival a)
    {
        Waiting.Visibility = Visibility.Collapsed;
        int ports = _watcher?.PortCount ?? 0;
        _log.Insert(0, new Row(a.NewPort ? CheckState.Pass : null, a.Name, a.NewPort ? $"Port {ports}" : "Same port as before"));
        Count.Text = ports.ToString();
        CountLabel.Text = ports == 1 ? "port checked so far" : "ports checked so far";
        if (ports > 0) Ctx.Suggest(TestOutcome.Pass);
    }

    protected override string? Detail(TestOutcome outcome)
    {
        int ports = _watcher?.PortCount ?? 0;
        string counted = $"{ports} port{(ports == 1 ? "" : "s")} recognised a device";
        return outcome switch
        {
            TestOutcome.Pass => counted + ".",
            TestOutcome.Fail => $"{counted}; at least one port didn't work.",
            _ => null,
        };
    }

    public override void Cleanup()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private sealed record Row(CheckState? State, string Name, string Note)
    {
        /// <summary>What screen readers announce for the row (the tick is otherwise only an icon).</summary>
        public override string ToString() => State == CheckState.Pass ? $"{Name}: {Note} works." : $"{Name}: {Note}.";
    }
}
