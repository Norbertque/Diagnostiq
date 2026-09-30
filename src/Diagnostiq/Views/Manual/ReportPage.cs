using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Diagnostiq.Controls;
using Diagnostiq.Presentation;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Diagnostiq.Views.Manual;

/// <summary>Results collected so far in this session.</summary>
public sealed class ReportPage : UserControl
{
    private readonly MainWindow _window;
    private readonly StackPanel _page = new() { Margin = new Thickness(32, 24, 32, 32) };

    public ReportPage(MainWindow window)
    {
        _window = window;
        _page.SetResourceReference(MaxWidthProperty, "Diag.Page.MaxWidth");
        Content = new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => { _window.SessionChanged += Build; Build(); };
        Unloaded += (_, _) => _window.SessionChanged -= Build;
    }

    private void Build()
    {
        _page.Children.Clear();
        var title = new TextBlock { Text = "Report", Margin = new Thickness(0, 0, 0, 20) };
        title.SetResourceReference(StyleProperty, "Diag.Text.Title");
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        _page.Children.Add(title);

        var snapshot = _window.Snapshot!;
        _page.Children.Add(new ScoreCard(Core.Scoring.HealthScore.Compute(snapshot, _window.Session)));
        // Same size and icon as the other primary actions (Start automatic check, the summary's Open report).
        var save = new Wpf.Ui.Controls.Button
        {
            Content = "Save and open report",
            Appearance = ControlAppearance.Primary,
            Icon = new SymbolIcon(SymbolRegular.Open24),
            MinHeight = 40,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 0, 16),
        };
        save.Click += async (_, _) =>
        {
            try
            {
                var saved = await Reports.SaveAsync(snapshot, _window.Session);
                Reports.Open(saved.HtmlPath);
                // The browser shows the report but not where the file went (next to the app, or Documents).
                _window.Notify("Report saved", System.IO.Path.GetDirectoryName(saved.HtmlPath)!, icon: SymbolRegular.CheckmarkCircle24,
                    timeout: TimeSpan.FromSeconds(8));
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                // A full or write-protected USB stick is the usual cause; leave the message up long enough to read.
                _window.Notify("Couldn't save the report", $"{ex.Message} Check that the drive has free space and isn't write-protected, then try again.",
                    icon: SymbolRegular.ErrorCircle24, timeout: TimeSpan.FromSeconds(15));
            }
        };
        _page.Children.Add(save);

        var results = new DetailSection("Results so far");
        var list = _window.Session.Results;
        if (list.Count == 0) results.Note("No tests run yet. Start the Automatic check or run tests from the Tests page.");
        foreach (var r in list) results.Row(r.Title, r.Detail, Outcomes.Label(r.Outcome), Outcomes.State(r.Outcome));
        _page.Children.Add(results);
    }
}
