using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Disjoint counts for retained marks in one captured query and scope. These counts grant no source authority.</summary>
public sealed record AnalysisSelectionVisibility(SessionIdentity Identity, int Total, int InScope, int Elsewhere, int HiddenByFilter, int Missing);

internal enum AnalysisMarkPlace { InScope, Elsewhere, HiddenByFilter, Missing }

internal sealed class AnalysisMarkCounter : IDisposable
{
    private ImmutableHashSet<ulong>.Enumerator _ids;
    private readonly int[] _counts = new int[4];
    internal readonly int Total;
    internal bool Complete { get; private set; }
    internal AnalysisMarkCounter(ImmutableHashSet<ulong> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count > AnalysisLimits.ReviewPaths || ids.Contains(0)) throw new ArgumentException("A bounded set of real captured path IDs is required.", nameof(ids));
        Total = ids.Count; _ids = ids.GetEnumerator();
    }
    internal bool Next(out ulong id)
    {
        if (_ids.MoveNext()) { id = _ids.Current; return true; }
        Complete = true; id = 0; return false;
    }
    internal void Count(AnalysisMarkPlace place) => _counts[(int)place]++;
    internal AnalysisSelectionVisibility Result(SessionIdentity identity)
    {
        if (!Complete || _counts.Sum() != Total) throw new InvalidOperationException("The captured mark counts are incomplete.");
        return new(identity, Total, _counts[0], _counts[1], _counts[2], _counts[3]);
    }
    public void Dispose() => _ids.Dispose();
}
