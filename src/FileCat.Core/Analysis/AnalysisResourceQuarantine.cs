namespace FileCat.Core.Analysis;

/// <summary>
/// Strong process-lifetime ownership of resources whose close result is unknown. There is no
/// retry/reclaim operation: native identity may already have been reused. Producers are the
/// bounded scratch/handle owners, never decoded inventory records.
/// </summary>
internal sealed class AnalysisResourceQuarantine
{
    internal static readonly AnalysisResourceQuarantine Shared = new();
    private readonly object _gate = new();
    private readonly HashSet<object> _owners = new(ReferenceEqualityComparer.Instance);
    internal int Count { get { lock (_gate) return _owners.Count; } }
    internal void Hold(object owner) { lock (_gate) _owners.Add(owner); }
}
