using System.Globalization;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisSavedCatalog
{
    /// <summary>Remaps display-only root references to the inventory wire namespace exactly once.</summary>
    public static AnalysisSavedView ViewForInventory(AnalysisSnapshot snapshot, AnalysisSavedView view)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(view);
        return ViewForInventory(snapshot.Roots, view);
    }
    public static AnalysisSavedView ViewForInventory(AnalysisDatasetDescriptor dataset, AnalysisSavedView view)
    {
        ArgumentNullException.ThrowIfNull(dataset); ArgumentNullException.ThrowIfNull(view);
        return ViewForInventory(dataset.Roots, view);
    }
    private static AnalysisSavedView ViewForInventory(System.Collections.Immutable.ImmutableArray<RootBinding> roots, AnalysisSavedView view)
    {
        if (view.Scope is null || view.Scope.Length > 4096) throw Invalid("The runtime aggregation scope is too long.");
        var aggregate = SplitAggregationScope(view.Scope);
        view = view with { Scope = aggregate.Skipped == 0 ? aggregate.Scope : AnalysisTree.OtherKey(aggregate.Scope, aggregate.Skipped) };
        ValidateView(view);
        if (view.RootDisplayIds is { } existing && (existing.Count != roots.Length || roots.Any(r => !existing.ContainsKey(r.Id))))
            throw Invalid("The saved root identifiers do not match the captured locations.");
        var ids = System.Collections.Immutable.ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        string? root = view.Root; string scope = aggregate.Scope;
        for (int index = 0; index < roots.Length; index++)
        {
            string original = roots[index].Id, wire = (index + 1).ToString(CultureInfo.InvariantCulture);
            ids.Add(wire, view.RootDisplayIds?.GetValueOrDefault(original) ?? original);
            if (view.Root == original) root = wire;
            if (view.Grouping == AnalysisGroupingDimension.CapturedLocation && aggregate.Scope == LocationKey(original)) scope = LocationKey(wire);
        }
        var result = view with { Root = root, Scope = aggregate.Skipped == 0 ? scope : AnalysisTree.OtherKey(scope, aggregate.Skipped), RootDisplayIds = new(ids) };
        ValidateView(result); return result;
    }
    private static string LocationKey(string root) => AnalysisGroupingRules.Create("location:" + root, "", "folder").Key;
    public static bool IsGroupScope(string scope) => scope.Length == 72 && scope.StartsWith("group:1:", StringComparison.Ordinal) &&
        scope.AsSpan(8).IndexOfAnyExcept("0123456789abcdef".AsSpan()) < 0;
    public static (string Scope, int Skipped) SplitAggregationScope(string key)
    {
        ArgumentNullException.ThrowIfNull(key); int skip = 0, depth = 0;
        while (AnalysisTree.Other(key, out int amount, out string parent))
        {
            if (++depth > AnalysisLimits.NavigationHistory) throw Invalid("The saved aggregation scope is too deep.");
            skip = checked(skip + amount); key = parent;
        }
        return (key, skip);
    }
}
