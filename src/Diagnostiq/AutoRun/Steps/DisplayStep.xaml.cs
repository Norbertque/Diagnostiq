using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.AutoRun.Steps;

/// <summary>Solid colour fills for dead/stuck pixels and backlight bleed, plus a gradient for banding.</summary>
public partial class DisplayStep : StepView
{
    private static readonly (Brush Fill, string Name, Brush Hint)[] Screens =
    [
        (Brushes.White, "White", Brushes.Gray),
        (Brushes.Black, "Black", Brushes.DimGray),
        (new SolidColorBrush(Color.FromRgb(255, 0, 0)), "Red", Brushes.White),
        (new SolidColorBrush(Color.FromRgb(0, 255, 0)), "Green", Brushes.Black),
        (new SolidColorBrush(Color.FromRgb(0, 0, 255)), "Blue", Brushes.White),
        (new SolidColorBrush(Color.FromRgb(128, 128, 128)), "Grey", Brushes.White),
        (new LinearGradientBrush(Color.FromRgb(0, 0, 0), Color.FromRgb(255, 255, 255), 0), "Gradient (look for banding)", Brushes.Gray),
    ];

    private int _index = -1;

    public DisplayStep() => InitializeComponent();

    public override string Id => TestIds.Display;
    public override string Title => "Screen";

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        Ctx.Window.SetFullBleed(true);
        // handledEventsToo: the window itself marks Esc handled (so it never ends the run), but here it ends the colours.
        Ctx.Window.AddHandler(PreviewKeyDownEvent, (KeyEventHandler)OnKey, handledEventsToo: true);
        ColorLayer.Visibility = Visibility.Visible;
        Intro.Visibility = Visibility.Collapsed;
        ColorLayer.Focus();   // the focused Start button is hidden now; keep keys coming to this window
        _index = -1;
        Next();
    }

    private void ColorLayer_Click(object sender, MouseButtonEventArgs e) => Next();

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space or Key.Enter or Key.Right or Key.Down or Key.PageDown: Next(); break;
            case Key.Left or Key.Up or Key.PageUp when _index > 0: _index -= 2; Next(); break;
            case Key.Escape: Finish(); break;
        }
        e.Handled = true;
    }

    private void Next()
    {
        if (++_index >= Screens.Length) { Finish(); return; }
        var (fill, name, hint) = Screens[_index];
        ColorLayer.Background = fill;
        ColorHint.Text = $"{name} · {_index + 1} of {Screens.Length}";
        ColorHint.Foreground = hint;
        // The hint fades out so it doesn't hide pixels underneath.
        ColorHint.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames = { new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)), new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))),
                          new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))) },
        });
    }

    private void Finish()
    {
        Ctx.Window.RemoveHandler(PreviewKeyDownEvent, (KeyEventHandler)OnKey);
        Ctx.Window.SetFullBleed(false);
        ColorLayer.Visibility = Visibility.Collapsed;
        Intro.Visibility = Visibility.Visible;
        Question.Visibility = Visibility.Visible;
        StartButton.Content = "Show again";
        StartButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        // Focus the question, not "Show again": one Space too many would restart all the colours.
        Question.Focusable = true;
        Question.FocusVisualStyle = null;
        Question.Focus();   // Tab goes on to Pass and Fail
    }

    protected override string? Detail(TestOutcome outcome) => outcome switch
    {
        TestOutcome.Pass => $"Clean on all {Screens.Length} test screens.",
        TestOutcome.Fail => "Screen defects seen: dead pixels, lines, blotches or uneven backlight.",
        _ => null,
    };

    public override void Cleanup()
    {
        if (ColorLayer.Visibility != Visibility.Visible) return;
        Ctx.Window.RemoveHandler(PreviewKeyDownEvent, (KeyEventHandler)OnKey);
        Ctx.Window.SetFullBleed(false);
    }
}
