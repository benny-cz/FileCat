using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed record AnalysisReportFilterContext(string Query, bool? CaseSensitive, bool UnknownOnly, bool? RootScoped);
/// <summary>Captured comparison captions only; identities are checked against the chart and never grant source authority.</summary>
public sealed record AnalysisComparisonChartContext(SessionIdentity Previous, SessionIdentity Current, bool PreviousSide,
    NamespaceCoverage PreviousCoverage, NamespaceCoverage CurrentCoverage, AnalysisFreshness PreviousFreshness,
    AnalysisFreshness CurrentFreshness, AnalysisTaint Taint, AnalysisComparisonCompatibility Compatibility, bool Structural);
public sealed record AnalysisReportOrder(AnalysisTableSort Sort, bool Descending)
{
    internal static AnalysisReportOrder Folder(AnalysisHierarchySort sort, bool descending) => new(sort switch
    {
        AnalysisHierarchySort.Metric => AnalysisTableSort.ActiveMetric, AnalysisHierarchySort.Files => AnalysisTableSort.FilePaths,
        AnalysisHierarchySort.FileSize => AnalysisTableSort.FileSize, AnalysisHierarchySort.Name => AnalysisTableSort.Name,
        AnalysisHierarchySort.MetadataState => AnalysisTableSort.MeasurementState, _ => throw new ArgumentOutOfRangeException(nameof(sort))
    }, descending);
    internal string Describe() => (Sort switch { AnalysisTableSort.ActiveMetric => "Active metric", AnalysisTableSort.Name => "Name", AnalysisTableSort.FileSize => "File size",
        AnalysisTableSort.FilePaths => "File paths", _ => "Measurement state" }) + (Descending ? ", descending" : ", ascending");
}
public sealed record AnalysisReportBases(ImmutableArray<string> Labels, bool DetailLimited)
{
    internal static AnalysisReportBases Capture(IEnumerable<string> source)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal); int characters = 0; bool limited = false;
        foreach (string label in source)
        {
            if (labels.Contains(label)) continue;
            if (label.Length > 256) { limited = true; continue; }
            if (labels.Count == AnalysisLimits.Components || label.Length > 65536 - characters) { limited = true; break; }
            labels.Add(label); characters += label.Length;
        }
        return new(labels.Order(StringComparer.Ordinal).ToImmutableArray(), limited);
    }
}
