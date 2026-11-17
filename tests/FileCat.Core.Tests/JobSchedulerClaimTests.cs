using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class JobSchedulerClaimTests
{
    private sealed class Probe(Action run) : IJobExecutor { public void Execute() => run(); }

    [Fact]
    public void Reentrant_cancel_retirement_and_throwing_observers_do_not_duplicate_handoff_or_retire_an_active_worker()
    {
        using var temp = new TempDir();
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Join(temp.Path, "journal")) { MaxConcurrent = 0 };
        const JobKind probeKind = (JobKind)2001;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var finished = new CountdownEvent(2);
        int starts = 0, active = 0, terminal = 0, changedAfterThrow = 0;
        JobExecutors.ExtraExecutors[probeKind] = (job, _, _, _) => new Probe(() =>
        {
            Assert.Equal(1, Interlocked.Increment(ref active));
            Interlocked.Increment(ref starts); entered.Set();
            try { Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)); }
            finally { Interlocked.Decrement(ref active); }
        });
        try
        {
            var canceled = manager.Submit(new() { Kind = probeKind, Sources = [] });
            var worker = manager.SubmitWithRegistration(new() { Kind = probeKind, Sources = [] }, job =>
            {
                job.Changed += changed => { if (changed.State is JobState.Running or JobState.Completed) throw new IOException("Observer fixture."); };
                job.Changed += changed => { if (changed.State == JobState.Completed) Interlocked.Increment(ref changedAfterThrow); };
            });
            manager.JobChanged += job => { if (ReferenceEquals(job, canceled) && job.State == JobState.Canceled) manager.Schedule(); };
            manager.JobFinished += _ => throw new IOException("Finished observer fixture.");
            manager.JobFinished += _ => { Interlocked.Increment(ref terminal); finished.Signal(); };
            manager.MaxConcurrent = 1;
            canceled.Cancel();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)); Assert.Equal(JobState.Running, worker.State);
            for (int i = 0; i < 8; i++) manager.Schedule();
            Assert.Equal(1, starts); Assert.False(worker.State.IsFinished());
            release.Set(); Assert.True(finished.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(JobState.Completed, worker.State); Assert.Equal(1, starts); Assert.Equal(2, terminal); Assert.Equal(1, changedAfterThrow);
        }
        finally { release.Set(); JobExecutors.ExtraExecutors.TryRemove(probeKind, out _); }
    }

    [Fact]
    public void Pre_start_paused_job_keeps_its_queue_position_but_can_be_explicitly_reordered_behind_later_work()
    {
        using var temp = new TempDir();
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Join(temp.Path, "journal")) { MaxConcurrent = 0 };
        var directory = Location.FileSystem(Path.Join(temp.Path, "target"));
        var first = manager.SubmitWithRegistration(new() { Kind = JobKind.CreateDirectory, Destination = directory, NewName = "same" }, job => job.Pause());
        var second = manager.Submit(new() { Kind = JobKind.CreateDirectory, Destination = directory, NewName = "same" });
        manager.MaxConcurrent = 1; manager.Schedule();
        Assert.Same(first, second.WaitingFor);
        first.Pause(); manager.Schedule(); Assert.Null(first.WaitingFor); Assert.Equal("Paused before start", first.WaitReason);
        Assert.True(manager.MoveInQueue(second, -1));
        Assert.True(SpinWait.SpinUntil(() => second.State.IsFinished(), TimeSpan.FromSeconds(5)));
        Assert.Null(first.StartedUtc); first.Cancel(); Assert.Equal(JobState.Canceled, first.State);
    }
}
