using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed partial class ArchiveCapturedInputsTests
{
    private sealed class Tree : IArchiveSelectionTree
    {
        internal Func<string, ArchiveSelectionEntry> Root = name => new(name, ArchiveSelectionKind.Directory, name);
        internal Action<ArchiveSelectionNode, Action<ArchiveSelectionEntry>> Children = (_, _) => { };
        internal Action? FirstInspection;
        internal int Inspections, Captures, Disposals;
        public ArchiveSelectionEntry InspectRoot(string leaf, CancellationToken ct)
        { if (++Inspections == 1) FirstInspection?.Invoke(); return Root(leaf); }
        public void Enumerate(ArchiveSelectionNode directory, Action<ArchiveSelectionEntry> visit, CancellationToken ct) => Children(directory, visit);
        public void CaptureFile(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode file, CancellationToken ct)
        { Captures++; inputs.Capture(id, new Source(id).Open, ct); }
        public void CaptureDirectory(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode directory, CancellationToken ct)
        { Captures++; inputs.CaptureDirectory(id, _ => Folder(id, identity: directory.Entry.Identity, leaf: directory.Entry.Leaf, parent: directory.Parent?.Entry.Identity ?? "pane"), ct); }
        public void Dispose() => Disposals++;
        public void ReviewDestination(string path, string identity, IReadOnlySet<string> selectedDirectories, CancellationToken ct) { }
    }
    [Fact]
    public void Recursive_selection_freezes_all_roots_before_native_work_and_disposes_exclusive_authority()
    {
        string[] roots = ["first", "second"]; var tree = new Tree { FirstInspection = () => roots[1] = "outside" };
        using var selection = ArchiveCapturedSelection.Capture(tree, roots, Ct);
        Assert.Equal(new[] { "first", "second" }, selection.Roots); Assert.Equal(2, tree.Captures);
        var plan = selection.CreatePlan(new(ArchiveWriteFormat.Tar), null, [], false, Ct);
        Assert.Equal(new[] { "first", "second" }, plan.Additions.Select(a => a.Name));
        Assert.Equal(2, plan.Directories.Count); Assert.Empty(plan.Inputs);
        selection.Dispose(); selection.Dispose(); Assert.Equal(1, tree.Disposals);
        Assert.Throws<ObjectDisposedException>(() => selection.CreatePlan(new(ArchiveWriteFormat.Tar), null, [], false, Ct));
    }
    [Fact]
    public void Recursive_exclusions_are_counted_sampled_and_require_explicit_review()
    {
        var tree = new Tree { Children = (_, visit) =>
        {
            for (int i = 0; i < 200; i++) visit(new("link-" + i, ArchiveSelectionKind.Link, "link"));
            visit(new("fifo", ArchiveSelectionKind.Special, "fifo")); visit(new("", ArchiveSelectionKind.UnrepresentableName, ""));
        } };
        using var selection = ArchiveCapturedSelection.Capture(tree, ["folder"], Ct);
        Assert.Equal(202, selection.ExcludedCount); Assert.Equal(200, selection.Excluded(ArchiveSelectionExclusionReason.Link));
        Assert.Equal(128, selection.ExclusionSamples.Count); Assert.Equal(1, tree.Captures);
        Assert.Throws<ArchiveWriteException>(() => selection.CreatePlan(new(ArchiveWriteFormat.Tar), null, [], false, Ct));
        var plan = selection.CreatePlan(new(ArchiveWriteFormat.Tar), null, [], true, Ct); Assert.Single(plan.Additions);
        Assert.True(plan.CaptureCost.HeapBytes > 100_000); ArchiveCapturedInputs.RequirePlanCost(plan, selection.Inputs.Bindings);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wholly_excluded_selections_refuse_an_accidental_empty_archive(bool special)
    {
        var tree = new Tree { Root = name => new(name, special ? ArchiveSelectionKind.Special : ArchiveSelectionKind.Link, name) };
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() => ArchiveCapturedSelection.Capture(tree, ["item"], Ct)).Reason);
        Assert.Equal(0, tree.Captures); Assert.Equal(1, tree.Disposals);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_capture_bounds_depth_and_all_visited_entries_including_exclusions(bool width)
    {
        var tree = new Tree { Children = (node, visit) =>
        {
            if (width) { for (int i = 0; i < ArchiveWritePolicy.MaxMembers; i++) visit(new("link-" + i, ArchiveSelectionKind.Link, "link")); }
            else visit(new("d", ArchiveSelectionKind.Directory, node.Depth.ToString()));
        } };
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => ArchiveCapturedSelection.Capture(tree, ["root"], Ct)).Reason);
        Assert.Equal(1, tree.Disposals); Assert.Equal(width ? 1 : ArchiveCapturedSelection.MaximumDepth, tree.Captures);
    }
    [Fact]
    public void Invalid_or_duplicate_roots_and_cancellation_refuse_before_inspection()
    {
        foreach (string[] roots in new[] { Array.Empty<string>(), ["same", "same"], ["folder/child"], [".."] })
        {
            var tree = new Tree(); Assert.Throws<ArchiveWriteException>(() => ArchiveCapturedSelection.Capture(tree, roots, Ct));
            Assert.Equal(0, tree.Inspections); Assert.Equal(1, tree.Disposals);
        }
        var cancelled = new Tree(); Assert.ThrowsAny<OperationCanceledException>(() => ArchiveCapturedSelection.Capture(cancelled, ["root"], new(true)));
        Assert.Equal(0, cancelled.Inspections); Assert.Equal(1, cancelled.Disposals);
    }
    [Fact]
    public void Recursive_capture_orders_nested_additions_and_charges_path_graph_alongside_bindings()
    {
        var tree = new Tree { Children = (node, visit) =>
        { if (node.Depth == 1) { visit(new("z", ArchiveSelectionKind.Directory, "child-z")); visit(new("a", ArchiveSelectionKind.Directory, "child-a")); } } };
        using var selection = ArchiveCapturedSelection.Capture(tree, ["root"], Ct);
        var plan = selection.CreatePlan(new(ArchiveWriteFormat.Tar), null, [], false, Ct);
        Assert.Equal(new[] { "root", "root/a", "root/z" }, plan.Additions.Select(a => a.Name));
        Assert.Equal(selection.Inputs.Cost, plan.CaptureCost); Assert.Equal(3, plan.Directories.Count);
    }
}
