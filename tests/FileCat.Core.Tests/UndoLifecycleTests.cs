using System.Collections.Concurrent;
using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class UndoLifecycleTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    public UndoLifecycleTests() => _providers.Register(new LocalFileSystemProvider());
    public void Dispose() => _dir.Dispose();
    private JobManager Manager(IFileSystemOperations? files = null, string journal = "journals") => new(files ?? new PortableFileOperations(), _providers, _dir.Dir(journal));
    private static ItemRef Item(string path) => ItemRef.ForFileSystemPath(path, Directory.Exists(path) ? EntryKind.Directory : EntryKind.File);
    private static Job Original(params UndoStep[] steps)
    {
        var original = new Job(new JobRequest { Kind = JobKind.Move }, "Original operation", "local", [], []);
        foreach (var step in steps) original.AddUndo(step);
        original.SetState(JobState.Completed);
        return original;
    }
    private static UndoStep MoveStep(string from, string to)
    {
        var info = new PortableFileOperations().TryGetInfo(from)!;
        return new(UndoKind.MoveBack, from, to, info.Size, info.ModifiedUtc.Ticks);
    }
    private static async Task Wait(Job job, TimeSpan? limit = null)
    {
        var elapsed = Stopwatch.StartNew();
        var timeout = limit ?? TimeSpan.FromSeconds(15);
        while (!job.State.IsFinished())
        {
            Assert.True(elapsed.Elapsed < timeout,
                $"{job.Title} remained {job.State} after {elapsed.Elapsed}; done={job.ItemsDone}, failed={job.ItemsFailed}, skipped={job.ItemsSkipped}, wait={job.WaitReason}");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
    private async Task<Job> CreateEmpty(JobManager jobs, string name = "empty.txt")
    {
        var job = jobs.Submit(new JobRequest { Kind = JobKind.CreateFile, Destination = Location.FileSystem(_dir.Path), NewName = name });
        await Wait(job); Assert.True(job.CanUndo);
        return job;
    }

    [Fact]
    public void A_single_reservation_owns_review_and_cancel_releases_it_without_consuming_steps()
    {
        var original = Original(new UndoStep(UndoKind.RemoveCreatedFile, "/created", "/created", 0, 0));
        var claims = new ConcurrentBag<UndoReservation>();
        Parallel.For(0, 40, _ => { if (original.TryReserveUndo() is { } claim) claims.Add(claim); });
        var owner = Assert.Single(claims);
        Assert.Equal(UndoAttemptState.Reviewing, original.UndoState);
        Assert.False(original.CanUndo); Assert.Single(original.UndoSteps);
        owner.Dispose();
        Assert.Equal(UndoAttemptState.Available, original.UndoState);
        using var next = Assert.IsType<UndoReservation>(original.TryReserveUndo());
        owner.Dispose(); // A stale owner cannot release the newer approval.
        Assert.Equal(UndoAttemptState.Reviewing, original.UndoState);
        Assert.Single(next.Plan.Steps);
    }

    [Fact]
    public void Registry_inverse_bytes_cannot_change_the_captured_approval()
    {
        byte[] expected = [1, 2], desired = [3, 4];
        var change = new RegistryChange(RegistryAction.SetValue, new Location(Schemes.Registry, "HKEY_CURRENT_USER\\Software\\Fixture"), "value",
            new RegistryValueSnapshot(3, expected), new RegistryValueSnapshot(3, desired));
        var original = Original(new UndoStep(UndoKind.RegistryInverse, "", "", 0, 0, Registry: change));
        using var claim = Assert.IsType<UndoReservation>(original.TryReserveUndo());
        expected[0] = 9; desired[0] = 9;
        var preview = claim.Plan.Steps[0].Registry!;
        Assert.Equal(new byte[] { 1, 2 }, preview.Expected!.Data);
        Assert.Equal(new byte[] { 3, 4 }, preview.Desired!.Data);
        preview.Expected.Data[0] = 8; preview.Desired.Data[0] = 8;
        Assert.Equal(new byte[] { 1, 2 }, claim.Plan.Steps[0].Registry!.Expected!.Data);
        Assert.Equal(new byte[] { 3, 4 }, claim.Plan.Steps[0].Registry!.Desired!.Data);
    }

    [Fact]
    public async Task An_admitted_inverse_is_active_once_and_survives_removing_the_original_history()
    {
        var fs = new HeldDeleteFiles(); var jobs = Manager(fs);
        var original = await CreateEmpty(jobs);
        using var claim = Assert.IsType<UndoReservation>(original.TryReserveUndo());
        fs.Hold = true;
        var inverse = jobs.SubmitUndo(claim);
        try
        {
            await fs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(jobs.HasActiveWork); Assert.Equal(JobKind.Undo, inverse.Kind);
            Assert.Same(claim.Plan, inverse.Request.UndoPlan);
            Assert.Equal(UndoAttemptState.Attempting, original.UndoState);
            Assert.Null(original.TryReserveUndo());
            Assert.Throws<InvalidOperationException>(() => jobs.SubmitUndo(claim));
            Assert.Throws<ArgumentException>(() => jobs.Submit(new JobRequest { Kind = JobKind.Undo, UndoPlan = claim.Plan }));
            jobs.Remove(original); jobs.ClearFinished();
            Assert.Single(jobs.Jobs); Assert.Same(inverse, jobs.Jobs[0]); Assert.True(jobs.HasActiveWork);
        }
        finally { fs.Release.Set(); }
        await Wait(inverse);
        Assert.Equal(1, fs.Deletes); Assert.False(File.Exists(Path.Combine(_dir.Path, "empty.txt")));
        Assert.Equal(UndoAttemptState.Consumed, original.UndoState); Assert.False(original.CanUndo);
        Assert.Equal(1, inverse.UndoReport!.Count(UndoOutcomeKind.Removed));
        Assert.Single(inverse.UndoReport.Outcomes); Assert.False(jobs.HasActiveWork);
    }

    [Fact]
    public async Task Overlapping_work_waits_for_the_inverse_mutation_scope()
    {
        var fs = new HeldDeleteFiles(); var jobs = Manager(fs); jobs.MaxConcurrent = 4;
        var original = await CreateEmpty(jobs);
        using var claim = Assert.IsType<UndoReservation>(original.TryReserveUndo());
        fs.Hold = true;
        var inverse = jobs.SubmitUndo(claim);
        Job next;
        try
        {
            await fs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            next = jobs.Submit(new JobRequest { Kind = JobKind.CreateFile, Destination = Location.FileSystem(_dir.Path), NewName = "empty.txt", Mode = QueueMode.Start });
            Assert.Equal(JobState.Queued, next.State); Assert.Same(inverse, next.WaitingFor);
            Assert.True(JobManager.Overlaps(inverse, next));
        }
        finally { fs.Release.Set(); }
        await Wait(inverse); await Wait(next);
        Assert.Equal(JobState.Completed, next.State); Assert.True(File.Exists(Path.Combine(_dir.Path, "empty.txt")));
    }

    [Fact]
    public async Task Canceling_an_admitted_queued_inverse_keeps_a_complete_unattempted_report_and_never_replays()
    {
        var jobs = Manager(); var original = await CreateEmpty(jobs); jobs.MaxConcurrent = 0;
        using var claim = Assert.IsType<UndoReservation>(original.TryReserveUndo());
        var inverse = jobs.SubmitUndo(claim);
        Assert.True(jobs.HasActiveWork); inverse.Cancel(); await Wait(inverse);
        Assert.Equal(JobState.Canceled, inverse.State); Assert.False(jobs.HasActiveWork);
        Assert.True(File.Exists(Path.Combine(_dir.Path, "empty.txt")));
        Assert.Equal(1, inverse.UndoReport!.Count(UndoOutcomeKind.NotAttempted)); Assert.False(original.CanUndo);
        Assert.Contains("no inverse steps ran", inverse.UndoReport.Outcomes[0].Reason);
    }

    [Fact]
    public void A_failed_handoff_preserves_the_only_recorded_inverse()
    {
        var original = Original(new UndoStep(UndoKind.RegistryInverse, "", "", 0, 0, Registry: new RegistryChange(RegistryAction.CreateKey, new Location(Schemes.Registry, "fixture"), "key")),
            new(UndoKind.RemoveCreatedFile, "/created", "/created", 0, 0));
        using (var claim = Assert.IsType<UndoReservation>(original.TryReserveUndo()))
        {
            Assert.Throws<NotSupportedException>(() => Manager().SubmitUndo(claim));
            Assert.Equal(UndoAttemptState.Reviewing, original.UndoState); Assert.Equal(2, original.UndoSteps.Count);
        }
        Assert.True(original.CanUndo); Assert.Equal(2, original.UndoSteps.Count);
    }

    [Fact]
    public async Task A_late_failure_and_guarded_keep_are_counted_and_retained_ahead_of_successes_with_full_paths()
    {
        var steps = Enumerable.Range(0, 70).Select(i => MoveStep(_dir.File($"current/{i:D2}/same.txt", "content"), Path.Combine(_dir.Path, "original", i.ToString("D2"), "same.txt"))).ToArray();
        string fail = steps[45].From, changed = steps[46].From;
        File.WriteAllText(changed, "changed content after original operation");
        var jobs = Manager(new FailingMoveFiles(fail));
        var original = Original(steps);
        using var claim = Assert.IsType<UndoReservation>(original.TryReserveUndo());
        // Seventy real filesystem inverses can exceed the small-job deadline
        // on a contended Windows runner; keep a bounded bulk-work deadline.
        var inverse = jobs.SubmitUndo(claim); await Wait(inverse, TimeSpan.FromSeconds(60));
        Assert.Equal(JobState.CompletedWithIssues, inverse.State);
        var report = Assert.IsType<UndoReport>(inverse.UndoReport);
        Assert.Equal(70, report.Outcomes.Count); Assert.Equal(68, report.Count(UndoOutcomeKind.Restored));
        Assert.Equal(1, report.Count(UndoOutcomeKind.Failed)); Assert.Equal(1, report.Count(UndoOutcomeKind.Kept));
        Assert.Equal(0, report.Count(UndoOutcomeKind.NotAttempted));
        string text = report.ToText();
        Assert.Contains(fail, text); Assert.Contains(changed, text); Assert.Contains(steps[69].To, text);
        Assert.True(text.IndexOf(fail, StringComparison.Ordinal) < text.IndexOf(steps[69].To, StringComparison.Ordinal));
        Assert.True(File.Exists(fail)); Assert.True(File.Exists(changed)); Assert.False(original.CanUndo);
        Assert.Same(report, inverse.UndoReport); // Closing a presentation does not consume or release its retained report.
    }

    [Fact]
    public async Task An_unexpected_worker_exit_still_accounts_for_every_captured_step()
    {
        var steps = Enumerable.Range(0, 5).Select(i => MoveStep(_dir.File($"current/{i}.txt", "contents"), Path.Combine(_dir.Path, "original", i + ".txt"))).ToArray();
        var jobs = Manager(new FailingMoveFiles(steps[2].From, unexpected: true));
        using var claim = Assert.IsType<UndoReservation>(Original(steps).TryReserveUndo());
        var inverse = jobs.SubmitUndo(claim); await Wait(inverse);
        var report = Assert.IsType<UndoReport>(inverse.UndoReport);
        Assert.Equal(JobState.Failed, inverse.State); Assert.Equal(5, report.Outcomes.Count);
        Assert.Equal(2, report.Count(UndoOutcomeKind.Restored)); Assert.Equal(1, report.Count(UndoOutcomeKind.Failed));
        Assert.Equal(2, report.Count(UndoOutcomeKind.NotAttempted));
        Assert.Contains("unexpectedly", Assert.Single(report.Outcomes, o => o.Outcome == UndoOutcomeKind.Failed).Reason);
    }

    [Fact]
    public async Task A_replacement_move_records_the_irreversible_destination_content_at_commit_time()
    {
        string source = _dir.File("source/item.txt", "incoming"), destination = _dir.Dir("destination");
        _dir.File("destination/item.txt", "overwritten old content");
        var jobs = Manager();
        var original = jobs.Submit(new JobRequest { Kind = JobKind.Move, Sources = [Item(source)], Destination = Location.FileSystem(destination), Options = new TransferOptions { Conflicts = ConflictPolicy.Replace } });
        await Wait(original);
        using var claim = Assert.IsType<UndoReservation>(original.TryReserveUndo());
        Assert.Single(claim.Plan.Steps); var remainder = Assert.Single(claim.Plan.Remainder);
        Assert.Equal(Path.Combine(destination, "item.txt"), remainder.Path); Assert.Contains("overwritten destination contents", remainder.Reason);
        var inverse = jobs.SubmitUndo(claim); await Wait(inverse);
        Assert.Equal("incoming", File.ReadAllText(source)); Assert.False(File.Exists(Path.Combine(destination, "item.txt")));
        Assert.Contains("will not restore", inverse.UndoReport!.ToText());
    }

    [Fact]
    public async Task A_mixed_move_discloses_the_copy_delete_subset_without_inventing_an_inverse_for_it()
    {
        string same = _dir.File("source/same.txt", "same-volume"), cross = _dir.File("cross/cross.txt", "cross-volume");
        string destination = _dir.Dir("destination"); var jobs = Manager(new MixedVolumeFiles(Path.GetDirectoryName(cross)!));
        var original = jobs.Submit(new JobRequest { Kind = JobKind.Move, Sources = [Item(same), Item(cross)], Destination = Location.FileSystem(destination), Options = new TransferOptions { Conflicts = ConflictPolicy.Skip } });
        await Wait(original);
        using var claim = Assert.IsType<UndoReservation>(original.TryReserveUndo());
        Assert.Single(claim.Plan.Steps);
        Assert.Equal(PathUtil.NormalizeForCompare(same), PathUtil.NormalizeForCompare(claim.Plan.Steps[0].To));
        Assert.Contains(claim.Plan.Remainder, r => r.Path == Path.Combine(destination, "cross.txt") && r.Reason.Contains("copy-then-delete", StringComparison.Ordinal));
        var inverse = jobs.SubmitUndo(claim); await Wait(inverse);
        Assert.True(File.Exists(same)); Assert.False(File.Exists(cross));
        Assert.Equal("cross-volume", File.ReadAllText(Path.Combine(destination, "cross.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canceling_during_batch_staging_rolls_back_and_retains_failed_rollback_evidence(bool failRollback)
    {
        var fs = new HeldRenameFiles { FailRollback = failRollback }; var jobs = Manager(fs);
        string a = _dir.File("a.txt", "A"), b = _dir.File("b.txt", "B");
        var original = jobs.Submit(new JobRequest { Kind = JobKind.Rename, Sources = [Item(a), Item(b)], NewNames = ["b.txt", "a.txt"] });
        await Wait(original); fs.Enabled = true;
        using var claim = Assert.IsType<UndoReservation>(original.TryReserveUndo());
        var inverse = jobs.SubmitUndo(claim);
        try
        {
            await fs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            inverse.Cancel();
        }
        finally { fs.Release.Set(); }
        await Wait(inverse);
        Assert.Equal(JobState.Canceled, inverse.State); Assert.Equal(2, inverse.UndoReport!.Outcomes.Count);
        Assert.Equal(failRollback, inverse.UndoReport.NeedsRecovery);
        var interrupted = JournalRecovery.Scan(_dir.Dir("journals"));
        if (failRollback)
        {
            Assert.Single(interrupted); Assert.Equal("Undo", interrupted[0].Kind);
            Assert.Single(JournalRecovery.FindRenameLeftovers(interrupted[0]));
            Assert.Equal(1, inverse.UndoReport.Count(UndoOutcomeKind.Failed));
            Assert.Equal(1, inverse.UndoReport.Count(UndoOutcomeKind.NotAttempted));
            Assert.Contains(BulkRenameRunner.TempPrefix, inverse.UndoReport.ToText());
        }
        else
        {
            Assert.Empty(interrupted); Assert.Equal("B", File.ReadAllText(a)); Assert.Equal("A", File.ReadAllText(b));
            Assert.Empty(Directory.GetFiles(_dir.Path, BulkRenameRunner.TempPrefix + "*"));
            Assert.Equal(2, inverse.UndoReport.Count(UndoOutcomeKind.NotAttempted));
        }
    }

    [Fact]
    public async Task A_batch_inverse_crash_has_durable_open_rename_intents_and_a_complete_failure_report()
    {
        var fs = new CrashRenameFiles(); var jobs = Manager(fs);
        var files = new[] { _dir.File("a.txt", "A"), _dir.File("b.txt", "B"), _dir.File("c.txt", "C") };
        var original = jobs.Submit(new JobRequest { Kind = JobKind.Rename, Sources = files.Select(Item).ToArray(), NewNames = ["b.txt", "c.txt", "a.txt"] });
        await Wait(original); fs.Enabled = true;
        using var claim = Assert.IsType<UndoReservation>(original.TryReserveUndo());
        var inverse = jobs.SubmitUndo(claim); await Wait(inverse);
        Assert.Equal(JobState.Failed, inverse.State); Assert.True(inverse.UndoReport!.NeedsRecovery);
        Assert.Equal(1, inverse.UndoReport.Count(UndoOutcomeKind.Restored)); Assert.Equal(2, inverse.UndoReport.Count(UndoOutcomeKind.Failed));
        var interrupted = Assert.Single(JournalRecovery.Scan(_dir.Dir("journals")));
        Assert.Equal("Undo", interrupted.Kind);
        var records = JobJournal.ReadAll(interrupted.JournalPath);
        Assert.Equal(original.Id.ToString("N"), records.First(r => r.Type == "begin").Get("undoOf"));
        Assert.DoesNotContain(records, r => r.Type == "end");
        var leftovers = JournalRecovery.FindRenameLeftovers(interrupted); Assert.Equal(2, leftovers.Count);
        Assert.Empty(JournalRecovery.FinishRenames(leftovers, out int finished)); Assert.Equal(2, finished);
        Assert.Equal(new[] { "A", "B", "C" }, files.Select(File.ReadAllText));
    }

    [Fact]
    public void Failed_canceled_and_truncated_originals_remain_ineligible()
    {
        foreach (var state in new[] { JobState.Failed, JobState.Canceled, JobState.Interrupted })
        {
            var job = Original(new UndoStep(UndoKind.RemoveCreatedFile, "/file", "/file", 0, 0)); job.SetState(state);
            Assert.False(job.CanUndo); Assert.Null(job.TryReserveUndo());
        }
        var large = Original();
        for (int i = 0; i <= Job.UndoLimit; i++) large.AddUndo(new(UndoKind.RemoveCreatedFile, "/file" + i, "/file" + i, 0, 0));
        Assert.True(large.UndoTruncated); Assert.False(large.CanUndo); Assert.Null(large.TryReserveUndo());
    }

    private sealed class HeldDeleteFiles : PortableFileOperations
    {
        public bool Hold; public int Deletes;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public override void DeleteFile(string path)
        {
            if (Hold) { Interlocked.Increment(ref Deletes); Entered.TrySetResult(); if (!Release.Wait(TimeSpan.FromSeconds(15))) throw new IOException("Fixture release timed out."); }
            base.DeleteFile(path);
        }
    }
    private sealed class FailingMoveFiles(string fail, bool unexpected = false) : PortableFileOperations
    {
        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
        {
            if (source == fail) { if (unexpected) throw new InvalidProgramException("Unexpected fixture failure."); throw new IOException("Locked fixture item."); }
            base.Move(source, destination, replaceExisting, writeThrough);
        }
    }
    private sealed class MixedVolumeFiles(string cross) : PortableFileOperations
    {
        public override string GetVolumeRoot(string path) => PathUtil.IsSameOrUnder(path, cross) ? "other-volume" : "same-volume";
    }
    private sealed class HeldRenameFiles : PortableFileOperations
    {
        public bool Enabled, FailRollback; private int _calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
        {
            if (Enabled && FailRollback && Path.GetFileName(source).StartsWith(BulkRenameRunner.TempPrefix, StringComparison.Ordinal)) throw new IOException("Rollback locked fixture.");
            base.Move(source, destination, replaceExisting, writeThrough);
            if (Enabled && Interlocked.Increment(ref _calls) == 1) { Entered.TrySetResult(); if (!Release.Wait(TimeSpan.FromSeconds(15))) throw new IOException("Fixture release timed out."); }
        }
    }
    private sealed class CrashRenameFiles : PortableFileOperations
    {
        public bool Enabled; private int _calls;
        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
        {
            if (Enabled && Interlocked.Increment(ref _calls) == 5) throw new InvalidProgramException("Controlled fixture interruption.");
            base.Move(source, destination, replaceExisting, writeThrough);
        }
    }
}
