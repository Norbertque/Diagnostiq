using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Diagnostiq.Core.Testing;
using Wpf.Ui.Controls;

namespace Diagnostiq.AutoRun;

/// <summary>
/// Fullscreen, topmost shell for the Automatic run: shows one step at a time and moves on
/// as soon as it's done. Esc and Alt+F4 never leave; only the Exit button does (with a
/// confirmation), so pressing keys during the tests can't end the run by accident.
/// </summary>
public partial class AutoRunWindow : Window
{
    private readonly AutoRunContext _ctx;
    private readonly List<StepView> _steps;
    private readonly List<Border> _segments = [];
    private readonly bool _single;
    private readonly CancellationTokenSource _runCts = new();
    private CancellationTokenSource? _stepCts;
    private StepView? _current;
    private bool _finished, _closeAllowed;

    /// <param name="single">Manual mode: one test, no summary; the window closes when it's done.</param>
    public AutoRunWindow(AutoRunContext ctx, IEnumerable<StepView> steps, bool single = false)
    {
        InitializeComponent();
        _ctx = ctx;
        _ctx.Window = this;
        _single = single;
        Segments.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        _steps = steps.Where(s => s.IsApplicable(ctx)).ToList();
        foreach (var _ in _steps)
        {
            var seg = new Border { Width = 28, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(2, 0, 2, 0) };
            _segments.Add(seg);
            Segments.Children.Add(seg);
        }
        _ctx.Suggested += outcome => Dispatcher.Invoke(() => Highlight(outcome));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) e.Handled = true; };
        Closing += OnClosing;
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
        for (int i = 0; i < _steps.Count && !_runCts.IsCancellationRequested; i++)
        {
            var step = _steps[i];
            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(_runCts.Token);
            _stepCts = stepCts;
            Show(step, i);
            try
            {
                await step.RunAsync(_ctx, stepCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Skip (or Exit): anything the step didn't record counts as skipped.
                foreach (var id in step.ResultIds)
                    if (_ctx.Run[id] is null) _ctx.Run.Record(new TestResult(id, step.Title, TestOutcome.Skipped));
            }
            finally
            {
                step.Cleanup();
                _stepCts = null;
            }
            PaintSegment(i, _ctx.Run[step.Id]?.Outcome);
        }
        if (_single) { _finished = _closeAllowed = true; Close(); }
        else ShowSummary();
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
    }

    private void ShowSummary()
    {
        _finished = true;
        _current = null;
        StepCounter.Text = "Automatic check";
        StepTitle.Text = _runCts.IsCancellationRequested ? "Stopped early" : "Finished";
        VerdictButtons.Visibility = Visibility.Collapsed;
        ExitButton.Content = "Close";
        Present(new SummaryView(_ctx.Run, _ctx.Snapshot, _ctx.Session, saveReport: _ctx.LiveDevices));
    }

    /// <summary>Slides the new screen in from the right (skipped when Windows animations are off).</summary>
    private void Present(FrameworkElement view)
    {
        Stage.Content = view;
        if (!AnimationsEnabled) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var shift = new TranslateTransform(40, 0);
        view.RenderTransform = shift;
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
    }

    private void PaintSegment(int i, TestOutcome? outcome, bool current = false) =>
        _segments[i].SetResourceReference(Border.BackgroundProperty, (outcome, current) switch
        {
            (_, true) => "AccentFillColorDefaultBrush",
            (TestOutcome.Pass, _) => "SystemFillColorSuccessBrush",
            (TestOutcome.Warn, _) => "SystemFillColorCautionBrush",
            (TestOutcome.Fail, _) => "SystemFillColorCriticalBrush",
            (TestOutcome.Skipped, _) => "TextFillColorTertiaryBrush",
            _ => "ControlStrongStrokeColorDefaultBrush",
        });

    /// <summary>Hides the top bar so a step can use every pixel (screen colour test).</summary>
    public void SetFullBleed(bool on) => TopBar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>A step can point at the obvious answer (all keys pressed → Pass).</summary>
    /// <remarks>Neutral until then, so an accent-coloured Pass doesn't invite clicking it without looking.</remarks>
    private void Highlight(TestOutcome? outcome)
    {
        PassButton.Appearance = outcome == TestOutcome.Pass ? ControlAppearance.Primary : ControlAppearance.Secondary;
        FailButton.Appearance = outcome == TestOutcome.Fail ? ControlAppearance.Danger : ControlAppearance.Secondary;
    }

    private void Pass_Click(object sender, RoutedEventArgs e) => _ctx.Judge(TestOutcome.Pass);
    private void Fail_Click(object sender, RoutedEventArgs e) => _ctx.Judge(TestOutcome.Fail);
    private void Skip_Click(object sender, RoutedEventArgs e) => _stepCts?.Cancel();

    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        if (_finished) { _closeAllowed = true; Close(); return; }
        if (await ConfirmStopAsync()) _runCts.Cancel();
    }

    private async Task<bool> ConfirmStopAsync()
    {
        var result = await new ContentDialog(DialogHost)
        {
            Title = "Stop the automatic check?",
            Content = new System.Windows.Controls.TextBlock
            {
                Text = "Results so far are kept and shown on the summary.", TextWrapping = TextWrapping.Wrap, MaxWidth = 420,
            },
            PrimaryButtonText = "Stop",
            CloseButtonText = "Keep testing",
            DefaultButton = ContentDialogButton.Close,
        }.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    /// <summary>Alt+F4 and similar: treat like Exit.</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeAllowed || _finished) { _current?.Cleanup(); return; }
        e.Cancel = true;
        if (await ConfirmStopAsync()) _runCts.Cancel();
    }
}
