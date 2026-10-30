using Avalonia.Automation;
using Avalonia.Controls;

namespace FileCat.App.Views;

/// <summary>Result feedback shared by the read-only viewers and the hex editor.</summary>
internal static class ViewerSearchFeedback
{
    // Null means the query is empty, pending, invalid, or its complete result is unknown.
    public static void Set(TextBox box, bool? found)
    {
        box.Classes.Set("searchPending", found is null);
        box.Classes.Set("searchFound", found == true);
        box.Classes.Set("searchMissing", found == false);
        AutomationProperties.SetHelpText(box, found switch
        {
            true => "Matches found.",
            false => "No matches found.",
            _ => string.Empty,
        });
    }
}
