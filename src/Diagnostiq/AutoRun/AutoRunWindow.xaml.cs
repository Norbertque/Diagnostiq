using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Diagnostiq.Core.Testing;
using Diagnostiq.Presentation;
using Wpf.Ui.Controls;

namespace Diagnostiq.AutoRun;

/// <summary>
/// Fullscreen, topmost shell for the Automatic run: shows one step at a time and moves on
/// as soon as it's done. Esc and Alt+F4 never leave mid-run; only the Exit button does (with a
/// confirmation), so pressing keys during the tests can't end the run by accident.
/// </summary>
public partial class AutoRunWindow : Window
{
    /// <summary>How long a stopped run may take to wind down before the window closes anyway.</summary>
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(10);

    private readonly AutoRunContext _ctx;
    private readonly List<StepView> _steps;
    private readonly List<Border> _segments = [];
    private readonly bool _single;
    private readonly CancellationTokenSource _runCts = new();
    private CancellationTokenSource? _stepCts;
    private StepView? _current;
    private ContentDialog? _confirm;
    private bool _finished, _closeAllowed, _closed;

    /// <param name="single">Manual mode: one test, no summary; the window closes when it's done.</param>
    public AutoRunWindow(AutoRunContext ctx, IEnumerable<StepView> steps, bool single = false)
    {
        InitializeComponent();
        _ctx = ctx;
        _ctx.Window = this;
        _single = single;
        SegmentsBox.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        _steps = steps.Where(s => s.IsApplicable(ctx)).ToList();
        if (single && _steps.Count > 0) Title = $"Diagnostiq: {_steps[0].Title}";
        foreach (var _ in _steps)
        {
            var seg = new Border { Width = 28, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(2, 0, 2, 0) };
            _segments.Add(seg);
            Segments.Children.Add(seg);
        }
        _ctx.Suggested += outcome => Dispatcher.Invoke(() => Highlight(outcome));
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
        Closed += (_, _) => _closed = true;
        ContentRendered += async (_, _) => { Activate(); await RunAsync(); };
        Loaded += (_, _) => Interop.KeepAwake.Begin();   // no screen-off or sleep mid-test
        Closed += (_, _) => Interop.KeepAwake.End();
    }

    /// <summary>Off for dev snapshots so captures don't catch a step mid-slide.</summary>
    public bool AnimationsEnabled { get; set; } = SystemParameters.ClientAreaAnimation;

    public AutoRunContext Context => _ctx;

    /// <summary>The finished run, for Home and the report.</summary>
    public TestRun Run => _ctx.Run;

    private async Task RunAsync()
    {
        try
        {
            for (int i = 0; i < _steps.Count && !_runCts.IsCancellationRequested; i++)
                await RunStepAsync(_steps[i], i);
        }
        catch (Exception ex)
        {
            // Steps are guarded below, so this is the shell itself failing: log it and wrap up
            // rather than leave a topmost fullscreen window that can't be closed.
            App.LogError(ex);
        }
        finally
        {
            _finished = true;   // from here on Exit, Esc and Alt+F4 simply close
        }

        if (_closed) return;   // closed while a stuck step was winding down
        if (_single) { _closeAllowed = true; Close(); }
        else ShowSummary();
    }

