using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

/// <summary>Fresh user intent over a displayed policy. This result is not a persisted approval or a storage capability.</summary>
internal static class AnalysisRollingHistoryPolicyDialog
{
    internal static async Task<bool> ConfirmAsync(IDialogService dialogs, AnalysisRollingHistoryPolicy current,
        AnalysisRollingHistoryPolicy proposed, string directory)
    {
        ArgumentNullException.ThrowIfNull(dialogs); ArgumentNullException.ThrowIfNull(current); ArgumentNullException.ThrowIfNull(proposed);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory); current.Validate(); proposed.Validate();
        if (current == proposed) return false;
        var body = new StackPanel { Spacing = 8 };
        void Text(string text) => body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        Text("Rolling history saves complete metadata inventories from successful live analyses. Captured paths, names, queries and observed metadata can be sensitive. File contents are excluded. Imported, stopped and failed sessions are not captured automatically; incomplete coverage remains visibly incomplete.");
        Text("Folder: " + AnalysisNames.Display(directory, 4096));
        Text(Describe("Current", current)); Text(Describe("Proposed", proposed));
        if (proposed.Enabled)
            Text("After this change, automatic pruning removes older rolling copies when any enabled retention limit is reached. Complete inventories too large for the byte limit are skipped; they are never replaced with truncated snapshots. Active protected work can defer capture and pruning.");
        else
            Text((current.Enabled ? "History will be turned off." : "History stays off.") + " Automatic capture and pruning stop; existing history is kept. Removing history requires a separate review.");
        Text("Only rolling history in this private store is affected. Named saved analyses, source files and copies pinned as named analyses are unchanged. Removing history is logical deletion, not secure erasure.");
        string preserve = current.Enabled ? "Keep current policy" : "Leave off";
        string apply = proposed.Enabled ? (current.Enabled ? "Apply retention and pruning" : "Enable history and pruning")
            : current.Enabled ? "Turn off history" : "Update limits and leave off";
        string title = proposed.Enabled ? (current.Enabled ? "Change rolling history" : "Enable rolling history")
            : current.Enabled ? "Turn off rolling history" : "Update disabled history limits";
        var result = await dialogs.ShowCustomAsync(title, body,
            [new DialogButton(preserve, false, IsDefault: true, IsCancel: true), new DialogButton(apply, true, IsDanger: proposed.Enabled)]);
        return result is true;
    }

    private static string Describe(string label, AnalysisRollingHistoryPolicy policy) =>
        label + ": " + (policy.Enabled ? "enabled" : "off") + " · up to " + policy.MaximumEntries.ToString("N0") + " successful analyses · " +
        policy.MaximumAgeDays.ToString("N0") + " days · " + DiskSpaceViewModel.Bytes((UInt128)policy.MaximumBytes) + " (" +
        policy.MaximumBytes.ToString("N0") + " bytes), whichever limit is reached first.";
}
