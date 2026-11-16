namespace FileCat.Core.Analysis;

/// <summary>Shared accounting-row selection and ranking for the comparison table and captured reports.</summary>
public static class AnalysisComparisonProjection
{
    public static IEnumerable<AnalysisComparisonRow> Apply(IEnumerable<AnalysisComparisonRow> rows,
        AnalysisComparisonCohort cohort, AnalysisComparisonOrder order)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!Enum.IsDefined(cohort) || !Enum.IsDefined(order)) throw new ArgumentOutOfRangeException(nameof(cohort));
        rows = cohort switch
        {
            AnalysisComparisonCohort.Growing => rows.Where(r => r.Change == AnalysisChange.Growing),
            AnalysisComparisonCohort.Shrinking => rows.Where(r => r.Change == AnalysisChange.Shrinking),
            AnalysisComparisonCohort.Added => rows.Where(r => r.Change == AnalysisChange.Added),
            AnalysisComparisonCohort.Removed => rows.Where(r => r.Change == AnalysisChange.Removed),
            AnalysisComparisonCohort.Moved => rows.Where(r => r.Change == AnalysisChange.Moved),
            AnalysisComparisonCohort.Attribution => rows.Where(r => r.Change == AnalysisChange.Attribution),
            AnalysisComparisonCohort.Replaced => rows.Where(r => r.Change == AnalysisChange.Replaced),
            AnalysisComparisonCohort.MetadataChanged => rows.Where(r => r.Change == AnalysisChange.MetadataChanged),
            AnalysisComparisonCohort.NoObservedMetadataChange => rows.Where(r => r.Change == AnalysisChange.NoObservedMetadataChange),
            AnalysisComparisonCohort.Unknown => rows.Where(r => r.Change is AnalysisChange.Unknown or AnalysisChange.PreviouslyUnknown),
            _ => rows
        };
        return order switch
        {
            AnalysisComparisonOrder.RelativeChange => rows.OrderByDescending(r => r.RelativePercent.HasValue).ThenByDescending(r => Math.Abs(r.RelativePercent ?? 0)),
            AnalysisComparisonOrder.CurrentAmount => rows.OrderByDescending(r => r.Current?.Value.IsKnown == true).ThenByDescending(r => r.Current?.Value.Value ?? 0),
            AnalysisComparisonOrder.Path => rows.OrderBy(r => r.Current?.Paths[0] ?? r.Previous!.Paths[0], StringComparer.Ordinal),
            _ => rows.OrderByDescending(r => r.Delta?.Magnitude ?? 0)
        };
    }
}
