namespace FileCat.Core.Analysis;

/// <summary>Shared accounting-row selection and ranking for the comparison table and captured reports.</summary>
public static class AnalysisComparisonProjection
{
    public static IEnumerable<AnalysisComparisonRow> Apply(IEnumerable<AnalysisComparisonRow> rows,
        AnalysisComparisonCohort cohort, AnalysisComparisonOrder order)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!Enum.IsDefined(cohort) || !Enum.IsDefined(order)) throw new ArgumentOutOfRangeException(nameof(cohort));
        rows = rows.Where(r => InCohort(r.Change, cohort));
        return order switch
        {
            AnalysisComparisonOrder.RelativeChange => rows.OrderByDescending(r => r.RelativePercent.HasValue).ThenByDescending(r => Math.Abs(r.RelativePercent ?? 0)),
            AnalysisComparisonOrder.CurrentAmount => rows.OrderByDescending(r => r.Current?.Value.IsKnown == true).ThenByDescending(r => r.Current?.Value.Value ?? 0),
            AnalysisComparisonOrder.Path => rows.OrderBy(r => r.Current?.Paths[0] ?? r.Previous!.Paths[0], StringComparer.Ordinal),
            _ => rows.OrderByDescending(r => r.Delta?.Magnitude ?? 0)
        };
    }
    internal static bool InCohort(AnalysisChange change, AnalysisComparisonCohort cohort) => cohort switch
    {
        AnalysisComparisonCohort.All => true,
        AnalysisComparisonCohort.Unknown => change is AnalysisChange.Unknown or AnalysisChange.PreviouslyUnknown,
        _ => change == (cohort switch
        {
            AnalysisComparisonCohort.Growing => AnalysisChange.Growing, AnalysisComparisonCohort.Shrinking => AnalysisChange.Shrinking,
            AnalysisComparisonCohort.Added => AnalysisChange.Added, AnalysisComparisonCohort.Removed => AnalysisChange.Removed,
            AnalysisComparisonCohort.Moved => AnalysisChange.Moved, AnalysisComparisonCohort.Attribution => AnalysisChange.Attribution,
            AnalysisComparisonCohort.Replaced => AnalysisChange.Replaced, AnalysisComparisonCohort.MetadataChanged => AnalysisChange.MetadataChanged,
            AnalysisComparisonCohort.NoObservedMetadataChange => AnalysisChange.NoObservedMetadataChange, _ => throw new ArgumentOutOfRangeException(nameof(cohort))
        })
    };
}
