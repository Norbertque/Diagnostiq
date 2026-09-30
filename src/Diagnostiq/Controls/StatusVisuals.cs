using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Diagnostiq.Core;
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
        (_icon.Symbol, var brush, var name, _icon.Filled) = State switch
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
