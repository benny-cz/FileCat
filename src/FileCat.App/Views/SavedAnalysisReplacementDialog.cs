using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

internal static class SavedAnalysisReplacementDialog
{
    internal static async Task<bool> ConfirmAsync(IDialogService dialogs, OwnedAnalysisReplacementReview review, string directory, DialogCloser? closer = null)
    {
        var body = new StackPanel { Spacing = 8 };
        void Text(string text) => body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        Text("Replace this named analysis with the complete prepared inventory. The previous saved inventory and its display settings will be removed after the new copy is confirmed. Observations already open in tabs remain open. Source files are unchanged.");
        Text("Name: " + AnalysisNames.Display(review.Previous.Entry.Name, 240));
        Text("Location: " + AnalysisNames.Display(Path.Combine(directory, review.Previous.Entry.Name), 4096));
        Text(Version("Previous", review.Previous.Bytes, review.Previous.StartedUtc, review.Previous.ObservedUtc, review.Previous.Entry.Coverage, review.Previous.Entry.Taint));
        Text(Version("Incoming", review.Incoming.Bytes, review.Incoming.StartedUtc, review.Incoming.ObservedUtc, review.Incoming.Coverage, review.Incoming.Taint));
        Text("Keep existing discards only the prepared incoming copy. If qualified replacement or backup space is unavailable, keep the existing analysis and choose a new name.");
        var result = await dialogs.ShowCustomAsync("Replace named analysis", body,
            [new DialogButton("Keep existing", false, IsDefault: true, IsCancel: true), new DialogButton("Replace analysis", true, IsDanger: true)], closer: closer);
        return result is true;
    }
    internal static string Version(string label, long bytes, DateTimeOffset started, DateTimeOffset observed, NamespaceCoverage coverage, AnalysisTaint taint) =>
        label + ": " + DiskSpaceViewModel.Bytes((UInt128)bytes) + " (" + bytes.ToString("N0") + " bytes) · observed " + started.ToString("u") + " – " + observed.ToString("u") +
        (coverage == NamespaceCoverage.CompleteUnderPolicy ? " · complete under the captured policy." : " · partial observations.") +
        (taint.HasFlag(AnalysisTaint.PrivilegedDerived) ? " Sensitive observations gathered with administrator access." : "") +
        (taint.HasFlag(AnalysisTaint.ImportedUnverified) ? " Imported, unverified observations." : "");
}

internal static class SavedAnalysisRecoveryDialog
{
    internal static async Task<(OwnedAnalysisRecoveryAction Action, string? Name)?> ReviewAsync(IDialogService dialogs,
        OwnedAnalysisReplacementRecoveryReview review, string directory, Func<DialogCloser?>? closer = null)
    {
        if (await ChooseAsync(dialogs, review, directory, closer?.Invoke()) is not OwnedAnalysisRecoveryAction action) return null;
        if (action != OwnedAnalysisRecoveryAction.CopyPrevious) return (action, null);
        var name = new TextBox { Text = "Recovered " + DateTime.Now.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".fcdsa", MaxLength = 120 };
        AutomationProperties.SetName(name, "Recovery inventory filename");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        name.TextChanged += (_, _) => error.Text = Error(name.Text) ?? "";
        var body = new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = "Copy the complete verified previous inventory to a new filename. The pending replacement, original name and remaining versions are kept. The copy opens by filename using the default view.", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Folder: " + AnalysisNames.Display(directory, 4096), TextWrapping = TextWrapping.Wrap }, name, error
        } };
        var result = await dialogs.ShowCustomAsync("Copy previous inventory", body,
            [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Save recovery copy", true) { IsAvailable = () => Error(name.Text) is null }], name, closer: closer?.Invoke());
        return result is true ? (action, name.Text!) : null;
        static string? Error(string? value)
        {
            try { AnalysisPrivateInventoryStore.ValidateName(value ?? ""); return null; }
            catch (ArgumentException) { return "Use a new portable filename ending in .fcdsa (up to 120 characters)."; }
        }
    }
    internal static Task<object?> ChooseAsync(IDialogService dialogs, OwnedAnalysisReplacementRecoveryReview review, string directory, DialogCloser? closer = null)
    {
        var body = new StackPanel { Spacing = 8 };
        void Text(string text) => body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        Text("A named replacement is pending. These facts were read from the remaining private files. Leave pending keeps them all. Each recovery action rechecks the reviewed objects and complete bytes before changing anything.");
        Text("Name: " + AnalysisNames.Display(review.Pending.Previous.Name, 240));
        Text("Location: " + AnalysisNames.Display(Path.Combine(directory, review.Pending.Previous.Name), 4096));
        File("At the original name", review.Original); File("Incoming prepared copy", review.Incoming); File("Previous backup", review.Previous);
        if (review.BlockedReason is { } problem) Text(problem);
        Text("Restore previous discards the reviewed incoming version and restores the previous inventory and its saved view. Keep incoming and finish discards the reviewed previous version and completes the incoming save. Already open observations and source files are unchanged.");
        if (review.Pending.Current is null) Text("If the incoming catalog record must be reconstructed, its display settings use the default view.");
        if (review.CopySource is not null) Text("Copy previous to a new name preserves the pending replacement and every remaining version. The copy opens by filename using the default view.");
        return dialogs.ShowCustomAsync("Review pending replacement", body,
            [new DialogButton("Leave pending", false, IsDefault: true, IsCancel: true),
             new DialogButton("Restore previous analysis", OwnedAnalysisRecoveryAction.RestorePrevious, IsDanger: true) { IsAvailable = () => review.CanRestorePrevious },
             new DialogButton("Keep incoming and finish", OwnedAnalysisRecoveryAction.FinishIncoming, IsDanger: true) { IsAvailable = () => review.CanFinishIncoming },
             new DialogButton("Copy previous to a new name…", OwnedAnalysisRecoveryAction.CopyPrevious) { IsAvailable = () => review.CopySource is not null }], closer: closer);

        void File(string label, OwnedAnalysisRecoveryFile file)
        {
            if (file.Inventory is { } value)
            {
                Text(SavedAnalysisReplacementDialog.Version(label, value.Bytes, value.StartedUtc, value.ObservedUtc, value.Coverage, value.Taint));
                if (file.Problem is { } problem) Text(label + ": " + AnalysisNames.Display(problem, 320));
            }
            else Text(label + (file.Present ? ": unavailable or unsupported. " + AnalysisNames.Display(file.Problem ?? "", 320) : ": absent."));
        }
    }
}
