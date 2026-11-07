using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisTree
{
    public AnalysisSelectionVisibility ClassifyMarks(ImmutableHashSet<ulong> marks, string scope = RootKey, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var counter = new AnalysisMarkCounter(marks);
        var inScope = new HashSet<ulong>();
        if (marks.Count != 0) foreach (ulong id in ScopeIds(scope, cancellation)) if (marks.Contains(id)) inScope.Add(id);
        while (counter.Next(out ulong id))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Items.TryGetValue(Key(id), out var item)) counter.Count(AnalysisMarkPlace.Missing);
            else if (FilterApplied && (GroupingDimension == AnalysisGroupingDimension.FolderHierarchy ? !item.Visible : FilterVisible?.Contains(id) != true))
                counter.Count(AnalysisMarkPlace.HiddenByFilter);
            else counter.Count(inScope.Contains(id) ? AnalysisMarkPlace.InScope : AnalysisMarkPlace.Elsewhere);
        }
        return counter.Result(Snapshot.Identity);
    }

    /// <summary>Only regular-file matches in this captured scope. Does not select future entries or context folders.</summary>
    public AnalysisSelection CaptureMatchingFiles(string scope = RootKey, CancellationToken cancellation = default)
    {
        var ids = new HashSet<ulong>();
        foreach (ulong id in ScopeIds(scope, cancellation))
        {
            if (!Projection.MatchingNodes.Contains(id) || Items[Key(id)] is not { Kind: AnalysisKind.File, IncludedByPolicy: true }) continue;
            if (ids.Add(id) && ids.Count > AnalysisLimits.ReviewPaths)
                throw new InvalidOperationException("Refine this scope to at most 100,000 matching file paths. No matches were added.");
        }
        var identity = Snapshot.Identity;
        return new(identity.SessionId, identity.Epoch, identity.Revision, ids.Order().ToImmutableArray())
        { Kind = AnalysisSelectionKind.VisibleMatches, ScopeSummary = "Matching files in captured scope: " + NameOf(scope), FilterSummary = Projection.FilterText +
            (Projection.IncludesUnknownFilterResults ? " · uncertain results" : "") };
    }

    private IEnumerable<ulong> ScopeIds(string scope, CancellationToken cancellation)
    {
        // Count/capture from explicit display membership; group parentage is not namespace ancestry.
        var pending = new Stack<string>(); pending.Push(scope);
        while (pending.TryPop(out string? key))
        {
            cancellation.ThrowIfCancellationRequested();
            if (Other(key, out _, out _)) { foreach (var child in ChildrenOf(key)) pending.Push(child.Key); continue; }
            if (!Items.TryGetValue(key, out var item)) throw new InvalidOperationException("That captured scope is absent from this revision.");
            if (item.NodeId is { } id) yield return id;
            foreach (ulong member in item.Members) { cancellation.ThrowIfCancellationRequested(); yield return member; }
            foreach (string child in item.Children) pending.Push(child);
        }
    }

    private static ImmutableHashSet<ulong> CaptureFilterVisibility(AnalysisSnapshot snapshot, AnalysisProjection projection,
        Func<AnalysisNode, bool>? namespaceVisible, CancellationToken cancellation)
    {
        var visible = projection.MatchingNodes.ToBuilder();
        foreach (var node in snapshot.Nodes)
        {
            cancellation.ThrowIfCancellationRequested();
            if (node.Kind == AnalysisKind.Directory && node.IncludedByPolicy && namespaceVisible?.Invoke(node) == true) visible.Add(node.Id);
        }
        foreach (var charge in projection.Charges) if (charge.DirectorySelf) visible.Add(charge.ParentId);
        foreach (var charge in projection.MissingCharges) if (charge.DirectoryId is { } id) visible.Add(id);
        for (int at = snapshot.Nodes.Length - 1; at >= 0; at--)
        {
            cancellation.ThrowIfCancellationRequested(); var node = snapshot.Nodes[at];
            if (node.ParentId != 0 && visible.Contains(node.Id)) visible.Add(node.ParentId);
        }
        return visible.ToImmutable();
    }
}
