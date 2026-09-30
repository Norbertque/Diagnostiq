using System.Management;
using System.Runtime.InteropServices;
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
    private bool _keysWorked, _sweepFailed;
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
            bool changed;
            try { changed = await Task.Run(() => BrightnessControl.Set(level), ct); }
            catch (Exception ex) when (IsWmiError(ex)) { changed = false; }
            if (!changed)
            {
                // A firmware or driver refusal: the keys can still be tested, and the user judges the rest.
                // Watch from the level the panel really has, or the refused one would pass as a key press.
                _sweepFailed = true;
                _lastSet = await Task.Run(BrightnessControl.Current, ct) ?? _lastSet;
                break;
            }
            Level.Text = $"{level}%";
            await Task.Delay(90, ct);
        }
        SweepIcon.State = _sweepFailed ? CheckState.Warn : CheckState.Pass;
        SweepText.Text = _sweepFailed ? "Windows couldn't change the brightness." : "Done. Did it change smoothly?";
        KeysText.Text = "Press your brightness keys now.";

        _watch = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = WatchKeysAsync(_watch.Token);
    }

    private static bool IsWmiError(Exception ex) => ex is ManagementException or COMException;

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
                int? now;
                try { now = await Task.Run(BrightnessControl.Current, ct); }
                catch (COMException) { continue; }   // WMI busy for a moment: try again next tick
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
        TestOutcome.Pass when _sweepFailed => _keysWorked ? "Brightness keys work; Windows couldn't change the brightness itself."
                                                          : "Windows couldn't change the brightness itself. Brightness keys weren't tried.",
        TestOutcome.Pass => _keysWorked ? "Changes smoothly; brightness keys work." : "Changes smoothly. Brightness keys weren't tried.",
        TestOutcome.Fail => "Brightness problem reported" + (_keysWorked ? "; the brightness keys work." : "; no brightness key press was detected."),
        _ => null,
    };

    public override void Cleanup()
    {
        _watch?.Cancel();
        if (_original is { } level)
            Task.Run(() => { try { BrightnessControl.Set(level); } catch (Exception ex) when (IsWmiError(ex)) { } });
    }
}
