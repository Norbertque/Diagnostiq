using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Diagnostiq.AutoRun.Keyboard;
using Diagnostiq.Core.Testing;
using Diagnostiq.Interop;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Diagnostiq.AutoRun.Steps;

/// <summary>
/// On-screen keyboard that lights up as keys are pressed, fed by a low-level hook that also
/// swallows the keys so nothing else reacts. Moves on by itself once every key has worked.
/// </summary>
public partial class KeyboardStep : StepView
{
    private const double Unit = 56, Gap = 4, KeyHeight = 52;
    private const int EscScan = 0x01;

    /// <summary>The keyboard-only way out: every key is captured here, so Pass, Fail and Skip can't be reached by keys.</summary>
    private static readonly TimeSpan EscHoldToFail = TimeSpan.FromSeconds(2);

    private readonly Dictionary<(int, bool), List<Border>> _keys = [];
    private readonly Dictionary<(int, bool), string> _labels = [];
    private readonly HashSet<(int, bool)> _required = [];
    private readonly HashSet<(int, bool)> _done = [];
    private readonly Dictionary<(int, bool), DateTime> _firstSeen = [];
    private readonly SortedSet<string> _others = [];
    private KeyboardHook? _hook;
    private DispatcherTimer? _escHold;
    private DateTime _hookStarted;
    private bool _iso, _finishing, _escReleasedOnce, _endedByEsc;

    public KeyboardStep() => InitializeComponent();

    public override string Id => TestIds.Keyboard;
    public override string Title => "Keyboard";

