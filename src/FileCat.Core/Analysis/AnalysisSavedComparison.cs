using System.Collections.Immutable;
using System.Text;

namespace FileCat.Core.Analysis;

public enum AnalysisComparisonCohort { All, Growing, Shrinking, Added, Removed, Moved, Attribution, Replaced, MetadataChanged, NoObservedMetadataChange, Unknown }
public enum AnalysisComparisonOrder { AbsoluteDifference, RelativeChange, CurrentAmount, Path }
public enum AnalysisComparisonChartMode { Table, Treemap, Sunburst }

/// <summary>Only applied display state. No field binds a source, invokes a provider or restores approval.</summary>
public sealed record AnalysisComparisonView
{
    public AnalysisMetric Metric { get; init; } = AnalysisMetric.FileSize;
    public AnalysisComparisonCohort Cohort { get; init; }
    public AnalysisComparisonOrder Order { get; init; }
    public string Query { get; init; } = "";
    public bool CaseSensitive { get; init; }
    public AnalysisComparisonSide QuerySide { get; init; }
    public bool ShowUnknown { get; init; }
    public AnalysisComparisonChartMode Chart { get; init; }
    public bool PreviousChart { get; init; }
    public string ChartScope { get; init; } = AnalysisTree.RootKey;
    public ulong FocusNode { get; init; }
    public bool PreviousFocus { get; init; }

    public static void Validate(AnalysisComparisonView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!Enum.IsDefined(view.Metric) || !Enum.IsDefined(view.Cohort) || !Enum.IsDefined(view.Order) || !Enum.IsDefined(view.QuerySide) || !Enum.IsDefined(view.Chart) ||
            view.Query is null || view.ChartScope is null || !AnalysisPrivateInventoryStore.WellFormedText(view.Query) || !AnalysisPrivateInventoryStore.WellFormedText(view.ChartScope) ||
            Encoding.UTF8.GetByteCount(view.Query) > AnalysisLimits.QueryBytes || Encoding.UTF8.GetByteCount(view.ChartScope) > 256 ||
            view.ShowUnknown && string.IsNullOrWhiteSpace(view.Query)) throw new InvalidDataException("Unsupported saved comparison view.");
        string scope = AnalysisSavedCatalog.SplitAggregationScope(view.ChartScope).Scope;
        if (scope == AnalysisTree.RootKey) return;
        if (scope.StartsWith("shared:", StringComparison.Ordinal)) scope = scope[7..];
        else if (scope.StartsWith("directory:", StringComparison.Ordinal)) scope = scope[10..];
        if (scope == AnalysisTree.RootKey) return;
        if (!scope.StartsWith("n:", StringComparison.Ordinal) || !ulong.TryParse(scope.AsSpan(2), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out ulong id) || id == 0 || scope != AnalysisTree.Key(id))
            throw new InvalidDataException("Unsupported saved comparison chart scope.");
    }
}

public sealed record AnalysisComparisonReceipt(Guid ComparisonId, string Name, long Bytes);

/// <summary>Owns both strict inert imports. The bundle is metadata, never current file-operation authority.</summary>
public sealed class AnalysisSavedComparison : IDisposable
{
    public Guid ComparisonId { get; }
    public AnalysisInventory Previous { get; }
    public AnalysisInventory Current { get; }
    public AnalysisComparisonView View { get; }
    public ImmutableDictionary<string, string>? StructuralRoots { get; }
    public string PreviousLabel { get; }
    public DateTimeOffset SavedUtc { get; }
    public AnalysisTaint Taint { get; }
    public ImmutableDictionary<string, string> PreviousRootDisplayIds { get; }
    public ImmutableDictionary<string, string> CurrentRootDisplayIds { get; }
    internal AnalysisSavedComparison(Guid id, AnalysisInventory previous, AnalysisInventory current, AnalysisComparisonView view,
        ImmutableDictionary<string, string>? roots, string label, DateTimeOffset saved, AnalysisTaint taint,
        ImmutableDictionary<string, string> previousRootDisplayIds, ImmutableDictionary<string, string> currentRootDisplayIds)
    {
        ComparisonId = id; Previous = previous; Current = current; View = view; StructuralRoots = roots; PreviousLabel = label; SavedUtc = saved;
        Taint = AnalysisPrivacy.Join(taint, AnalysisPrivacy.Join(previous.Snapshot.Taint, current.Snapshot.Taint));
        PreviousRootDisplayIds = previousRootDisplayIds; CurrentRootDisplayIds = currentRootDisplayIds;
    }
    public void Dispose() { try { Previous.Dispose(); } finally { Current.Dispose(); } }
}
