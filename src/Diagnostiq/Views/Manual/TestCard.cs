using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Diagnostiq.AutoRun;
using Diagnostiq.Controls;
using Diagnostiq.Core.Testing;
using Diagnostiq.Presentation;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Diagnostiq.Views.Manual;

/// <summary>One test on the Manual page: what it checks, its last result, and a Run button.</summary>
public sealed class TestCard : Border
{
    private readonly string _id;
    private readonly StatusPill _pill = new();
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Wpf.Ui.Controls.Button _run = new() { Content = "Run", Icon = new SymbolIcon(SymbolRegular.Play24), MinHeight = 36 };

    public TestCard(string id, string title, string description, SymbolRegular icon, Action run)
    {
        _id = id;
        SetResourceReference(StyleProperty, "Diag.Card");
        Margin = new Thickness(0, 0, 12, 12);
        AutomationProperties.SetName(this, title);

        var symbol = new SymbolIcon(icon) { FontSize = 20, Margin = new Thickness(0, 2, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        symbol.SetResourceReference(SymbolIcon.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        var heading = new TextBlock { Text = title };
        heading.SetResourceReference(StyleProperty, "Diag.Text.BodyStrong");
        var desc = new TextBlock { Text = description };
        desc.SetResourceReference(StyleProperty, "Diag.Text.Caption");
        _detail.SetResourceReference(StyleProperty, "Diag.Text.Caption");
        _run.Click += (_, _) => run();

        var text = new StackPanel();
        text.Children.Add(heading);
        text.Children.Add(desc);

        var top = new DockPanel();
        DockPanel.SetDock(symbol, Dock.Left);
        top.Children.Add(symbol);
        top.Children.Add(text);

        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        DockPanel.SetDock(_run, Dock.Right);
        bottom.Children.Add(_run);
        _pill.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(_pill);

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(bottom);
        stack.Children.Add(_detail);
        Child = stack;
    }

    public void Refresh(TestRun session)
    {
        var r = session[_id];
        _pill.Text = Outcomes.Label(r?.Outcome);
        _pill.State = Outcomes.State(r?.Outcome);
        _detail.Text = r?.Detail ?? "";
        _detail.Visibility = string.IsNullOrEmpty(r?.Detail) ? Visibility.Collapsed : Visibility.Visible;
        _run.Content = r is null ? "Run" : "Run again";
    }
}
