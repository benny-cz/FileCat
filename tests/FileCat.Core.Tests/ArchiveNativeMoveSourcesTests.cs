using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class ArchiveCapturedInputsTests
{
    private sealed class RemovalTree(Tree inner, Action<ArchiveSelectionNode> remove) : IArchiveSelectionTree, IArchiveSelectionRemoval
    {
        public bool CanRemoveSources => true;
        public ArchiveSelectionEntry InspectRoot(string leaf, CancellationToken ct) => inner.InspectRoot(leaf, ct);
        public void Enumerate(ArchiveSelectionNode node, Action<ArchiveSelectionEntry> visit, CancellationToken ct) => inner.Enumerate(node, visit, ct);
        public void CaptureFile(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode node, CancellationToken ct) => inner.CaptureFile(inputs, id, node, ct);
        public void CaptureDirectory(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode node, CancellationToken ct) => inner.CaptureDirectory(inputs, id, node, ct);
        public void ReviewDestination(string path, string id, IReadOnlySet<string> selected, CancellationToken ct) => inner.ReviewDestination(path, id, selected, ct);
        public void RemoveFile(ArchiveSelectionNode node, ArchiveFileObservation expected, CancellationToken ct, string? asideLeaf = null) { ct.ThrowIfCancellationRequested(); remove(node); }
        public void RemoveDirectory(ArchiveSelectionNode node, ArchiveDirectoryObservation expected, CancellationToken ct) { ct.ThrowIfCancellationRequested(); remove(node); }
        public void Dispose() => inner.Dispose();
    }
    private sealed class MoveBackend(Action<ArchiveJobExecution> run) : IArchiveJobBackend
    {
        public ArchiveWriteFormat Format => ArchiveWriteFormat.Tar;
        public ArchiveJobAvailability Availability(ArchivePlan plan) => ArchiveJobAvailability.Ready;
        public void Execute(ArchiveJobExecution execution) => run(execution);
    }
    private static async Task<Job> RunNativeMove(Tree tree, List<string> removed, List<bool> reviews,
        Action<ArchiveSelectionNode, Job>? deleting = null, Action<Job>? published = null, int changedAtReview = -1,
        IReadOnlySet<string>? committedMembers = null)
    {
        using var temp = new TempDir(); Job? active = null;
        var plan = new ArchivePlan(Path.Combine(temp.Path, "output.tar"), null,
            [new(ArchiveChangeKind.AddFolder, "moved", Path.Combine(temp.Path, "tree"))], Profile: new(ArchiveWriteFormat.Tar)) { MoveSources = true };
        var planning = new ArchiveJobPlanning(plan, Ct);
        using var selection = ArchiveSelectionForest.Capture(planning.Sources, _ => new RemovalTree(tree, node =>
        { deleting?.Invoke(node, active!); removed.Add(node.NativeRelativePath); }), Ct);
        var backend = new MoveBackend(execution =>
        {
            active = execution.Job; active.AddTotals(1, 0); active.ItemDone(); published?.Invoke(active);
            ArchiveNativeMoveSources.Remove(active, execution.Journal, execution.Plan, selection, (full, ct) =>
            { ct.ThrowIfCancellationRequested(); reviews.Add(full); if (reviews.Count == changedAtReview) throw new IOException("Published archive changed."); },
                committedMembers is null ? null : selection.Members.Where(member => committedMembers.Contains(member.Name)).Select(member => member.Id).ToHashSet());
        });
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(temp.Path, "journals"), new([backend]));
        var job = manager.SubmitArchive(plan);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (!job.State.IsFinished()) await Task.Delay(5, deadline.Token);
        return job;
    }

    [Fact]
    public async Task Move_removes_files_before_empty_folders_and_completes_the_selected_root()
    {
        var removed = new List<string>(); var reviews = new List<bool>();
        var job = await RunNativeMove(new Tree { Children = (_, visit) => visit(new("file", ArchiveSelectionKind.File, "file")) }, removed, reviews);
        Assert.Equal(new[] { "tree/file", "tree" }, removed); Assert.Equal(new[] { true, false, false }, reviews);
        Assert.Equal(JobState.Completed, job.State); Assert.Equal(new[] { 0 }, job.CompletedRootIndices); Assert.Equal(0, job.FailedRootCount);
        Assert.Contains("Archive saved", job.Summary); Assert.Equal(EntryKind.Directory, Assert.Single(job.Request.Sources).Kind);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Move_keeps_skipped_children_and_their_parents_even_when_the_directory_merge_was_committed(bool commitGood)
    {
        var removed = new List<string>(); var reviews = new List<bool>();
        var tree = new Tree { Children = (_, visit) => { visit(new("good", ArchiveSelectionKind.File, "good")); visit(new("skipped", ArchiveSelectionKind.File, "skipped")); } };
        var committed = new HashSet<string> { "tree" }; if (commitGood) committed.Add("tree/good");
        var job = await RunNativeMove(tree, removed, reviews, committedMembers: committed);
        Assert.Equal(commitGood ? new[] { "tree/good" } : Array.Empty<string>(), removed);
        Assert.Equal(JobState.CompletedWithIssues, job.State); Assert.Equal(0, job.CompletedRootCount); Assert.Equal(1, job.FailedRootCount);
        Assert.Contains("not confirmed removed", job.Summary);
    }

    [Fact]
    public async Task Move_keeps_failed_children_and_all_their_captured_ancestors()
    {
        var removed = new List<string>(); var reviews = new List<bool>();
        var tree = new Tree { Children = (_, visit) => { visit(new("good", ArchiveSelectionKind.File, "good")); visit(new("bad", ArchiveSelectionKind.File, "bad")); } };
        var job = await RunNativeMove(tree, removed, reviews, (node, _) => { if (node.Entry.Leaf == "bad") throw new IOException("Source changed."); });
        Assert.Equal(new[] { "tree/good" }, removed); Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Equal(0, job.CompletedRootCount); Assert.Equal(1, job.FailedRootCount);
        Assert.Contains(job.Issues, issue => issue.Message.Contains("Source changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Move_remembers_excluded_ancestors_beyond_the_bounded_preview_samples()
    {
        var removed = new List<string>(); var reviews = new List<bool>();
        var tree = new Tree { Children = (_, visit) =>
        { for (int i = 0; i < 200; i++) visit(new("link" + i, ArchiveSelectionKind.Link, "link" + i)); visit(new("file", ArchiveSelectionKind.File, "file")); } };
        var job = await RunNativeMove(tree, removed, reviews);
        Assert.Equal(new[] { "tree/file" }, removed); Assert.Equal(1, job.FailedRootCount); Assert.Equal(JobState.CompletedWithIssues, job.State);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Move_cancellation_preserves_the_committed_archive_and_remaining_originals(bool afterFirst)
    {
        var removed = new List<string>(); var reviews = new List<bool>();
        var job = await RunNativeMove(new Tree { Children = (_, visit) => visit(new("file", ArchiveSelectionKind.File, "file")) }, removed, reviews,
            deleting: (_, current) => { if (afterFirst) current.Cancel(); }, published: current => { if (!afterFirst) current.Cancel(); });
        Assert.Equal(afterFirst ? new[] { "tree/file" } : [], removed);
        Assert.Equal(JobState.Canceled, job.State); Assert.Equal(1, job.FailedRootCount);
        Assert.Contains("Archive saved", job.Summary); Assert.Contains("move canceled", job.Summary);
    }

    [Fact]
    public async Task Move_stops_remaining_removals_when_the_publication_pin_fails_review()
    {
        var removed = new List<string>(); var reviews = new List<bool>();
        var tree = new Tree { Children = (_, visit) => { visit(new("a", ArchiveSelectionKind.File, "a")); visit(new("b", ArchiveSelectionKind.File, "b")); } };
        var job = await RunNativeMove(tree, removed, reviews, changedAtReview: 3);
        Assert.Single(removed); Assert.Equal(1, job.FailedRootCount); Assert.Equal(JobState.CompletedWithIssues, job.State);
    }
}
