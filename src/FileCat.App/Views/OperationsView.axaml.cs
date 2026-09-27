using Avalonia.Controls;
using Avalonia.Interactivity;
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

    private async void OnUndo(object? sender, RoutedEventArgs e)
    {
        // The button undoes the job on its own row, never whichever operation happens to be newest.
        if (JobOf(sender) is { } j && Main is { } main) await main.UndoJobAsync(j.Job);
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
            c.Manager.Remove(j.Job);
            c.Jobs.Remove(j);
            c.UpdateSummary();
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
        var (leftovers, incomplete) = await Task.Run(() => (JournalRecovery.FindStagedLeftovers(item.Job), JournalRecovery.FindIncompleteCopies(item.Job)));
        var intents = item.Job.OpenIntents.Select(i => $"• {i.Operation}: {i.Path}{(i.Target is null ? "" : " → " + i.Target)}").ToList();
        var message = leftovers.Count == 0 && incomplete.Count == 0
            ? "No partial files of this operation remain."
            : string.Empty;
        if (leftovers.Count > 0)
            message += $"{leftovers.Count} partial file(s) were never published and can be deleted safely:\n" + string.Join("\n", leftovers.Take(10).Select(l => "• " + l));
        if (incomplete.Count > 0)
            message += (message.Length > 0 ? "\n\n" : string.Empty) + $"{incomplete.Count} copied file(s) differ from their source and are probably incomplete (created by this operation; check any you changed yourself since):\n"
                + string.Join("\n", incomplete.Take(10).Select(l => "• " + l));
        leftovers = [.. leftovers, .. incomplete];
        if (intents.Count > 0) message += "\n\nSteps that were in progress (inspect these items yourself; nothing is replayed automatically):\n" + string.Join("\n", intents.Take(10));
        if (!await main.Dialogs.ConfirmAsync("Interrupted operation", message, leftovers.Count > 0 ? "Delete partial files" : "OK")) return;
        int deleted = 0;
        foreach (var f in leftovers)
        {
            try
            {
                File.Delete(f);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        JournalRecovery.Close(item.Job, $"Reviewed; {deleted} partial file(s) deleted.");
        c.Interrupted.Remove(item);
        c.UpdateSummary();
        main.Notify($"Removed {Formatters.Plural(deleted, "partial file", "partial files")} of the interrupted operation.");
    }

    private async void OnRunAgainInterrupted(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InterruptedJobViewModel item || Main is not { } main || Center is not { } c) return;
        if (!await main.RunInterruptedAgainAsync(item.Job)) return;
        c.Interrupted.Remove(item);
        c.UpdateSummary();
    }

    private void OnDismissInterrupted(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InterruptedJobViewModel item || Center is not { } c) return;
        JournalRecovery.Close(item.Job, "Dismissed by the user.");
        c.Interrupted.Remove(item);
        c.UpdateSummary();
    }
}
