using System.Windows;
using System.Windows.Media;

namespace Diagnostiq.Controls;

/// <summary>Minimal live line chart (last <see cref="Capacity"/> points), for the stress dashboard.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly List<double> _values = [];

    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public int Capacity { get; set; } = 180;

    /// <summary>Fixed axis bounds keep lines comparable (e.g. 30–100 °C); null = fit the data.</summary>
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }

    /// <summary>Timeline mode for a run of known length: points fill from the left edge instead of scrolling in from the right.</summary>
    public bool AnchorLeft { get; set; }

    public void Add(double value)
    {
        _values.Add(value);
        if (_values.Count > Capacity) _values.RemoveAt(0);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_values.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0) return;
        double min = Minimum ?? _values.Min(), max = Maximum ?? _values.Max();
        if (max - min < 1e-6) { max += 1; min -= 1; }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            double step = ActualWidth / (Capacity - 1);
            double x0 = AnchorLeft ? 0 : ActualWidth - (_values.Count - 1) * step;   // otherwise newest point on the right edge
            for (int i = 0; i < _values.Count; i++)
            {
                double y = ActualHeight - (Math.Clamp(_values[i], min, max) - min) / (max - min) * (ActualHeight - 2) - 1;
                var p = new Point(x0 + i * step, y);
                if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round }, geometry);
    }
}
