using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

internal static class SavedComparisonMaintenanceDialog
{
    internal static async Task<bool> ConfirmAsync(IDialogService dialogs, OwnedAnalysisComparisonDeleteReview review, string directory,
        CancellationToken cancellation = default)
    {
        var closer = new DialogCloser();
        using var close = cancellation.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => closer.Close(false)));
        var body = new StackPanel { Spacing = 8 };
        void Text(string value) => body.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
        Text("Delete this private comparison bundle, including both captured inventories and its saved display settings. Independently saved analyses, source files and observations already open in other tabs are unchanged.");
        Text("Name: " + AnalysisNames.Display(review.Name, 240));
        Text("Location: " + AnalysisNames.Display(Path.Combine(directory, review.Name), 4096));
        Text("Comparison size: " + DiskSpaceViewModel.Bytes((UInt128)review.Bytes) + " (" + review.Bytes.ToString("N0") + " bytes)");
        Text("Saved: " + review.SavedUtc.ToString("u"));
        if (review.Taint.HasFlag(AnalysisTaint.PrivilegedDerived)) Text("Contains sensitive observations gathered with administrator access.");
        Text("Deletion is not secure erasure. If removal cannot be confirmed, inspect the remaining private files before retrying.");
        return await dialogs.ShowCustomAsync("Delete saved comparison", body,
            [new DialogButton("Keep comparison", false, IsDefault: true, IsCancel: true), new DialogButton("Delete comparison", true, IsDanger: true)], closer: closer) is true;
    }
}
