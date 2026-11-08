using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

/// <summary>One bounded history page; rows are inert facts and every action obtains fresh runtime ownership.</summary>
internal static class AnalysisHistoryBrowser
{
    private enum Action { Close, Refresh, Previous, Next, Inspect, Open, Pin, Delete, Restore, Recover, RemoveOpaque }
    private sealed record Choice(Action Action, AnalysisHistoryRow? Row);
    internal static async Task ShowAsync(AppServices services, IDialogService dialogs, CancellationToken cancellation = default)
    {
        int offset = 0; string? notice = null; Guid? selectedId = null;
        while (!cancellation.IsCancellationRequested)
        {
            AnalysisHistoryPage page;
            try { page = await ProgressAsync("Reading analysis history", dialogs, token => services.AnalysisHistory.ReadHistoryPageAsync(offset, token), cancellation); }
            catch (OperationCanceledException) { return; }
            catch (Exception error) when (Expected(error)) { await dialogs.AlertAsync("History unavailable", DisplayError(error)); return; }
            offset = page.Offset;
            var list = new ListBox { ItemsSource = page.Rows, MinHeight = 140, MaxHeight = 300 };
            AutomationProperties.SetName(list, "Rolling history entries");
            list.SelectedItem = page.Rows.FirstOrDefault(row => row.InventoryId == selectedId);
            var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = page.Total == 0 ? "No rolling history entries."
                : $"Showing {offset + 1:N0}–{offset + page.Rows.Length:N0} of {page.Total:N0}." };
            var body = new StackPanel { Spacing = 8, Children =
            {
                new TextBlock { Text = "Rolling history · " + AnalysisNames.Display(services.Paths.AnalysisHistoryDirectory, 4096), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = (services.AnalysisHistory.Policy.Enabled ? "History is enabled." : "History is off; existing entries are kept.") +
                    " Open observations stay offline and cannot connect to recorded sources. Open views and pending reviews protect their rolling copy from automatic pruning. Pin an independent named copy to keep it outside retention.", TextWrapping = TextWrapping.Wrap },
                summary, list,
                new TextBlock { Text = notice ?? services.AnalysisHistory.LastMaintenance?.Detail ?? "Select an entry to inspect, open, pin or review its removal. Unindexed copies require full inspection and explicit recovery before normal use.", TextWrapping = TextWrapping.Wrap }
            } };
            AnalysisHistoryRow? Selected() => list.SelectedItem as AnalysisHistoryRow;
            var browserCloser = new DialogCloser();
            var navigation = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
            Button Button(string label, Action action) { var button = new Button { Content = label }; button.Click += (_, _) => browserCloser.Close(new Choice(action, Selected())); return button; }
            navigation.Children.Add(Button("Refresh", Action.Refresh));
            if (offset > 0) navigation.Children.Add(Button("Previous entries", Action.Previous));
            if (page.HasMore) navigation.Children.Add(Button("Next entries", Action.Next));
            var commands = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
            var inspect = Button("Inspect history entry", Action.Inspect); var open = Button("Open observations", Action.Open); var pinCommand = Button("Pin named copy", Action.Pin);
            var remove = Button("Review removal", Action.Delete); var restoreCommand = Button("Restore history entry", Action.Restore); var recover = Button("Inspect recovery", Action.Recover);
            var opaque = Button("Review damaged or unindexed removal", Action.RemoveOpaque);
            foreach (var command in new[] { inspect, open, pinCommand, remove, restoreCommand, recover, opaque }) commands.Children.Add(command);
            void SelectionChanged()
            {
                var state = Selected()?.State;
                inspect.IsVisible = state is AnalysisHistoryRowState.Saved or AnalysisHistoryRowState.PendingPresent;
                open.IsVisible = pinCommand.IsVisible = state == AnalysisHistoryRowState.Saved;
                remove.IsVisible = state is AnalysisHistoryRowState.Saved or AnalysisHistoryRowState.PendingPresent or AnalysisHistoryRowState.PendingAbsent;
                restoreCommand.IsVisible = state == AnalysisHistoryRowState.PendingPresent; recover.IsVisible = state == AnalysisHistoryRowState.Unindexed;
                opaque.IsVisible = state is AnalysisHistoryRowState.Saved or AnalysisHistoryRowState.PendingPresent or AnalysisHistoryRowState.Unindexed;
            }
            list.SelectionChanged += (_, _) => SelectionChanged(); SelectionChanged();
            body.Children.Insert(3, navigation); body.Children.Add(commands);
            using var closeOnCancel = cancellation.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => browserCloser.Close(Action.Close)));
            var result = await dialogs.ShowCustomAsync("Analysis history", body,
                [new DialogButton("Close", Action.Close, IsDefault: true, IsCancel: true)], initialFocus: list, closer: browserCloser);
            if (result is not Choice choice) return;
            var action = choice.Action;
            if (action == Action.Previous) { offset = Math.Max(0, offset - 128); continue; }
            if (action == Action.Next) { offset += page.Rows.Length; continue; }
            if (action == Action.Refresh) continue;
            if (choice.Row is not { } row) continue;
            selectedId = row.InventoryId;
            try
            {
                switch (action)
                {
                    case Action.Inspect:
                        var details = await ProgressAsync("Inspecting history", dialogs, token => services.AnalysisHistory.InspectHistoryAsync(row, token), cancellation);
                        notice = "Verified complete-file checksum and record framing · " + details.Bytes.ToString("N0") + " bytes · observed " + details.StartedUtc.ToString("u") +
                            " – " + details.ObservedUtc.ToString("u") + ". " + (details.Entry.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "Complete under recorded policy." : "Partial observations.") +
                            " Historical paths remain unverified; opening also validates the full record graph.";
                        break;
                    case Action.Open:
                        var source = await ProgressAsync("Opening history", dialogs, token => services.AnalysisHistory.OpenHistoryAsync(row, token), cancellation);
                        var window = new DiskSpaceWindow();
                        try { await window.AddHistorySessionAsync(services, source, cancellation); window.Show(); notice = "Historical observations opened in a separate analysis window."; }
                        catch { window.ForceClose(); await window.WaitForIndexedCloseAsync(); await source.DisposeAsync(); throw; }
                        break;
                    case Action.Pin:
                        var filename = new TextBox { Text = "Pinned analysis " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".fcdsa", MaxLength = 120 };
                        AutomationProperties.SetName(filename, "Pinned analysis filename");
                        var pin = await dialogs.ShowCustomAsync("Pin named analysis", new StackPanel { Spacing = 8, Children =
                        {
                            new TextBlock { Text = "Save an independent complete named copy in " + AnalysisNames.Display(services.Paths.AnalysisSnapshotsDirectory, 4096) +
                                ". It is outside rolling pruning. If a named target exists, a separate Keep existing / Replace review follows. Source files and this rolling copy are unchanged.", TextWrapping = TextWrapping.Wrap }, filename
                        } }, [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Prepare named copy", true) { IsAvailable = () => ValidName(filename.Text) }], filename);
                        if (pin is true)
                        {
                            // Pin may show the prepared replacement review; do not nest it behind a progress modal.
                            var pinned = await services.AnalysisHistory.PinHistoryAsync(row, filename.Text!, dialogs, cancellation);
                            notice = pinned.Detail;
                        }
                        break;
                    case Action.Delete:
                    case Action.Restore:
                        await using (var captured = await ProgressAsync("Checking history removal", dialogs, token => services.AnalysisHistory.PrepareHistoryMaintenanceAsync(row, token), cancellation))
                        {
                            bool restore = action == Action.Restore;
                            if (await ConfirmMaintenanceAsync(dialogs, captured.Review, services.Paths.AnalysisHistoryDirectory, restore))
                            {
                                var changed = await ProgressAsync(restore ? "Restoring history entry" : "Removing history entry", dialogs,
                                    token => services.AnalysisHistory.ChangeHistoryAsync(captured, restore, token), cancellation);
                                notice = restore ? "The history catalog entry was restored; its bytes were kept." : changed.Detail;
                            }
                            else notice = "The history copy and pending metadata were kept.";
                        }
                        break;
                    case Action.Recover:
                        await using (var captured = await ProgressAsync("Validating complete history copy", dialogs, token => services.AnalysisHistory.PrepareHistoryRecoveryAsync(row, token), cancellation))
                        {
                            var recoveryChoice = await dialogs.ShowCustomAsync("Recover complete history copy", new TextBlock { TextWrapping = TextWrapping.Wrap, Text =
                                "The complete checksum, record graph and accounting were validated for " + AnalysisNames.Display(row.Name, 160) + " (" + captured.Review.Bytes.ToString("N0") + " bytes). " +
                                "Add its catalog record with default display settings and a new saved time. The recovered copy becomes eligible for the configured rolling retention. This recovery removes no other files; named analyses and source files are unchanged." },
                                [new DialogButton("Leave unindexed", false, IsDefault: true, IsCancel: true), new DialogButton("Recover history entry", true, IsDanger: true)]);
                            if (recoveryChoice is true)
                            {
                                var recovered = await ProgressAsync("Recovering history catalog", dialogs, token => services.AnalysisHistory.RecoverHistoryAsync(captured, token), cancellation);
                                notice = recovered.Detail;
                            }
                            else notice = "The complete copy was kept unindexed. Automatic history changes stay paused until unresolved data is reviewed.";
                        }
                        break;
                    case Action.RemoveOpaque:
                        await using (var captured = await ProgressAsync("Checking exact private history bytes", dialogs, token => services.AnalysisHistory.PrepareOpaqueHistoryRemovalAsync(row, token), cancellation))
                        {
                            if (await ConfirmOpaqueRemovalAsync(dialogs, captured.Review, services.Paths.AnalysisHistoryDirectory))
                            {
                                var removed = await ProgressAsync("Removing reviewed history copy", dialogs, token => services.AnalysisHistory.RemoveOpaqueHistoryAsync(captured, token), cancellation);
                                notice = removed.Detail;
                            }
                            else notice = "The private history copy was kept. No source or named file was changed.";
                        }
                        break;
                }
            }
            catch (Exception error) when (Expected(error)) { services.AnalysisHistory.RecordHistoryFailure(error); notice = DisplayError(error); }
        }
    }
    internal static async Task<bool> ConfirmOpaqueRemovalAsync(IDialogService dialogs, AnalysisRollingOpaqueDeleteReview review, string directory)
    {
        return await dialogs.ShowCustomAsync("Remove damaged or unindexed history copy", new TextBlock { TextWrapping = TextWrapping.Wrap, Text =
            "Remove only these exact private history bytes and any associated history catalog record. The bytes may be incomplete or corrupt; this review does not validate their inventory records. Recovery may become impossible after removal." +
            "\nName: " + AnalysisNames.Display(review.Name, 160) + "\nFolder: " + AnalysisNames.Display(directory, 4096) +
            "\nActual size: " + review.Bytes.ToString("N0") + " bytes\nSHA256: " + review.Sha256 +
            "\nOther history entries, named analyses and source files are unchanged. Removal is logical deletion, not secure erasure." },
            [new DialogButton("Keep private copy", false, IsDefault: true, IsCancel: true), new DialogButton("Remove private history copy", true, IsDanger: true)]) is true;
    }
    private static async Task<bool> ConfirmMaintenanceAsync(IDialogService dialogs, OwnedAnalysisDeleteReview review, string directory, bool restore)
    {
        var body = new TextBlock { TextWrapping = TextWrapping.Wrap, Text =
            (restore ? "Restore this rolling catalog entry and keep its saved bytes. It becomes eligible for the enabled retention policy again. " :
                review.PayloadPresent ? "Remove only this rolling inventory and its private catalog record. " : "The inventory is already absent. Finish only its pending private catalog cleanup. ") +
            "\nName: " + AnalysisNames.Display(review.Entry.Name, 160) + "\nFolder: " + AnalysisNames.Display(directory, 4096) +
            "\nRecorded size: " + review.Entry.Bytes.ToString("N0") + " bytes · saved " + review.Entry.SavedUtc.ToString("u") +
            "\nNamed copies and source files are unchanged. Removal is logical deletion, not secure erasure." };
        return await dialogs.ShowCustomAsync(restore ? "Restore history entry" : "Remove history entry", body,
            restore ? [new DialogButton("Leave pending", false, IsDefault: true, IsCancel: true), new DialogButton("Restore history entry", true)]
                : [new DialogButton("Keep history entry", false, IsDefault: true, IsCancel: true), new DialogButton("Remove history entry", true, IsDanger: true)]) is true;
    }
    internal static async Task<T> ProgressAsync<T>(string title, IDialogService dialogs, Func<CancellationToken, Task<T>> operation, CancellationToken cancellation)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var task = operation(stop.Token); if (task.IsCompleted) return await task;
        var closer = new DialogCloser();
        var dismissed = dialogs.ShowCustomAsync(title, new TextBlock { Text = "Working in the private history store. Cancellation waits for actual native work and cleanup.", TextWrapping = TextWrapping.Wrap },
            [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true)], closer: closer);
        try
        {
            if (await Task.WhenAny(task, dismissed) == dismissed) stop.Cancel();
            var result = await task;
            if (stop.IsCancellationRequested && result is IAsyncDisposable owned)
            { await owned.DisposeAsync(); throw new OperationCanceledException(stop.Token); }
            return result;
        }
        finally { closer.Close(null); }
    }
    private static bool ValidName(string? name) { try { AnalysisPrivateInventoryStore.ValidateName(name ?? ""); return true; } catch (ArgumentException) { return false; } }
    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException or OverflowException or OperationCanceledException;
    private static string DisplayError(Exception error) => error switch
    {
        AnalysisHistoryPinException pin => "Named copy published as " + AnalysisNames.Display(pin.Receipt.Name, 160) + ", but cleanup was not confirmed. Inspect that named copy before retrying.",
        AnalysisRollingCaptureException recovery => recovery.Outcome.Detail,
        OwnedAnalysisDeletionException removal => removal.Outcome.Detail,
        OperationCanceledException => "History operation canceled. Inspect the current list before retrying; no successful change is claimed.",
        _ => AnalysisNames.Display(error.Message, 500)
    };
}
