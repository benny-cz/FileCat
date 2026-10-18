using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;

namespace FileCat.App.Views;

public partial class OperationsView : UserControl
{
    private bool _historyReviewOpen;
    public OperationsView() => InitializeComponent();

    private OperationCenterViewModel? Center => DataContext as OperationCenterViewModel;

    private MainViewModel? Main => TopLevel.GetTopLevel(this)?.DataContext as MainViewModel;

    private static JobViewModel? JobOf(object? sender) => (sender as Control)?.DataContext as JobViewModel;

    private void OnPause(object? sender, RoutedEventArgs e) => JobOf(sender)?.Job.Pause();

    private void OnResume(object? sender, RoutedEventArgs e)
    {
        JobOf(sender)?.Job.Resume();
        Center?.Manager.Schedule();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => JobOf(sender)?.Job.Cancel();

    private static SyncGroup? SyncGroupOf(object? sender) => ((sender as Button)?.Tag as SyncGroupViewModel)?.Group;

    private void OnStopSyncGroup(object? sender, RoutedEventArgs e)
    {
        if (SyncGroupOf(sender) is not { } group) return;
        group.Stop();
        Main?.Notify("Stopping this synchronization's unfinished steps. Completed changes and their reports remain.");
    }

    private void OnSkipSyncRemovals(object? sender, RoutedEventArgs e)
    {
        if (SyncGroupOf(sender)?.SkipRemovals() == true)
            Main?.Notify("Pending synchronization removals were skipped; completed transfers remain.");
    }

    private void OnListSyncRemovals(object? sender, RoutedEventArgs e)
    {
        if (SyncGroupOf(sender) is not { } group) return;
        new ReportWindow("Pending synchronization removals", Formatters.SafeName(group.Target), ct => Task.Run(() =>
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine($"Reviewed plan: {group.ReviewedPlan}");
            report.AppendLine($"Source: {Formatters.SafeName(group.Source)}");
            report.AppendLine($"Target: {Formatters.SafeName(group.Target)}");
            report.AppendLine($"Pending target-only paths: {group.PendingRemovalPaths.Count:N0}");
            report.AppendLine("Each path is removed only while the compared target state still matches.");
            report.AppendLine();
            foreach (string path in group.PendingRemovalPaths)
            {
                ct.ThrowIfCancellationRequested();
                report.AppendLine(Formatters.SafeName(path));
            }
            return report.ToString();
        }, ct)).Show();
    }

    private async void OnReviewSyncRemovals(object? sender, RoutedEventArgs e)
    {
        if (SyncGroupOf(sender) is not { PendingRemovalReview: true } group || Main is not { } main) return;
        if (main.Dialogs.IsOpen)
        {
            main.Notify("Finish or cancel the open dialog before reviewing pending removals.");
            return;
        }
        string steps = string.Join("\n", group.Steps.Select(job =>
            $"• {Formatters.SafeName(job.Title)} — {job.State.Describe()}, {job.Summary}"));
        string paths = string.Join("\n", group.PendingRemovalPaths.Take(8).Select(Formatters.SafeName));
        if (group.PendingRemovalPaths.Count > 8)
            paths += $"\n… and {group.PendingRemovalPaths.Count - 8:N0} more; use List pending paths for the complete set.";
        bool permanent = group.Steps.Any(job => job.Kind == JobKind.Delete);
        bool approved = await main.Dialogs.ConfirmAsync("Review pending synchronization removals",
            $"Source: {Formatters.SafeName(group.Source)}\nTarget: {Formatters.SafeName(group.Target)}\n" +
            $"Reviewed plan: {group.ReviewedPlan}\n\nTransfers stopped or failed. Changes already committed are kept. " +
            "The pending target-only removals were held. Review the step reports and the current target before continuing. " +
            "Each removal still checks the compared target state when it runs.\n\n" +
            $"Pending target-only paths ({group.PendingRemovalPaths.Count:N0}):\n{paths}\n\n" + steps,
            "Run pending removals", danger: permanent);
        if (approved && !group.ContinueRemovals())
            main.Notify("The pending removals are no longer available to continue.");
    }

    private async void OnReviewDecision(object? sender, RoutedEventArgs e)
    {
        if (JobOf(sender) is { } j && Main is { } main) await main.ReviewDecisionAsync(j.Job);
    }

    private async void OnUndo(object? sender, RoutedEventArgs e)
    {
        // The button undoes the job on its own row, never whichever operation happens to be newest.
        if (JobOf(sender) is { } j && Main is { } main) await main.UndoJobAsync(j.Job);
    }

    private void OnShowUndoReport(object? sender, RoutedEventArgs e)
    {
        if (JobOf(sender) is { } j && Main is { } main) main.ShowUndoResults(j.Job);
    }

    private async void OnShowUndoAffected(object? sender, RoutedEventArgs e)
    {
        if (JobOf(sender) is { } j && Main is { } main) await main.ShowUndoAffectedAsync(j.Job);
    }

    private async void OnRetryElevated(object? sender, RoutedEventArgs e)
    {
        if (JobOf(sender) is { } j && Main is { } main) await main.RetryElevatedAsync(j.Job);
    }

    private async void OnShowResults(object? sender, RoutedEventArgs e)
    {
        // The results of the check on this row (and of the check started with it), as they opened when it ended.
        if (JobOf(sender) is { } j && Main is { } main) await main.ShowVerificationResultsAsync(j.Job);
    }

    private async void OnIssueDetails(object? sender, RoutedEventArgs e)
    {
        if (Center?.Selected is { } selected && Center.SelectedIssue is { } issue && Main is { } main)
            await OperationIssueDialog.ShowAsync(main, selected.Job, issue);
    }

