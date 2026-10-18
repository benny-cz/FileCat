using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class SyncGroupTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private JobManager Manager()
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        return new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir.Path, "journal"));
    }

    private JobRequest Copy(string source, string target, ConflictPolicy conflicts = ConflictPolicy.Ask) => new()
    {
        Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
        Destination = Location.FileSystem(target), Mode = QueueMode.Queue,
        Options = new TransferOptions { Conflicts = conflicts },
    };

    private JobRequest Remove(string path) => new()
    {
        Kind = JobKind.Delete, Sources = [ItemRef.ForFileSystemPath(path, EntryKind.File)], Mode = QueueMode.Queue,
    };

    private static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 500 && !done(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(done(), "Timed out waiting for synchronization job state.");
    }

    [Fact]
    public async Task Stop_cancels_every_unstarted_child_but_not_an_identical_endpoint_group_or_unrelated_job()
    {
        string source = _dir.Dir("source"), target = _dir.Dir("target"), other = _dir.Dir("other");
        string a = _dir.File("source/a.txt", "a"), b = _dir.File("source/b.txt", "b"), c = _dir.File("source/c.txt", "c");
        string extra = _dir.File("target/extra.txt", "keep");
        var manager = Manager();
        manager.MaxConcurrent = 0;
        var first = manager.SubmitSyncGroup([Copy(a, target), Remove(extra)], source, target, "copy 1, remove 1");
        var second = manager.SubmitSyncGroup([Copy(b, target)], source, target, "copy 1");
        var unrelated = manager.Submit(Copy(c, other));
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.ReviewRevision, second.ReviewRevision);
        Assert.Equal(first.Steps.Select(j => j.Id), first.ChildIds);
        Assert.All(first.Steps, j => Assert.Equal(JobState.Queued, j.State));
        first.Stop();
        manager.MaxConcurrent = 4;
        manager.Schedule();
        await Until(() => first.IsFinished && second.IsFinished && unrelated.State.IsFinished());
        Assert.All(first.Steps, j => Assert.Equal(JobState.Canceled, j.State));
        Assert.False(File.Exists(Path.Combine(target, "a.txt")));
        Assert.True(File.Exists(extra));
        Assert.Equal("b", File.ReadAllText(Path.Combine(target, "b.txt")));
        Assert.Equal("c", File.ReadAllText(Path.Combine(other, "c.txt")));
        Assert.Contains("Synchronization stopped", first.Outcome);
        Assert.Contains("1/1 steps finished", second.Outcome);
    }

    [Fact]
    public async Task Failed_transfer_holds_pending_removal_until_explicit_review()
    {
        string source = _dir.Dir("source"), target = _dir.Dir("target");
        string missing = Path.Combine(source, "missing.txt");
        string extra = _dir.File("target/extra.txt", "keep");
        var manager = Manager();
        var group = manager.SubmitSyncGroup([Copy(missing, target), Remove(extra)], source, target, "copy 1, remove 1");
        await Until(() => group.Steps[0].State.IsFinished() && group.PendingRemovalReview);
        Assert.True(File.Exists(extra));
        Assert.Equal(JobState.Queued, group.Steps[1].State);
        Assert.True(group.Steps[1].IsPaused);
        Assert.Contains("held for review", group.Outcome);
        Assert.True(group.ContinueRemovals());
        await Until(() => group.IsFinished);
        Assert.False(File.Exists(extra));
        Assert.False(group.ContinueRemovals());
        Assert.True(group.Steps[0].State is JobState.Failed or JobState.CompletedWithIssues);
        Assert.Equal(JobState.Completed, group.Steps[1].State);
        Assert.Contains("1 failed", group.Outcome);
    }

    [Fact]
    public async Task Stop_during_a_delayed_transfer_keeps_unstarted_removal_and_unrelated_work_continues()
    {
        string source = _dir.Dir("source"), target = _dir.Dir("target"), other = _dir.Dir("other");
        string incoming = _dir.File("source/a.txt", "incoming");
        string independent = _dir.File("source/b.txt", "independent");
        _dir.File("target/a.txt", "existing");
        string extra = _dir.File("target/extra.txt", "keep");
        var manager = Manager();
        var group = manager.SubmitSyncGroup([Copy(incoming, target), Remove(extra)], source, target, "copy 1, remove 1");
        await Until(() => group.Steps[0].State == JobState.AwaitingDecision);
        Assert.Equal(JobState.Queued, group.Steps[1].State);
        var unrelated = manager.Submit(Copy(independent, other));
        group.Stop();
        await Until(() => group.IsFinished && unrelated.State.IsFinished());
        Assert.Equal(JobState.Canceled, group.Steps[1].State);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.True(File.Exists(extra));
        Assert.Equal("independent", File.ReadAllText(Path.Combine(other, "b.txt")));
    }

    [Fact]
    public async Task Canceling_one_transfer_holds_later_removal_for_review()
    {
        string source = _dir.Dir("source"), target = _dir.Dir("target");
        string incoming = _dir.File("source/a.txt", "incoming");
        _dir.File("target/a.txt", "existing");
        string extra = _dir.File("target/extra.txt", "keep");
        var manager = Manager();
        var group = manager.SubmitSyncGroup([Copy(incoming, target), Remove(extra)], source, target, "copy 1, remove 1");
        await Until(() => group.Steps[0].State == JobState.AwaitingDecision);
        group.Steps[0].Cancel();
        await Until(() => group.PendingRemovalReview);
        Assert.False(group.StopRequested);
        Assert.Equal(JobState.Queued, group.Steps[1].State);
        Assert.True(File.Exists(extra));
        Assert.True(group.SkipRemovals());
        await Until(() => group.IsFinished);
        Assert.True(File.Exists(extra));
    }

    [Fact]
    public async Task Expected_policy_skip_does_not_hold_target_only_removal()
    {
        string source = _dir.Dir("source"), target = _dir.Dir("target");
        string original = _dir.File("source/same.txt", "incoming");
        _dir.File("target/same.txt", "existing");
        string extra = _dir.File("target/extra.txt", "remove");
        var manager = Manager();
        var group = manager.SubmitSyncGroup([Copy(original, target, ConflictPolicy.Skip), Remove(extra)],
            source, target, "copy 1, remove 1");
        await Until(() => group.IsFinished);
        Assert.False(group.PendingRemovalReview);
        Assert.Equal(JobState.CompletedWithIssues, group.Steps[0].State);
        Assert.True(group.Steps[0].ItemsSkipped > 0);
        Assert.Equal(0, group.Steps[0].ItemsFailed);
        Assert.False(File.Exists(extra));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(target, "same.txt")));
    }

    [Fact]
    public async Task Removal_only_plan_starts_without_a_transfer_gate()
    {
        string source = _dir.Dir("source"), target = _dir.Dir("target");
        string extra = _dir.File("target/extra.txt", "remove");
        var manager = Manager();
        var group = manager.SubmitSyncGroup([Remove(extra)], source, target, "remove 1");
        Assert.Equal([extra.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)], group.PendingRemovalPaths);
        await Until(() => group.IsFinished);
        Assert.False(File.Exists(extra));
        Assert.False(group.PendingRemovalReview);
    }

    [Fact]
    public async Task Skipping_held_removals_keeps_target_and_retires_only_finished_group_history()
    {
        string source = _dir.Dir("source"), target = _dir.Dir("target");
        string extra = _dir.File("target/extra.txt", "keep");
        var manager = Manager();
        var group = manager.SubmitSyncGroup([Copy(Path.Combine(source, "missing.txt"), target), Remove(extra)],
            source, target, "copy 1, remove 1");
        await Until(() => group.PendingRemovalReview);
        Assert.True(group.SkipRemovals());
        await Until(() => group.IsFinished);
        Assert.True(File.Exists(extra));
        Assert.True(group.RemovalsSkipped);
        Assert.Equal(JobState.Canceled, group.Steps[1].State);
        Assert.Contains(group, manager.SyncGroups);
        manager.RemoveSyncGroup(group);
        Assert.DoesNotContain(group, manager.SyncGroups);
        Assert.Contains(group.Steps[0], manager.Jobs);
    }
}
