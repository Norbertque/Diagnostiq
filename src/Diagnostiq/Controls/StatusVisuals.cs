using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Diagnostiq.Core;
using Diagnostiq.Core.Testing;
using Wpf.Ui.Controls;

namespace Diagnostiq.Controls;

/// <summary>
/// Pass/warn/fail/unknown icon in the matching Fluent status color. A null state means
/// "still checking". Shape differs per state too, so it doesn't rely on color alone.
/// </summary>
public sealed class StatusIcon : Decorator
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(CheckState?), typeof(StatusIcon), new PropertyMetadata(null, (d, _) => ((StatusIcon)d).Update()));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(StatusIcon), new PropertyMetadata(20.0, (d, _) => ((StatusIcon)d).Update()));

    /// <summary>Test results: same as <see cref="State"/> plus a distinct "skipped" icon. Wins over State when set.</summary>
    public static readonly DependencyProperty OutcomeProperty = DependencyProperty.Register(
        nameof(Outcome), typeof(TestOutcome?), typeof(StatusIcon), new PropertyMetadata(null, (d, _) => ((StatusIcon)d).Update()));

    public TestOutcome? Outcome { get => (TestOutcome?)GetValue(OutcomeProperty); set => SetValue(OutcomeProperty, value); }

    private readonly SymbolIcon _icon = new() { Filled = true };

    public StatusIcon()
    {
        Child = _icon;
        Focusable = false;
        Update();
    }

    public CheckState? State { get => (CheckState?)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    private void Update()
    {
        _icon.FontSize = Size;
        if (Outcome == TestOutcome.Skipped)
        {
            _icon.Symbol = SymbolRegular.SubtractCircle24;
            _icon.Filled = false;
            _icon.SetResourceReference(SymbolIcon.ForegroundProperty, "TextFillColorSecondaryBrush");
            AutomationProperties.SetName(this, "Skipped");
            return;
        }
        var state = Outcome switch
        {
            TestOutcome.Pass => CheckState.Pass,
            TestOutcome.Warn => CheckState.Warn,
            TestOutcome.Fail => CheckState.Fail,
            _ => State,
        };
        (_icon.Symbol, var brush, var name, _icon.Filled) = state switch
        {
            CheckState.Pass => (SymbolRegular.CheckmarkCircle24, "SystemFillColorSuccessBrush", "Passed", true),
            CheckState.Warn => (SymbolRegular.Warning24, "SystemFillColorCautionBrush", "Warning", true),
            CheckState.Fail => (SymbolRegular.DismissCircle24, "SystemFillColorCriticalBrush", "Failed", true),
            CheckState.Unknown => (SymbolRegular.QuestionCircle24, "TextFillColorSecondaryBrush", "Unknown", false),
            _ => (SymbolRegular.Circle24, "TextFillColorTertiaryBrush", "Checking", false),
        };
        _icon.SetResourceReference(SymbolIcon.ForegroundProperty, brush);
        AutomationProperties.SetName(this, name);
    }

    // A Decorator has no automation peer, so without this the name set above never reaches screen readers,
    // while the icon font's private-use glyph shows up as unreadable text.
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(StatusIcon owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override string GetClassNameCore() => nameof(StatusIcon);
        protected override List<AutomationPeer>? GetChildrenCore() => null;   // hide the glyph
    }
}

/// <summary>Rounded status label ("Healthy", "Needs admin") using the Diag.Pill.* token styles.</summary>
public sealed class StatusPill : Border
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(CheckState?), typeof(StatusPill), new PropertyMetadata(null, (d, _) => ((StatusPill)d).Update()));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusPill), new PropertyMetadata(null, (d, _) => ((StatusPill)d).Update()));

    private readonly System.Windows.Controls.TextBlock _text = new() { FontSize = 12, FontWeight = FontWeights.SemiBold };

    public StatusPill()
    {
        Child = _text;
        Update();
    }

    public CheckState? State { get => (CheckState?)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    private void Update()
    {
        _text.Text = Text;
        Visibility = string.IsNullOrEmpty(Text) ? Visibility.Collapsed : Visibility.Visible;
        SetResourceReference(StyleProperty, State switch
        {
            CheckState.Pass => "Diag.Pill.Pass",
            CheckState.Warn => "Diag.Pill.Warn",
            CheckState.Fail => "Diag.Pill.Fail",
            _ => "Diag.Pill.Neutral",
        });
    }
}
