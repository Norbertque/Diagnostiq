using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Diagnostiq.Controls;

/// <summary>Live reading tile: label, big number, one line of context and a sparkline.</summary>
public sealed class MetricCard : Border
{
    private readonly TextBlock _value = new() { Text = "–" };
    private readonly TextBlock _sub = new() { Margin = new Thickness(0, 2, 0, 0) };
    private readonly SymbolIcon _icon = new() { FontSize = 18, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };

    public MetricCard()
    {
        SetResourceReference(StyleProperty, "Diag.Card");
        Margin = new Thickness(0, 0, 12, 12);
        _label.SetResourceReference(StyleProperty, "Diag.Text.Caption");
        _value.SetResourceReference(StyleProperty, "Diag.Text.Metric");
        _sub.SetResourceReference(StyleProperty, "Diag.Text.Caption");
        _icon.SetResourceReference(SymbolIcon.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        Spark.SetResourceReference(Sparkline.StrokeProperty, "AccentFillColorDefaultBrush");

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(_icon);
        header.Children.Add(_label);
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_value);
        stack.Children.Add(_sub);
        stack.Children.Add(Spark);
        Child = stack;
    }

    public Sparkline Spark { get; } = new() { Height = 40, Margin = new Thickness(0, 12, 0, 0) };

    public string Label { get => _label.Text; set { _label.Text = value; AutomationProperties.SetName(this, value); } }
    public SymbolRegular Icon { get => _icon.Symbol; set => _icon.Symbol = value; }

    public void Set(string value, string? sub = null)
    {
        _value.Text = value;
        _sub.Text = sub ?? "";
    }
}
