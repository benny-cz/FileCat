using System.Collections.Immutable;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class StorageExecutorTests
{
    private static CancellationToken Stop => TestContext.Current.CancellationToken;
    private sealed class Node(string name, bool directory = false, string? identity = null)
    {
        internal readonly string Name = name;
        internal readonly bool Directory = directory;
        internal readonly StorageObjectIdentity Identity = new(11, identity ?? name) { VolumeBinding = "fixture-volume" };
        internal readonly List<Node> Children = [];
        internal Node? Parent;
        internal StorageObservationState Compression = StorageObservationState.Disabled;
        internal StorageProblem? Ineligible;
        internal Func<StorageObservation, StorageMutationResult>? Mutation;
        internal Action? Enumerating;
        internal Action? Closing;
        internal int Calls, Preparations, Active;
        internal string Path => Parent is null ? @"C:\fixture\" + Name : Parent.Path + "\\" + Name;
        internal StorageObservation State => new(Compression, StorageObservationState.Disabled, Identity, Directory, Ineligible);
        internal Node Add(Node child) { child.Parent = this; Children.Add(child); return this; }
    }
    private sealed class Pin(Node node) : IStorageReviewLease
    {
        public StorageTarget Target { get; } = new(Source(node), node.Path);
        public StorageObservation Observation { get; } = node.State;
        internal Node Node => node;
        public bool IsAlive { get; private set; } = true;
        public void Dispose() => IsAlive = false;
    }
    private sealed class Held(Node node) : IStorageObjectLease
    {
        private bool _closed;
        public string Path { get; } = node.Path;
        public StorageObjectIdentity Identity => node.Identity;
        internal Node Node => node;
        internal static Held Open(Node node) { node.Active++; return new(node); }
        public StorageObservation Observe(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return node.State; }
        public StorageObservation PrepareMutation(StorageAction action, CancellationToken ct) { ct.ThrowIfCancellationRequested(); node.Preparations++; return node.State; }
        public StorageMutationResult ApplyAdmitted(StorageAction action)
        {
            node.Calls++; var before = node.State;
            if (node.Mutation is { } mutate) return mutate(before);
            node.Compression = action == StorageAction.Compress ? StorageObservationState.Enabled : StorageObservationState.Disabled;
            return new(before, node.State, StorageEffect.Changed, null) { NativeCallAttempted = true };
        }
        public void Dispose() { if (_closed) return; _closed = true; node.Active--; node.Closing?.Invoke(); }
    }
    private sealed class Adapter : IStorageTransforms
    {
        internal Func<Node, IEnumerable<StorageTraversalEntry>>? ReadEntries;
        public StorageProblem? Unavailable(StorageAction action) => null;
        public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct) => throw new InvalidOperationException("The fixture already owns its reviewed pins.");
        public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct) => new Traversal(((Pin)root).Node, this);
    }
    private sealed class Traversal(Node root, Adapter adapter) : IStorageTraversal
    {
        private readonly List<Held> _held = [];
        public IStorageObjectLease Root { get; } = Held.Open(root);
        public IReadOnlyList<StorageObjectIdentity> RootAncestors(CancellationToken ct)
        {
            var list = new List<StorageObjectIdentity>(); for (var parent = root.Parent; parent is not null; parent = parent.Parent) list.Add(parent.Identity);
            list.Reverse(); return list;
        }
        public IEnumerable<StorageTraversalEntry> Entries(IStorageObjectLease directory, CancellationToken ct)
        {
            var node = ((Held)directory).Node; node.Enumerating?.Invoke();
            if (adapter.ReadEntries is { } entries) { foreach (var entry in entries(node)) yield return entry; yield break; }
            foreach (var child in node.Children)
            {
                ct.ThrowIfCancellationRequested(); var held = Held.Open(child); _held.Add(held);
                yield return new(child.Path, child.Directory, held, null);
            }
        }
        public IEnumerable<IStorageObjectLease> Children(IStorageObjectLease directory, CancellationToken ct) => throw new InvalidOperationException("Execution must preserve per-child failure entries.");
        public void Dispose() { Root.Dispose(); foreach (var held in _held) held.Dispose(); }
    }
    private sealed class Release : IDisposable { public void Dispose() { } }
    private static ItemRef Source(Node node)
    {
        int slash = node.Path.LastIndexOf('\\');
        return new(Location.FileSystem(node.Path[..slash]), node.Path[(slash + 1)..], node.Directory ? EntryKind.Directory : EntryKind.File);
    }
    private sealed class Harness : IDisposable
    {
        internal readonly Job Job;
        internal readonly StorageReviewOwner Owner;
        internal readonly StorageTransformJournalWriter Writer;
        internal readonly Adapter Adapter = new();
        private readonly MemoryStream _stream = new();
        internal Harness(Node[] roots, StorageScope scope = StorageScope.ExistingDescendants, Action? flush = null, int recordLimit = StorageJournalPolicy.RecordLimit,
            StorageAction action = StorageAction.Compress)
        {
            var distinct = roots.Distinct().ToArray(); var pins = distinct.Select(node => new Pin(node)).ToArray();
            var payload = new StorageTransformRequest(action, scope, Guid.NewGuid())
            { Roots = roots.Select((node, source) => new StorageReviewedRoot(source, Array.IndexOf(distinct, node), node.State)).ToImmutableArray() };
            var request = new JobRequest { Kind = JobKind.StorageTransform, Sources = roots.Select(Source).ToArray(), StorageTransform = payload };
            Job = new(request, "Storage executor fixture", "fixture", [], []); Job.SetState(JobState.Queued); Job.SealAdmission();
            Assert.Equal(JobStartDisposition.Started, Job.TryStart());
            Owner = new(pins.Cast<IStorageReviewLease>().ToImmutableArray(), new Release());
            Writer = new(_stream, flush ?? (() => { }), JobJournal.CaptureStorageHeader(request, Job.Id, Job.CreatedUtc), recordLimit: recordLimit);
            Job.AttachStorageWriter(Writer);
        }
        internal StorageResultSnapshot Run(int observations = StorageVisitLedger.ObservationLimit)
        {
            new StorageTransformExecutor(Job, Adapter, Owner, Writer, observations).Execute();
            var result = Job.StorageResult!;
            if (!Writer.IsFaulted) Writer.Finish(result);
            return result;
        }
        public void Dispose() { Writer.Dispose(); Owner.Dispose(); _stream.Dispose(); }
    }

    [Fact]
    public void Reverse_order_overlapping_selections_count_each_physical_object_once_and_complete_every_mapping()
    {
        var file = new Node("file"); var root = new Node("root", true).Add(file);
        using var h = new Harness([file, root, root]); var result = h.Run();
        Assert.Equal(2, result.Counts.Changed); Assert.Equal(JobState.Completed, result.FinalState());
        Assert.All(result.Roots, row => Assert.Equal(StorageCompleteness.Complete, row.Completeness));
        Assert.Equal(1, file.Calls); Assert.Equal(1, root.Calls); Assert.Equal(0, file.Active + root.Active);
        Assert.Equal(2, h.Job.ItemsDone); Assert.Equal(2, h.Job.ItemsTotal); Assert.False(h.Writer.NeedsRecovery);
    }

    [Fact]
    public void Already_satisfied_directory_descends_without_write_preparation_and_default_precedes_children()
    {
        var file = new Node("file"); var root = new Node("root", true).Add(file); root.Compression = StorageObservationState.Enabled;
        root.Enumerating = () => Assert.Equal(StorageObservationState.Enabled, root.Compression);
        using var h = new Harness([root]); var result = h.Run();
        Assert.Equal(1, result.Counts.AlreadySatisfied); Assert.Equal(1, result.Counts.Changed);
        Assert.Equal(0, root.Calls + root.Preparations); Assert.Equal(1, file.Calls); Assert.Equal(JobState.Completed, result.FinalState());
    }

    [Fact]
    public void Selected_only_scope_does_not_walk_contents_or_prune_an_explicit_child()
    {
        var file = new Node("file"); var root = new Node("root", true).Add(file);
        root.Enumerating = () => throw new InvalidOperationException("Selected-only scope must not enumerate.");
        using var h = new Harness([root, file], StorageScope.SelectedItems); var result = h.Run();
        Assert.Equal(2, result.Counts.Changed); Assert.Equal(JobState.Completed, result.FinalState());
    }

    [Fact]
    public void Changed_directory_then_enumeration_failure_keeps_one_changed_object_and_unreached_covered_root()
    {
        var file = new Node("file"); var root = new Node("root", true).Add(file);
        root.Enumerating = () => throw new IOException("Enumeration fixture failure");
        using var h = new Harness([file, root]); var result = h.Run();
        Assert.Equal(1, result.Counts.Changed); Assert.Equal(1, result.Counts.NotProcessed); Assert.Equal(0, result.Counts.Failed);
        Assert.Equal(StorageCompleteness.UnknownRemainder, result.Discovery); Assert.Equal(0, file.Calls);
        Assert.All(result.Roots, row => Assert.Equal(StorageCompleteness.Incomplete, row.Completeness));
        Assert.Equal(JobState.CompletedWithIssues, result.FinalState()); Assert.Equal(0, root.Active);
    }

    [Fact]
    public void Child_open_failure_does_not_hide_independent_siblings_or_add_a_second_directory_outcome()
    {
        var sibling = new Node("sibling"); var root = new Node("root", true).Add(sibling); using var h = new Harness([root]);
        h.Adapter.ReadEntries = _ => [new(root.Path + "\\denied", false, null, new(StorageReasonKind.PermissionOrPolicy, "denied", "Fixture denied", true)),
            new(sibling.Path, false, Held.Open(sibling), null)];
        var result = h.Run(); Assert.Equal(2, result.Counts.Changed); Assert.Equal(1, result.Counts.Failed);
        Assert.Equal(1, sibling.Calls); Assert.True(result.ObservationCounts); Assert.Equal(JobState.CompletedWithIssues, result.FinalState());
    }

    [Fact]
    public void Closed_id_rediscovery_never_mutates_again_or_reuses_an_earlier_complete_subtree()
    {
        var first = new Node("first", true, "same-id"); var second = new Node("second", true, "same-id");
        var root = new Node("root", true).Add(first).Add(second); using var h = new Harness([root]); var result = h.Run();
        Assert.Equal(2, result.Counts.Changed); Assert.Equal(1, result.Counts.NotProcessed); Assert.Equal(1, result.AmbiguousDuplicates);
        Assert.Equal(0, second.Calls + second.Preparations); Assert.Equal(StorageCompleteness.UnknownRemainder, result.Discovery);
        Assert.Equal(JobState.CompletedWithIssues, result.FinalState()); Assert.True(h.Writer.Collector.Summary!.ObservationCounts);
    }

    [Fact]
    public void Stop_after_directory_call_preserves_change_but_does_not_complete_its_unwalked_contents()
    {
        var file = new Node("file"); var root = new Node("root", true).Add(file); using var h = new Harness([root]);
        root.Mutation = before => { root.Compression = StorageObservationState.Enabled; h.Job.Cancel(); return new(before, root.State, StorageEffect.Changed, null) { NativeCallAttempted = true }; };
        var result = h.Run(); Assert.Equal(1, result.Counts.Changed); Assert.True(result.Canceled); Assert.Equal(0, file.Calls);
        Assert.Equal(StorageCompleteness.UnknownRemainder, result.Discovery); Assert.Equal(JobState.Canceled, result.FinalState());
        Assert.Equal(0, root.Active); Assert.False(h.Writer.NeedsRecovery);
    }

    [Fact]
    public void Uncertain_native_effect_stops_independent_roots_and_outranks_user_stop()
    {
        var first = new Node("first"); var next = new Node("next"); using var h = new Harness([first, next]);
        first.Mutation = before => { h.Job.Cancel(); return new(before, null, StorageEffect.Uncertain, new(StorageReasonKind.Unknown, "unknown", "Fixture uncertain", true)) { NativeCallAttempted = true }; };
        var result = h.Run(); Assert.Equal(1, result.Counts.Uncertain); Assert.Equal(1, result.Counts.NotProcessed); Assert.Equal(0, next.Calls);
        Assert.True(result.ReviewPending); Assert.True(result.Canceled); Assert.Equal(JobState.Failed, result.FinalState());
        Assert.True(h.Writer.NeedsRecovery); Assert.Equal(0, first.Active + next.Active);
    }

    [Fact]
    public void Result_flush_failure_preserves_observed_change_and_admits_no_later_call()
    {
        var first = new Node("first"); var next = new Node("next"); int flushes = 0;
        using var h = new Harness([first, next], flush: () => { if (++flushes == 3) throw new IOException("Result flush fixture failure"); });
        var result = h.Run(); Assert.Equal(1, result.Counts.Changed); Assert.Equal(1, result.Counts.NotProcessed); Assert.Equal(0, next.Calls);
        Assert.True(result.PersistenceFault); Assert.True(result.ReviewPending); Assert.Equal(JobState.Failed, result.FinalState());
        Assert.Equal(0, first.Active + next.Active); Assert.True(h.Writer.NeedsRecovery);
    }

    [Fact]
    public void Observation_limit_stops_before_the_next_mutation_and_preserves_known_root_counts()
    {
        var first = new Node("first"); var next = new Node("next"); using var h = new Harness([first, next]); var result = h.Run(observations: 1);
        Assert.Equal(1, result.Counts.Changed); Assert.Equal(1, result.Counts.NotProcessed); Assert.Equal(0, next.Calls);
        Assert.Equal(JobState.CompletedWithIssues, result.FinalState()); Assert.False(h.Writer.NeedsRecovery);
    }

    [Fact]
    public void All_policy_skipped_roots_are_issues_without_becoming_success_or_an_execution_error()
    {
        var file = new Node("file"); using var h = new Harness([file], StorageScope.SelectedItems);
        file.Ineligible = new(StorageReasonKind.FileCatBoundary, "policy", "Fixture policy exclusion", false);
        var result = h.Run(); Assert.Equal(1, result.Counts.Skipped); Assert.Equal(0, result.Counts.Successful);
        Assert.Equal(0, file.Calls + file.Preparations); Assert.Equal(JobState.CompletedWithIssues, result.FinalState());
    }

    [Fact]
    public void Desired_postcondition_with_native_error_counts_one_changed_object_and_preserves_the_issue()
    {
        var file = new Node("file"); using var h = new Harness([file], StorageScope.SelectedItems);
        file.Mutation = before => { file.Compression = StorageObservationState.Enabled; return new(before, file.State, StorageEffect.Changed,
            new(StorageReasonKind.TransientFailure, "native-5", "The desired state was observed despite a failed native return.", true)) { NativeCallAttempted = true }; };
        var result = h.Run(); Assert.Equal(1, result.Counts.Changed); Assert.Equal(0, result.Counts.Failed); Assert.Equal(0, h.Job.ItemsFailed);
        Assert.Equal(JobState.CompletedWithIssues, result.FinalState()); Assert.Equal("native-5", Assert.Single(result.Reasons).Problem.Code);
    }

    [Fact]
    public void Depth_limit_never_opens_the_next_directory_and_does_not_invent_omitted_descendant_counts()
    {
        var root = new Node("root", true); var current = root;
        var nodes = new List<Node> { root };
        for (int i = 0; i < 64; i++) { var child = new Node("d" + i, true); current.Add(child); nodes.Add(child); current = child; }
        using var h = new Harness([root]); var result = h.Run();
        Assert.Equal(64, result.Counts.Changed); Assert.Equal(0, result.Counts.NotProcessed); Assert.Equal(0, nodes[^1].Calls);
        Assert.Equal(StorageCompleteness.UnknownRemainder, result.Discovery); Assert.All(nodes, node => Assert.Equal(0, node.Active));
    }

    [Fact]
    public void Complete_reason_counts_survive_sample_limits_and_bounded_summary_roundtrip()
    {
        var root = new Node("root", true);
        for (int i = 0; i < 300; i++) root.Add(new Node("f" + i) { Ineligible = new(StorageReasonKind.FileCatBoundary, "policy-" + i, new string('x', 1800), false) });
        using var h = new Harness([root]); var result = h.Run();
        Assert.Equal(300, result.Counts.Skipped); Assert.Equal(300, result.Reasons.Sum(reason => reason.Count));
        Assert.InRange(result.Reasons.Length, 1, 64); Assert.InRange(result.Examples.Length, 0, 16);
        Assert.Equal(300, result.Examples.Length + result.OmittedExamples); Assert.False(h.Writer.NeedsRecovery);
        Assert.Equal(result.OmittedExamples, h.Writer.Collector.Summary!.OmittedExamples);
    }

    [Fact]
    public void Stop_after_child_open_accounts_that_known_unprocessed_child_and_closes_every_owner()
    {
        var child = new Node("child"); var root = new Node("root", true).Add(child); using var h = new Harness([root]);
        h.Adapter.ReadEntries = _ => StopAfterOpen();
        IEnumerable<StorageTraversalEntry> StopAfterOpen()
        {
            var held = Held.Open(child); h.Job.Cancel(); yield return new(child.Path, false, held, null);
        }
        var result = h.Run(); Assert.Equal(1, result.Counts.Changed); Assert.Equal(1, result.Counts.NotProcessed);
        Assert.True(result.Canceled); Assert.Equal(0, child.Calls); Assert.Equal(0, child.Active + root.Active);
    }

    [Fact]
    public void Root_summary_flush_failure_happens_after_held_objects_retire_and_keeps_the_observed_change()
    {
        var file = new Node("file"); int flushes = 0;
        using var h = new Harness([file], StorageScope.SelectedItems, () =>
        {
            if (++flushes == 4) { Assert.Equal(0, file.Active); throw new IOException("Root summary flush fixture"); }
        });
        var result = h.Run(); Assert.Equal(1, result.Counts.Changed); Assert.True(result.PersistenceFault); Assert.True(result.ReviewPending);
        Assert.Equal(JobState.Failed, result.FinalState()); Assert.True(h.Writer.NeedsRecovery); Assert.Equal(0, file.Active);
    }

    [Fact]
    public void Remove_compression_clears_directory_defaults_before_processing_existing_children()
    {
        var file = new Node("file") { Compression = StorageObservationState.Enabled };
        var root = new Node("root", true) { Compression = StorageObservationState.Enabled }; root.Add(file);
         root.Enumerating = () => Assert.Equal(StorageObservationState.Disabled, root.Compression);
        using var h = new Harness([file, root], action: StorageAction.RemoveCompression); var result = h.Run();
        Assert.Equal(JobState.Completed, result.FinalState()); Assert.Equal(2, result.Counts.Changed);
        Assert.Equal(StorageObservationState.Disabled, file.Compression); Assert.Equal(1, root.Calls); Assert.Equal(1, file.Calls);
    }

    [Fact]
    public void A_started_owner_cleanup_fault_stops_later_mutations_and_preserves_the_observed_change_for_review()
    {
        var first = new Node("first"); var later = new Node("later"); using var h = new Harness([first, later], StorageScope.SelectedItems);
        first.Closing = () => throw new IOException("Owner retirement fixture");
        var result = h.Run(); Assert.Equal(1, result.Counts.Changed); Assert.Equal(1, result.Counts.NotProcessed);
        Assert.Equal(0, later.Calls); Assert.Equal(0, first.Active + later.Active); Assert.True(result.ReviewPending);
        Assert.Equal(JobState.Failed, result.FinalState()); Assert.Contains(result.Reasons, item => item.Problem.Code == "owner-retirement-failed");
    }
}
