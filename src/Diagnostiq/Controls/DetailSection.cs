using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Diagnostiq.Core;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Diagnostiq.Controls;

/// <summary>A titled card of label/value rows, for the Hardware and Windows detail pages.</summary>
public sealed class DetailSection : Border
{
    private readonly StackPanel _body = new();
    private readonly Grid _rows = new();

    public DetailSection(string title)
    {
        SetResourceReference(StyleProperty, "Diag.Card");
        Margin = new Thickness(0, 0, 0, 12);
        _rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        _rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var heading = new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 8) };
        heading.SetResourceReference(StyleProperty, "Diag.Text.BodyStrong");
        heading.FontSize = 16;
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        _body.Children.Add(heading);
        _body.Children.Add(_rows);
        Child = _body;
    }

    /// <summary>Adds a row; null or empty values are skipped so sections only show what the laptop reported.</summary>
    public DetailSection Row(string label, string? value, string? pill = null, CheckState? state = null)
    {
        if (string.IsNullOrWhiteSpace(value) && pill is null) return this;
        int r = _rows.RowDefinitions.Count;
        _rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var l = new TextBlock { Text = label, Margin = new Thickness(0, 4, 16, 4) };
        l.SetResourceReference(StyleProperty, "Diag.Text.Body");
        l.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        Grid.SetRow(l, r);
        _rows.Children.Add(l);

        // A WrapPanel hands its width to the text so long values wrap, and the pill moves to the next line when
        // there's no room beside it (a horizontal StackPanel measures with infinite width and clips both).
        var v = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        if (!string.IsNullOrWhiteSpace(value))
        {
            var t = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                                    Margin = new Thickness(0, 0, pill is null ? 0 : 10, 0) };
            t.SetResourceReference(StyleProperty, "Diag.Text.Body");
            v.Children.Add(t);
        }
        if (pill is not null) v.Children.Add(new StatusPill { Text = pill, State = state, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(v, r);
        Grid.SetColumn(v, 1);
        _rows.Children.Add(v);
        return this;
    }

    /// <summary>Small caption heading inside the section ("Drive 0", "Slot A").</summary>
    public DetailSection Group(string title)
    {
        int r = _rows.RowDefinitions.Count;
        _rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var t = new TextBlock { Text = title, Margin = new Thickness(0, r == 0 ? 0 : 12, 0, 2) };
        t.SetResourceReference(StyleProperty, "Diag.Text.BodyStrong");
        Grid.SetRow(t, r);
        Grid.SetColumnSpan(t, 2);
        _rows.Children.Add(t);
        return this;
    }

    /// <summary>A full-width line of text (explanations, "nothing found").</summary>
    public DetailSection Note(string text)
    {
        NoteBlock(text);
        return this;
    }

    /// <summary>Like <see cref="Note"/>, but returns the text block so it can be updated later (live readings).</summary>
    public TextBlock NoteBlock(string text)
    {
        int r = _rows.RowDefinitions.Count;
        _rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 4, 0, 4) };
        t.SetResourceReference(StyleProperty, "Diag.Text.Body");
        Grid.SetRow(t, r);
        Grid.SetColumnSpan(t, 2);
        _rows.Children.Add(t);
        return t;
    }
}