    private async Task RunStepAsync(StepView step, int index)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(_runCts.Token);
        _stepCts = stepCts;
        try
        {
            Show(step, index);
            await step.RunAsync(_ctx, stepCts.Token);
        }
        catch (OperationCanceledException) when (stepCts.IsCancellationRequested)
        {
            // Skip (or Exit): anything the step didn't record counts as skipped.
            RecordMissing(step, null);
        }
        catch (Exception ex)
        {
            // A step that can't run (device gone, WMI refusing, disk full) is noted and the run moves on.
            App.LogError(ex);
            RecordMissing(step, $"Couldn't run: {ex.Message.Trim().TrimEnd('.')}.");
        }
        finally
        {
            try { step.Cleanup(); }
            catch (Exception ex) { App.LogError(ex); }
            _stepCts = null;
            _current = null;
        }
        PaintSegment(index, _ctx.Run[step.Id]?.Outcome);
    }

    private void RecordMissing(StepView step, string? detail)
    {
        foreach (var id in step.ResultIds)
            if (_ctx.Run[id] is null) _ctx.Run.Record(new TestResult(id, step.Title, TestOutcome.Skipped, detail));
    }

    private void Show(StepView step, int index)
    {
        _current = step;
        StepCounter.Text = _single ? "Single test" : $"Step {index + 1} of {_steps.Count}";
        StepTitle.Text = step.Title;
        bool judged = step.Mode == StepMode.Judged;
        PassButton.Visibility = FailButton.Visibility = judged ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.Visibility = Visibility.Visible;
        Highlight(null);
        for (int i = 0; i < _segments.Count; i++)
            if (i >= index) PaintSegment(i, null, current: i == index);
        Present(step);
        Announce($"{StepCounter.Text}: {step.Title}");
    }

    private void ShowSummary()
    {
        _current = null;
        StepCounter.Text = "Automatic check";
        StepTitle.Text = _runCts.IsCancellationRequested ? "Stopped early" : "Finished";
        VerdictButtons.Visibility = Visibility.Collapsed;
        ExitButton.Content = "Close";
        ExitButton.Icon = new SymbolIcon { Symbol = SymbolRegular.Dismiss24 };   // the run is over: a plain Close
        var summary = new SummaryView(_ctx.Run, _ctx.Snapshot, _ctx.Session, saveReport: _ctx.LiveDevices);
        Present(summary);
        Announce($"Automatic check {StepTitle.Text.ToLowerInvariant()}. {summary.Headline}");
    }

    /// <summary>Slides the new screen in from the right (skipped when Windows animations are off).</summary>
    private void Present(FrameworkElement view)
    {
        Stage.Content = view;
        FocusInto(view);
        if (!AnimationsEnabled) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var shift = new TranslateTransform(40, 0);
        view.RenderTransform = shift;
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
    }

    /// <summary>
    /// Keyboard users continue in the new screen: its first control, or the screen itself when it
    /// has none (Tab then reaches the top bar). The old screen's focused button is gone by now.
    /// </summary>
    private void FocusInto(FrameworkElement view) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (Stage.Content != view || view.MoveFocus(new TraversalRequest(FocusNavigationDirection.First))) return;
            view.Focusable = true;
            view.FocusVisualStyle = null;
            view.Focus();
        }, DispatcherPriority.Loaded);

    /// <summary>Tells screen readers which step is showing: focus alone doesn't say it.</summary>
    private void Announce(string text) =>
        UIElementAutomationPeer.CreatePeerForElement(this)?.RaiseNotificationEvent(
            AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text, "Diagnostiq.Step");

    /// <summary>Colour plus shape: results stand out tall, skipped steps are a thin line, pending ones a plain bar.</summary>
    private void PaintSegment(int i, TestOutcome? outcome, bool current = false)
    {
        var seg = _segments[i];
        seg.Height = (outcome, current) switch
        {
            (_, true) => 6,
            (TestOutcome.Fail or TestOutcome.Warn, _) => 10,
            (TestOutcome.Skipped, _) => 2,
            _ => 6,
        };
        seg.SetResourceReference(Border.BackgroundProperty, (outcome, current) switch
        {
            (_, true) => "AccentFillColorDefaultBrush",
            (TestOutcome.Pass, _) => "SystemFillColorSuccessBrush",
            (TestOutcome.Warn, _) => "SystemFillColorCautionBrush",
            (TestOutcome.Fail, _) => "SystemFillColorCriticalBrush",
            (TestOutcome.Skipped, _) => "TextFillColorSecondaryBrush",
            _ => "ControlStrongStrokeColorDefaultBrush",
        });
        seg.ToolTip = current ? $"{_steps[i].Title}: in progress" : outcome is null ? _steps[i].Title : $"{_steps[i].Title}: {Outcomes.Label(outcome)}";
    }

    /// <summary>Hides the top bar so a step can use every pixel (screen colour test).</summary>
    public void SetFullBleed(bool on) => TopBar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>A step can point at the obvious answer (all keys pressed → Pass).</summary>
    /// <remarks>
    /// Neutral until then, so an accent-coloured Pass doesn't invite clicking it without looking;
    /// the icons stay green and red so the two can't be mixed up.
    /// </remarks>
    private void Highlight(TestOutcome? outcome)
    {
        PassButton.Appearance = outcome == TestOutcome.Pass ? ControlAppearance.Primary : ControlAppearance.Secondary;
        FailButton.Appearance = outcome == TestOutcome.Fail ? ControlAppearance.Danger : ControlAppearance.Secondary;
        TintIcon(PassButton, outcome != TestOutcome.Pass ? "SystemFillColorSuccessBrush" : null);
        TintIcon(FailButton, outcome != TestOutcome.Fail ? "SystemFillColorCriticalBrush" : null);
    }

    /// <summary>Colours a neutral button's icon; a highlighted one takes the button's own text colour.</summary>
    private static void TintIcon(Wpf.Ui.Controls.Button button, string? brushKey)
    {
        if (button.Icon is not { } icon) return;
        if (brushKey is null) icon.ClearValue(IconElement.ForegroundProperty);
        else icon.SetResourceReference(IconElement.ForegroundProperty, brushKey);
    }

    private void Pass_Click(object sender, RoutedEventArgs e) => _ctx.Judge(TestOutcome.Pass);
    private void Fail_Click(object sender, RoutedEventArgs e) => _ctx.Judge(TestOutcome.Fail);
    private void Skip_Click(object sender, RoutedEventArgs e) => _stepCts?.Cancel();

    /// <summary>Esc never stops a run (it's one of the keys being tested), but it closes the summary or the Exit prompt.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (_confirm is not null) _confirm.Hide(ContentDialogResult.None);
        else if (_finished) { _closeAllowed = true; Close(); }
    }

    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        if (_finished) { _closeAllowed = true; Close(); return; }
        if (await ConfirmStopAsync()) Stop();
    }

    private async Task<bool> ConfirmStopAsync()
    {
        if (_confirm is not null) return false;   // already asking
        _confirm = new ContentDialog(DialogHost)
        {
            Title = _single ? "Stop this test?" : "Stop the automatic check?",
            Content = new System.Windows.Controls.TextBlock
            {
                Text = _single ? "Anything it measured so far is kept; the rest is marked as skipped."
                               : "Results so far are kept and shown on the summary.",
                TextWrapping = TextWrapping.Wrap, MaxWidth = 420,
            },
            PrimaryButtonText = _single ? "Stop test" : "Stop check",
            CloseButtonText = "Keep testing",
            DefaultButton = ContentDialogButton.Close,
        };
        try { return await _confirm.ShowAsync() == ContentDialogResult.Primary; }
        finally { _confirm = null; }
    }

    /// <summary>Cancels the run; if a step is stuck in a driver call and can't react, closes the window anyway.</summary>
    private async void Stop()
    {
        _runCts.Cancel();
        await Task.Delay(StopGrace);
        if (_finished || _closed) return;
        App.LogError(new TimeoutException($"The {_current?.Title ?? "current"} step didn't stop within {StopGrace.TotalSeconds:0} s."));
        _closeAllowed = true;
        Close();
    }

    /// <summary>Alt+F4 and similar: treat like Exit.</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeAllowed || _finished)
        {
            try { _current?.Cleanup(); }
            catch (Exception ex) { App.LogError(ex); }
            return;
        }
        e.Cancel = true;
        if (await ConfirmStopAsync()) Stop();
    }
}
