using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Counts recorded facts, not an assertion that every physical component has been measured.</summary>
public sealed record AnalysisComponentFacts(string ComponentId, string BasisId,
    ImmutableDictionary<MeasurementState, ulong> States);

public static class AnalysisComponents
{
    public const string UnmeasuredExplanation = "Separate stream, fork and xattr coverage is unqualified. Reported facts may already include some components; no extra allocation is inferred.";

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
            $" · recorded component facts: {known:N0} known, {unavailable:N0} unavailable. " + UnmeasuredExplanation;
    }
}
