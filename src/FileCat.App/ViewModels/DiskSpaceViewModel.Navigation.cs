using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.App.ViewModels;

public sealed record AnalysisBreadcrumb(string Key, string Label);

public sealed partial class DiskSpaceViewModel
{
    private sealed record HierarchyTarget(Guid Session, long Epoch, ulong Path);
    private HierarchyTarget? _hierarchyTarget;
    public Task ShowFocusedInHierarchyAsync()
    {
        if (_disposed || Tree is not { } tree || tree.Items.GetValueOrDefault(Selection.Focus ?? "")?.NodeId is not { } path)
            return Task.CompletedTask;
        _grouping = AnalysisGroupingDimension.FolderHierarchy;
        _hierarchyTarget = new(tree.Snapshot.Identity.SessionId, tree.Snapshot.Identity.Epoch, path);
        return RebuildAsync(true);
    }
    private static string ProjectionScope(AnalysisTree tree, string requested, AnalysisGroupingDimension? previous, HierarchyTarget? target)
    {
        if (tree.GroupingDimension == AnalysisGroupingDimension.FolderHierarchy && target is not null && HierarchyFocus(tree, target) is not null)
        {
            var node = tree.Snapshot.Nodes.First(node => node.Id == target.Path);
            return node.Kind == AnalysisKind.Directory ? AnalysisTree.Key(node.Id) : node.ParentId == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(node.ParentId);
        }
        if (tree.GroupingDimension == AnalysisGroupingDimension.FolderHierarchy && target is not null) return AnalysisTree.RootKey;
        if (previous is not null && previous != tree.GroupingDimension || tree.GroupingDimension != AnalysisGroupingDimension.FolderHierarchy && !tree.CanExplore(requested))
            return AnalysisTree.RootKey;
        return requested;
    }
    private static string? HierarchyFocus(AnalysisTree tree, HierarchyTarget? target) => target is not null &&
        tree.GroupingDimension == AnalysisGroupingDimension.FolderHierarchy && tree.Snapshot.Identity.SessionId == target.Session && tree.Snapshot.Identity.Epoch == target.Epoch &&
        tree.Snapshot.Nodes.Any(node => node.Id == target.Path) ? AnalysisTree.Key(target.Path) : null;
    public AnalysisTableSort Sort { get; set; } = AnalysisTableSort.ActiveMetric;
    public bool SortDescending { get; set; } = true;
    private AnalysisOrderedRows? _orderedRows;
    private CancellationTokenSource? _rowOrderCancel;
    private readonly Func<ImmutableArray<AnalysisTreeItem>, AnalysisTableSort, bool, CancellationToken, AnalysisOrderedRows> _orderRows = AnalysisTableOrder.Apply;
    internal DiskSpaceViewModel(AnalysisSnapshot observations,
        Func<ImmutableArray<AnalysisTreeItem>, AnalysisTableSort, bool, CancellationToken, AnalysisOrderedRows> orderRows) : this(observations)
    { _orderRows = orderRows ?? throw new ArgumentNullException(nameof(orderRows)); }
    public ImmutableArray<AnalysisBreadcrumb> Breadcrumbs
    {
        get
        {
            if (Tree is not { } tree) return [];
            var trail = new Stack<AnalysisBreadcrumb>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            string key = Selection.Scope;
            while (seen.Add(key))
            {
                if (trail.Count >= AnalysisLimits.Depth + 1) break;
                trail.Push(new(key, tree.NameOf(key)));
                if (key == AnalysisTree.RootKey) return trail.ToImmutableArray();
                key = tree.ParentOf(key);
            }
            // Very deep synthetic Other chains retain a root exit and the nearest bounded ancestry.
            trail.Push(new(AnalysisTree.RootKey, tree.NameOf(AnalysisTree.RootKey)));
            return trail.ToImmutableArray();
        }
    }
    public void NavigateToScope(string key)
    {
        if (_disposed || Tree is not { } tree || !Breadcrumbs.Any(part => part.Key == key)) return;
        NavigateRows(key, () => Selection.Navigate(key));
    }
}
