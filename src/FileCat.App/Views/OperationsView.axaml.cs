using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;

namespace FileCat.App.Views;

public partial class OperationsView : UserControl
{
    private const double MinimumDrawerHeight = 140;
    private bool _historyReviewOpen;
    private Window? _drawerOwner;
    private Grid? _workspaceHost;
    private double _preferredDrawerHeight = 300;
    private double _nonDrawerHeight = 150;
    private bool _drawerHeightChanged;
    private IPointer? _resizePointer;
    private double _resizeStartY;
    private double _resizeStartHeight;

    public OperationsView()
    {
        InitializeComponent();
        DetailsDrawer.PropertyChanged += (_, e) =>
        {
            if (e.Property != Visual.IsVisibleProperty || !DetailsDrawer.IsVisible) return;
            // Wait until the reopened drawer has measured its list before revealing the selected job.
            Dispatcher.UIThread.Post(() =>
            {
                if (Center is { IsOpen: true, Selected: not null } && HistoryList.SelectedIndex >= 0)
                    HistoryList.ScrollIntoView(HistoryList.SelectedIndex);
            }, DispatcherPriority.Loaded);
        };
        DrawerResizeHandle.PointerPressed += (_, e) =>
        {
            if (_drawerOwner is null || !e.GetCurrentPoint(DrawerResizeHandle).Properties.IsLeftButtonPressed) return;
            _resizePointer = e.Pointer;
            _resizeStartY = e.GetPosition(_drawerOwner).Y;
            _resizeStartHeight = DetailsDrawer.Height;
            e.Pointer.Capture(DrawerResizeHandle);
            e.Handled = true;
        };
        DrawerResizeHandle.PointerMoved += (_, e) =>
        {
            if (!ReferenceEquals(_resizePointer, e.Pointer) || _drawerOwner is null) return;
            SetDrawerHeight(_resizeStartHeight + _resizeStartY - e.GetPosition(_drawerOwner).Y, userChoice: true);
            e.Handled = true;
        };
        DrawerResizeHandle.PointerReleased += (_, e) =>
        {
            if (!ReferenceEquals(_resizePointer, e.Pointer)) return;
            _resizePointer = null;
            e.Pointer.Capture(null);
            SaveDrawerHeight();
            e.Handled = true;
        };
        DrawerResizeHandle.PointerCaptureLost += (_, _) =>
        {
            _resizePointer = null;
            SaveDrawerHeight();
        };
        DrawerResizeHandle.KeyDown += (_, e) =>
        {
            // A smaller window may clamp the displayed drawer below the saved
            // preference. Relative keys must start from what the user sees.
            double displayedHeight = DetailsDrawer.Height;
            double? next = e.Key switch
            {
                Key.Up => displayedHeight + 16,
                Key.Down => displayedHeight - 16,
                Key.PageUp => displayedHeight + 64,
                Key.PageDown => displayedHeight - 64,
                Key.Home => 0,
                Key.End => double.MaxValue,
                _ => null,
            };
            if (next is null) return;
            SetDrawerHeight(next.Value, userChoice: true);
            e.Handled = true;
        };
        DrawerResizeHandle.KeyUp += (_, _) => SaveDrawerHeight();
        DrawerResizeHandle.LostFocus += (_, _) => SaveDrawerHeight();
        AttachedToVisualTree += (_, _) =>
        {
            _drawerOwner = TopLevel.GetTopLevel(this) as Window;
            if (_drawerOwner is null) return;
            _drawerOwner.SizeChanged += OnOwnerSizeChanged;
            _workspaceHost = (_drawerOwner as MainWindow)?.FindControl<Grid>("WorkspaceHost");
            if (_workspaceHost is not null) _workspaceHost.SizeChanged += OnOwnerSizeChanged;
            double saved = Main?.Services.Settings.OperationsDrawerHeight ?? 300;
            _preferredDrawerHeight = double.IsFinite(saved) ? Math.Clamp(saved, MinimumDrawerHeight, 720) : 300;
            RefreshDrawerHeight();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _resizePointer?.Capture(null);
            _resizePointer = null;
            if (_workspaceHost is not null) _workspaceHost.SizeChanged -= OnOwnerSizeChanged;
            _workspaceHost = null;
            if (_drawerOwner is not null) _drawerOwner.SizeChanged -= OnOwnerSizeChanged;
            _drawerOwner = null;
        };
    }

