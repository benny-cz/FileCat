using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Platform.Windows;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private void EditHex()
    {
        var tab = ActiveTab;
        if (tab is null || !tab.Listing.TryGetFocused(out var row) || row.IsContainer || row.Kind == EntryKind.Parent) return;
        string? path = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex).FileSystemPath;
        if (path is null) return;
        if (HexEditorWindow.OpenOrActivate(Services, path) is { } error) Notify(error, true);
    }

    /// <summary>Unsaved hex editors hold exit and sign-out like running jobs do (plan §9.3).</summary>
    private static IReadOnlyList<HexEditorWindow> UnsavedHexEditors() => HexEditorWindow.OpenWindows.Where(w => w.HasUnsavedWork).ToList();

    /// <summary>Asks about unsaved hex editors before exit. False keeps FileCat open.</summary>
    private async Task<bool> ConfirmCloseHexEditorsAsync()
    {
        var unsaved = UnsavedHexEditors();
        if (unsaved.Count == 0) return true;
        var list = ExactList(unsaved.Select(w => $"{w.DisplayName} — {w.UnsavedSummary}"));
        var answer = await Dialogs.ShowCustomAsync("Unsaved hex edits",
            new Avalonia.Controls.TextBlock { Text = $"These hex editors have work that is not saved:\n\n{list}", TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 620 },
            [new DialogButton("Keep FileCat open", "keep", IsCancel: true), new DialogButton("Review", "review", IsDefault: true),
             new DialogButton("Discard edits and exit", "discard", IsDanger: true)]);
        if (answer as string == "review")
        {
            unsaved[0].BringForward();
            return false;
        }
        if (answer as string != "discard") return false;
        if (unsaved.Any(w => w.UnsavedSummary == "saving"))
        {
            Notify("A hex save is still running; FileCat can close when it finishes.", true);
            return false;
        }
        foreach (var w in HexEditorWindow.OpenWindows.ToList()) w.CloseNow();
        return true;
    }

    private async Task RecoverHexAsync()
    {
        var paths = HexSaveJournal.Pending(Services.Paths.HexRecoveryDirectory);
        if (paths.Count == 0) { Notify("No interrupted hex saves need recovery."); return; }
        var loaded = await Task.Run(() => paths.Select(p =>
        {
            try
            {
                var record = HexSaveJournal.Read(p);
                HexRecoveryInspection? inspection;
                try { inspection = HexSaveJournal.Inspect(record); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { inspection = new HexRecoveryInspection(0, 0, 0, ex.Message); }
                return (Path: p, Record: (HexRecoveryRecord?)record, Inspection: (HexRecoveryInspection?)inspection, Error: (string?)null);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return (Path: p, Record: (HexRecoveryRecord?)null, Inspection: (HexRecoveryInspection?)null, Error: (string?)ex.Message);
            }
        }).ToList());
        var items = loaded.Select(l => l.Record is { } r
            ? new ChoiceItem(Path.GetFileName(r.TargetPath), $"{r.TargetPath} · {Describe(r, l.Inspection!)} · {r.CreatedUtc.ToLocalTime():g}")
            : new ChoiceItem("Unreadable journal " + Path.GetFileName(l.Path), l.Error)).ToList();
        var chosen = await Dialogs.ChooseAsync(new ChoiceOptions("Interrupted hex saves", items)
        {
            Hint = "Enter reviews the save: finish it, restore the original bytes, or discard the journal. Nothing changes before you confirm.",
        });
        if (chosen.Index < 0) return;
        var (journal, record, inspection, error) = loaded[chosen.Index];
        if (record is null)
        {
            if (await Dialogs.ConfirmAsync("Discard unreadable journal?",
                    $"The journal {journal} cannot be read ({error}), so it cannot finish or undo anything. Discarding it deletes it and leaves the file as it is.",
                    "Discard journal", danger: true))
                TryDeleteJournal(journal);
            return;
        }
        var editor = HexEditorWindow.OpenWindows.FirstOrDefault(w => string.Equals(w.DisplayName, record.TargetPath, StringComparison.OrdinalIgnoreCase));
        if (editor is not null)
        {
            editor.BringForward();
            Notify("That file is open in a hex editor. Use Finish saving or Restore original bytes there, or close the editor first.");
            return;
        }
        var choices = new List<DialogButton> { new("Cancel", "cancel", IsCancel: true) };
        if (inspection!.Blocker is null)
        {
            choices.Add(new DialogButton("Restore original bytes", "rollback"));
            choices.Add(new DialogButton("Finish saving", "finish", IsDefault: true));
        }
        choices.Insert(1, new DialogButton("Discard journal", "discard", IsDanger: true));
        string body = $"{record.TargetPath}\n{Formatters.ExactSize(record.Length)} · {Formatters.Plural(record.Ranges.Count, "changed range", "changed ranges")} · " +
                      $"the save recorded {record.WrittenRanges} of {record.Ranges.Count} ranges as written before it stopped.\n\n" +
                      (inspection.Blocker is { } blocker
                          ? blocker + "\n\nDiscarding deletes the journal (with its copy of the original bytes) and leaves the file as it is."
                          : $"Now: {Describe(record, inspection)}.\n\nFinish saving writes the remaining new bytes; Restore original bytes writes the journaled originals back. " +
                            "Both first check that this is the same file and that every byte is either original or new, then flush and read back before removing the journal.");
        var answer = await Dialogs.ShowCustomAsync("Recover interrupted hex save",
            new Avalonia.Controls.SelectableTextBlock { Text = body, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 640 }, choices);
        switch (answer as string)
        {
            case "discard":
                if (inspection.Blocker is null && !inspection.AlreadyFinished && !inspection.AlreadyRolledBack &&
                    !await Dialogs.ConfirmAsync("Leave the file mixed?",
                        "The file keeps its current mix of original and new bytes, and the journal's copy of the original bytes is deleted. This cannot be undone.",
                        "Discard journal", danger: true))
                    return;
                if (TryDeleteJournal(journal)) Notify("The recovery journal was discarded; the file was not changed.");
                return;
            case "finish":
            case "rollback":
                bool rollback = answer as string == "rollback";
                try
                {
                    Notify("Verifying and recovering the hex save…");
                    await Task.Run(() => HexSaveJournal.Recover(record, rollback));
                    Notify(rollback ? "Original bytes restored and verified; the recovery journal was removed." : "Save finished and verified; the recovery journal was removed.");
                    RefreshTabsShowing(Path.GetDirectoryName(record.TargetPath));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or System.ComponentModel.Win32Exception)
                {
                    await Dialogs.AlertAsync("Recovery stopped safely", ex.Message + "\n\nThe journal is kept for a later retry.");
                }
                return;
        }
    }

    private static string Describe(HexRecoveryRecord record, HexRecoveryInspection inspection) =>
        inspection.Blocker is not null ? "blocked: " + inspection.Blocker
        : inspection.AlreadyFinished ? "every range already has the new bytes"
        : inspection.AlreadyRolledBack ? "every range still has the original bytes"
        : $"{inspection.ReplacedRanges} of {record.Ranges.Count} ranges new" + (inspection.MixedRanges > 0 ? $", {inspection.MixedRanges} partly written" : "");

    private bool TryDeleteJournal(string journal)
    {
        try
        {
            File.Delete(journal);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify("The journal could not be deleted: " + ex.Message, true);
            return false;
        }
    }

    private void RefreshTabsShowing(string? directory)
    {
        if (directory is null) return;
        foreach (var panel in Workspace.Panels)
            foreach (var tab in panel.Tabs)
                if (tab.Location?.IsFileSystem == true && directory.Equals(tab.Location.Path, StringComparison.OrdinalIgnoreCase))
                    tab.Refresh();
    }
}
