using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Owns two validated private indexed imports; saved locators remain inert.</summary>
public sealed class AnalysisSavedIndexedComparison : IAsyncDisposable
{
    private readonly object _gate = new();
    private Task? _close;
    public Guid ComparisonId { get; }
    public AnalysisIndexedInventory Previous { get; }
    public AnalysisIndexedInventory Current { get; }
    public AnalysisComparisonView View { get; }
    public ImmutableDictionary<string, string>? StructuralRoots { get; }
    public string PreviousLabel { get; }
    public DateTimeOffset SavedUtc { get; }
    public AnalysisTaint Taint { get; }
    public ImmutableDictionary<string, string> PreviousRootDisplayIds { get; }
    public ImmutableDictionary<string, string> CurrentRootDisplayIds { get; }
    internal AnalysisSavedIndexedComparison(Guid id, AnalysisIndexedInventory previous, AnalysisIndexedInventory current,
        AnalysisComparisonView view, ImmutableDictionary<string, string>? roots, string label, DateTimeOffset saved,
        AnalysisTaint taint, ImmutableDictionary<string, string> previousRootIds, ImmutableDictionary<string, string> currentRootIds)
    {
        ComparisonId = id; Previous = previous; Current = current; View = view; StructuralRoots = roots;
        PreviousLabel = label; SavedUtc = saved; Taint = AnalysisPrivacy.Join(taint, previous.Descriptor.Taint, current.Descriptor.Taint);
        PreviousRootDisplayIds = previousRootIds; CurrentRootDisplayIds = currentRootIds;
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_close ??= AnalysisIndexedComparison.CloseAsync(Current, Previous));
    }
}
