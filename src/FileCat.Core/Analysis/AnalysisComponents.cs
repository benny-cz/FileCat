using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Counts recorded facts, not an assertion that every physical component has been measured.</summary>
public sealed record AnalysisComponentFacts(string ComponentId, string BasisId,
    ImmutableDictionary<MeasurementState, ulong> States);

public static class AnalysisComponents
{
    internal const string NamedStreamComponent = "named-data-streams", NamedStreamBasis = "ntfs-named-stream-allocation-v1";
    internal const string NamedStreamMethod = "NTFS FileStreamInformation named data stream allocation";
    internal const string ResourceForkComponent = "resource-fork", ResourceForkBasis = "apfs-resource-fork-allocation-v1";
    internal const string ResourceForkMethod = "APFS fgetattrlist resource fork allocation";
    internal const string DarwinTotalAllocationBasis = "darwin-stat-total-blocks-v1", LinuxAllocationBasis = "linux-statx-blocks-v1";
    internal const string WindowsPrimaryAllocationBasis = "windows-primary-allocation-v1";
    public const string UnmeasuredExplanation = "Detailed scans can record named streams on NTFS and resource forks on APFS. Separate extended attribute allocation is unavailable.";

    internal static bool IsExtraAllocation(StorageFact fact) =>
        fact.ObjectId is not null && fact.Includes.IsEmpty &&
        (fact is { ComponentId: NamedStreamComponent, BasisId: NamedStreamBasis, Method: NamedStreamMethod } ||
         fact is { ComponentId: ResourceForkComponent, BasisId: ResourceForkBasis, Method: ResourceForkMethod });
    internal static bool ExtraAllocationRequested(ScanPolicy policy) =>
        policy.ComponentPreset == "detailed" && (policy.BasisIds.Contains(NamedStreamBasis) || policy.BasisIds.Contains(ResourceForkBasis));

    public static ImmutableArray<AnalysisComponentFacts> Summarize(AnalysisSnapshot snapshot) => snapshot.Facts
        .GroupBy(f => (f.ComponentId, f.BasisId)).OrderBy(g => g.Key.ComponentId, StringComparer.Ordinal).ThenBy(g => g.Key.BasisId, StringComparer.Ordinal)
        .Select(g => new AnalysisComponentFacts(g.Key.ComponentId, g.Key.BasisId,
            g.GroupBy(f => f.Measurement.State).ToImmutableDictionary(states => states.Key, states => (ulong)states.Count()))).ToImmutableArray();

    public static string Describe(AnalysisSnapshot snapshot)
    {
        ulong known = 0, unavailable = 0;
        foreach (var component in Summarize(snapshot))
        foreach (var state in component.States)
            if (state.Key == MeasurementState.Known) known = checked(known + state.Value); else unavailable = checked(unavailable + state.Value);
        return (snapshot.Policy.ComponentPreset == "detailed" ? "Detailed allocation" : "Standard allocation") +
            $" · recorded component facts: {known:N0} known, {unavailable:N0} unavailable. " +
            (snapshot.Policy.ComponentPreset == "detailed" ? UnmeasuredExplanation : "Named streams and resource forks are not separately measured.");
    }
}
