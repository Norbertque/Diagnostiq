using System.Windows;
using System.Windows.Controls;
using Diagnostiq.Controls;
using Diagnostiq.Presentation;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Diagnostiq.Views.Manual;

/// <summary>Results collected so far in this session.</summary>
public sealed class ReportPage : UserControl
{
    private readonly MainWindow _window;
    private readonly StackPanel _page = new() { Margin = new Thickness(32, 24, 32, 32), MaxWidth = 1000 };

    public ReportPage(MainWindow window)
    {
        _window = window;
        Content = new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => { _window.SessionChanged += Build; Build(); };
        Unloaded += (_, _) => _window.SessionChanged -= Build;
    }

    private void Build()
    {
        _page.Children.Clear();
        var title = new TextBlock { Text = "Report", Margin = new Thickness(0, 0, 0, 20) };
        title.SetResourceReference(StyleProperty, "Diag.Text.Title");
        _page.Children.Add(title);

        var results = new DetailSection("Results so far");
        var list = _window.Session.Results;
        if (list.Count == 0) results.Note("No tests run yet. Start the Automatic check or run tests from the Tests page.");
        foreach (var r in list) results.Row(r.Title, r.Detail, Outcomes.Label(r.Outcome), Outcomes.State(r.Outcome));
        _page.Children.Add(results);
    }
}
