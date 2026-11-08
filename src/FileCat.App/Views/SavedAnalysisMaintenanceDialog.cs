using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

internal enum SavedAnalysisMaintenanceAction { Delete, Restore, Replacement }

internal static class SavedAnalysisMaintenanceDialog
{
    internal static async Task<bool> ConfirmAsync(IDialogService dialogs, OwnedAnalysisDeleteReview review, string directory, bool restore)
    {
        var entry = review.Entry;
        var body = new StackPanel { Spacing = 8 };
        void Text(string text) => body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        Text(restore ? "Restore this saved analysis to the named catalog. Its saved inventory file will be kept." : review.PayloadPresent
            ? "Delete this saved inventory and its catalog entry, including saved display settings. Any independent observations already open in other tabs remain there. Source files are unchanged."
            : "The saved inventory file is already absent. Finish removing its private pending catalog record. Source files are unchanged.");
        Text("Name: " + AnalysisNames.Display(entry.Name, 240));
        Text("Location: " + AnalysisNames.Display(Path.Combine(directory, entry.Name), 4096));
        Text("Captured inventory size: " + DiskSpaceViewModel.Bytes((UInt128)entry.Bytes) + " (" + entry.Bytes.ToString("N0") + " bytes)");
        Text("Saved: " + entry.SavedUtc.ToString("u"));
        if (entry.Taint.HasFlag(AnalysisTaint.PrivilegedDerived)) Text("Contains sensitive observations gathered with administrator access.");
        if (!restore) Text("Deletion is not secure erasure. Remaining recovery metadata is shown if the operation cannot finish.");
        var result = await dialogs.ShowCustomAsync(restore ? "Restore named analysis" : "Delete named analysis", body,
            restore ? [new DialogButton("Leave pending", false, IsDefault: true, IsCancel: true), new DialogButton("Restore named analysis", true)]
                : [new DialogButton("Keep analysis", false, IsDefault: true, IsCancel: true), new DialogButton("Delete analysis", true, IsDanger: true)]);
        return result is true;
    }
}
