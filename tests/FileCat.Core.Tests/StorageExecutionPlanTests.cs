using System.Collections.Immutable;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class StorageExecutionPlanTests
{
    private static CancellationToken Stop => TestContext.Current.CancellationToken;
    private static StorageObjectIdentity Id(string id) => new(7, id) { VolumeBinding = "held-volume" };
    private static StorageObservation State(string id, bool directory = true) => new(StorageObservationState.Disabled,
        StorageObservationState.Disabled, Id(id), directory, null);

    [Fact]
    public void A_path_prefix_and_equal_names_do_not_prove_overlap_without_held_ancestry()
    {
        var pins = new[] { new Pin("parent", State("parent")), new Pin("parent\\child", State("child")) }; using var owner = Owner(pins);
        var plan = StorageExecutionPlan.Build(owner, Request(pins, StorageScope.ExistingDescendants, [0, 1]),
            pin => new Traversal(pin.Observation.Identity, [], () => { }), () => { }, Stop);
        Assert.All(plan.Roots, root => { Assert.Null(root.ParentPin); Assert.Null(root.Problem); });
    }

    [Fact]
    public void A_denied_root_remains_an_incomplete_obligation_while_independent_roots_remain_available()
    {
        var pins = new[] { new Pin("denied", State("denied")), new Pin("independent", State("independent")) }; using var owner = Owner(pins);
        var plan = StorageExecutionPlan.Build(owner, Request(pins, StorageScope.ExistingDescendants, [0, 1]),
            pin => pin == pins[0] ? throw new UnauthorizedAccessException("Fixture refusal") : new Traversal(pin.Observation.Identity, [], () => { }), () => { }, Stop);
        Assert.Equal(StorageReasonKind.PermissionOrPolicy, plan.Roots[0].Problem!.Kind); Assert.Equal(1UL, plan.Roots[0].Sources);
        Assert.Null(plan.Roots[1].Problem); Assert.Equal(2UL, plan.Roots[1].Sources); Assert.True(owner.IsAlive);
    }

    [Fact]
    public void Selected_only_scope_preserves_every_root_without_any_overlap_inspection_or_pruning()
    {
        var pins = new[] { new Pin("parent", State("parent")), new Pin("parent\\child", State("child")) }; using var owner = Owner(pins);
        var plan = StorageExecutionPlan.Build(owner, Request(pins, StorageScope.SelectedItems, [0, 1]),
            _ => throw new InvalidOperationException("Selected-only planning needs no native prepass."), () => { }, Stop);
        Assert.All(plan.Roots, root => { Assert.Null(root.ParentPin); Assert.Null(root.Problem); });
        Assert.Equal(1UL, plan.AncestorSources(0)); Assert.Equal(2UL, plan.AncestorSources(1));
    }

    [Fact]
    public void Recursive_overlap_uses_nearest_held_ancestor_and_preserves_every_source_mapping()
    {
        var pins = new[] { new Pin("leaf", State("leaf", false)), new Pin("middle", State("middle")), new Pin("outer", State("outer")) };
        using var owner = Owner(pins); int closes = 0;
        var plan = StorageExecutionPlan.Build(owner, Request(pins, StorageScope.ExistingDescendants, [0, 1, 2, 0]),
            pin => new Traversal(pin.Observation.Identity, pin == pins[0] ? [Id("outer"), Id("middle")] : pin == pins[1] ? [Id("outer")] : [], () => closes++), () => { }, Stop);
        Assert.Equal(1, plan.Roots[0].ParentPin); Assert.Equal(2, plan.Roots[1].ParentPin); Assert.Null(plan.Roots[2].ParentPin);
        Assert.Equal(9UL, plan.Roots[0].Sources); Assert.Equal(15UL, plan.AncestorSources(0)); Assert.Equal(3, closes);
    }

    [Fact]
    public void Root_moves_that_form_an_inspection_cycle_keep_every_mapping_incomplete()
    {
        var pins = new[] { new Pin("one", State("one")), new Pin("two", State("two")) }; using var owner = Owner(pins);
        var plan = StorageExecutionPlan.Build(owner, Request(pins, StorageScope.ExistingDescendants, [0, 1]),
            pin => new Traversal(pin.Observation.Identity, [pin == pins[0] ? Id("two") : Id("one")], () => { }), () => { }, Stop);
        Assert.All(plan.Roots, root => { Assert.Null(root.ParentPin); Assert.Equal("overlap-changed", root.Problem!.Code); });
    }

    [Theory]
    [InlineData("self")]
    [InlineData("duplicate")]
    [InlineData("foreign-volume")]
    [InlineData("too-deep")]
    [InlineData("changed-root")]
    public void Invalid_held_ancestry_is_refused_and_its_probe_is_retired(string failure)
    {
        var pin = new Pin("root", State("root")); using var owner = Owner([pin]); int closed = 0;
        StorageObjectIdentity[] ancestors = failure switch
        {
            "self" => [Id("root")], "duplicate" => [Id("parent"), Id("parent")],
            "foreign-volume" => [new(8, "parent") { VolumeBinding = "foreign" }],
            "too-deep" => Enumerable.Range(0, 64).Select(i => Id("ancestor-" + i)).ToArray(), _ => [],
        };
        var plan = StorageExecutionPlan.Build(owner, Request([pin], StorageScope.ExistingDescendants, [0]),
            _ => new Traversal(Id(failure == "changed-root" ? "replacement" : "root"), ancestors, () => closed++), () => { }, Stop);
        Assert.Equal(1, closed); Assert.Equal(StorageReasonKind.IdentityChanged, Assert.Single(plan.Roots).Problem!.Kind); Assert.True(owner.IsAlive);
    }

    [Fact]
    public void Cancellation_between_probes_leaves_original_pin_retirement_to_the_job_owner()
    {
        var pins = new[] { new Pin("one", State("one")), new Pin("two", State("two")) }; using var owner = Owner(pins);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Stop); int closed = 0, checks = 0;
        Assert.Throws<OperationCanceledException>(() => StorageExecutionPlan.Build(owner, Request(pins, StorageScope.ExistingDescendants, [0, 1]),
            pin => new Traversal(pin.Observation.Identity, [], () => { closed++; stop.Cancel(); }), () => checks++, stop.Token));
        Assert.Equal(1, closed); Assert.Equal(2, checks); Assert.True(owner.IsAlive);
    }

    private static StorageReviewOwner Owner(Pin[] pins) => new(pins.Cast<IStorageReviewLease>().ToImmutableArray(), new Release());
    private static StorageTransformRequest Request(Pin[] pins, StorageScope scope, int[] mapping) => new(StorageAction.Compress, scope, Guid.NewGuid())
    { Roots = mapping.Select((pin, source) => new StorageReviewedRoot(source, pin, pins[pin].Observation)).ToImmutableArray() };
    private sealed class Release : IDisposable { public void Dispose() { } }
    private sealed class Pin(string path, StorageObservation state) : IStorageReviewLease
    {
        public StorageTarget Target { get; } = new(ItemRef.ForFileSystemPath(@"C:\captured\" + path, state.IsDirectory ? EntryKind.Directory : EntryKind.File), @"C:\captured\" + path);
        public StorageObservation Observation => state;
        public bool IsAlive { get; private set; } = true;
        public void Dispose() => IsAlive = false;
    }
    private sealed class Traversal(StorageObjectIdentity identity, IReadOnlyList<StorageObjectIdentity> ancestors, Action close) : IStorageTraversal
    {
        public IStorageObjectLease Root { get; } = new Held(identity);
        public IReadOnlyList<StorageObjectIdentity> RootAncestors(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return ancestors; }
        public IEnumerable<IStorageObjectLease> Children(IStorageObjectLease directory, CancellationToken ct) => throw new InvalidOperationException("Overlap inspection must not enumerate contents.");
        public void Dispose() => close();
    }
    private sealed class Held(StorageObjectIdentity identity) : IStorageObjectLease
    {
        public string Path => @"C:\captured\root";
        public StorageObjectIdentity Identity => identity;
        public StorageObservation Observe(CancellationToken ct) => throw new InvalidOperationException("Overlap inspection must not query storage state.");
        public StorageObservation PrepareMutation(StorageAction action, CancellationToken ct) => throw new InvalidOperationException("Overlap inspection must not request mutation access.");
        public StorageMutationResult ApplyAdmitted(StorageAction action) => throw new InvalidOperationException("Overlap inspection must not mutate.");
        public void Dispose() { }
    }
}