    private async void OnCopyReport(object? sender, RoutedEventArgs e)
    {
        if (Center?.Selected?.Job is not { } job || Main is not { } main) return;
        try
        {
            var snapshot = OperationReport.Capture(job);
            string? report = await Task.Run(() => OperationReport.ForClipboard(snapshot));
            if (report is null)
            {
                main.Notify("The complete retained report is too large for one clipboard publication. Use Save report to export every retained detail.", true);
                return;
            }
            if (main.View.Clipboard is not { } clipboard)
            {
                main.Notify("The clipboard is not available.", true);
                return;
            }
            await clipboard.SetTextAsync(report);
            main.Notify($"Copied the report for {Formatters.SafeName(job.Title)} ({snapshot.Issues.Retained:N0} retained details).");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            main.Notify("The report could not be copied: " + Formatters.SafeName(ex.Message), true);
        }
    }

    private async void OnSaveReport(object? sender, RoutedEventArgs e)
    {
        if (Center?.Selected?.Job is not { } job || Main is not { } main || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var picked = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the complete retained operation report",
                SuggestedFileName = $"FileCat operation {job.ShortId}.txt",
                DefaultExtension = "txt",
                FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }],
            });
            if (picked is null) return;
            if (picked.TryGetLocalPath() is not { } path)
            {
                main.Notify("Choose a local file to save the complete report.", true);
                return;
            }
            var snapshot = OperationReport.Capture(job);
            await Task.Run(() => OperationReport.SaveAtomic(path, snapshot, CancellationToken.None));
            main.Notify($"Saved the report for {Formatters.SafeName(job.Title)} to {Formatters.SafeName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            main.Notify("The report could not be saved: " + Formatters.SafeName(ex.Message), true);
        }
    }

    private void OnToggleDetails(object? sender, RoutedEventArgs e)
    {
        if (Center is { } c) c.IsOpen = !c.IsOpen;
    }

    private async void OnClearFinished(object? sender, RoutedEventArgs e)
    {
        if (_historyReviewOpen || Center is not { } center || Main is not { } main) return;
        var review = new OperationHistoryRetirement(center.Jobs.Select(j => j.Job));
        if (review.Jobs.Count == 0) return;
        if (review.NeedsConfirmation && main.Dialogs.IsOpen)
        {
            main.Notify("Finish or cancel the open dialog before clearing operation history.");
            return;
        }
        _historyReviewOpen = true;
        try
        {
            if (await review.ConfirmAsync(main, "Clear finished history")) center.RemoveFinished(review.Jobs);
        }
        finally { _historyReviewOpen = false; }
    }

    private async void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (_historyReviewOpen || JobOf(sender) is not { } row || Center is not { } center || Main is not { } main) return;
        var review = new OperationHistoryRetirement([row.Job]);
        if (review.Jobs.Count == 0) return;
        if (review.NeedsConfirmation && main.Dialogs.IsOpen)
        {
            main.Notify("Finish or cancel the open dialog before removing operation history.");
            return;
        }
        _historyReviewOpen = true;
        try
        {
            if (await review.ConfirmAsync(main, "Remove this history row")) center.RemoveFinished(review.Jobs);
        }
        finally { _historyReviewOpen = false; }
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e)
    {
        if (JobOf(sender) is { } j) Center?.Manager.MoveInQueue(j.Job, -1);
    }

    private async void OnLimit(object? sender, RoutedEventArgs e)
    {
        if (JobOf(sender) is not { } j || Main is not { } main) return;
        var r = await main.Dialogs.PromptAsync(new PromptOptions("Limit speed", "Maximum speed in MB/s (0 removes the limit):")
        {
            Text = j.Job.RateLimit > 0 ? (j.Job.RateLimit / 1024.0 / 1024).ToString("0.#") : "0",
            Validate = t => double.TryParse(t, out var v) && v >= 0 ? null : "Enter a number.",
        });
        if (r is not null && double.TryParse(r.Text, out var mb)) j.Job.RateLimit = (long)(mb * 1024 * 1024);
    }

    private async void OnCleanupInterrupted(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InterruptedJobViewModel item || Main is not { } main || Center is not { } c) return;
        if (!await main.CleanupInterruptedAsync(item.Job)) return;
        c.Interrupted.Remove(item);
        c.UpdateSummary();
    }

    private async void OnRunAgainInterrupted(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InterruptedJobViewModel item || Main is not { } main || Center is not { } c) return;
        if (!await main.RunInterruptedAgainAsync(item.Job)) return;
        c.Interrupted.Remove(item);
        c.UpdateSummary();
    }

    private void OnHideInterrupted(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InterruptedJobViewModel item || Center is not { } c || Main is not { } main) return;
        c.Interrupted.Remove(item);
        c.UpdateSummary();
        main.Notify("The recovery notice is hidden until FileCat restarts. Its journal and files are unchanged.");
    }

    private async void OnReviewNewInterrupted(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InterruptedJobViewModel item || Main is not { } main || Center is not { } c) return;
        if (!await main.ReviewNewInterruptedTransferAsync(item.Job)) return;
        c.Interrupted.Remove(item);
        c.UpdateSummary();
    }

    private async void OnInterruptedReport(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is InterruptedJobViewModel item && Main is { } main)
            await main.ShowInterruptedReportAsync(item.Job);
    }

    private async void OnMarkInterruptedReviewed(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InterruptedJobViewModel item || Main is not { } main || Center is not { } center) return;
        if (!await main.MarkInterruptedReviewedAsync(item.Job)) return;
        center.Interrupted.Remove(item);
        center.UpdateSummary();
    }

    private void OnReviewedRecoveryReport(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RecoveryReviewRecord record) return;
        new ReportWindow("Reviewed recovery report", Formatters.SafeName(record.Title), _ => Task.FromResult(record.Report)).Show();
    }
}
