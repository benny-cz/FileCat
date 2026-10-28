using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace FileCat.App.Controls;

/// <summary>
/// A tooltip's pop-up names itself with its text, so a screen reader that reports tooltips reads what it says: NVDA
/// said only "tool tip" over a disabled option whose tooltip explains why it is unavailable.
/// </summary>
internal static class ToolTipNames
{
    private static int s_installed;

    internal static void Install()
    {
        if (Interlocked.Exchange(ref s_installed, 1) != 0) return;
        ContentControl.ContentProperty.Changed.AddClassHandler<ToolTip>((tip, e) =>
            AutomationProperties.SetName(tip, TextOf(e.NewValue)));
    }

    /// <summary>The plain text of a tooltip's content: a string, a laid-out <see cref="RichTip"/> or a text block.</summary>
    internal static string? TextOf(object? content) => content switch
    {
        string text => text,
        RichTip rich => rich.Text,
        TextBlock block => block.Text,
        _ => null,
    };
}
