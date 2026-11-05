using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.App.ViewModels;

public sealed record AnalysisBreadcrumb(string Key, string Label);

public sealed partial class DiskSpaceViewModel
{
    public AnalysisTableSort Sort { get; set; } = AnalysisTableSort.ActiveMetric;
    public bool SortDescending { get; set; } = true;
    private AnalysisOrderedRows? _orderedRows;
    private CancellationTokenSource? _rowOrderCancel;
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
        Selection.Navigate(key); RefreshRows();
    }
}
