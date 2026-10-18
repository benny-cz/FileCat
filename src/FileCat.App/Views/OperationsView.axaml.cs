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

    private void OnClearFinished(object? sender, RoutedEventArgs e) => Center?.RemoveFinished();

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (JobOf(sender) is { } j && Center is { } c)
        {
            c.RemoveFinished(j);
        }
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

    private async void OnDismissInterrupted(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InterruptedJobViewModel item || Center is not { } c || Main is not { } main) return;
        if (!await main.DismissInterruptedAsync(item.Job)) return;
        c.Interrupted.Remove(item);
        c.UpdateSummary();
    }

    private async void OnReviewNewInterrupted(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InterruptedJobViewModel item || Main is not { } main || Center is not { } c) return;
        if (!await main.ReviewNewInterruptedTransferAsync(item.Job)) return;
        c.Interrupted.Remove(item);
        c.UpdateSummary();
    }
}
