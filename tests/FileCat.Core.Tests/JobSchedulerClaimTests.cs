using System.Collections.Concurrent;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

// Storage fixtures below reserve process-wide review pins and the single storage native allowance.
[Collection("Storage review process resources")]
public sealed class JobSchedulerClaimTests
{
    private sealed class Probe(Action run) : IJobExecutor { public void Execute() => run(); }
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    /// <summary>Pins plain local files and changes each held root once. It has no EFS, stream or key service.</summary>
    private sealed class StorageFixture : IStorageTransforms
    {
        internal readonly List<FixturePin> Pins = [];
        internal int Traversals, Calls;
        internal Action? DuringCall;
        internal bool AllAlive { get { lock (Pins) return Pins.All(pin => pin.IsAlive); } }
        internal bool AllRetiredOnce { get { lock (Pins) return Pins.All(pin => pin.Disposals == 1); } }
        public StorageProblem? Unavailable(StorageAction action) => null;
        public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct)
        {
            var pin = new FixturePin(target, new(StorageObservationState.Disabled, StorageObservationState.Disabled,
                new StorageObjectIdentity(5, target.Path) { VolumeBinding = "scheduler-fixture" }, false, null));
            lock (Pins) Pins.Add(pin);
            return pin;
        }
        public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct)
        { Interlocked.Increment(ref Traversals); return new FixtureTraversal(this, (FixturePin)root); }
    }
    private sealed class FixturePin(StorageTarget target, StorageObservation observation) : IStorageReviewLease
    {
        private int _disposals;
        public StorageTarget Target => target;
        public StorageObservation Observation => observation;
        public int Disposals => Volatile.Read(ref _disposals);
        public bool IsAlive => Disposals == 0;
        public void Dispose() => Interlocked.Increment(ref _disposals);
    }
    private sealed class FixtureTraversal(StorageFixture adapter, FixturePin pin) : IStorageTraversal
    {
        public IStorageObjectLease Root { get; } = new FixtureObject(adapter, pin);
        public IReadOnlyList<StorageObjectIdentity> RootAncestors(CancellationToken ct) => [];
        public IEnumerable<IStorageObjectLease> Children(IStorageObjectLease directory, CancellationToken ct) => [];
        public void Dispose() => Root.Dispose();
    }
    private sealed class FixtureObject(StorageFixture adapter, FixturePin pin) : IStorageObjectLease
    {
        private StorageObservation _state = pin.Observation;
        public string Path => pin.Target.Path;
        public StorageObjectIdentity Identity => pin.Observation.Identity;
        public StorageObservation Observe(CancellationToken ct) => _state;
        public StorageObservation PrepareMutation(StorageAction action, CancellationToken ct) => _state;
        public StorageMutationResult ApplyAdmitted(StorageAction action)
        {
            Interlocked.Increment(ref adapter.Calls); adapter.DuringCall?.Invoke();
            var before = _state; _state = before with { Compression = StorageObservationState.Enabled };
            return new(before, _state, StorageEffect.Changed, null) { NativeCallAttempted = true };
        }
        public void Dispose() { }
    }
    private static JobManager StorageManager(TempDir temp, StorageFixture adapter) =>
        new(new PortableFileOperations(), new ProviderRegistry(), adapter, Path.Join(temp.Path, "journal")) { MaxConcurrent = 0 };
    /// <summary>Prepare before any storage worker holds the native allowance; the review hands its pins to the job.</summary>
    private static Job SubmitStorage(JobManager manager, int roots, Action<Job>? register = null)
    {
        var sources = Enumerable.Range(0, roots).Select(i => new ItemRef(Location.FileSystem(@"C:\Scheduler"), "item-" + i, EntryKind.File)).ToArray();
        using var review = manager.PrepareStorageReview(sources, StorageAction.Compress, Ct);
        return manager.SubmitStorage(review, review.Approve(StorageAction.Compress, StorageScope.SelectedItems), register ?? (_ => { }));
    }

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
    public async Task Pre_start_paused_job_keeps_its_queue_position_but_can_be_explicitly_reordered_behind_later_work()
    {
        using var temp = new TempDir();
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Join(temp.Path, "journal")) { MaxConcurrent = 0 };
        var directory = Location.FileSystem(Path.Join(temp.Path, "target"));
        var first = manager.SubmitWithRegistration(new() { Kind = JobKind.CreateDirectory, Destination = directory, NewName = "same" }, job => job.Pause());
        var second = manager.Submit(new() { Kind = JobKind.CreateDirectory, Destination = directory, NewName = "same" });
        var firstFinished = new TaskCompletionSource<JobState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFinished = new TaskCompletionSource<JobState>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.JobFinished += job =>
        {
            if (ReferenceEquals(job, first)) firstFinished.TrySetResult(job.State);
            if (ReferenceEquals(job, second)) secondFinished.TrySetResult(job.State);
        };
        manager.MaxConcurrent = 1; manager.Schedule();
        Assert.Same(first, second.WaitingFor);
        first.Pause(); manager.Schedule(); Assert.Null(first.WaitingFor); Assert.Equal("Paused before start", first.WaitReason);
        Assert.True(manager.MoveInQueue(second, -1));

        var timeout = TimeSpan.FromSeconds(5);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(JobState.Completed, await secondFinished.Task.WaitAsync(timeout, ct));
        Assert.Null(first.StartedUtc);
        first.Cancel();
        // The finishing worker may have claimed cancellation on its own scheduling pass.
        Assert.Equal(JobState.Canceled, await firstFinished.Task.WaitAsync(timeout, ct));
        Assert.Null(first.StartedUtc);
    }

    [Fact]
    public void An_actual_storage_worker_handoff_failure_enters_no_worker_retires_the_owner_once_and_publishes_failed()
    {
        using var temp = new TempDir(); var adapter = new StorageFixture(); var manager = StorageManager(temp, adapter);
        const JobKind kind = (JobKind)2121; using var laterRan = new ManualResetEventSlim();
        int handoffs = 0, finished = 0, reviews = 0;
        JobExecutors.ExtraExecutors[kind] = (_, _, _, _) => new Probe(laterRan.Set);
        try
        {
            manager.StartWorker = _ => { handoffs++; throw new OutOfMemoryException("Worker handoff fixture."); };
            var job = SubmitStorage(manager, 2);
            manager.JobFinished += finishedJob => { if (ReferenceEquals(finishedJob, job)) { finished++; Assert.True(adapter.AllRetiredOnce); } };
            manager.StorageReviewRequired += _ => reviews++;
            manager.MaxConcurrent = 1; manager.Schedule();
            Assert.Equal(1, handoffs); Assert.Equal(JobState.Failed, job.State); Assert.Equal(1, finished); Assert.Equal(0, reviews);
            // No worker entered: no evidence reservation, journal, traversal or native call.
            Assert.Equal(0, adapter.Traversals + adapter.Calls); Assert.True(adapter.AllRetiredOnce);
            Assert.False(Directory.Exists(Path.Join(temp.Path, "journal")));
            var result = job.StorageResult!;
            Assert.True(result.ExecutionRefused); Assert.False(result.ReviewPending); Assert.False(result.Canceled);
            Assert.Equal(new StorageOutcomeCounts(NotProcessed: 2), result.Counts); Assert.Equal(JobState.Failed, result.FinalState());
            Assert.All(result.Roots, root => Assert.Equal(StorageCompleteness.Incomplete, root.Completeness));
            Assert.Contains(job.Issues, issue => issue.Severity == IssueSeverity.Error && issue.Message.Contains("could not start", StringComparison.Ordinal));
            for (int i = 0; i < 4; i++) manager.Schedule();
            Assert.Equal(1, finished); Assert.True(adapter.AllRetiredOnce);
            // The failed claim was released: later work is scheduled and starts normally.
            manager.StartWorker = thread => thread.Start();
            var later = manager.Submit(new() { Kind = kind, Sources = [] });
            Assert.True(laterRan.Wait(Wait, Ct)); Assert.True(SpinWait.SpinUntil(() => later.State == JobState.Completed, Wait));
        }
        finally { JobExecutors.ExtraExecutors.TryRemove(kind, out _); }
    }

    [Fact]
    public void A_handoff_failure_for_one_batch_entry_finalizes_only_that_claim_and_still_starts_later_entries()
    {
        using var temp = new TempDir(); var adapter = new StorageFixture(); var manager = StorageManager(temp, adapter);
        const JobKind kind = (JobKind)2127; int runs = 0; var finished = new ConcurrentDictionary<Job, int>();
        using var storageFinished = new ManualResetEventSlim();
        JobExecutors.ExtraExecutors[kind] = (_, _, _, _) => new Probe(() => Interlocked.Increment(ref runs));
        try
        {
            var failing = manager.Submit(new() { Kind = kind, Sources = [], Description = "failing handoff" });
            var started = manager.Submit(new() { Kind = kind, Sources = [], Description = "started probe" });
            var storage = SubmitStorage(manager, 1);
            manager.JobFinished += job => { finished.AddOrUpdate(job, 1, (_, count) => count + 1); if (ReferenceEquals(job, storage)) storageFinished.Set(); };
            manager.StartWorker = thread =>
            {
                if (thread.Name == "FileCat job " + failing.ShortId) throw new OutOfMemoryException("Batch handoff fixture.");
                thread.Start();
            };
            manager.MaxConcurrent = 3; manager.Schedule(); // one pass reserves both probes; the first handoff fails
            Assert.Equal(JobState.Failed, failing.State);
            Assert.Contains(failing.Issues, issue => issue.Message.Contains("could not start", StringComparison.Ordinal));
            Assert.True(storageFinished.Wait(Wait, Ct));
            Assert.True(SpinWait.SpinUntil(() => finished.Count == 3, Wait)); // each reserved entry reached exactly one terminal publication
            Assert.Equal(JobState.Completed, started.State); Assert.Equal(1, Volatile.Read(ref runs)); Assert.Equal(JobState.Completed, storage.State);
            Assert.Equal(1, adapter.Calls); Assert.True(adapter.AllRetiredOnce); Assert.All(finished.Values, count => Assert.Equal(1, count));
        }
        finally { JobExecutors.ExtraExecutors.TryRemove(kind, out _); }
    }

    [Fact]
    public void Stop_while_a_storage_launch_claim_is_held_resolves_that_claim_once_before_or_after_the_handoff()
    {
        using var temp = new TempDir(); var adapter = new StorageFixture(); var manager = StorageManager(temp, adapter);
        int handoffs = 0; var finished = new ConcurrentDictionary<Job, int>();
        manager.StartWorker = thread => { Interlocked.Increment(ref handoffs); thread.Start(); };
        manager.JobFinished += job => finished.AddOrUpdate(job, 1, (_, count) => count + 1);

        // Before TryStart: a later canceled entry retires in the same pass, while the storage job already holds its claim.
        var claimed = SubmitStorage(manager, 1);
        var trigger = manager.Submit(new() { Kind = (JobKind)2122, Sources = [] });
        Assert.Equal("Waiting for a free slot", claimed.WaitReason);
        string? reasonAtStop = "unset"; JobState stateAtStop = default;
        manager.JobFinished += job =>
        {
            if (!ReferenceEquals(job, trigger)) return;
            reasonAtStop = claimed.WaitReason; stateAtStop = claimed.State; // selection clears the wait, TryStart has not run
            claimed.Cancel();
        };
        manager.MaxConcurrent = 1; manager.CancelTogether([trigger]);
        Assert.Null(reasonAtStop); Assert.Equal(JobState.Queued, stateAtStop);
        Assert.Equal(JobState.Canceled, claimed.State); Assert.Equal(JobState.Canceled, trigger.State);
        Assert.Equal(0, handoffs); Assert.Null(claimed.StartedUtc); Assert.True(adapter.AllRetiredOnce);
        Assert.Equal(1, finished[claimed]); Assert.True(claimed.StorageResult!.Canceled); Assert.Equal(JobState.Canceled, claimed.StorageResult.FinalState());
        Assert.False(Directory.Exists(Path.Join(temp.Path, "journal")));

        // After TryStart: Stop arrives at the actual handoff. Repeated scheduling never retires the claimed owner;
        // the worker that does enter owns retirement and makes no native call.
        Job? handedOff = null; bool aliveAtHandoff = false; JobState stateAtHandoff = default;
        using var done = new ManualResetEventSlim();
        manager.JobFinished += job => { if (ReferenceEquals(job, handedOff)) done.Set(); };
        manager.StartWorker = thread =>
        {
            Interlocked.Increment(ref handoffs); handedOff!.Cancel();
            for (int i = 0; i < 3; i++) manager.Schedule();
            aliveAtHandoff = adapter.Pins[^1].IsAlive; stateAtHandoff = handedOff.State;
            thread.Start();
        };
        var stopped = SubmitStorage(manager, 1, job => handedOff = job);
        Assert.True(done.Wait(Wait, Ct));
        Assert.Equal(1, Volatile.Read(ref handoffs)); Assert.True(aliveAtHandoff); Assert.Equal(JobState.Stopping, stateAtHandoff);
        Assert.Equal(JobState.Canceled, stopped.State); Assert.Equal(1, finished[stopped]); Assert.True(adapter.AllRetiredOnce);
        Assert.Equal(0, adapter.Traversals + adapter.Calls); Assert.True(stopped.StorageResult!.Canceled);
        Assert.Equal(new StorageOutcomeCounts(NotProcessed: 1), stopped.StorageResult.Counts);
    }

    [Fact]
    public void A_throwing_running_observer_in_a_multi_entry_batch_starts_each_claim_once_while_storage_waits_for_all()
    {
        using var temp = new TempDir(); var adapter = new StorageFixture(); var manager = StorageManager(temp, adapter);
        const JobKind kind = (JobKind)2123; var ct = Ct;
        var order = new ConcurrentQueue<string>(); var runs = new ConcurrentDictionary<string, int>();
        using var entered = new CountdownEvent(2); using var releaseFirst = new ManualResetEventSlim(); using var releaseSecond = new ManualResetEventSlim();
        using var storageFinished = new ManualResetEventSlim();
        JobExecutors.ExtraExecutors[kind] = (job, _, _, _) => new Probe(() =>
        {
            string name = job.Request.Description!; runs.AddOrUpdate(name, 1, (_, count) => count + 1); entered.Signal();
            Assert.True((name == "first probe" ? releaseFirst : releaseSecond).Wait(Wait, ct));
            order.Enqueue(name);
        });
        adapter.DuringCall = () => order.Enqueue("storage call");
        int runningAfterThrow = 0, managerAfterThrow = 0;
        try
        {
            var first = manager.SubmitWithRegistration(new() { Kind = kind, Sources = [], Description = "first probe" }, job =>
            {
                job.Changed += changed => { if (changed.State == JobState.Running) throw new IOException("Running observer fixture."); };
                job.Changed += changed => { if (changed.State == JobState.Running) Interlocked.Increment(ref runningAfterThrow); };
            });
            var second = manager.Submit(new() { Kind = kind, Sources = [], Description = "second probe" });
            var storage = SubmitStorage(manager, 1);
            manager.JobChanged += job => { if (ReferenceEquals(job, first) && job.State == JobState.Running) throw new IOException("Manager observer fixture."); };
            manager.JobChanged += job => { if (ReferenceEquals(job, first) && job.State == JobState.Running) Interlocked.Increment(ref managerAfterThrow); };
            manager.JobFinished += job => { if (ReferenceEquals(job, storage)) storageFinished.Set(); };
            manager.MaxConcurrent = 3; manager.Schedule(); // one pass reserves both unrelated probes; the global storage reservation waits
            Assert.True(entered.Wait(Wait, ct));
            Assert.Equal(1, Volatile.Read(ref runningAfterThrow)); Assert.Equal(1, Volatile.Read(ref managerAfterThrow));
            Assert.Equal(JobState.Running, first.State); Assert.Equal(JobState.Running, second.State);
            Assert.Equal(JobState.Queued, storage.State); Assert.Same(first, storage.WaitingFor);
            Assert.Contains("storage changes run exclusively", storage.WaitReason); Assert.Contains("first probe", storage.WaitReason);
            releaseFirst.Set();
            Assert.True(SpinWait.SpinUntil(() => first.State.IsFinished(), Wait));
            manager.Schedule(); // the wait now names the remaining blocker
            Assert.Equal(JobState.Queued, storage.State); Assert.Same(second, storage.WaitingFor); Assert.Contains("second probe", storage.WaitReason);
            releaseSecond.Set();
            Assert.True(storageFinished.Wait(Wait, ct));
            Assert.Equal(JobState.Completed, storage.State); Assert.Null(storage.WaitingFor); Assert.Null(storage.WaitReason);
            Assert.Equal(new[] { "first probe", "second probe", "storage call" }, order);
            Assert.Equal(2, runs.Count); Assert.All(runs.Values, count => Assert.Equal(1, count));
            Assert.Equal(JobState.Completed, first.State); Assert.True(SpinWait.SpinUntil(() => second.State == JobState.Completed, Wait));
            Assert.Equal(1, adapter.Calls); Assert.True(adapter.AllRetiredOnce);
        }
        finally { releaseFirst.Set(); releaseSecond.Set(); JobExecutors.ExtraExecutors.TryRemove(kind, out _); }
    }

    [Fact]
    public void A_paused_unstarted_storage_request_may_be_overtaken_and_its_wait_state_stays_truthful()
    {
        using var temp = new TempDir(); var adapter = new StorageFixture(); var manager = StorageManager(temp, adapter);
        const JobKind kind = (JobKind)2124; var ct = Ct; var order = new ConcurrentQueue<string>();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var storageFinished = new ManualResetEventSlim();
        JobExecutors.ExtraExecutors[kind] = (_, _, _, _) => new Probe(() => { entered.Set(); Assert.True(release.Wait(Wait, ct)); order.Enqueue("probe"); });
        adapter.DuringCall = () => order.Enqueue("storage call");
        try
        {
            var storage = SubmitStorage(manager, 1, job => job.Pause());
            var later = manager.Submit(new() { Kind = kind, Sources = [], Description = "later probe" });
            manager.JobFinished += job => { if (ReferenceEquals(job, storage)) storageFinished.Set(); };
            manager.MaxConcurrent = 1; manager.Schedule();
            Assert.Null(storage.WaitingFor); Assert.Equal("Paused before start", storage.WaitReason);
            Assert.Same(storage, later.WaitingFor); Assert.Contains("storage changes run exclusively", later.WaitReason);
            Assert.True(manager.MoveInQueue(later, -1)); // an explicit reorder may overtake the paused, unstarted storage request
            Assert.True(entered.Wait(Wait, ct));
            Assert.Equal(JobState.Running, later.State); Assert.Null(later.WaitingFor); Assert.Null(later.WaitReason);
            Assert.Equal(JobState.Queued, storage.State); Assert.Null(storage.StartedUtc); Assert.True(adapter.AllAlive); Assert.Equal(0, adapter.Traversals);
            storage.Resume(); // it now waits for the active job it no longer precedes
            Assert.Same(later, storage.WaitingFor); Assert.Contains("later probe", storage.WaitReason); Assert.Contains("storage changes run exclusively", storage.WaitReason);
            storage.Pause(); manager.Schedule(); // a request paused before start keeps no stale blocker
            Assert.Null(storage.WaitingFor); Assert.Equal("Paused before start", storage.WaitReason);
            storage.Resume(); Assert.Same(later, storage.WaitingFor);
            release.Set();
            Assert.True(storageFinished.Wait(Wait, ct));
            Assert.Equal(JobState.Completed, storage.State); Assert.Null(storage.WaitingFor); Assert.Null(storage.WaitReason);
            Assert.Equal(new[] { "probe", "storage call" }, order); Assert.Equal(1, adapter.Calls); Assert.True(adapter.AllRetiredOnce);
        }
        finally { release.Set(); JobExecutors.ExtraExecutors.TryRemove(kind, out _); }
    }

    [Fact]
    public void An_active_or_paused_storage_owner_is_never_bypassed_by_reordering_or_a_start_now_request()
    {
        using var temp = new TempDir(); var adapter = new StorageFixture(); var manager = StorageManager(temp, adapter);
        const JobKind kind = (JobKind)2125; var ct = Ct; int storageCalls = 0;
        using var probeRan = new ManualResetEventSlim(); using var storageFinished = new ManualResetEventSlim();
        using var callEntered = new ManualResetEventSlim(); using var releaseCall = new ManualResetEventSlim();
        JobExecutors.ExtraExecutors[kind] = (_, _, _, _) => new Probe(probeRan.Set);
        adapter.DuringCall = () => { if (Interlocked.Increment(ref storageCalls) == 1) { callEntered.Set(); Assert.True(releaseCall.Wait(Wait, ct)); } };
        try
        {
            var storage = SubmitStorage(manager, 2);
            manager.JobFinished += job => { if (ReferenceEquals(job, storage)) storageFinished.Set(); };
            manager.MaxConcurrent = 2; manager.Schedule();
            Assert.True(callEntered.Wait(Wait, ct)); // the active owner is inside its first admitted call
            var startNow = manager.Submit(new() { Kind = kind, Sources = [], Mode = QueueMode.Start, Description = "start-now probe" });
            Assert.Equal(JobState.Queued, startNow.State); Assert.Same(storage, startNow.WaitingFor);
            Assert.Contains("storage changes run exclusively", startNow.WaitReason);
            Assert.False(manager.CanMoveEarlier(startNow)); Assert.False(manager.MoveInQueue(startNow, -1));
            storage.Pause(); releaseCall.Set();
            Assert.True(SpinWait.SpinUntil(() => storage.State == JobState.Paused, Wait)); // paused between roots, still the active owner
            manager.Schedule();
            Assert.Equal(JobState.Queued, startNow.State); Assert.Same(storage, startNow.WaitingFor); Assert.False(probeRan.IsSet);
            Assert.True(adapter.AllAlive); Assert.Equal(1, adapter.Calls);
            storage.Resume();
            Assert.True(storageFinished.Wait(Wait, ct)); Assert.True(probeRan.Wait(Wait, ct));
            Assert.Equal(JobState.Completed, storage.State); Assert.Equal(2, adapter.Calls); Assert.True(adapter.AllRetiredOnce);
            Assert.True(SpinWait.SpinUntil(() => startNow.State == JobState.Completed, Wait)); Assert.Null(startNow.WaitingFor);
        }
        finally { releaseCall.Set(); JobExecutors.ExtraExecutors.TryRemove(kind, out _); }
    }

    [Fact]
    public void Unrelated_local_and_remote_scoped_jobs_wait_only_for_the_global_storage_reservation()
    {
        using var temp = new TempDir(); var adapter = new StorageFixture(); var manager = StorageManager(temp, adapter);
        const JobKind kind = (JobKind)2126; var ct = Ct; var remote = new Location(Schemes.Sftp, "/srv/archive", null, "fixture-host");
        using var remoteEntered = new ManualResetEventSlim(); using var releaseRemote = new ManualResetEventSlim(); using var bothRunning = new CountdownEvent(2);
        using var callEntered = new ManualResetEventSlim(); using var releaseCall = new ManualResetEventSlim(); using var storageFinished = new ManualResetEventSlim();
        JobExecutors.ExtraExecutors[kind] = (job, _, _, _) => new Probe(() =>
        {
            if (job.Request.Description == "earlier remote") { remoteEntered.Set(); Assert.True(releaseRemote.Wait(Wait, ct)); return; }
            bothRunning.Signal(); Assert.True(bothRunning.Wait(Wait, ct)); // completes only while both unrelated jobs run at once
        });
        adapter.DuringCall = () => { callEntered.Set(); Assert.True(releaseCall.Wait(Wait, ct)); };
        try
        {
            manager.MaxConcurrent = 4;
            var earlier = manager.Submit(new() { Kind = kind, Sources = [new(remote, "earlier.bin", EntryKind.File)], Description = "earlier remote" });
            Assert.True(remoteEntered.Wait(Wait, ct));
            var storage = SubmitStorage(manager, 1); // storage also waits for an unrelated remote operation that is already active
            manager.JobFinished += job => { if (ReferenceEquals(job, storage)) storageFinished.Set(); };
            Assert.Equal(JobState.Queued, storage.State); Assert.Same(earlier, storage.WaitingFor);
            Assert.Contains("earlier remote", storage.WaitReason); Assert.Contains("storage changes run exclusively", storage.WaitReason);
            var local = manager.Submit(new() { Kind = kind, Sources = [new(Location.FileSystem(temp.Path), "local.bin", EntryKind.File)], Description = "local probe" });
            var later = manager.Submit(new() { Kind = kind, Sources = [new(remote, "later.bin", EntryKind.File)], Description = "remote probe" });
            foreach (var waiting in new[] { local, later })
            {
                Assert.Equal(JobState.Queued, waiting.State); Assert.Same(storage, waiting.WaitingFor);
                Assert.Contains("storage changes run exclusively", waiting.WaitReason); Assert.DoesNotContain("same items", waiting.WaitReason);
            }
            releaseRemote.Set();
            Assert.True(callEntered.Wait(Wait, ct)); // the global reservation runs alone
            Assert.Equal(JobState.Queued, local.State); Assert.Equal(JobState.Queued, later.State);
            Assert.Same(storage, local.WaitingFor); Assert.Same(storage, later.WaitingFor);
            releaseCall.Set();
            Assert.True(storageFinished.Wait(Wait, ct));
            Assert.True(SpinWait.SpinUntil(() => local.State.IsFinished() && later.State.IsFinished(), Wait));
            Assert.Equal(JobState.Completed, storage.State); Assert.Equal(JobState.Completed, local.State); Assert.Equal(JobState.Completed, later.State);
            Assert.Null(local.WaitingFor); Assert.Null(later.WaitingFor); Assert.True(adapter.AllRetiredOnce);
        }
        finally { releaseRemote.Set(); releaseCall.Set(); JobExecutors.ExtraExecutors.TryRemove(kind, out _); }
    }
}
