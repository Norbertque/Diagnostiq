using System.IO;
using System.Windows;
using System.Windows.Controls;
using Diagnostiq.Controls;
using Diagnostiq.Core;
using Diagnostiq.Core.Formatting;
using Diagnostiq.Core.Reporting;
using Diagnostiq.Core.Scoring;
using Diagnostiq.Core.Testing;
using Diagnostiq.Presentation;

namespace Diagnostiq.AutoRun;

/// <summary>End of the Automatic run: health score, the saved report, and every result in test order.</summary>
public partial class SummaryView : UserControl
{
    private SavedReport? _saved;

    /// <summary>The one-line verdict ("2 problems found"), for the window to announce.</summary>
    public string Headline => Heading.Text;

    /// <param name="session">Earlier manual results, included in the score and the report.</param>
    public SummaryView(TestRun run, SystemSnapshot snapshot, TestRun? session = null, bool saveReport = true)
    {
        InitializeComponent();
        var results = run.Results;
        int pass = results.Count(r => r.Outcome == TestOutcome.Pass);
        int warn = results.Count(r => r.Outcome == TestOutcome.Warn);
        int fail = results.Count(r => r.Outcome == TestOutcome.Fail);
        int skipped = results.Count(r => r.Outcome == TestOutcome.Skipped);

        Heading.Text = fail > 0 ? $"{fail} problem{(fail == 1 ? "" : "s")} found"
                     : warn > 0 ? "Working, with a few things to check"
                     : pass == 0 ? "Nothing was tested"
                     : "Everything tested works";
        Subheading.Text = $"{snapshot.Device?.DisplayName ?? "This laptop"} · {results.Count} test{(results.Count == 1 ? "" : "s")} · " +
                          Format.Minutes(DateTimeOffset.Now - run.Started);

        var all = session is null ? run : Reports.Combine(session, run);
        ScoreHost.Content = new ScoreCard(HealthScore.Compute(snapshot, all));

        AddCount(pass, "passed", CheckState.Pass);
        AddCount(warn, warn == 1 ? "warning" : "warnings", CheckState.Warn);
        AddCount(fail, "failed", CheckState.Fail);
        AddCount(skipped, "skipped", null);
        ResultList.ItemsSource = results.Select(r => new Row(r)).ToList();

        if (saveReport) Loaded += async (_, _) => await SaveAsync(snapshot, all);
        else ReportTitle.Text = "The report is saved when the run finishes.";
    }

    private async Task SaveAsync(SystemSnapshot snapshot, TestRun all)
    {
        try
        {
            _saved = await Reports.SaveAsync(snapshot, all);
            ReportTitle.Text = "Report saved";
            ReportPath.Text = Path.GetDirectoryName(_saved.HtmlPath);
            ReportActions.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportTitle.Text = "Couldn't save the report";
            ReportPath.Text = $"{ex.Message} Check that the drive has free space and isn't write-protected.";
            ReportPath.TextTrimming = TextTrimming.None;   // the whole reason, not just the start of it
        }
    }

    private void OpenReport_Click(object sender, RoutedEventArgs e) { if (_saved is not null) Reports.Open(_saved.HtmlPath); }
    private void ShowFolder_Click(object sender, RoutedEventArgs e) { if (_saved is not null) Reports.ShowInFolder(_saved.HtmlPath); }

    private void AddCount(int n, string label, CheckState? state)
    {
        if (n == 0) return;
        Counts.Children.Add(new StatusPill { Text = $"{n} {label}", State = state, Margin = new Thickness(0, 0, 8, 0) });
    }

    private sealed record Row(TestResult Result)
    {
        public TestOutcome Outcome => Result.Outcome;
        public string Title => Result.Title;
        public string DetailText => Result.Detail ?? (Result.Outcome == TestOutcome.Skipped ? "Skipped." : "");

        /// <summary>What screen readers announce for the row (the status is otherwise only an icon).</summary>
        public override string ToString() =>
            string.IsNullOrEmpty(Result.Detail) ? $"{Title}: {Outcomes.Label(Outcome)}." : $"{Title}: {Outcomes.Label(Outcome)}. {Result.Detail}";
    }
}
