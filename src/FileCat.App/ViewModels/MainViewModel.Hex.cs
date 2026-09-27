using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Platform.Windows;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private void EditHexAsync()
    {
        var tab = ActiveTab;
        if (tab is null || !tab.Listing.TryGetFocused(out var row) || row.IsContainer || row.Kind == EntryKind.Parent) return;
        string? path = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex).FileSystemPath;
        if (path is null) return;
        try
        {
            foreach (var pending in HexSaveJournal.Pending(Services.Paths.HexRecoveryDirectory))
            {
                HexRecoveryRecord record;
                try { record = HexSaveJournal.Read(pending); }
                catch (InvalidDataException) { continue; }
                if (Path.GetFullPath(record.TargetPath).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                {
                    Notify("This file has an interrupted hex save. Use Tools → Recover interrupted hex save before editing it again.", true);
                    return;
                }
            }
            new HexEditorWindow(Services, path).Show();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.ComponentModel.Win32Exception)
        { Notify($"Cannot start protected hex editing: {ex.Message}", true); }
    }

    private async Task RecoverHexAsync()
    {
        var paths = HexSaveJournal.Pending(Services.Paths.HexRecoveryDirectory);
        if (paths.Count == 0) { Notify("No interrupted hex saves need recovery."); return; }
        var selected = await Dialogs.ChooseAsync(new ChoiceOptions("Interrupted hex saves",
            paths.Select(p => new ChoiceItem(Path.GetFileName(p), p)).ToArray())
        { Hint = "Recovery reads each journal and verifies the target identity and current bytes before changing anything." });
        if (selected.Index < 0) return;
        HexRecoveryRecord record;
        try { record = await Task.Run(() => HexSaveJournal.Read(paths[selected.Index])); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { await Dialogs.AlertAsync("Journal cannot be read", ex.Message + "\n\nThe journal was preserved for manual review."); return; }
        var choice = await Dialogs.ChooseAsync(new ChoiceOptions("Recover hex save", [
            new ChoiceItem("Resume save", "Write the journaled replacement bytes"),
            new ChoiceItem("Roll back", "Restore the journaled original bytes"),
        ])
        {
            Hint = $"Target: {record.TargetPath}\nOriginal length: {record.Length:N0} bytes · {record.Ranges.Count:N0} changed ranges. " +
                   "Both choices first verify that this is the same file and each current byte is an original or replacement byte. Unrelated changes block recovery.",
        });
        if (choice.Index < 0) return;
        bool rollback = choice.Index == 1;
        if (!await Dialogs.ConfirmAsync("Apply hex recovery?",
            $"{(rollback ? "Restore original" : "Finish replacement")} bytes in {record.TargetPath}? The result is flushed before the journal is removed.",
            rollback ? "Roll back" : "Resume save", danger: true)) return;
        try
        {
            Notify("Verifying and recovering hex save…");
            await Task.Run(() => HexSaveJournal.Recover(record, rollback));
            Notify(rollback ? "Original bytes restored; the recovery journal was removed." : "Replacement bytes saved; the recovery journal was removed.");
            foreach (var panel in Workspace.Panels)
                foreach (var tab in panel.Tabs)
                    if (tab.Location?.IsFileSystem == true &&
                        Path.GetDirectoryName(record.TargetPath)?.Equals(tab.Location.Path, StringComparison.OrdinalIgnoreCase) == true)
                        tab.Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        { await Dialogs.AlertAsync("Recovery stopped safely", ex.Message + "\n\nThe journal remains available for manual review or a later retry."); }
    }
}