    protected override Task OnStartAsync(CancellationToken ct)
    {
        _iso = KeyLayout.DefaultIso();
        Build();
        if (!Ctx.LiveDevices) return Task.CompletedTask;
        _hook = new KeyboardHook();
        _hook.Key += OnKey;
        _hookStarted = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    /// <summary>
    /// A timer, not key repeats, so it also works with auto-repeat off. An Esc that is already down when
    /// the test starts (a stuck key) doesn't count until it has been released once.
    /// </summary>
    private void OnEsc(bool down)
    {
        if (!down)
        {
            _escReleasedOnce = true;
            _escHold?.Stop();
            return;
        }
        if (_escHold is { IsEnabled: true }) return;   // auto-repeat of the same hold
        if (!_escReleasedOnce && DateTime.UtcNow - _hookStarted < TimeSpan.FromMilliseconds(500)) return;
        _escHold ??= new DispatcherTimer(EscHoldToFail, DispatcherPriority.Normal, (_, _) => EndByEsc(), Dispatcher) { IsEnabled = false };
        _escHold.Start();
    }

    private void EndByEsc()
    {
        _escHold?.Stop();
        if (_hook is null) return;   // the step already ended
        _endedByEsc = true;
        _done.Add((EscScan, false));   // Esc evidently works: it's what ended the test
        Paint((EscScan, false), pressed: false);
        Ctx.Judge(TestOutcome.Fail);
    }

    private void Build()
    {
        Board.Children.Clear();
        _keys.Clear();
        _labels.Clear();
        _required.Clear();
        foreach (var row in KeyLayout.Rows(_iso))
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, Gap) };
            for (int i = 0; i < row.Count; i++)
            {
                var k = row[i];
                if (k.Half && i + 1 < row.Count && row[i + 1].Half)
                {
                    // Up/down arrows share one key slot, stacked.
                    var stack = new StackPanel { Width = Unit - Gap, Margin = new Thickness(0, 0, Gap, 0) };
                    stack.Children.Add(MakeKey(k, (KeyHeight - Gap) / 2, bottomGap: Gap));
                    stack.Children.Add(MakeKey(row[++i], (KeyHeight - Gap) / 2, bottomGap: 0));
                    line.Children.Add(stack);
                }
                else line.Children.Add(MakeKey(k, KeyHeight));
            }
            Board.Children.Add(line);
        }
        foreach (var id in _done.Where(_keys.ContainsKey)) Paint(id, pressed: false);
        IsoButton.Appearance = _iso ? ControlAppearance.Primary : ControlAppearance.Secondary;
        AnsiButton.Appearance = _iso ? ControlAppearance.Secondary : ControlAppearance.Primary;
        UpdateStatus();
    }

    private Border MakeKey(KeyDef k, double height, double bottomGap = 0)
    {
        // ISO Enter is two shapes for one key: the upper one carries the label, the lower one reaches up
        // through the row gap and covers the upper one's bottom edge, so the two read as one keycap.
        bool isoEnter = _iso && !k.IsGap && k.Id == (0x1C, false);
        bool lowerHalf = isoEnter && _keys.ContainsKey(k.Id);
        var label = lowerHalf ? "" : KeyLayout.Label(k);
        var text = new TextBlock
        {
            Text = label, FontSize = label.Length == 1 ? 15 : 12,   // letters and symbols large, named keys small
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var key = new Border
        {
            Width = k.Width * Unit - Gap, Height = height, Margin = new Thickness(0, 0, k.Half ? 0 : Gap, bottomGap),
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = text,
            Opacity = k.Untestable ? 0.45 : 1,
            ToolTip = k.Untestable ? "Fn is handled by the keyboard itself and never reaches Windows."
                    : k.Optional ? "Not every laptop has this key." : null,
        };
        key.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
        key.SetResourceReference(Border.BorderBrushProperty, "ControlStrongStrokeColorDefaultBrush");
        text.SetResourceReference(TextBlock.ForegroundProperty, k.Optional ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");

        if (!k.IsGap && !k.Untestable)
        {
            if (!_keys.TryGetValue(k.Id, out var list)) _keys[k.Id] = list = [];
            list.Add(key);   // ISO Enter is two shapes for one key
            _labels.TryAdd(k.Id, label);
            if (!k.Optional) _required.Add(k.Id);
        }

        if (isoEnter && !lowerHalf) key.CornerRadius = new CornerRadius(6, 6, 0, 6);
        if (!lowerHalf) return key;

        // The key fill is translucent, so the lower half sits on an opaque patch of the page colour
        // that hides the upper half's bottom edge where the two meet (2 px, so no anti-aliased seam is left
        // when the board is scaled).
        const double overlap = 2;
        key.Height = height + Gap + overlap;
        key.Margin = new Thickness(0);
        key.BorderThickness = new Thickness(1, 0, 1, 1);
        key.CornerRadius = new CornerRadius(0, 0, 6, 6);
        var patch = new Border { Child = key, CornerRadius = key.CornerRadius, Margin = new Thickness(0, -(Gap + overlap), Gap, bottomGap) };
        patch.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        return patch;
    }

    private void OnKey(KeyEvent e)
    {
        if (_hook is null) return;   // queued before the step ended: must not judge the next one

        // Holding Esc for 2 s fails the keyboard: the keyboard-only way out of this step.
        if (e.Scan == EscScan && !e.Extended) OnEsc(e.Down);

        var id = (e.Scan, e.Extended);
        if (e.Vk == 0xA1) id = (0x36, false);          // right Shift reports odd flags on some boards
        if (e.Scan == 0x2A && e.Extended) return;       // "fake shift" sent around arrows/Num Lock

        // The Copilot key arrives as Win + Shift + F23: don't count those two as pressed by it.
        if (e.Vk == 0x86 && e.Down)
        {
            foreach (var mod in new[] { (0x5B, true), (0x2A, false) })
                if (_firstSeen.TryGetValue(mod, out var at) && DateTime.UtcNow - at < TimeSpan.FromMilliseconds(400))
                {
                    _done.Remove(mod);
                    _firstSeen.Remove(mod);
                    Paint(mod, pressed: false);
                }
        }

        if (!_keys.ContainsKey(id))
        {
            if (e.Scan == KeyLayout.IsoExtraKey && !_iso) { _iso = true; Build(); }   // the ISO-only key exists: it's an ISO board
            else if (e.Down) _others.Add(KeyLayout.OtherKeyName(e));
            if (!_keys.ContainsKey(id)) { UpdateStatus(); return; }
        }

        if (e.Down) _firstSeen.TryAdd(id, DateTime.UtcNow);
        else _done.Add(id);
        Paint(id, e.Down);
        UpdateStatus();
    }

    private void Paint((int, bool) key, bool pressed)
    {
        if (!_keys.TryGetValue(key, out var borders)) return;
        bool done = _done.Contains(key);
        foreach (var b in borders)
        {
            // Keys that worked get a solid fill, so the few that didn't stand out clearly (not just by a faint tint).
            string bg = pressed ? "AccentFillColorDefaultBrush" : done ? "SystemFillColorSuccessBrush" : "ControlFillColorDefaultBrush";
            string stroke = done && !pressed ? "SystemFillColorSuccessBrush" : "ControlStrongStrokeColorDefaultBrush";
            b.SetResourceReference(Border.BackgroundProperty, bg);
            b.SetResourceReference(Border.BorderBrushProperty, stroke);
            ((TextBlock)b.Child).SetResourceReference(TextBlock.ForegroundProperty,
                pressed ? "TextOnAccentFillColorPrimaryBrush" : done ? Theme.ThemeService.OnStatusFillKey : "TextFillColorPrimaryBrush");
        }
    }

    private void UpdateStatus()
    {
        int done = _required.Count(_done.Contains);
        Counter.Text = $"{done} of {_required.Count}";
        var missing = MissingNames();
        Missing.Text = missing.Count == 0 ? "Every key works." : $"Not pressed yet: {string.Join(", ", missing.Take(12))}{(missing.Count > 12 ? $" and {missing.Count - 12} more" : "")}";
        Others.Text = _others.Count > 0 ? $"Other keys that work: {string.Join(", ", _others)}" : "";

        if (missing.Count == 0 && _required.Count > 0 && !_finishing)
        {
            _finishing = true;
            Ctx.Suggest(TestOutcome.Pass);
            Missing.Text = "Every key works. Moving on…";
            _ = FinishSoonAsync();
        }
    }

    private async Task FinishSoonAsync()
    {
        var judge = Ctx.JudgeCurrentStep();   // not whatever step is showing in 2 s
        await Task.Delay(TimeSpan.FromSeconds(2));
        judge(TestOutcome.Pass);
    }

    private List<string> MissingNames() =>
        _required.Where(id => !_done.Contains(id)).Select(KeyName).ToList();

    /// <summary>"Shift" twice is ambiguous; say which one.</summary>
    private string KeyName((int Scan, bool Ext) id) => id switch
    {
        (0x2A, _) => "Left Shift",
        (0x36, _) => "Right Shift",
        (0x1D, false) => "Left Ctrl",
        (0x1D, true) => "Right Ctrl",
        _ => _labels.GetValueOrDefault(id, $"{id.Scan:X2}"),
    };

    private void Iso_Click(object sender, RoutedEventArgs e) { _iso = true; Build(); }
    private void Ansi_Click(object sender, RoutedEventArgs e) { _iso = false; Build(); }

    protected override string? Detail(TestOutcome outcome)
    {
        int done = _required.Count(_done.Contains);
        var missing = MissingNames();
        string summary = missing.Count == 0 ? $"All {_required.Count} keys worked." : $"{done} of {_required.Count} keys worked.";
        if (_endedByEsc)   // the user gave up: the rest weren't necessarily tried, so don't call them broken
            return $"{summary} Ended early by holding Esc." +
                   (missing.Count > 0 ? $" Not pressed: {string.Join(", ", missing.Take(15))}{(missing.Count > 15 ? ", …" : "")}." : "");
        return outcome == TestOutcome.Fail && missing.Count > 0
            ? $"{summary} Not working: {string.Join(", ", missing.Take(15))}{(missing.Count > 15 ? ", …" : "")}."
            : summary;
    }

    public override void Cleanup()
    {
        _escHold?.Stop();
        if (_hook is not null) _hook.Key -= OnKey;
        _hook?.Dispose();
        _hook = null;
    }
}
