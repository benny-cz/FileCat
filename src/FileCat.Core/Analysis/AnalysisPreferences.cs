namespace FileCat.Core.Analysis;

/// <summary>Diagnostic-safe display preferences and finite limits. Private scope, query and inventory data belong elsewhere.</summary>
public sealed record AnalysisPreferences
{
    public AnalysisRollingHistoryPolicy RollingHistory { get; init; } = AnalysisRollingHistoryPolicy.Default;
}
