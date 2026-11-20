using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

[Collection("Storage review process resources")]
public sealed class StorageSelectionReviewTests
{
    private static CancellationToken Stop => TestContext.Current.CancellationToken;
    private static ItemRef Item(int i = 0) => new(Location.FileSystem(@"C:\captured"), "file-" + i, EntryKind.File);
    private sealed class Pin(StorageTarget target, StorageObservation observation) : IStorageReviewLease
    {
        public StorageTarget Target => target;
        public StorageObservation Observation { get; set; } = observation;
        public int Disposed;
        public bool IsAlive => Disposed == 0;
        public void Dispose() => Disposed++;
    }
    private sealed class Adapter : IStorageTransforms
    {
        public readonly List<Pin> Pins = [];
        public int Initial, Refreshed, Evaluated;
        public string? Missing, Changed;
        public bool ReadOnly, Cached = true;
        public Action<StorageTarget>? Entered;
        public StorageProblem? Unavailable(StorageAction action) => null;
        private StorageObservation Observe(Pin pin, StorageAction? action) => pin.Observation with
        { Ineligible = ReadOnly && action == StorageAction.Compress ? new(StorageReasonKind.PermissionOrPolicy, "read-only-file", "Read-only fixture", true) : null };
        public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct)
        {
            Initial++; Entered?.Invoke(target);
            if (target.Source.Name == Missing) throw new FileNotFoundException("Fixture is not present.");
            var pin = new Pin(target, new(StorageObservationState.Disabled, StorageObservationState.Disabled, new(1, target.Path), false, null));
            pin.Observation = Observe(pin, action); Pins.Add(pin); return pin;
        }
        public StorageObservation? EvaluatePinnedRoot(IStorageReviewLease root, StorageAction? action)
        {
            Evaluated++; if (!Cached) return null; var pin = Assert.IsType<Pin>(root);
            return pin.Observation = Observe(pin, action);
        }
        public StorageObservation InspectPinnedRoot(IStorageReviewLease root, StorageAction? action, CancellationToken ct)
        {
            Refreshed++; var pin = Assert.IsType<Pin>(root); Entered?.Invoke(pin.Target);
            if (pin.Target.Source.Name == Changed) throw new StorageInspectionException(new(StorageReasonKind.IdentityChanged, "review-stale", "Close and select again.", true));
            return pin.Observation = Observe(pin, action);
        }
        public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct) => throw new NotSupportedException();
    }
    private static async Task Inspect(StorageSelectionReview review, long revision, DeviceIoScheduler io) =>
        await review.InspectAsync(io, "fixture-storage", revision, Stop);

    [Fact]
    public async Task Unset_action_has_no_approval_and_cached_action_scope_changes_keep_every_original_pin()
    {
        using var io = new DeviceIoScheduler(); var adapter = new Adapter { ReadOnly = true };
        using var review = new StorageSelectionReview(adapter, [Item()]);
        long revision = review.Request(null, StorageScope.SelectedItems); await Inspect(review, revision, io);
        Assert.False(review.State.CanApprove); Assert.Throws<InvalidOperationException>(() => review.Freeze(revision));
        var pin = Assert.Single(adapter.Pins); Assert.True(pin.IsAlive);
        revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems);
        Assert.False(review.State.NeedsInspection); Assert.False(review.State.CanApprove); Assert.Equal("read-only-file", review.State.Roots[0].Observation!.Ineligible!.Code);
        revision = review.Request(StorageAction.RemoveCompression, StorageScope.SelectedItems);
        Assert.True(review.State.CanApprove); Assert.False(review.State.NeedsInspection);
        long old = revision; revision = review.Request(StorageAction.RemoveCompression, StorageScope.ExistingDescendants);
        Assert.Throws<InvalidOperationException>(() => review.Freeze(old)); Assert.True(review.State.CanApprove);
        using var ready = review.Freeze(revision); var approval = ready.Approve(StorageAction.RemoveCompression, StorageScope.ExistingDescendants);
        Assert.Equal(ready.Revision, approval.ReviewRevision); Assert.Equal(1, adapter.Initial); Assert.Equal(0, adapter.Refreshed);
        Assert.Throws<InvalidOperationException>(() => review.Freeze(revision));
        review.Dispose(); Assert.Equal(0, pin.Disposed); ready.Dispose(); Assert.Equal(1, pin.Disposed);
        Assert.Throws<ObjectDisposedException>(() => review.Freeze(revision));
    }

    [Fact]
    public async Task Missing_root_retains_its_siblings_and_Retry_only_acquires_its_first_pin()
    {
        using var io = new DeviceIoScheduler(); var adapter = new Adapter { Missing = "file-1" };
        using var review = new StorageSelectionReview(adapter, [Item(), Item(1), Item(2)]);
        long revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems); await Inspect(review, revision, io);
        Assert.False(review.State.CanApprove); Assert.Equal(3, review.State.Checked); Assert.Equal(2, adapter.Pins.Count);
        Assert.NotNull(review.State.Roots[1].Problem); Assert.All(adapter.Pins, p => Assert.Equal(0, p.Disposed));
        adapter.Missing = null; revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems, retry: true);
        Assert.False(review.State.CanApprove); await Inspect(review, revision, io);
        Assert.True(review.State.CanApprove); Assert.Equal(4, adapter.Initial); Assert.Equal(2, adapter.Refreshed);
        Assert.All(adapter.Pins, p => Assert.Equal(0, p.Disposed)); review.Dispose(); Assert.All(adapter.Pins, p => Assert.Equal(1, p.Disposed));
    }

    [Fact]
    public async Task Lost_binding_is_sticky_and_no_later_action_or_Retry_can_retarget_it()
    {
        using var io = new DeviceIoScheduler(); var adapter = new Adapter(); using var review = new StorageSelectionReview(adapter, [Item()]);
        long revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems); await Inspect(review, revision, io);
        adapter.Changed = "file-0"; revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems, true); await Inspect(review, revision, io);
        Assert.True(review.State.Roots[0].Stale); Assert.False(review.State.CanApprove);
        adapter.Changed = null; revision = review.Request(StorageAction.RemoveCompression, StorageScope.SelectedItems, true); await Inspect(review, revision, io);
        Assert.True(review.State.Roots[0].Stale); Assert.False(review.State.CanApprove); Assert.Equal(1, adapter.Initial); Assert.Equal(1, adapter.Refreshed);
    }

    [Fact]
    public async Task Close_during_an_ignoring_native_call_waits_for_actual_retirement_without_returning_capacity()
    {
        using var io = new DeviceIoScheduler(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var adapter = new Adapter { Entered = _ => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); } };
        using var review = new StorageSelectionReview(adapter, [Item()]); long revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems);
        using var cancel = new CancellationTokenSource(); var work = review.InspectAsync(io, "fixture-storage", revision, cancel.Token);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Stop)); review.Dispose(); cancel.Cancel(); Assert.False(work.IsCompleted);
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(Stop); probe.CancelAfter(TimeSpan.FromSeconds(5));
            var capacity = StorageReviewResources.ReserveNativeAsync(probe.Token); Assert.False(capacity.IsCompleted);
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
            using var returned = await capacity; Assert.All(adapter.Pins, p => Assert.Equal(1, p.Disposed));
        }
        finally { release.Set(); try { await work; } catch (OperationCanceledException) { } }
    }

    [Fact]
    public async Task A_late_revision_is_not_adopted_and_newer_inspection_uses_the_same_immutable_capture()
    {
        using var io = new DeviceIoScheduler(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var adapter = new Adapter { Entered = _ => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); } };
        var source = new[] { Item() }; using var review = new StorageSelectionReview(adapter, source); source[0] = Item(7);
        long older = review.Request(StorageAction.Compress, StorageScope.SelectedItems); var work = review.InspectAsync(io, "fixture-storage", older, Stop);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Stop)); long newer = review.Request(StorageAction.RemoveCompression, StorageScope.ExistingDescendants);
            Assert.False(review.State.CanApprove); release.Set(); await work; Assert.Equal(1, Assert.Single(adapter.Pins).Disposed);
            adapter.Entered = null; await Inspect(review, newer, io); Assert.True(review.State.CanApprove);
            Assert.Equal("file-0", review.Sources[0].Name); Assert.Equal(2, adapter.Initial);
        }
        finally { release.Set(); await work; }
    }

    [Fact]
    public async Task Capacity_wait_is_outside_the_device_queue_and_stopped_enqueue_cannot_leak_it()
    {
        using var io = new DeviceIoScheduler(); var adapter = new Adapter(); using var review = new StorageSelectionReview(adapter, [Item()]);
        long revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems);
        using var held = await StorageReviewResources.ReserveNativeAsync(Stop);
        using var cancel = new CancellationTokenSource(); var waiting = review.InspectAsync(io, "fixture-storage", revision, cancel.Token);
        Assert.False(waiting.IsCompleted); Assert.True(await io.RunOwned("fixture-storage", IoPriority.Normal, _ => true, Stop));
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting); held.Dispose();
        io.Dispose(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => review.InspectAsync(io, "fixture-storage", review.State.Revision, Stop));
        using var returned = await StorageReviewResources.ReserveNativeAsync(Stop); Assert.Equal(0, adapter.Initial);
    }

    [Fact]
    public async Task A_mutable_adapter_observation_cannot_replace_the_original_bound_identity()
    {
        using var io = new DeviceIoScheduler(); var adapter = new Adapter(); using var review = new StorageSelectionReview(adapter, [Item()]);
        long revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems); await Inspect(review, revision, io);
        var pin = Assert.Single(adapter.Pins); var original = pin.Observation.Identity;
        adapter.Entered = _ => pin.Observation = pin.Observation with { Identity = new(1, "replacement") };
        revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems, true); await Inspect(review, revision, io);
        Assert.True(review.State.Roots[0].Stale); Assert.False(review.State.CanApprove);
        Assert.Equal(original, review.State.Roots[0].Observation!.Identity); Assert.Equal(1, adapter.Initial);
    }

    [Fact]
    public async Task Original_membership_and_duplicate_source_mappings_survive_the_one_shot_handoff()
    {
        using var io = new DeviceIoScheduler(); var adapter = new Adapter(); var sources = new[] { Item(), Item() };
        using var review = new StorageSelectionReview(adapter, sources); sources[0] = Item(9);
        long revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems); await Inspect(review, revision, io);
        using var frozen = review.Freeze(revision);
        Assert.Equal(new[] { 0, 1 }, frozen.Roots.Select(r => r.SourceIndex)); Assert.Equal(new[] { 0, 1 }, frozen.Roots.Select(r => r.PinIndex));
        Assert.All(frozen.Sources, s => Assert.Equal("file-0", s.Name)); Assert.Equal(2, adapter.Pins.Count);
        review.Dispose(); Assert.All(adapter.Pins, p => Assert.Equal(0, p.Disposed));
        frozen.Dispose(); Assert.All(adapter.Pins, p => Assert.Equal(1, p.Disposed));
    }

    [Fact]
    public void Invalid_actions_and_over_limit_sources_fail_before_native_work_or_pin_reservation()
    {
        var adapter = new Adapter(); using var review = new StorageSelectionReview(adapter, [Item()]);
        Assert.Throws<NotSupportedException>(() => review.Request(StorageAction.EncryptEfs, StorageScope.SelectedItems));
        Assert.Throws<NotSupportedException>(() => review.Request(StorageAction.DecryptEfs, StorageScope.SelectedItems));
        Assert.Throws<ArgumentOutOfRangeException>(() => review.Request((StorageAction)99, StorageScope.SelectedItems));
        Assert.Throws<ArgumentOutOfRangeException>(() => review.Request(null, (StorageScope)99));
        Assert.Throws<ArgumentException>(() => new StorageSelectionReview(adapter, Enumerable.Range(0, 65).Select(Item).ToArray()));
        Assert.Equal(0, adapter.Initial); Assert.Equal(0, adapter.Refreshed);
    }
}
