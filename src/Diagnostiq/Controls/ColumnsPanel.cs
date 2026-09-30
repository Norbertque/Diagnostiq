using System.Windows;
using System.Windows.Controls;

namespace Diagnostiq.Controls;

/// <summary>
/// Equal-width columns where each row is only as tall as its tallest item
/// (UniformGrid makes every cell as tall as the tallest item overall).
/// </summary>
public sealed class ColumnsPanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(ColumnsPanel),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure), v => (int)v >= 1);

    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }

    protected override Size MeasureOverride(Size available)
    {
        double columnWidth = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width / Columns;
        double height = 0, widest = 0;
        foreach (var row in Rows())
        {
            double rowHeight = 0;
            foreach (UIElement child in row)
            {
                child.Measure(new Size(columnWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
                widest = Math.Max(widest, child.DesiredSize.Width);
            }
            height += rowHeight;
        }
        return new Size(double.IsInfinity(available.Width) ? widest * Columns : available.Width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double columnWidth = final.Width / Columns, y = 0;
        foreach (var row in Rows())
        {
            double rowHeight = row.Max(c => c.DesiredSize.Height);
            for (int i = 0; i < row.Count; i++)
                row[i].Arrange(new Rect(i * columnWidth, y, columnWidth, rowHeight));
            y += rowHeight;
        }
        return final;
    }

    private IEnumerable<List<UIElement>> Rows() =>
        InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).Chunk(Columns).Select(c => c.ToList());
}
