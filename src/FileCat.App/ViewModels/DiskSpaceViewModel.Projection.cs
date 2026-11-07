using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    private static AnalysisTree BuildCapturedTree(AnalysisSnapshot s, AnalysisSavedView view, TimeZoneInfo? zone, CancellationToken token)
    {
        string query = view.Query; string? root = view.Root; var metric = view.Metric; var grouping = view.Grouping;
        bool sensitive = view.CaseSensitive, showUnknown = view.ShowUnknown;
        var rootScopes = new AnalysisRootScopeIndex(s, token);
        var filter = AnalysisFilter.Parse(query, zone ?? TimeZoneInfo.Utc, sensitive, roots: s.Roots, rootScopes: rootScopes, allowDateOnly: zone is not null,
            rootDisplayIds: view.RootDisplayIds);
        var nodes = s.Nodes.ToDictionary(n => n.Id);
        var pathTextAvailable = new Dictionary<ulong, bool>();
        foreach (var node in s.Nodes)
        {
            token.ThrowIfCancellationRequested();
            pathTextAvailable.Add(node.Id, node.ParentId == 0 || pathTextAvailable[node.ParentId] && AnalysisRawNames.HasLosslessText(node));
        }
        var all = AnalysisAggregate.Project(s, metric, cancellation: token);
        var allocation = s.Facts.Where(f => f.ObjectId is not null).ToLookup(f => f.ObjectId!, StringComparer.Ordinal);
        var unknown = ImmutableHashSet.CreateBuilder<ulong>();
        AnalysisMeasurement Allocation(AnalysisObject? obj)
        {
            if (obj is null) return AnalysisMeasurement.Missing(MeasurementState.Unsupported);
            return AnalysisAggregate.CompatibleAllocation(allocation[obj.Id]);
        }
        bool Matches(AnalysisNode n, AnalysisObject? obj)
        {
            var active = all.UnmeasuredStates.GetValueOrDefault(n.Id, metric == AnalysisMetric.UniqueFileSize && obj?.VerifiedIdentity is null ? MeasurementState.Unsupported : MeasurementState.Known);
            var truth = filter.Evaluate(new(n, obj, RelativePath(n, nodes), Allocation(obj), Stale: s.Freshness != AnalysisFreshness.Observed,
                ActiveState: active, RelativePathTextAvailable: pathTextAvailable[n.Id]));
            if (root is not null) truth = AnalysisFilter.And(rootScopes.IsMember(root, n), truth);
            if (truth == AnalysisTruth.Unknown) unknown.Add(n.Id);
            return showUnknown ? truth == AnalysisTruth.Unknown : truth == AnalysisTruth.True;
        }
        bool filtered = !string.IsNullOrWhiteSpace(query) || root is not null;
        // A location selector is a namespace scope, not a file predicate. Keep that scope's
        // independent directory allocation; name/type/age predicates do not match non-file facts.
        Func<AnalysisNode, bool>? directoryFilter = filtered && filter.IsNamespaceOnly && !showUnknown
            ? n => Matches(n, null) : null;
        var projection = filtered ? AnalysisAggregate.Project(s, metric, Matches, token, directoryFilter) : all;
        projection = projection with { UnknownFilterNodes = unknown.ToImmutable(), IncludesUnknownFilterResults = filtered && showUnknown, FilterText = query };
        return grouping == AnalysisGroupingDimension.FolderHierarchy ? AnalysisTree.Build(s, projection, filtered, token, directoryFilter)
            : AnalysisTree.BuildGrouped(s, projection, grouping, filtered, token, directoryFilter);
    }
}
