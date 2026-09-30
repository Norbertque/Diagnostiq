using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Diagnostiq.Core;
using Diagnostiq.Core.Scoring;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Diagnostiq.Controls;

/// <summary>Health score, verdict, what cost points and what wasn't tested.</summary>
public sealed class ScoreCard : Border
{
    public ScoreCard(HealthReport h, int maxDeductions = 6)
    {
        SetResourceReference(StyleProperty, "Diag.Surface");
        Margin = new Thickness(0, 0, 0, 16);

        var state = h.Verdict switch { HealthVerdict.Excellent => CheckState.Pass, HealthVerdict.Ok => CheckState.Warn, _ => CheckState.Fail };
        var left = new StackPanel { Margin = new Thickness(0, 0, 32, 0), MinWidth = 140 };
        var score = new TextBlock { Text = h.Score.ToString() };
        score.SetResourceReference(StyleProperty, "Diag.Text.Display");
        // The card (a Border) has no automation peer; the number does, so it carries the full sentence.
        AutomationProperties.SetName(score, $"Health score {h.Score} of 100, {HealthScore.Label(h.Verdict)}");
        var of = new TextBlock { Text = "health score, out of 100", Margin = new Thickness(0, 0, 0, 8) };
        of.SetResourceReference(StyleProperty, "Diag.Text.Caption");
        left.Children.Add(score);
        left.Children.Add(of);
        left.Children.Add(new StatusPill { Text = HealthScore.Label(h.Verdict), State = state });

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        // Before any test, the score only reflects what the startup scan found; don't let it read as a clean bill.
        bool nothingTested = h.NothingTested;
        if (nothingTested)
        {
            var note = Body("No tests run yet, so this score only covers battery wear, drive health and driver problems.");
            note.Margin = new Thickness(0, 0, 0, h.Deductions.Count > 0 ? 6 : 0);
            right.Children.Add(note);
        }
        else if (h.Deductions.Count == 0)
            right.Children.Add(Body("No problems found in the tests that were run."));
        foreach (var d in h.Deductions.Take(maxDeductions))
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = new StatusIcon { State = d.Severity == Severity.Critical ? CheckState.Fail : CheckState.Warn, Size = 18, Margin = new Thickness(0, 1, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            var text = Body($"{d.Area}: {d.Reason} (−{d.Points})");
            Grid.SetColumn(text, 1);
            row.Children.Add(icon);
            row.Children.Add(text);
            right.Children.Add(row);
        }
        if (h.Deductions.Count > maxDeductions)
            right.Children.Add(Caption($"{h.Deductions.Count - maxDeductions} more in the full report."));
        if (h.NotTested.Count > 0 && !nothingTested)   // listing all of them repeats "No tests run yet"
            right.Children.Add(Caption($"Not tested: {string.Join(", ", h.NotTested)}."));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        Child = grid;
    }

    private static TextBlock Body(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(StyleProperty, "Diag.Text.Body");
        return t;
    }

    private static TextBlock Caption(string text)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 0) };
        t.SetResourceReference(StyleProperty, "Diag.Text.Caption");
        return t;
    }
}
