namespace FileCat.Core.Analysis;

/// <summary>A recorded directory component used only to check a fresh native observation.</summary>
internal sealed record AnalysisBranchComponent(string Name, string Identity, DateTimeOffset CreatedUtc, ulong CreatedPrecisionTicks)
{
    // Windows retains the opened chain while rebinding. Bound it to the same
    // depth as the native traversal so a refresh cannot exceed its handle budget.
    internal const int MaximumDepth = 48;

    internal static void Validate(IReadOnlyList<AnalysisBranchComponent> components, bool windows)
    {
        if (components.Count is < 1 or > MaximumDepth)
            throw new NotSupportedException("This branch is too deep for a qualified refresh.");

        int characters = 0;
        foreach (var component in components)
        {
            if (!AnalysisNames.IsOrdinaryChild(component.Name, windows) || component.CreatedPrecisionTicks == 0 ||
                string.IsNullOrEmpty(component.Identity))
                throw new NotSupportedException("This branch has no qualified native folder binding.");
            characters += component.Name.Length + 1;
            if (characters > 8192) throw new NotSupportedException("This branch exceeds the native refresh path limit.");
        }
    }

    internal void RequireMatch(AnalysisEntry observed)
    {
        if (observed.Kind != AnalysisKind.Directory || observed.Cloud == true || observed.MetadataState != MeasurementState.Known ||
            observed.Identity != Identity || observed.CreatedUtc != CreatedUtc || observed.CreatedPrecisionTicks != CreatedPrecisionTicks)
            throw new AnalysisSourceChangedException();
    }
}
