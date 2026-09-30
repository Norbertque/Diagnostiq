using Diagnostiq.Core;
using Diagnostiq.Core.Hardware;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.AutoRun.Steps;

/// <summary>
/// Sweeps the panel brightness through WMI, then watches for level changes the app didn't
/// make: those come from the brightness keys. The original level is restored afterwards.
/// </summary>
public partial class BrightnessStep : StepView
{
    private int? _original;
    private volatile int _lastSet = -1;
    private bool _keysWorked;
    private CancellationTokenSource? _watch;

    public BrightnessStep() => InitializeComponent();

    public override string Id => TestIds.Brightness;
    public override string Title => "Brightness";

    public override bool IsApplicable(AutoRunContext ctx) => BrightnessControl.Current() is not null;

    protected override async Task OnStartAsync(CancellationToken ct)
    {
        _original = await Task.Run(BrightnessControl.Current, ct);
        Level.Text = $"{_original}%";

        // Down to 10 %, up to 100 %, back to where it was: ~5 s of visible change.
        int start = _original ?? 50;
        var levels = Steps(start, 10).Concat(Steps(10, 100)).Concat(Steps(100, start));
        foreach (var level in levels)
        {
            ct.ThrowIfCancellationRequested();
            _lastSet = level;
            await Task.Run(() => BrightnessControl.Set(level), ct);
            Level.Text = $"{level}%";
            await Task.Delay(90, ct);
        }
        SweepIcon.State = CheckState.Pass;
        SweepText.Text = "Done. Did it change smoothly?";
        KeysText.Text = "Press your brightness keys now.";

        _watch = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = WatchKeysAsync(_watch.Token);
    }

    private static IEnumerable<int> Steps(int from, int to)
    {
        int step = from < to ? 10 : -10;
        for (int v = from; step > 0 ? v < to : v > to; v += step) yield return v;
        yield return to;
    }

    private async Task WatchKeysAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(300, ct);
                var now = await Task.Run(BrightnessControl.Current, ct);
                if (now is null) continue;
                Level.Text = $"{now}%";
                if (!_keysWorked && now != _lastSet)
                {
                    _keysWorked = true;
                    KeysIcon.State = CheckState.Pass;
                    KeysText.Text = "Working.";
                    Ctx.Suggest(TestOutcome.Pass);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    protected override string? Detail(TestOutcome outcome) => outcome switch
    {
        TestOutcome.Pass => _keysWorked ? "Changes smoothly; brightness keys work." : "Changes smoothly. Brightness keys weren't tried.",
        TestOutcome.Fail => "Brightness problem reported" + (_keysWorked ? "." : "; the brightness keys didn't respond."),
        _ => null,
    };

    public override void Cleanup()
    {
        _watch?.Cancel();
        if (_original is { } level)
            Task.Run(() => { try { BrightnessControl.Set(level); } catch (System.Management.ManagementException) { } });
    }
}
