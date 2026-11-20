using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

internal static class AnalysisReportStatistics
{
    internal static AnalysisLogicalTotals Logical(IEnumerable<AnalysisLogicalTotals> rows)
    {
        UInt128 known = 0; ulong files = 0; var missing = new Dictionary<MeasurementState, ulong>();
        foreach (var row in rows)
        {
            known = checked(known + row.KnownBytes); files = checked(files + row.Files);
            foreach (var pair in row.Missing) missing[pair.Key] = checked(missing.GetValueOrDefault(pair.Key) + pair.Value);
        }
        return new(known, files, missing.ToImmutableDictionary());
    }
    internal static AnalysisTotals Active(IEnumerable<AnalysisTreeItem> rows)
    {
        UInt128 known = 0, unverified = 0; ulong files = 0; bool compatible = true; var missing = new Dictionary<MeasurementState, ulong>();
        foreach (var item in rows)
        {
            var row = item.ReportTotals ?? throw new InvalidOperationException("The captured scope lacks report accounting.");
            known = checked(known + row.Known); unverified = checked(unverified + row.IdentityUnverified); files = checked(files + row.MatchingPaths); compatible &= row.CompatibleBasis;
            foreach (var pair in row.Missing) missing[pair.Key] = checked(missing.GetValueOrDefault(pair.Key) + pair.Value);
        }
        return new(known, unverified, files, missing.ToImmutableDictionary(), compatible);
    }
}
