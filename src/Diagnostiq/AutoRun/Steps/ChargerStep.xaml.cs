using Diagnostiq.Core;
using Diagnostiq.Core.Hardware;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.AutoRun.Steps;

/// <summary>
/// Unplug and plug the charger back in; passes by itself once both are seen and the battery
/// reports charging (or is already full). Also the reminder to reconnect after the drain test.
/// </summary>
public partial class ChargerStep : StepView
{
    private ChargerWatcher? _watcher;
    private bool _sawUnplug, _sawPlug, _finishing;
    private string? _chargeDetail;

    public ChargerStep() => InitializeComponent();

    public override string Id => TestIds.Charger;
    public override string Title => "Charger";

    public override bool IsApplicable(AutoRunContext ctx) => ctx.Snapshot.Battery.Value?.Present == true;

    protected override Task OnStartAsync(CancellationToken ct)
    {
        bool onAc = ChargerWatcher.OnAcPower() == true;
        _sawUnplug = !onAc;   // still unplugged from the battery drain test: that counts
        Refresh();
        if (!Ctx.LiveDevices) return Task.CompletedTask;
        _watcher = new ChargerWatcher();
        _watcher.AcChanged += ac => Dispatcher.BeginInvoke(() => OnAcChanged(ac));
        return Task.CompletedTask;
    }

    private async void OnAcChanged(bool onAc)
    {
        if (!onAc) { _sawUnplug = true; Refresh(); return; }
        if (!_sawUnplug) return;   // plugged in before being unplugged: nothing new
        _sawPlug = true;
        PlugText.Text = "Checking that it charges…";
        Refresh();

        // Give the charge controller a few seconds to start.
        BatteryLive? live = null;
        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            live = await Task.Run(() => { try { return BatteryLiveProbe.Read(); } catch (System.Management.ManagementException) { return null; } });
            if (live?.Charging == true || live?.ChargePercent >= 99) break;
        }
        _chargeDetail = live switch
        {
            { Charging: true, ChargeRateMw: > 0 } l => $"charging at {l.ChargeRateMw / 1000.0:0} W",
            { Charging: true } => "charging",
            { ChargePercent: >= 99 } => "battery already full, so charging couldn't be confirmed",
            _ => "Windows reports it plugged in but not charging",
        };
        Refresh();
    }

    private void Refresh()
    {
        UnplugIcon.State = _sawUnplug ? CheckState.Pass : null;
        UnplugText.Text = _sawUnplug ? "Detected." : "Waiting…";
        bool charging = _chargeDetail is not null && !_chargeDetail.StartsWith("Windows reports", StringComparison.Ordinal);
        PlugIcon.State = _chargeDetail is null ? null : charging ? CheckState.Pass : CheckState.Warn;
        if (_chargeDetail is not null) PlugText.Text = char.ToUpperInvariant(_chargeDetail[0]) + _chargeDetail[1..] + ".";
        else if (!_sawPlug) PlugText.Text = "Waiting…";

        Instruction.Text = !_sawUnplug ? "Unplug the charger."
                         : !_sawPlug ? "Now plug the charger back in."
                         : charging ? "The charger works." : "Plugged in.";

        if (_sawUnplug && _sawPlug && _chargeDetail is not null && !_finishing)
        {
            _finishing = true;
            if (charging) { Ctx.Suggest(TestOutcome.Pass); _ = FinishSoonAsync(); }
            else Ctx.Suggest(TestOutcome.Fail);
        }
    }

    private async Task FinishSoonAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        Ctx.Judge(TestOutcome.Pass);
    }

    protected override string? Detail(TestOutcome outcome) => outcome switch
    {
        TestOutcome.Pass => _chargeDetail is null ? "Charger works." : $"Unplug and plug-in detected; {_chargeDetail}.",
        TestOutcome.Fail => _chargeDetail is not null ? $"Charger problem: {_chargeDetail}." : "The charger wasn't detected.",
        _ => null,
    };

    public override void Cleanup()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
