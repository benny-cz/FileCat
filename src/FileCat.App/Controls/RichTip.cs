using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FileCat.App.Controls;

/// <summary>
/// A tooltip laid out rather than run together (I80; the owner: "improve tooltips for icons, style them better according
/// to the selected theme"): what the button does and its key, then what it does in more words, then how else it is used,
/// in the theme's colors (Styles.axaml: <c>ToolTip</c>, <c>tipTitle</c>, <c>tipKey</c>, <c>tipHint</c>). Its plain text,
/// <see cref="Text"/>, is what screen readers are given and what <see cref="ToString"/> returns.
/// </summary>
public sealed class RichTip : StackPanel
{
    public RichTip(string title, string? key = null, string? detail = null, IEnumerable<string?>? hints = null)
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
