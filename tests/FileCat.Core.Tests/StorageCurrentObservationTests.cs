using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// T-REC.C: a history review shows the recorded objects as they are now (plan §6.5.2). The observation inspects each
/// object read-only through the ordinary owned review path, isolates failures per object, releases every pin before it
/// returns and never asks the adapter for a traversal or mutation.
/// </summary>
[Collection("Storage review process resources")]
public sealed class StorageCurrentObservationTests
{
    private sealed class Pin(StorageTarget target, StorageObservation observation) : IStorageReviewLease
    {
        public StorageTarget Target => target;
        public StorageObservation Observation => observation;
        public int Disposals;
        public bool IsAlive => Disposals == 0;
        public void Dispose() => Disposals++;
    }

    private sealed class Adapter(StorageProblem? unavailable = null) : IStorageTransforms
    {
        public readonly List<Pin> Pins = [];
        public readonly List<StorageAction?> Actions = [];
        public StorageProblem? Unavailable(StorageAction action) => unavailable;
        public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct)
        {
            Actions.Add(action);
            if (target.Path.EndsWith("missing.bin", StringComparison.Ordinal))
                throw new StorageInspectionException(new(StorageReasonKind.IdentityChanged, "open-status-C0000034", "The object is gone.", true));
            var state = target.Path.EndsWith("compressed.bin", StringComparison.Ordinal) ? StorageObservationState.Enabled : StorageObservationState.Disabled;
            var problem = target.Path.EndsWith("junction", StringComparison.Ordinal)
                ? new StorageProblem(StorageReasonKind.FileCatBoundary, "reparse-entry", "Reparse format.", false) : null;
            var pin = new Pin(target, new(state, StorageObservationState.Disabled, new StorageObjectIdentity(9, target.Path) { VolumeBinding = "v" },
                target.Source.Kind == EntryKind.Directory, problem));
            Pins.Add(pin);
            return pin;
        }
        public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct) =>
            throw new InvalidOperationException("An observation never opens a traversal.");
    }

    // A fully qualified path on this platform; a relative path is lexically invalid everywhere.
    private static string P(string name) => OperatingSystem.IsWindows() ? @"C:\data\" + name : "/data/" + name;

    private static JobManager Manager(TempDir temp, IStorageTransforms adapter) =>
        new(new PortableFileOperations(), new ProviderRegistry(), adapter, Path.Join(temp.Path, "journal")) { MaxConcurrent = 0 };

    [Fact]
    public void Each_recorded_object_is_observed_read_only_and_released_with_failures_kept_to_that_object()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Storage targets are Windows drive paths; other platforms refuse them before any adapter.");
        using var temp = new TempDir();
        var adapter = new Adapter();
        var manager = Manager(temp, adapter);
        var observed = manager.ObserveStorageObjects([(P("compressed.bin"), false), (P("missing.bin"), false),
            (P("junction"), true), ("data/relative.bin", false), (P("folder"), true)], TestContext.Current.CancellationToken);
        Assert.Equal(5, observed.Count);
        Assert.Equal((StorageObservationState.Enabled, (string?)null), (observed[0].Observation!.Compression, observed[0].Problem?.Code));
        Assert.Equal(("open-status-C0000034", (StorageObservation?)null), (observed[1].Problem?.Code, observed[1].Observation));
        Assert.Equal("reparse-entry", observed[2].Problem?.Code);
        Assert.NotNull(observed[2].Observation);
        Assert.Equal(("source-path", (StorageObservation?)null), (observed[3].Problem?.Code, observed[3].Observation));
        Assert.True(observed[4].Observation!.IsDirectory);
        Assert.All(adapter.Actions, action => Assert.Null(action)); // read-only: no action is ever requested
        Assert.Equal(4, adapter.Actions.Count); // the relative, lexically invalid path never reached the adapter
        Assert.All(adapter.Pins, pin => Assert.Equal(1, pin.Disposals));
        Assert.Empty(manager.Jobs);
        Assert.False(Directory.Exists(Path.Join(temp.Path, "journal")));
    }

    [Fact]
    public void An_unavailable_adapter_reports_its_reason_for_every_object_without_inspecting()
    {
        using var temp = new TempDir();
        var reason = new StorageProblem(StorageReasonKind.FileCatBoundary, "elevated-process", "Open FileCat as your usual Windows user.", false);
        var adapter = new Adapter(reason);
        var observed = Manager(temp, adapter).ObserveStorageObjects([(P("a.bin"), false), (P("b"), true)], TestContext.Current.CancellationToken);
        Assert.All(observed, o => { Assert.Null(o.Observation); Assert.Same(reason, o.Problem); });
        Assert.Empty(adapter.Actions);
    }

    [Fact]
    public void Observation_is_bounded_and_cancellation_stops_before_the_next_object()
    {
        using var temp = new TempDir();
        var adapter = new Adapter();
        var manager = Manager(temp, adapter);
        var many = Enumerable.Range(0, 66).Select(i => (P($"file{i}.bin"), false)).ToArray();
        Assert.Throws<ArgumentException>(() => manager.ObserveStorageObjects(many, TestContext.Current.CancellationToken));
        Assert.Empty(adapter.Actions);
        Assert.Equal(65, manager.ObserveStorageObjects(many[..65], TestContext.Current.CancellationToken).Count);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        adapter.Actions.Clear();
        Assert.Throws<OperationCanceledException>(() => manager.ObserveStorageObjects(many[..3], canceled.Token));
        Assert.Empty(adapter.Actions);
        Assert.All(adapter.Pins, pin => Assert.Equal(1, pin.Disposals));
    }
}
