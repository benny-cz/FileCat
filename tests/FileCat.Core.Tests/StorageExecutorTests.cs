using System.Collections.Immutable;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class StorageExecutorTests
{
    private static CancellationToken Stop => TestContext.Current.CancellationToken;
    private sealed class Node(string name, bool directory = false, string? identity = null, ulong volume = 11, string volumeBinding = "fixture-volume")
    {
        internal readonly string Name = name;
        internal readonly bool Directory = directory;
        internal readonly StorageObjectIdentity Identity = new(volume, identity ?? name) { VolumeBinding = volumeBinding };
        internal readonly List<Node> Children = [];
        internal Node? Parent;
        internal StorageObservationState Compression = StorageObservationState.Disabled;
        internal StorageProblem? Ineligible;
        internal Func<StorageObservation, StorageMutationResult>? Mutation;
        internal Action? Enumerating;
        internal Action? Closing;
        internal Action? Preparing;
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
        public StorageObservation PrepareMutation(StorageAction action, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); node.Preparations++; node.Preparing?.Invoke(); return node.State; }
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
        internal StorageResultSnapshot Run(int observations = StorageVisitLedger.ObservationLimit, IStorageTransforms? adapter = null)
        {
            new StorageTransformExecutor(Job, adapter ?? Adapter, Owner, Writer, observations).Execute();
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

    private static StorageMutationResult Refused(StorageObservation before) => new(before, before, StorageEffect.Failed,
        new(StorageReasonKind.PermissionOrPolicy, "native-denied", "The native call was refused; the object stayed unchanged.", true)) { NativeCallAttempted = true };
    private static IOException SharingViolation() => new("Sharing violation fixture.", unchecked((int)0x80070020));
    // Begin, one root summary and the fixed closure records, one whole call reservation, then three more two-record calls.
    private const int FourCallRecordLimit = 1 + 1 + StorageJournalPolicy.ClosureRecords + StorageJournalPolicy.CallRecords + 2 * 3;

    [Fact]
    public void Intent_flush_failure_admits_no_native_call_and_keeps_the_whole_job_pending_review()
    {
        var first = new Node("first"); var next = new Node("next"); int flushes = 0;
        using var h = new Harness([first, next], StorageScope.SelectedItems, () => { if (++flushes == 2) throw new IOException("Intent flush fixture failure"); });
        var result = h.Run();
        Assert.Equal(0, first.Calls + next.Calls); Assert.Equal(0, next.Preparations); Assert.Equal(2, result.Counts.NotProcessed);
        Assert.True(result.PersistenceFault); Assert.True(result.ReviewPending); Assert.Equal(JobState.Failed, result.FinalState());
        Assert.Contains(result.Reasons, reason => reason.Problem.Code == "storage-evidence-failed");
        Assert.All(result.Roots, row => Assert.Equal(StorageCompleteness.Incomplete, row.Completeness));
        Assert.True(h.Writer.NeedsRecovery); Assert.NotNull(h.Writer.Collector.Pending); Assert.Equal(0, first.Active + next.Active);
    }

    [Fact]
    public void Repeated_native_failures_exhaust_the_record_budget_and_stop_with_a_truthful_incomplete_outcome()
    {
        var root = new Node("root", true) { Compression = StorageObservationState.Enabled };
        var children = Enumerable.Range(0, 10).Select(i => new Node("f" + i) { Mutation = Refused }).ToArray();
        foreach (var child in children) root.Add(child);
        using var h = new Harness([root], recordLimit: FourCallRecordLimit); var result = h.Run();
        Assert.Equal(new[] { 1, 1, 1, 1, 0, 0, 0, 0, 0, 0 }, children.Select(child => child.Calls));
        Assert.Equal(1, children[4].Preparations); Assert.All(children[5..], child => Assert.Equal(0, child.Preparations));
        // Undiscovered siblings stay an unknown remainder; they are never computed as not processed.
        Assert.Equal(new StorageOutcomeCounts(AlreadySatisfied: 1, Failed: 4, NotProcessed: 1), result.Counts);
        Assert.Equal(4, result.Reasons.Single(reason => reason.Problem.Code == "native-denied").Count);
        Assert.Equal(1, result.Reasons.Single(reason => reason.Problem.Code == "record-limit").Count);
        Assert.Equal(StorageCompleteness.UnknownRemainder, result.Discovery); Assert.Equal(StorageCompleteness.Incomplete, Assert.Single(result.Roots).Completeness);
        Assert.Equal(JobState.CompletedWithIssues, result.FinalState()); Assert.False(result.ReviewPending);
        // The fixed summary and closure capacity survived the exhausted call budget: the history is clean and complete.
        Assert.False(h.Writer.NeedsRecovery); Assert.True(h.Writer.Collector.Ended); Assert.InRange(h.Writer.Prefix!.Sequence, 1, FourCallRecordLimit);
        Assert.Equal(0, root.Active + children.Sum(child => child.Active));
    }

    [Fact]
    public void Fresh_passes_spend_no_records_on_a_completed_or_excluded_prefix_and_keep_reaching_pending_work()
    {
        var root = new Node("root", true) { Compression = StorageObservationState.Enabled };
        var excluded = Enumerable.Range(0, 200).Select(i => new Node("excluded" + i)
        { Ineligible = new(StorageReasonKind.FileCatBoundary, "policy", "Fixture policy exclusion.", false) }).ToArray();
        var pending = Enumerable.Range(0, 10).Select(i => new Node("pending" + i)).ToArray();
        foreach (var node in excluded.Concat(pending)) root.Add(node);
        var passes = new List<StorageResultSnapshot>(); var sequences = new List<long>();
        for (int pass = 0; pass < 3; pass++)
        {
            using var h = new Harness([root], recordLimit: FourCallRecordLimit);
            passes.Add(h.Run()); sequences.Add(h.Writer.Prefix!.Sequence); Assert.False(h.Writer.NeedsRecovery);
        }
        // Each pass passes the 200 exclusions and every earlier change without a durable record, then reaches new work.
        Assert.Equal(new StorageOutcomeCounts(Changed: 4, AlreadySatisfied: 1, Skipped: 200, NotProcessed: 1), passes[0].Counts);
        Assert.Equal(new StorageOutcomeCounts(Changed: 4, AlreadySatisfied: 5, Skipped: 200, NotProcessed: 1), passes[1].Counts);
        Assert.Equal(new StorageOutcomeCounts(Changed: 2, AlreadySatisfied: 9, Skipped: 200), passes[2].Counts);
        Assert.Equal(new long[] { 12, 12, 8 }, sequences);
        Assert.All(pending, node => Assert.Equal(1, node.Calls)); Assert.All(excluded, node => Assert.Equal(0, node.Calls + node.Preparations));
        Assert.Equal(StorageCompleteness.UnknownRemainder, passes[1].Discovery);
        Assert.Equal(StorageCompleteness.Incomplete, passes[2].Discovery); Assert.Equal(JobState.CompletedWithIssues, passes[2].FinalState());
        Assert.Equal(200, passes[2].Reasons.Single(reason => reason.Problem.Code == "policy").Count);
    }

    [Fact]
    public void A_partially_processed_directory_moved_to_another_root_leaves_both_roots_incomplete_and_is_not_mutated_again()
    {
        var first = new Node("first"); var second = new Node("second");
        var moved = new Node("moved", true).Add(first).Add(second);
        var a = new Node("a", true).Add(moved); var b = new Node("b", true);
        using var h = new Harness([a, b]);
        h.Adapter.ReadEntries = node =>
        {
            if (node == b) moved.Parent = b; // the incomplete directory moved under the second selected root
            return node == a || node == b ? [new(moved.Path, true, Held.Open(moved), null)] : node == moved ? PartlyEnumerated() : [];
        };
        IEnumerable<StorageTraversalEntry> PartlyEnumerated()
        {
            yield return new(first.Path, false, Held.Open(first), null);
            throw new IOException("The directory moved during enumeration.");
        }
        var result = h.Run();
        Assert.Equal(1, moved.Calls); Assert.Equal(1, first.Calls); Assert.Equal(0, second.Calls + second.Preparations);
        Assert.Equal(4, result.Counts.Changed); Assert.Equal(1, result.Counts.NotProcessed); Assert.Equal(1, result.AmbiguousDuplicates);
        Assert.True(result.ObservationCounts); Assert.All(result.Roots, row => Assert.Equal(StorageCompleteness.Incomplete, row.Completeness));
        Assert.Equal(StorageCompleteness.UnknownRemainder, result.Discovery); Assert.Equal(JobState.CompletedWithIssues, result.FinalState());
        Assert.Contains(result.Reasons, reason => reason.Problem.Code == "enumeration-failed");
        Assert.Contains(result.Reasons, reason => reason.Problem is { Code: "scope-changed", Kind: StorageReasonKind.IdentityChanged });
        Assert.Equal(0, a.Active + b.Active + moved.Active + first.Active + second.Active); Assert.False(h.Writer.NeedsRecovery);
    }

    [Fact]
    public void A_cursor_entry_bound_to_another_lease_or_problem_is_churn_closed_without_mutation()
    {
        var stray = new Node("stray"); var troubled = new Node("troubled"); var sibling = new Node("sibling");
        var root = new Node("root", true).Add(sibling); using var h = new Harness([root]);
        h.Adapter.ReadEntries = node => node == root ? Entries() : [];
        IEnumerable<StorageTraversalEntry> Entries()
        {
            yield return new(root.Path + @"\expected", false, Held.Open(stray), null); // the cursor named one child; the lease is another object
            yield return new(troubled.Path, false, Held.Open(troubled), new(StorageReasonKind.Unknown, "late-problem", "Fixture problem beside a lease.", true));
            yield return new(sibling.Path, false, Held.Open(sibling), null);
        }
        var result = h.Run();
        Assert.Equal(0, stray.Calls + stray.Preparations + troubled.Calls + troubled.Preparations); Assert.Equal(1, sibling.Calls);
        Assert.Equal(0, root.Active + stray.Active + troubled.Active + sibling.Active);
        Assert.Equal(new StorageOutcomeCounts(Changed: 2), result.Counts);
        var churn = result.Reasons.Single(reason => reason.Problem.Code == "scope-changed");
        Assert.Equal(2, churn.Count); Assert.Equal(StorageReasonKind.IdentityChanged, churn.Problem.Kind);
        Assert.Equal(StorageCompleteness.Incomplete, Assert.Single(result.Roots).Completeness); Assert.Equal(StorageCompleteness.UnknownRemainder, result.Discovery);
        Assert.Equal(JobState.CompletedWithIssues, result.FinalState()); Assert.Equal(2, h.Writer.Collector.LastIntent!.Call); Assert.False(h.Writer.NeedsRecovery);
    }

    [Theory]
    [InlineData(12UL, "fixture-volume")]
    [InlineData(11UL, "mounted-volume")]
    public void A_discovered_object_on_another_volume_is_not_mutated_walked_or_completed(ulong volume, string binding)
    {
        var inside = new Node("inside"); var crossed = new Node("crossed", true, volume: volume, volumeBinding: binding).Add(inside);
        crossed.Enumerating = () => throw new InvalidOperationException("A crossed volume must never be enumerated.");
        var sibling = new Node("sibling"); var root = new Node("root", true).Add(crossed).Add(sibling);
        using var h = new Harness([root]); var result = h.Run();
        Assert.Equal(0, crossed.Calls + crossed.Preparations + inside.Calls + inside.Preparations); Assert.Equal(1, sibling.Calls);
        Assert.Equal(new StorageOutcomeCounts(Changed: 2, NotProcessed: 1), result.Counts); Assert.True(result.ObservationCounts);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal("scope-changed", reason.Problem.Code); Assert.Equal(StorageReasonKind.IdentityChanged, reason.Problem.Kind);
        Assert.Equal(StorageCompleteness.Incomplete, Assert.Single(result.Roots).Completeness); Assert.Equal(StorageCompleteness.UnknownRemainder, result.Discovery);
        Assert.Equal(JobState.CompletedWithIssues, result.FinalState()); Assert.Equal(2, h.Writer.Collector.LastIntent!.Call);
        Assert.Equal(0, root.Active + crossed.Active + inside.Active + sibling.Active); Assert.False(h.Writer.NeedsRecovery);
    }

    [Theory]
    [InlineData(DecisionAction.Retry)]
    [InlineData(DecisionAction.Skip)]
    [InlineData(DecisionAction.CancelJob)]
    public void Preparation_sharing_conflicts_retry_twice_then_ask_one_job_scoped_decision_before_any_intent(DecisionAction answer)
    {
        var file = new Node("file"); var later = new Node("later"); int conflicts = 0;
        file.Preparing = () => { if (++conflicts <= 3) throw SharingViolation(); };
        using var h = new Harness([file, later], StorageScope.SelectedItems); var asked = new List<ErrorRequest>();
        h.Job.Changed += job =>
        {
            if (job.State != JobState.AwaitingDecision || job.Decision is not { } pending) return;
            asked.Add(Assert.IsType<ErrorRequest>(pending.Request)); pending.Resolve(new(answer));
        };
        var result = h.Run();
        var request = Assert.Single(asked); // two automatic retries, then exactly one decision
        Assert.Equal(file.Path, request.Path); Assert.True(request.CanRetry); Assert.Equal("error-storage-Compress-sharing", request.ClassKey);
        switch (answer)
        {
            case DecisionAction.Retry: // the same held object is revalidated, then admitted once
                Assert.Equal(4, file.Preparations); Assert.Equal(1, file.Calls); Assert.Equal(1, later.Calls);
                Assert.Equal(new StorageOutcomeCounts(Changed: 2), result.Counts); Assert.Equal(JobState.Completed, result.FinalState());
                break;
            case DecisionAction.Skip:
                Assert.Equal(3, file.Preparations); Assert.Equal(0, file.Calls); Assert.Equal(1, later.Calls);
                Assert.Equal(new StorageOutcomeCounts(Changed: 1, Skipped: 1), result.Counts);
                Assert.Contains(result.Reasons, reason => reason.Problem.Code == "sharing-skipped"); Assert.Equal(JobState.CompletedWithIssues, result.FinalState());
                break;
            default: // Stop: completed changes would be kept, and nothing further is admitted
                Assert.Equal(3, file.Preparations); Assert.Equal(0, file.Calls + later.Calls + later.Preparations);
                Assert.True(result.Canceled); Assert.Equal(JobState.Canceled, result.FinalState());
                break;
        }
        // No conflict leaves an intent behind: every durable intent belongs to an actual native call.
        Assert.Equal(file.Calls + later.Calls, h.Writer.Collector.LastIntent?.Call ?? 0); Assert.False(h.Writer.NeedsRecovery);
    }

    [Theory]
    [InlineData(DecisionAction.Retry)]
    [InlineData(DecisionAction.Proceed)]
    [InlineData(DecisionAction.Skip)]
    [InlineData(DecisionAction.CancelJob)]
    public void An_unreachable_key_asks_once_before_preparation_may_let_Windows_wait(DecisionAction answer)
    {
        var file = new Node("file"); var later = new Node("later"); bool accepted = false; int checks = 0;
        var problem = new StorageProblem(StorageReasonKind.PermissionOrPolicy, "efs-key-not-reachable", "Fixture key behind a removed card.", true);
        // Retry checks again and finds the card connected; Decrypt anyway lets the same item's preparation continue.
        file.Preparing = () => { if (++checks == 1 && !accepted) throw new StorageKeyWaitException(problem, () => accepted = true); };
        using var h = new Harness([file, later], StorageScope.SelectedItems); var asked = new List<ConfirmRequest>();
        h.Job.Changed += job =>
        {
            if (job.State != JobState.AwaitingDecision || job.Decision is not { } pending) return;
            asked.Add(Assert.IsType<ConfirmRequest>(pending.Request)); pending.Resolve(new(answer));
        };
        var result = h.Run();
        var request = Assert.Single(asked);
        Assert.Equal(file.Path, request.Path); Assert.Equal("confirm-storage-key-wait", request.ClassKey);
        Assert.Equal(problem.Message, request.Message); Assert.Equal(DecisionAction.Retry, request.InitialAction);
        Assert.Equal([DecisionAction.Retry, DecisionAction.Proceed, DecisionAction.Skip, DecisionAction.CancelJob], request.Actions);
        Assert.Equal(answer == DecisionAction.Proceed, accepted);
        switch (answer)
        {
            case DecisionAction.Retry or DecisionAction.Proceed: // the same held item is prepared again, then admitted once
                Assert.Equal(2, file.Preparations); Assert.Equal(1, file.Calls); Assert.Equal(1, later.Calls);
                Assert.Equal(new StorageOutcomeCounts(Changed: 2), result.Counts); Assert.Equal(JobState.Completed, result.FinalState());
                break;
            case DecisionAction.Skip:
                Assert.Equal(1, file.Preparations); Assert.Equal(0, file.Calls); Assert.Equal(1, later.Calls);
                Assert.Equal(new StorageOutcomeCounts(Changed: 1, Skipped: 1), result.Counts);
                Assert.Contains(result.Reasons, reason => reason.Problem.Code == "efs-key-skipped"); Assert.Equal(JobState.CompletedWithIssues, result.FinalState());
                break;
            default:
                Assert.Equal(1, file.Preparations); Assert.Equal(0, file.Calls + later.Calls + later.Preparations);
                Assert.True(result.Canceled); Assert.Equal(JobState.Canceled, result.FinalState());
                break;
        }
        // The question comes before any durable intent: every intent belongs to an actual native call.
        Assert.Equal(file.Calls + later.Calls, h.Writer.Collector.LastIntent?.Call ?? 0); Assert.False(h.Writer.NeedsRecovery);
    }

    [Fact]
    public void A_sharing_failure_inside_an_admitted_call_is_uncertain_and_never_retried_or_turned_into_a_decision()
    {
        var file = new Node("file") { Mutation = _ => throw SharingViolation() }; var later = new Node("later");
        using var h = new Harness([file, later], StorageScope.SelectedItems); int asked = 0;
        h.Job.Changed += job => { if (job.State == JobState.AwaitingDecision) asked++; };
        var result = h.Run();
        Assert.Equal(1, file.Calls); Assert.Equal(1, file.Preparations); Assert.Equal(0, asked); Assert.Equal(0, later.Calls);
        Assert.Equal(1, result.Counts.Uncertain); Assert.True(result.ReviewPending); Assert.Equal(JobState.Failed, result.FinalState());
        Assert.True(h.Writer.NeedsRecovery);
    }

    [Theory]
    [InlineData(DecisionAction.Retry)]
    [InlineData(DecisionAction.Skip)]
    public void A_child_sharing_conflict_retries_through_its_bound_cursor_entry_before_one_decision(DecisionAction answer)
    {
        var child = new Node("child"); var sibling = new Node("sibling"); var root = new Node("root", true).Add(child).Add(sibling);
        using var h = new Harness([root]); int retries = 0, asked = 0;
        var conflict = new StorageProblem(StorageReasonKind.TransientFailure, "sharing-violation", "Fixture sharing violation.", true);
        h.Adapter.ReadEntries = node => node == root ? Entries() : [];
        IEnumerable<StorageTraversalEntry> Entries()
        {
            yield return new(child.Path, false, null, conflict)
            {
                Retry = _ => ++retries <= 2 || answer == DecisionAction.Skip ? new(child.Path, false, null, conflict) : new(child.Path, false, Held.Open(child), null),
            };
            yield return new(sibling.Path, false, Held.Open(sibling), null);
        }
        h.Job.Changed += job => { if (job.State == JobState.AwaitingDecision && job.Decision is { } pending) { asked++; pending.Resolve(new(answer)); } };
        var result = h.Run();
        Assert.Equal(1, asked); Assert.Equal(1, sibling.Calls); Assert.Equal(0, root.Active + child.Active + sibling.Active);
        if (answer == DecisionAction.Retry)
        {
            Assert.Equal(3, retries); Assert.Equal(1, child.Calls);
            Assert.Equal(new StorageOutcomeCounts(Changed: 3), result.Counts); Assert.Equal(JobState.Completed, result.FinalState());
        }
        else
        {
            Assert.Equal(2, retries); Assert.Equal(0, child.Calls + child.Preparations);
            Assert.Equal(new StorageOutcomeCounts(Changed: 2, Skipped: 1), result.Counts); Assert.Contains(result.Reasons, reason => reason.Problem.Code == "sharing-skipped");
            Assert.Equal(StorageCompleteness.Incomplete, Assert.Single(result.Roots).Completeness); Assert.Equal(JobState.CompletedWithIssues, result.FinalState());
        }
    }

    [Theory]
    [InlineData(StorageAction.Compress)]
    [InlineData(StorageAction.RemoveCompression)]
    public void Compression_never_consults_EFS_key_or_stream_services_on_an_adapter_that_also_offers_them(StorageAction action)
    {
        var initial = action == StorageAction.Compress ? StorageObservationState.Disabled : StorageObservationState.Enabled;
        var file = new Node("file") { Compression = initial }; var root = new Node("root", true) { Compression = initial }.Add(file);
        using var h = new Harness([root], action: action); var trap = new EfsTrap(h.Adapter);
        var result = h.Run(adapter: trap);
        Assert.Equal(0, trap.Calls); Assert.Equal(1, file.Calls); Assert.Equal(1, root.Calls);
        Assert.Equal(new StorageOutcomeCounts(Changed: 2), result.Counts); Assert.Equal(JobState.Completed, result.FinalState());
        Assert.Null(h.Writer.Header.EfsConsent); Assert.False(h.Writer.Header.RequiresEfsRecoveryEvidence); Assert.False(h.Writer.NeedsRecovery);
    }

    /// <summary>Offers every EFS, key and backup surface; each one fails the test if a compression path touches it.</summary>
    private sealed class EfsTrap(Adapter inner) : IStorageTransforms, IStorageEfsJobAdapter, IStorageEfsSetup, IStorageEfsKeys, IStorageEfsFileBackup
    {
        internal int Calls;
        private T Trap<T>() { Calls++; throw new InvalidOperationException("A compression job must never use an EFS, key or stream service."); }
        public StorageProblem? Unavailable(StorageAction action) => StorageJournalPolicy.IsEfs(action) ? Trap<StorageProblem?>() : inner.Unavailable(action);
        public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct) => inner.InspectRoot(target, action, ct);
        public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct) => inner.OpenReviewedRoot(root, action, ct);
        public StorageProblem? ScopeUnavailable(StorageScope scope) => Trap<StorageProblem?>();
        public IStorageTraversal OpenReviewedEfsRoot(IStorageReviewLease root, Job job, int sourceIndex, StorageEfsRecoverySession recovery, CancellationToken ct) => Trap<IStorageTraversal>();
        public StorageEfsSetupResult InspectSetup(string invokerSid, StorageEfsConsent scopeConsent, bool explicitSetupAccepted, CancellationToken ct) => Trap<StorageEfsSetupResult>();
        public StorageEfsKeyEvidence ProbeAssociatedCertificate(string invokerSid, string certificateSha1, CancellationToken ct) => Trap<StorageEfsKeyEvidence>();
        public StorageEfsFileBackupReview InspectFiles(ImmutableArray<StorageTarget> targets, CancellationToken ct) => Trap<StorageEfsFileBackupReview>();
        public StorageEfsFileBackupResult BackUpFileKey(StorageEfsFileBackupReview review, string certificateSha1, nint ownerWindow, CancellationToken ct) => Trap<StorageEfsFileBackupResult>();
    }
}
