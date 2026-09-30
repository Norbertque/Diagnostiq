using System.Windows;
using System.Windows.Controls;
using Diagnostiq.Controls;
using Diagnostiq.Core;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.AutoRun;

/// <summary>End of the Automatic run: every result in the order it was tested.</summary>
public partial class SummaryView : UserControl
{
    public SummaryView(TestRun run, SystemSnapshot snapshot)
    {
        InitializeComponent();
        var results = run.Results;
        int pass = results.Count(r => r.Outcome == TestOutcome.Pass);
        int warn = results.Count(r => r.Outcome == TestOutcome.Warn);
        int fail = results.Count(r => r.Outcome == TestOutcome.Fail);
        int skipped = results.Count(r => r.Outcome == TestOutcome.Skipped);

        Heading.Text = fail > 0 ? $"{fail} problem{(fail == 1 ? "" : "s")} found"
                     : warn > 0 ? "Working, with a few things to check"
                     : "Everything tested works";
        Subheading.Text = $"{snapshot.Device?.DisplayName ?? "This laptop"} · {results.Count} tests · " +
                          Presentation.Format.Minutes(DateTimeOffset.Now - run.Started);

        AddCount(pass, "passed", CheckState.Pass);
        AddCount(warn, warn == 1 ? "warning" : "warnings", CheckState.Warn);
        AddCount(fail, "failed", CheckState.Fail);
        AddCount(skipped, "skipped", null);

        ResultList.ItemsSource = results.Select(r => new Row(r)).ToList();
    }

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
    }
}
