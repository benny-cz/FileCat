using System.Collections;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

[Collection("Storage review process resources")]
public sealed class StorageReviewOwnershipTests
{
    private sealed class Adapter : IStorageTransforms
    {
        public readonly List<Pin> Pins = [];
        public int Inspections, Availability, Traversals;
        public bool SameIdentity, ThrowOnDispose, DistinctVolumeBindings;
        public StorageProblem? Ineligible;
        public Action? DuringInspect;
        public StorageProblem? Unavailable(StorageAction action)
        {
            Assert.True(action is StorageAction.Compress or StorageAction.RemoveCompression);
            Availability++;
            return null; // A C-only fake has no EFS, stream or key service.
        }
        public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct)
        {
            Inspections++;
            DuringInspect?.Invoke();
            var pin = new Pin(target, new(StorageObservationState.Disabled, StorageObservationState.Disabled,
                new StorageObjectIdentity(7, SameIdentity ? "same-live-object" : target.Path) { VolumeBinding = DistinctVolumeBindings ? target.Path : null },
                target.Source.Kind == EntryKind.Directory, Ineligible), ThrowOnDispose);
            Pins.Add(pin);
            return pin;
        }
        public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct)
        { Traversals++; throw new InvalidOperationException("Unqualified dispatch must never request native traversal."); }
    }
    private sealed class Pin(StorageTarget target, StorageObservation observation, bool throwOnDispose) : IStorageReviewLease
    {
        public StorageTarget Target => target;
        public StorageObservation Observation => observation;
        public int Disposals;
        public bool IsAlive => Disposals == 0;
        public void Dispose() { Disposals++; if (throwOnDispose) throw new IOException("Fixture close failure."); }
    }
    private static ItemRef Source(string name = "Original 名前") => new(Location.FileSystem(@"C:\Captured"), name, EntryKind.File, 123, 456);
    private static ItemRef[] Sources(int count) => Enumerable.Range(0, count).Select(i => Source(i.ToString())).ToArray();
    private static JobManager Manager(TempDir temp, Adapter adapter) => new(new PortableFileOperations(), new ProviderRegistry(), adapter, Path.Join(temp.Path, "journal")) { MaxConcurrent = 0 };

    [Fact]
    public void All_sources_and_the_metadata_budget_are_validated_before_the_first_native_pin()
    {
        using var temp = new TempDir(); var adapter = new Adapter(); var manager = Manager(temp, adapter);
        Assert.Throws<ArgumentException>(() => manager.PrepareStorageReview([Source(), Source("bad:stream")], StorageAction.Compress, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => manager.PrepareStorageReview([new(Location.FileSystem(@"C:\Captured"), @"nested\child", EntryKind.File)], StorageAction.Compress, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => manager.PrepareStorageReview(new CountOnly(65), StorageAction.Compress, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => manager.PrepareStorageReview([], StorageAction.Compress, TestContext.Current.CancellationToken));
        string hugeName = new('a', 32000);
        Assert.Throws<ArgumentException>(() => manager.PrepareStorageReview(Enumerable.Repeat(Source(hugeName), 64).ToArray(), StorageAction.Compress, TestContext.Current.CancellationToken));
        Assert.Equal(0, adapter.Inspections); Assert.Empty(manager.Jobs); Assert.False(Directory.Exists(Path.Join(temp.Path, "journal")));
    }
    private sealed class CountOnly(int count) : IReadOnlyList<ItemRef>
    {
        public int Count => count;
        public ItemRef this[int index] => throw new InvalidOperationException("Over-limit selection was materialized.");
        public IEnumerator<ItemRef> GetEnumerator() => throw new InvalidOperationException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void Ineligible_roots_and_a_different_composed_adapter_cannot_admit_a_partial_selection()
    {
        using var temp = new TempDir(); var adapter = new Adapter
        { Ineligible = new(StorageReasonKind.PermissionOrPolicy, "denied", "Fixture refusal.", true) };
        var manager = Manager(temp, adapter);
        using var denied = manager.PrepareStorageReview([Source()], StorageAction.Compress, TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => denied.Approve(StorageAction.Compress, StorageScope.SelectedItems));
        Assert.True(denied.OwnsPins); Assert.Empty(manager.Jobs);
        adapter.Ineligible = null;
        using var valid = manager.PrepareStorageReview([Source()], StorageAction.Compress, TestContext.Current.CancellationToken);
        var other = Manager(temp, new Adapter());
        Assert.Throws<InvalidOperationException>(() => other.SubmitStorage(valid, valid.Approve(StorageAction.Compress, StorageScope.SelectedItems), _ => { }));
        Assert.True(valid.OwnsPins); Assert.Empty(other.Jobs);
    }

    [Fact]
    public void Unset_action_unknown_enums_reserved_EFS_and_stale_approval_do_not_consume_the_owner()
    {
        using var temp = new TempDir(); var adapter = new Adapter(); var manager = Manager(temp, adapter);
        int composedAvailability = adapter.Availability;
        Assert.Equal(1, composedAvailability); // The manager caches command availability once at composition.
        Assert.Throws<NotSupportedException>(() => manager.PrepareStorageReview([Source()], StorageAction.EncryptEfs, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.PrepareStorageReview([Source()], (StorageAction)1234, TestContext.Current.CancellationToken));
        Assert.Equal(composedAvailability, adapter.Availability); Assert.Equal(0, adapter.Inspections);
        using var unset = manager.PrepareStorageReview([Source()], null, TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => unset.Approve(StorageAction.Compress, StorageScope.SelectedItems));
        using var review = manager.PrepareStorageReview([Source()], StorageAction.Compress, TestContext.Current.CancellationToken);
        var approval = review.Approve(StorageAction.Compress, StorageScope.SelectedItems);
        foreach (var invalid in new[] { approval with { ReviewRevision = Guid.NewGuid() }, approval with { Action = StorageAction.RemoveCompression } })
            Assert.Throws<InvalidOperationException>(() => manager.SubmitStorage(review, invalid, _ => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.SubmitStorage(review, approval with { Scope = (StorageScope)45 }, _ => { }));
        Assert.Throws<NotSupportedException>(() => manager.SubmitStorage(review, approval with { Action = StorageAction.DecryptEfs }, _ => { }));
        Assert.True(review.OwnsPins); Assert.All(adapter.Pins, pin => Assert.True(pin.IsAlive)); Assert.Empty(manager.Jobs);
    }

    [Fact]
    public void Generic_submission_cannot_reconstruct_storage_authority_or_smuggle_a_payload_to_another_kind()
    {
        using var temp = new TempDir(); var adapter = new Adapter(); var manager = Manager(temp, adapter);
        var payload = new StorageTransformRequest(StorageAction.Compress, StorageScope.SelectedItems, Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => manager.Submit(new() { Kind = JobKind.StorageTransform, Sources = [Source()], StorageTransform = payload }));
        Assert.Throws<ArgumentException>(() => manager.Submit(new() { Kind = JobKind.StorageTransform, Sources = [Source()] }));
        Assert.Throws<ArgumentException>(() => manager.Submit(new() { Kind = JobKind.Checksum, Sources = [Source()], StorageTransform = payload }));
        Assert.Empty(manager.Jobs); Assert.Equal(0, adapter.Inspections); Assert.False(Directory.Exists(Path.Join(temp.Path, "journal")));
    }

    [Fact]
    public void Queued_job_consumes_once_keeps_pins_and_retires_before_terminal_observers_without_losing_sources()
    {
        using var temp = new TempDir(); var adapter = new Adapter(); var manager = Manager(temp, adapter);
        var originals = new List<ItemRef> { Source() };
        using var review = manager.PrepareStorageReview(originals, StorageAction.Compress, TestContext.Current.CancellationToken);
        var approval = review.Approve(StorageAction.Compress, StorageScope.ExistingDescendants);
        Job? registered = null; int finished = 0;
        manager.JobAdded += job => Assert.Same(registered, job);
        manager.JobFinished += job =>
        {
            finished++; Assert.All(adapter.Pins, pin => Assert.Equal(1, pin.Disposals));
            Assert.Equal("Original 名前", Assert.Single(job.Request.Sources).Name);
        };
        var job = manager.SubmitStorage(review, approval, job => registered = job);
        review.Dispose(); originals[0] = Source("later selection");
        Assert.False(review.OwnsPins); Assert.True(Assert.Single(adapter.Pins).IsAlive);
        Assert.Equal("Original 名前", Assert.Single(review.Sources).Name); Assert.Single(review.Roots);
        Assert.Equal(StorageScope.ExistingDescendants, job.Request.StorageTransform!.Scope);
        Assert.Equal(approval.ReviewRevision, job.Request.StorageTransform.ReviewRevision);
        Assert.Throws<InvalidOperationException>(() => manager.SubmitStorage(review, approval, _ => { }));
        job.Cancel(); job.Cancel(); manager.Schedule();
        Assert.Equal(JobState.Canceled, job.State); Assert.Equal(1, finished); Assert.Equal(1, job.StorageResult!.Counts.NotProcessed);
        Assert.False(Directory.Exists(Path.Join(temp.Path, "journal")));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Failed_registration_or_add_observer_rolls_back_and_closes_every_consumed_pin(bool addedObserver)
    {
        using var temp = new TempDir(); var adapter = new Adapter { ThrowOnDispose = true }; var manager = Manager(temp, adapter);
        using var review = manager.PrepareStorageReview([Source("one"), Source("two")], StorageAction.Compress, TestContext.Current.CancellationToken);
        int removed = 0;
        manager.JobRemoved += _ => throw new IOException("Removal observer fixture.");
        manager.JobRemoved += job => { removed++; Assert.Equal(JobState.Canceled, job.State); Assert.All(adapter.Pins, p => Assert.Equal(1, p.Disposals)); };
        if (addedObserver) manager.JobAdded += _ => throw new IOException("Failed addition fixture.");
        Assert.Throws<IOException>(() => manager.SubmitStorage(review, review.Approve(StorageAction.Compress, StorageScope.SelectedItems),
            _ => { if (!addedObserver) throw new IOException("Failed registration fixture."); }));
        Assert.All(adapter.Pins, p => Assert.Equal(1, p.Disposals)); Assert.Empty(manager.Jobs); Assert.False(review.OwnsPins);
        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(Path.Join(temp.Path, "journal")));
    }

    [Fact]
    public void Deduplication_preserves_every_original_mapping_and_requires_simultaneously_live_pins()
    {
        using var temp = new TempDir(); var adapter = new Adapter { SameIdentity = true }; var manager = Manager(temp, adapter);
        var review = manager.PrepareStorageReview([Source("alias one"), Source("alias two")], StorageAction.RemoveCompression, TestContext.Current.CancellationToken);
        Assert.Equal(2, adapter.Inspections); Assert.Equal(2, review.Roots.Length);
        Assert.Equal(0, review.Roots[0].PinIndex); Assert.Equal(0, review.Roots[1].PinIndex);
        Assert.Equal(0, adapter.Pins[0].Disposals); Assert.Equal(1, adapter.Pins[1].Disposals);
        review.Dispose(); review.Dispose(); Assert.All(adapter.Pins, p => Assert.Equal(1, p.Disposals));
        Assert.Equal(2, review.Sources.Length); Assert.Equal(2, review.Roots.Length);
    }

    [Fact]
    public void Colliding_serial_and_file_ID_on_distinct_verified_volumes_are_not_deduplicated()
    {
        using var temp = new TempDir(); var adapter = new Adapter { SameIdentity = true, DistinctVolumeBindings = true }; var manager = Manager(temp, adapter);
        using var review = manager.PrepareStorageReview([Source("volume one"), Source("volume two")], StorageAction.Compress, TestContext.Current.CancellationToken);
        Assert.Equal(0, review.Roots[0].PinIndex); Assert.Equal(1, review.Roots[1].PinIndex);
        Assert.All(adapter.Pins, pin => Assert.True(pin.IsAlive));
    }

    [Fact]
    public void Process_pin_allowance_is_atomic_and_released_as_a_whole()
    {
        using var temp = new TempDir(); var adapter = new Adapter(); var manager = Manager(temp, adapter);
        var reviews = new List<ReviewedStorageRoots>();
        try
        {
            for (int i = 0; i < 4; i++) reviews.Add(manager.PrepareStorageReview(Sources(64), StorageAction.Compress, TestContext.Current.CancellationToken));
            Assert.Equal(256, adapter.Inspections);
            Assert.Throws<InvalidOperationException>(() => manager.PrepareStorageReview([Source()], StorageAction.Compress, TestContext.Current.CancellationToken));
            Assert.Equal(256, adapter.Inspections);
            reviews[0].Dispose();
            using var replacement = manager.PrepareStorageReview(Sources(64), StorageAction.Compress, TestContext.Current.CancellationToken);
            Assert.Equal(320, adapter.Inspections);
        }
        finally { foreach (var review in reviews) review.Dispose(); }
        Assert.All(adapter.Pins, p => Assert.Equal(1, p.Disposals));
    }

    [Fact]
    public async Task Canceled_preparation_keeps_the_native_allowance_until_the_started_delegate_returns()
    {
        using var temp = new TempDir(); var adapter = new Adapter(); var manager = Manager(temp, adapter);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var stop = new CancellationTokenSource();
        adapter.DuringInspect = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)); };
        var pending = Task.Run(() => manager.PrepareStorageReview([Source()], StorageAction.Compress, stop.Token), TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        try
        {
            stop.Cancel();
            using var otherStop = new CancellationTokenSource(); otherStop.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => manager.PrepareStorageReview([Source()], StorageAction.Compress, otherStop.Token));
            Assert.False(pending.IsCompleted); Assert.Equal(1, adapter.Inspections);
        }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.Equal(1, Assert.Single(adapter.Pins).Disposals);
        adapter.DuringInspect = null;
        using var subsequent = manager.PrepareStorageReview([Source()], StorageAction.Compress, TestContext.Current.CancellationToken);
        Assert.True(subsequent.OwnsPins);
    }

    [Fact]
    public void A_refused_platform_traversal_preserves_the_reviewed_root_and_retires_its_owner()
    {
        using var temp = new TempDir(); var adapter = new Adapter(); var manager = Manager(temp, adapter); manager.MaxConcurrent = 1;
        using var review = manager.PrepareStorageReview([Source()], StorageAction.Compress, TestContext.Current.CancellationToken);
        using var finished = new ManualResetEventSlim();
        manager.JobFinished += _ => finished.Set();
        var job = manager.SubmitStorage(review, review.Approve(StorageAction.Compress, StorageScope.SelectedItems), _ => { });
        Assert.True(finished.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)); Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(1, adapter.Traversals); Assert.Contains(job.StorageResult!.Reasons, reason => reason.Problem.Code == "execution-failed");
        Assert.Equal(1, Assert.Single(adapter.Pins).Disposals); Assert.Equal(1, job.StorageResult.Counts.NotProcessed);
        Assert.Contains("not processed", job.Summary); Assert.Equal(123, job.Request.Sources[0].Size);
    }

    [Fact]
    public void Oversize_exact_header_refuses_before_owner_transfer_or_job_creation_without_dropping_roots()
    {
        using var temp = new TempDir(); var adapter = new Adapter { SameIdentity = true }; var manager = Manager(temp, adapter);
        string parent = @"C:\" + string.Join('\\', Enumerable.Repeat(new string('\u6F22', 250), 4));
        var sources = Enumerable.Range(0, 64).Select(i => new ItemRef(Location.FileSystem(parent), i.ToString(), EntryKind.File)).ToArray();
        using var review = manager.PrepareStorageReview(sources, StorageAction.Compress, TestContext.Current.CancellationToken);
        var approval = review.Approve(StorageAction.Compress, StorageScope.SelectedItems);
        Assert.Throws<IOException>(() => manager.SubmitStorage(review, approval, _ => { }));
        Assert.True(review.OwnsPins); Assert.Equal(64, review.Sources.Length); Assert.Empty(manager.Jobs);
        Assert.False(Directory.Exists(Path.Join(temp.Path, "journal")));
    }

    private sealed class CloseCharge : IDisposable
    {
        internal bool Closed;
        public void Dispose() => Closed = true;
    }
    private sealed class FailingFlush(string path) : FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read)
    {
        private int _flushes;
        public override void Flush(bool flushToDisk)
        {
            if (++_flushes >= 2) throw new IOException("Fixture durable-summary failure.");
            base.Flush(flushToDisk);
        }
    }

    [Fact]
    public void Pending_history_is_published_once_after_writer_and_pin_retirement_before_finished_observers()
    {
        using var temp = new TempDir(); var adapter = new Adapter(); var charge = new CloseCharge(); FailingFlush? stream = null;
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Join(temp.Path, "journal"), ArchiveJobBackends.Empty, adapter,
            (directory, leaf, _) => { Directory.CreateDirectory(directory); stream = new FailingFlush(Path.Join(directory, leaf)); return new(stream, charge); });
        using var review = manager.PrepareStorageReview([Source()], StorageAction.Compress, TestContext.Current.CancellationToken);
        using var finished = new ManualResetEventSlim(); InterruptedJob? candidate = null; int publications = 0;
        manager.StorageReviewRequired += _ => throw new InvalidOperationException("Fixture observer failure must be contained.");
        manager.StorageReviewRequired += pending =>
        {
            Assert.True(charge.Closed); Assert.False(stream!.CanRead); Assert.All(adapter.Pins, p => Assert.Equal(1, p.Disposals));
            candidate = pending; publications++;
        };
        manager.JobFinished += _ => finished.Set();
        var job = manager.SubmitStorage(review, review.Approve(StorageAction.Compress, StorageScope.SelectedItems), _ => { });
        Assert.True(finished.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(JobState.Failed, job.State); Assert.True(job.StorageResult!.ReviewPending); Assert.Equal(1, publications); Assert.NotNull(candidate);
        Assert.Equal(manager.StorageHistoryCandidate(job).JournalPath, candidate.JournalPath); Assert.True(candidate.IsStorageTransform);
        manager.Schedule(); Assert.Equal(1, publications);
    }
}
