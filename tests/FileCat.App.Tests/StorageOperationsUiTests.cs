using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using TaskbarProgressState = FileCat.Platform.Windows.TaskbarProgressState;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class StorageOperationsUiTests
{
    private static readonly StorageObjectIdentity Identity = new(42, "retained-object") { VolumeBinding = "verified-volume" };
    private static Job MakeJob()
    {
        var observation = new StorageObservation(StorageObservationState.Disabled, StorageObservationState.Disabled, Identity, false, null);
        return new(new JobRequest
        {
            Kind = JobKind.StorageTransform,
            Sources = [new ItemRef(Location.FileSystem(@"C:\captured"), "line\n\"name.txt", EntryKind.File)],
            StorageTransform = new(StorageAction.Compress, StorageScope.ExistingDescendants, Guid.NewGuid()) { Roots = [new(0, 0, observation)] },
        }, "Compress captured files", "fixture", [], []);
    }
    private static StorageResultSnapshot Result(Job job, StorageOutcomeCounts? counts = null) =>
        new(job.Request.StorageTransform!.Action, job.Request.StorageTransform.Scope, job.Request.StorageTransform.ReviewRevision, counts ?? new(AlreadySatisfied: 3))
        { Roots = [new(0, Identity, StorageCompleteness.Complete, false)], Discovery = StorageCompleteness.Complete };

    [Theory]
    [InlineData(JobState.Queued)]
    [InlineData(JobState.Running)]
    [InlineData(JobState.Pausing)]
    [InlineData(JobState.Paused)]
    [InlineData(JobState.AwaitingDecision)]
    [InlineData(JobState.Stopping)]
    [InlineData(JobState.Completed)]
    [InlineData(JobState.CompletedWithIssues)]
    [InlineData(JobState.Canceled)]
    [InlineData(JobState.Failed)]
    public void Every_storage_state_omits_transfer_metrics_and_taskbar_fractions(JobState state)
    {
        var job = MakeJob(); job.AddTotals(100, 100_000_000); job.AddBytes(100_000_000); job.TotalsFinal = true;
        job.SetStorageResult(Result(job)); job.SetState(state);
        var row = new JobViewModel(job); row.Refresh();
        Assert.Empty(row.PercentText); Assert.Equal(0, row.Percent); Assert.Empty(row.TimeLeftText);
        Assert.Empty(row.SpeedText); Assert.Empty(row.DataText); Assert.Empty(row.GraphCaption); Assert.Empty(row.SpeedHistory);
        Assert.False(row.HasSpeedHistory); Assert.False(row.ShowsFileProgress); Assert.False(row.CanRetryElevated); Assert.False(row.CanUndo);
        Assert.Equal("Stop", row.CancelLabel); Assert.Contains("admitted call finishes", row.CancelTooltip);
        Assert.Equal(state == JobState.Running, row.ShowsOverallProgress);
        Assert.Equal(0, OperationCenterViewModel.TaskbarFor(state.IsFinished() ? null : row, row).Fraction);
        if (state == JobState.Running) Assert.Equal(TaskbarProgressState.Indeterminate, OperationCenterViewModel.TaskbarFor(row, row).State);
        Assert.Contains("3 already satisfied", row.DetailText);
    }

    [Fact]
    public void Stop_preserves_complete_error_severity_without_sampled_issues_and_current_path_is_exact()
    {
        var job = MakeJob(); string path = @"C:\captured\very long folder\exact-file.txt";
        job.SetStorageResult(Result(job, new(Changed: 2, Failed: 1, NotProcessed: 7)) with
        { Canceled = true, Discovery = StorageCompleteness.UnknownRemainder, CurrentPath = path });
        job.SetState(JobState.Canceled); var row = new JobViewModel(job); row.Refresh();
        Assert.Equal(0, job.IssueCount); Assert.Equal("error", row.Severity); Assert.True(row.HasIssues);
        Assert.Equal(path, row.CurrentFileText); Assert.Equal("exact-file.txt", row.CurrentItem);
        Assert.Contains("completed changes remain", row.DetailText); Assert.Contains("Additional items", row.DetailText);
        Assert.Contains("2 changed", row.DetailText); Assert.Contains("1 failed", row.DetailText);
    }

    [AvaloniaFact]
    public void Typed_outcomes_replace_no_issues_and_startup_merge_preserves_selected_pending_history()
    {
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.GetTempPath());
        var center = new OperationCenterViewModel(manager); var job = MakeJob();
        job.SetStorageResult(Result(job) with { ReviewPending = true, Discovery = StorageCompleteness.Incomplete }); job.SetState(JobState.Failed);
        var row = new JobViewModel(job); row.Refresh(); center.Jobs.Add(row); center.Selected = row; center.Refresh(job);
        Assert.DoesNotContain("No issues reported", center.IssueSummary); Assert.Contains("Result not confirmed", center.IssueSummary);
        var candidate = manager.StorageHistoryCandidate(job); center.AddInterrupted(candidate); var selected = Assert.Single(center.Interrupted);
        center.SelectedInterrupted = selected;
        center.LoadInterrupted([candidate, candidate with { JournalPath = candidate.JournalPath + "-another" }]);
        Assert.Equal(2, center.Interrupted.Count); Assert.Same(selected, center.SelectedInterrupted);
        Assert.True(row.CanReviewStorage); Assert.False(selected.CanCleanup); Assert.False(selected.CanMarkReviewed);
        Assert.False(selected.CanRunAgain); Assert.False(selected.CanReviewNewTransfer); Assert.Contains("Review storage changes", selected.ReportButtonText);
        Assert.DoesNotContain("interrupted", selected.Title, StringComparison.OrdinalIgnoreCase);
        center.RemoveFinished(); Assert.Equal(2, center.Interrupted.Count); Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void Summary_capture_keeps_one_snapshot_all_roots_and_no_transfer_claims()
    {
        var job = MakeJob(); var first = Result(job) with { DurablePrefix = new(job.Id, 7, 1024, "EXACT-DIGEST"), ObservedUtc = DateTime.UnixEpoch };
        job.SetStorageResult(first); job.SetState(JobState.Completed); var captured = OperationReport.Capture(job);
        job.SetStorageResult(Result(job, new(Uncertain: 1)) with { ReviewPending = true });
        string report = Assert.IsType<string>(OperationReport.ForClipboard(captured));
        Assert.Contains("Summary", report); Assert.Contains("Call details", report); Assert.Contains("Examples", report);
        Assert.Contains("3 already satisfied", report); Assert.DoesNotContain("1 uncertain", report); Assert.Contains("Recorded through sequence: 7", report);
        Assert.Contains(JsonSerializer.Serialize("C:\\captured\\line\n\"name.txt"), report);
        Assert.Contains("retained-object", report); Assert.Contains("verified-volume", report);
        Assert.DoesNotContain("Average transfer", report); Assert.DoesNotContain("Destination:", report); Assert.DoesNotContain("100%", report);
        Assert.DoesNotContain("Actual data processed", report); Assert.Contains("not a complete per-item log", report);
        Assert.Same(first, captured.Storage);
    }

    [Fact]
    public void Generic_review_store_refuses_storage_closure_booleans()
    {
        var record = new RecoveryReviewRecord("unopened", "storage", nameof(JobKind.StorageTransform), DateTime.UtcNow, "review", "text", true);
        Assert.Throws<InvalidOperationException>(() => RecoveryReviewStore.Save("unopened", record));
    }

    private static StorageReviewStatus HistoryStatus()
    {
        var job = MakeJob(); var before = job.Request.StorageTransform!.Roots[0].Observation;
        var history = new StorageHistorySnapshot(new(job.Id, 7, 1024, "digest"),
            [new(0, 0, "C:\\captured\\line\n\"name.txt", before)], StorageAction.Compress, StorageScope.ExistingDescendants,
            job.Request.StorageTransform.ReviewRevision, true, true, false, false, false,
            Result(job, new(Changed: 2, NotProcessed: 1)) with { ReviewPending = true, Discovery = StorageCompleteness.UnknownRemainder },
            new(1, 0, "C:\\captured\\pending.txt", Identity, before, StorageObservationState.Enabled, null, null, null));
        return new(new(job.Id, "private-directory", "job-storage-history.fcj", new("exact-evidence-id", 1024, "digest"), history), null, true, []);
    }

    [Fact]
    public void History_summary_preserves_unresolved_call_identity_all_roots_and_lower_bounds_without_crash_claims()
    {
        var status = HistoryStatus(); string report = StorageHistoryReport.Format(status);
        Assert.Contains("Result not confirmed", report); Assert.Contains("durable lower bound", report);
        Assert.Contains(JsonSerializer.Serialize(status.Current.History.Roots[0].Path), report);
        Assert.Contains("retained-object", report); Assert.Contains("pending.txt", report); Assert.Contains("no matching durable result", report);
        Assert.Contains("Save call details", report); Assert.DoesNotContain("crash", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Detailed call records may later be removed", report); Assert.Contains("Examples", report);
        var missing = status with { Current = status.Current with { History = status.Current.History with { Roots = [], Result = null, LastCall = null, Damaged = true } } };
        Assert.Contains("Selected-root identities are unavailable", StorageHistoryReport.Format(missing));
        Assert.Contains("Summary counters are unavailable", StorageHistoryReport.Format(missing));
    }

    [AvaloniaFact]
    public void Startup_reviewed_reports_cannot_erase_a_current_session_storage_acknowledgment()
    {
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.GetTempPath());
        var center = new OperationCenterViewModel(manager); var s = HistoryStatus();
        var binding = new StorageReviewBinding(s.Current.AssociationId, s.Current.DirectoryIdentity, s.Current.JournalLeaf,
            s.Current.Evidence, s.Current.History.Prefix.JobId, 7, 1, true, false, false);
        var ack = new StorageReviewAcknowledgment(1, Guid.NewGuid(), Guid.NewGuid(), StorageReviewChoice.AcknowledgeReviewedHistory,
            binding, "retained-report", new("exact-report-id", 256, "report-digest"), DateTime.UtcNow);
        var record = new RecoveryReviewRecord("storage-history", "Storage", nameof(JobKind.StorageTransform), DateTime.UtcNow,
            "Acknowledged reviewed storage history", "immutable text", true) { StorageAcknowledgment = ack };
        center.AddReviewedRecovery(record); center.LoadReviewedRecovery([]);
        Assert.Same(record, Assert.Single(center.ReviewedRecovery)); Assert.Contains("Reviewed; effect remains uncertain", record.Display);
        Assert.DoesNotContain("tracking closed", record.Display);
    }

    [AvaloniaFact]
    public async Task Narrow_snapshot_window_requires_displayed_evidence_before_actions_and_keeps_actions_reachable()
    {
        string text = StorageHistoryReport.Format(HistoryStatus()); int saved = 0, acknowledged = 0;
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new ReportWindow("Storage history snapshot", "Storage changes", _ => ready.Task) { Width = 640, Height = 420 };
        window.ConfigureStorageReview(text, () => { saved++; return Task.CompletedTask; }, () => { acknowledged++; return Task.CompletedTask; });
        try
        {
            window.Show(); window.UpdateLayout();
            for (int i = 0; i < 100 && window.Reading is null; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            var acknowledge = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Acknowledge reviewed history…");
            acknowledge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(0, acknowledged);
            ready.SetResult(text);
            await Assert.IsAssignableFrom<Task>(window.Reading); window.UpdateLayout();
            acknowledge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, acknowledged);
            var save = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Save call details…");
            Assert.True(save.IsEffectivelyVisible); Assert.True(acknowledge.IsEffectivelyVisible); Assert.True(acknowledge.Focus());
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, saved);
            Assert.InRange(save.Bounds.Width, 1, window.ClientSize.Width); Assert.InRange(acknowledge.Bounds.Width, 1, window.ClientSize.Width);
        }
        finally { ready.TrySetCanceled(); window.Close(); }
    }
}
