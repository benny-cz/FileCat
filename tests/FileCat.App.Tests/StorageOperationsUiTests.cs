using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
    private static readonly StorageObjectIdentity EfsIdentity = new(42, "0123456789ABCDEF0123456789ABCDEF")
    { VolumeBinding = "01234567-89ab-cdef-0123-456789abcdef" };

    private static Job MakeJob(StorageAction action = StorageAction.Compress)
    {
        bool efs = action is StorageAction.EncryptEfs or StorageAction.DecryptEfs;
        var identity = efs ? EfsIdentity : Identity;
        var observation = new StorageObservation(StorageObservationState.Disabled,
            action == StorageAction.DecryptEfs ? StorageObservationState.Enabled : StorageObservationState.Disabled, identity, false, null);
        var revision = Guid.NewGuid();
        const StorageScope scope = StorageScope.ExistingDescendants;
        var consent = efs ? StorageEfsConsent.Capture(revision, action, scope, [identity], new(true, action == StorageAction.EncryptEfs)) : null;
        return new(new JobRequest
        {
            Kind = JobKind.StorageTransform,
            Sources = [new ItemRef(Location.FileSystem(@"C:\captured"), "line\n\"name.txt", EntryKind.File)],
            StorageTransform = new(action, scope, revision) { Roots = [new(0, 0, observation)], EfsConsent = consent },
        }, StorageOperationText.Action(action) + " captured files", "fixture", [], []);
    }
    private static StorageResultSnapshot Result(Job job, StorageOutcomeCounts? counts = null) =>
        new(job.Request.StorageTransform!.Action, job.Request.StorageTransform.Scope, job.Request.StorageTransform.ReviewRevision, counts ?? new(AlreadySatisfied: 3))
        { Roots = [new(0, job.Request.StorageTransform.Roots[0].Observation.Identity, StorageCompleteness.Complete, false)], Discovery = StorageCompleteness.Complete };

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

    [AvaloniaTheory]
    [InlineData(StorageAction.Compress, "Compress")]
    [InlineData(StorageAction.EncryptEfs, "Encrypt with EFS")]
    [InlineData(StorageAction.DecryptEfs, "Decrypt EFS")]
    public void Typed_outcomes_and_startup_merge_preserve_the_selected_pending_history(StorageAction action, string title)
    {
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.GetTempPath());
        var center = new OperationCenterViewModel(manager); var job = MakeJob(action);
        job.SetStorageResult(Result(job) with { ReviewPending = true, Discovery = StorageCompleteness.Incomplete }); job.SetState(JobState.Failed);
        var row = new JobViewModel(job); row.Refresh(); center.Jobs.Add(row); center.Selected = row; center.Refresh(job);
        Assert.DoesNotContain("No issues reported", center.IssueSummary); Assert.Contains("Result not confirmed", center.IssueSummary);
        var candidate = manager.StorageHistoryCandidate(job); center.AddInterrupted(candidate); var selected = Assert.Single(center.Interrupted);
        center.SelectedInterrupted = selected;
        center.LoadInterrupted([candidate, candidate with { JournalPath = candidate.JournalPath + "-another", StorageJournalLeaf = candidate.JournalPath + "-another" }]);
        Assert.Equal(2, center.Interrupted.Count); Assert.Same(selected, center.SelectedInterrupted);
        Assert.True(row.CanReviewStorage); Assert.False(selected.CanCleanup); Assert.False(selected.CanMarkReviewed);
        Assert.False(selected.CanRunAgain); Assert.False(selected.CanReviewNewTransfer); Assert.Contains("Review storage changes", selected.ReportButtonText);
        Assert.StartsWith(title, selected.Title);
        Assert.DoesNotContain("interrupted", selected.Title, StringComparison.OrdinalIgnoreCase);
        center.RemoveFinished(); Assert.Equal(2, center.Interrupted.Count); Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Operations_keyboard_entry_focuses_the_pending_storage_review()
    {
        var fixture = AccessibilityTests.OpenMainWindow(width: 640, height: 420);
        try
        {
            var center = fixture.Vm.Operations;
            var job = MakeJob();
            job.SetStorageResult(Result(job) with { ReviewPending = true });
            job.SetState(JobState.Failed);
            var row = new JobViewModel(job);
            center.Jobs.Add(row);
            center.Selected = row;
            center.AddInterrupted(fixture.Services.Jobs.StorageHistoryCandidate(job));
            center.IsOpen = true;
            fixture.Window.UpdateLayout();
            var pane = fixture.Window.GetVisualDescendants().OfType<OperationsView>().Single();
            Assert.True(pane.FocusForKeyboard());
            var button = Assert.IsType<Button>(fixture.Window.FocusManager?.GetFocusedElement());
            Assert.Equal("Review storage changes", button.Content);
            Assert.Same(center.SelectedInterrupted, button.Tag);
            Assert.True(pane.FocusForKeyboard(prioritizeStorageReview: false));
            var history = pane.FindControl<ListBox>("HistoryList")!;
            var focused = Assert.IsAssignableFrom<Control>(fixture.Window.FocusManager?.GetFocusedElement());
            Assert.True(ReferenceEquals(history, focused) || history.IsVisualAncestorOf(focused));
        }
        finally { AccessibilityTests.Close(fixture.Services, fixture.Window, fixture.Root); }
    }

    [AvaloniaFact]
    public void Enter_on_a_focused_reviewed_storage_row_reaches_its_report_action()
    {
        var fixture = AccessibilityTests.OpenMainWindow(width: 640, height: 420);
        try
        {
            var center = fixture.Vm.Operations;
            center.AddReviewedRecovery(new("unavailable-fixture", "Reviewed storage", nameof(JobKind.StorageTransform),
                DateTime.UtcNow, "Acknowledged reviewed history", string.Empty, true));
            center.IsOpen = true;
            fixture.Window.UpdateLayout();
            var pane = fixture.Window.GetVisualDescendants().OfType<OperationsView>().Single();
            var reports = pane.FindControl<ListBox>("ReviewedReports")!;
            var row = Assert.IsType<ListBoxItem>(reports.ContainerFromIndex(0));
            Assert.True(row.Focus());
            var enter = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = row };
            row.RaiseEvent(enter);
            Assert.True(enter.Handled);
        }
        finally { AccessibilityTests.Close(fixture.Services, fixture.Window, fixture.Root); }
    }

    [Fact]
    public void Active_summary_does_not_invent_a_pending_call_when_no_call_prefix_is_available()
    {
        var job = MakeJob(); job.SetStorageResult(Result(job)); job.SetState(JobState.Running);
        string report = Assert.IsType<string>(OperationReport.ForClipboard(OperationReport.Capture(job)));
        Assert.Contains("If this active prefix ends with an intent without a result", report);
        Assert.DoesNotContain("An in-flight intent is pending", report);
        Assert.Contains("No durable call-record snapshot", report);
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

    private static StorageReviewStatus HistoryStatus(StorageAction action = StorageAction.Compress)
    {
        var job = MakeJob(action); var request = job.Request.StorageTransform!;
        var before = request.Roots[0].Observation;
        var desired = action == StorageAction.DecryptEfs ? StorageObservationState.Disabled : StorageObservationState.Enabled;
        var history = new StorageHistorySnapshot(new(job.Id, 7, 1024, "digest"),
            [new(0, 0, "C:\\captured\\line\n\"name.txt", before)], action, request.Scope,
            request.ReviewRevision, true, true, false, false, false,
            Result(job, new(Changed: 2, NotProcessed: 1)) with { ReviewPending = true, Discovery = StorageCompleteness.UnknownRemainder },
            new(1, 0, "C:\\captured\\pending.txt", before.Identity, before, desired, null, null, null)) { EfsConsent = request.EfsConsent };
        return new(new(job.Id, "private-directory", "job-storage-history.fcj", new("exact-evidence-id", 1024, "digest"), history), null, true, []);
    }

    [Fact]
    public void History_review_shows_the_current_observation_apart_from_the_recorded_history()
    {
        var status = HistoryStatus(); var h = status.Current.History;
        var root = h.Roots[0]; var call = h.LastCall!;
        var now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        var current = new StorageCurrentObservation[]
        {
            new(root.Path, root.Observation with { Compression = StorageObservationState.Enabled }, null),
            new(call.Path, null, new(StorageReasonKind.IdentityChanged, "open-status-C0000034", "The object is gone.", true)),
        };
        string report = StorageHistoryReport.Format(status, current, now);
        string section = report[report.IndexOf("Current observation", StringComparison.Ordinal)..];
        Assert.Contains("(UTC 2026-11-21T12:00:00.0000000Z)", section);
        Assert.Contains("not evidence of what a recorded call did", section);
        Assert.Contains("Selected root [1]: " + JsonSerializer.Serialize(root.Path) + ": main data compressed; not EFS encrypted; same volume and object ID as recorded (not proof of continuity)", section);
        Assert.Contains("Last call object: " + JsonSerializer.Serialize(call.Path) + ": unavailable now - \"open-status-C0000034\"", section);
        // The recorded facts above it are unchanged by the observation.
        Assert.Equal(StorageHistoryReport.Format(status)[..report.IndexOf("Current observation", StringComparison.Ordinal)].TrimEnd(),
            report[..report.IndexOf("Current observation", StringComparison.Ordinal)].TrimEnd());
        var replaced = new StorageCurrentObservation[] { new(root.Path, root.Observation with
            { Identity = root.Observation.Identity with { ObjectId = "replacement" } }, null) };
        Assert.Contains("a different object or volume from the recorded one", StorageHistoryReport.Format(status, replaced, now));
        Assert.DoesNotContain("Current observation", StorageHistoryReport.Format(status));
    }

    [Theory]
    [InlineData(StorageAction.Compress, "Compress", null)]
    [InlineData(StorageAction.EncryptEfs, "Encrypt with EFS", "Removal of compression from all main/named streams")]
    [InlineData(StorageAction.DecryptEfs, "Decrypt EFS", "All-stream decompression consent: not applicable to EFS decryption")]
    public void History_summary_preserves_action_consent_and_unresolved_call_evidence(StorageAction action, string label, string? consentText)
    {
        var status = HistoryStatus(action); string report = StorageHistoryReport.Format(status);
        Assert.Contains("Result not confirmed", report); Assert.Contains("durable lower bound", report);
        Assert.Contains("Requested action: " + label, report);
        if (consentText is not null)
        {
            Assert.Contains(status.Current.History.EfsConsent!.ConsentId.ToString("D"), report);
            Assert.Contains(consentText, report);
        }
        else Assert.DoesNotContain("Recorded EFS scope consent", report);
        Assert.Contains(JsonSerializer.Serialize(status.Current.History.Roots[0].Path), report);
        Assert.Contains(status.Current.History.Roots[0].Observation.Identity.ObjectId, report);
        Assert.Contains("pending.txt", report); Assert.Contains("no matching durable result", report);
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
        var retained = Assert.Single(center.ReviewedRecovery); Assert.Empty(retained.Report); Assert.Equal(ack.FileName, retained.StorageRetainedLeaf);
        Assert.True(retained.StorageVerified); Assert.Contains("Reviewed; effect remains uncertain", retained.Display);
        Assert.DoesNotContain("tracking closed", record.Display);
    }

    [AvaloniaFact]
    public void Storage_history_shares_one_row_bound_preserves_selection_and_does_not_retain_report_bodies()
    {
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.GetTempPath());
        var center = new OperationCenterViewModel(manager); string large = new('x', 1_000_000);
        for (int i = 0; i < 200; i++)
        {
            center.AddInterrupted(manager.StorageHistoryCandidate($"job-storage-20000101000000-{Guid.NewGuid():N}.fcj") with { Title = new string('漢', 10_000) });
            Assert.InRange(center.StorageDescriptorCount, 1, 64);
        }
        var selected = center.Interrupted[0]; center.SelectedInterrupted = selected;
        for (int i = 0; i < 80; i++)
        {
            string artifact = $"storage-{Guid.NewGuid():N}-ack-{Guid.NewGuid():N}.json";
            center.AddReviewedRecovery(new(artifact, new string('漢', 10_000), nameof(JobKind.StorageTransform), DateTime.MinValue,
                large, large, false) { StorageRetainedLeaf = artifact });
            Assert.Equal(64, center.StorageDescriptorCount); Assert.Contains(selected, center.Interrupted);
        }
        Assert.Equal(63, center.ReviewedRecovery.Count); Assert.Same(selected, center.SelectedInterrupted);
        Assert.True(center.HasMoreStorageHistory); Assert.True(center.StorageRowsOmitted);
        Assert.All(center.ReviewedRecovery, r => { Assert.Empty(r.Report); Assert.True(r.Title.Length <= 257); Assert.Equal("Storage history", r.Action); });
        Assert.DoesNotContain("0001", center.ReviewedRecovery[0].Display); Assert.Contains("verification required", center.ReviewedRecovery[0].Display);
        center.LoadInterrupted([]); Assert.Contains(selected, center.Interrupted); Assert.Equal(64, center.StorageDescriptorCount);
    }

    [AvaloniaTheory]
    [InlineData(StorageAction.Compress)]
    [InlineData(StorageAction.EncryptEfs)]
    [InlineData(StorageAction.DecryptEfs)]
    public async Task Narrow_snapshot_window_requires_displayed_evidence_before_actions_and_keeps_actions_reachable(StorageAction action)
    {
        string text = StorageHistoryReport.Format(HistoryStatus(action)); int saved = 0, acknowledged = 0, calls = 0;
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new ReportWindow("Storage history snapshot", "Storage changes", _ => ready.Task) { Width = 640, Height = 420 };
        window.ConfigureStorageReview(text, () => { saved++; return Task.CompletedTask; }, () => { acknowledged++; return Task.CompletedTask; });
        window.ConfigureStorageCalls(() => { calls++; return Task.CompletedTask; });
        try
        {
            window.Show(); window.UpdateLayout();
            for (int i = 0; i < 100 && window.Reading is null; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            var acknowledge = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Acknowledge reviewed history…");
            acknowledge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(0, acknowledged);
            var viewCalls = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "View recorded calls…");
            viewCalls.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(0, calls);
            ready.SetResult(text);
            await Assert.IsAssignableFrom<Task>(window.Reading); window.UpdateLayout();
            acknowledge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, acknowledged);
            viewCalls.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, calls); Assert.True(viewCalls.IsEffectivelyVisible);
            var save = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Save call details…");
            Assert.True(save.IsEffectivelyVisible); Assert.True(acknowledge.IsEffectivelyVisible); Assert.True(acknowledge.Focus());
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, saved);
            Assert.InRange(save.Bounds.Width, 1, window.ClientSize.Width); Assert.InRange(acknowledge.Bounds.Width, 1, window.ClientSize.Width);
        }
        finally { ready.TrySetCanceled(); window.Close(); }
    }
}