    private void OnOwnerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshDrawerHeight, DispatcherPriority.Background);
    }

    private double MaximumDrawerHeight()
    {
        if (_drawerOwner is not { ClientSize.Height: > 0 } window) return 720;
        // Remember the surrounding chrome while the workspace still has measurable space. After a
        // sudden window shrink the workspace can momentarily measure zero; its current height then
        // cannot be used to recover the available drawer height.
        if (DetailsDrawer.IsVisible && (window as MainWindow)?.WorkspaceViewport is { Height: > 0 } workspace)
            _nonDrawerHeight = Math.Clamp(window.ClientSize.Height - DetailsDrawer.Bounds.Height - workspace.Height,
                0, window.ClientSize.Height);
        return Math.Clamp(window.ClientSize.Height - _nonDrawerHeight - 100, MinimumDrawerHeight, 720);
    }

    private void RefreshDrawerHeight()
    {
        if (_drawerOwner is null) return;
        ApplyDrawerHeight(Math.Clamp(_preferredDrawerHeight, MinimumDrawerHeight, MaximumDrawerHeight()));
    }

    private void ApplyDrawerHeight(double height)
    {
        DetailsDrawer.Height = height;
        RecoveryScroll.MaxHeight = Math.Clamp(height * .25, 35, 180);
    }

    private void SetDrawerHeight(double requested, bool userChoice)
    {
        double next = Math.Clamp(requested, MinimumDrawerHeight, MaximumDrawerHeight());
        ApplyDrawerHeight(next);
        if (!userChoice || Math.Abs(next - _preferredDrawerHeight) < .5) return;
        _preferredDrawerHeight = next;
        _drawerHeightChanged = true;
    }

    private void SaveDrawerHeight()
    {
        if (!_drawerHeightChanged || Main is not { } main) return;
        _drawerHeightChanged = false;
        main.Services.Settings.OperationsDrawerHeight = _preferredDrawerHeight;
        main.Services.SaveSettings();
    }

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

    private void OnOpenReport(object? sender, RoutedEventArgs e)
    {
        if (Center?.Selected?.Job is not { } job) return;
        var snapshot = OperationReport.Capture(job);
        new ReportWindow("Operation report", Formatters.SafeName(job.Title), ct => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return OperationReport.ForClipboard(snapshot) ??
                "This retained report exceeds the window's 4 million character reading limit. " +
                "Use Save report in the operations drawer to export every retained detail.";
        }, ct)).Show();
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
        if (JobOf(sender) is not { } j || Center is not { } center) return;
        if (center.Manager.MoveInQueue(j.Job, -1))
            Main?.Notify($"Moved {Formatters.SafeName(j.Job.Title)} earlier in the queue.");
        else
            center.UpdateSummary(); // The job may have started between drawing the button and clicking it.
    }

    private async void OnLimit(object? sender, RoutedEventArgs e)
    {
        if (JobOf(sender) is not { CanLimitSpeed: true } j || Main is not { } main) return;
        string initial = RateLimitInput.Format(j.Job.RateLimit);
        string? ValidateRate(string text) => text == initial ? null
            : RateLimitInput.TryParse(text, out _, out var error) ? null : error;
        var r = await main.Dialogs.PromptAsync(new PromptOptions("Limit speed", "Maximum speed in MB/s (0 removes the limit):")
        {
            Text = initial,
            Validate = ValidateRate,
        });
        if (r is null) return;
        if (j.Job.State.IsFinished())
        {
            main.Notify("This operation finished before its speed limit changed.");
            return;
        }
        // A formatted MB/s decimal cannot represent every possible integer byte rate exactly.
        if (r.Text == initial) return;
        if (RateLimitInput.TryParse(r.Text, out var rate, out _)) j.Job.RateLimit = rate;
    }

    private async void OnCleanupInterrupted(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InterruptedJobViewModel item || Main is not { } main || Center is not { } c) return;
        if (!await main.CleanupInterruptedAsync(item.Job)) return;
        c.Interrupted.Remove(item);
        c.UpdateSummary();
    }

    private void OnStopInterruptedReview(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is InterruptedJobViewModel item && Main is { } main)
            main.StopInterruptedReview(item.Job);
    }

    private void OnRunAgainInterrupted(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InterruptedJobViewModel item || Main is not { } main || Center is not { } c) return;
        // Release the native button/AT action before any source or journal probe.
        // Some accessibility backends serialize queries behind an active action;
        // posting the review lets them observe its busy state and Stop control.
        Dispatcher.UIThread.Post(async () =>
        {
            var review = main.RunInterruptedAgainAsync(item.Job);
            RevealRecoveryReview(item);
            if (!await review) return;
            c.Interrupted.Remove(item);
            c.UpdateSummary();
        }, DispatcherPriority.Background);
    }

    internal void RevealRecoveryReview(InterruptedJobViewModel item)
    {
        // The bounded recovery viewport can hide the action row at a high
        // display scale. Reveal the current Stop control immediately when the
        // caller has already measured the busy row, then repeat after layout
        // settles for the production path that starts the review and reveals
        // it in the same dispatcher turn. This does not change the user's
        // saved drawer size.
        void Reveal()
        {
            if (Center is { IsOpen: true } center &&
                ReferenceEquals(center.SelectedInterrupted, item) && item.CanStopReview)
                RecoveryScroll.ScrollToEnd();
        }

        Reveal();
        Dispatcher.UIThread.Post(Reveal, DispatcherPriority.Loaded);
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
