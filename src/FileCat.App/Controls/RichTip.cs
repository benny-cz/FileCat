using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FileCat.App.Controls;

/// <summary>
/// A tooltip laid out rather than run together (I80; the owner: "improve tooltips for icons, style them better according
/// to the selected theme"): what the button does and its key, then what it does in more words, then a legend of terms
/// and what each means, then how else it is used, in the theme's colors (Styles.axaml: <c>ToolTip</c>, <c>tipTitle</c>,
/// <c>tipKey</c>, <c>tipTerm</c>, <c>tipHint</c>). Its plain text, <see cref="Text"/>, is what screen readers are given
/// and what <see cref="ToString"/> returns.
/// </summary>
public sealed class RichTip : StackPanel
{
    public RichTip(string title, string? key = null, string? detail = null, IEnumerable<string?>? hints = null,
        IEnumerable<(string Term, string Meaning)>? legend = null)
    {
        Spacing = 3;
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(new TextBlock { Classes = { "tipTitle" }, Text = title, TextWrapping = TextWrapping.Wrap });
        if (key is { Length: > 0 })
        {
            var keyText = new TextBlock { Classes = { "tipKey" }, Text = key, Margin = new(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(keyText, 1);
            head.Children.Add(keyText);
        }
        Children.Add(head);
        var text = new System.Text.StringBuilder(title);
        if (key is { Length: > 0 }) text.Append(" (").Append(key).Append(')');
        if (detail is { Length: > 0 })
        {
            Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap });
            text.Append('\n').Append(detail);
        }
        // A legend: each term in the theme's key color, its meaning beside it, aligned in two columns.
        if (legend?.ToList() is { Count: > 0 } rows)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new(0, 2, 0, 2) };
            for (int i = 0; i < rows.Count; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var term = new TextBlock { Classes = { "tipTerm" }, Text = rows[i].Term, Margin = new(0, 1, 12, 1) };
                var meaning = new TextBlock { Text = rows[i].Meaning, TextWrapping = TextWrapping.Wrap, Margin = new(0, 1, 0, 1) };
                Grid.SetRow(term, i);
                Grid.SetRow(meaning, i);
                Grid.SetColumn(meaning, 1);
                grid.Children.Add(term);
                grid.Children.Add(meaning);
                text.Append('\n').Append(rows[i].Term).Append(" — ").Append(rows[i].Meaning);
            }
            Children.Add(grid);
        }
        foreach (var hint in hints ?? [])
        {
            if (hint is not { Length: > 0 }) continue;
            Children.Add(new TextBlock { Classes = { "tipHint" }, Text = hint, TextWrapping = TextWrapping.Wrap });
            text.Append('\n').Append(hint);
        }
        Text = text.ToString();
    }

    /// <summary>The tip as plain text: the title and its key, then the rest, a line each.</summary>
    public string Text { get; }

    public override string ToString() => Text;
}

/// <summary>
/// A column value's tooltip, so its explanation does not read as one wall of text: the column's name small and quiet,
/// the value prominent, then the explanation a line each, styled by what the line is. A tag line ("Tag 0x…") reads as
/// code in the theme's key color; the line saying where and when the value was read ("Scope: …", "Observed …") is
/// small and quiet; the rest is text. Its plain <see cref="Text"/> is the column, the value and the lines, as before.
/// </summary>
public sealed class ValueTip : StackPanel
{
    public ValueTip(string column, string value, IReadOnlyList<string> lines)
    {
        Spacing = 2;
        Children.Add(new TextBlock { Classes = { "tipLabel" }, Text = column });
        Children.Add(new TextBlock { Classes = { "tipValue" }, Text = value, TextWrapping = TextWrapping.Wrap });
        var text = new System.Text.StringBuilder(column).Append(": ").Append(value);
        for (int i = 0; i < lines.Count; i++)
        {
            var line = new TextBlock { Text = lines[i], TextWrapping = TextWrapping.Wrap, Margin = new(0, i == 0 ? 4 : 0, 0, 0) };
            if (IsCode(lines[i])) line.Classes.Add("tipCode");
            else if (IsSource(lines[i])) line.Classes.Add("tipSource");
            Children.Add(line);
            text.Append('\n').Append(lines[i]);
        }
        Text = text.ToString();
    }

    internal static bool IsCode(string line) => line.StartsWith("Tag 0x", StringComparison.Ordinal);

    internal static bool IsSource(string line) =>
        line.StartsWith("Scope: ", StringComparison.Ordinal) || line.StartsWith("Observed ", StringComparison.Ordinal);

    /// <summary>The tip as plain text: "column: value", then the explanation's lines.</summary>
    public string Text { get; }

    public override string ToString() => Text;
}
