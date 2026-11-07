using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Focus, marks, navigation and the inert review basket have independent lifetimes.</summary>
public sealed class AnalysisSelectionState
{
    private ImmutableHashSet<ulong> _marks = [], _basket = [];
    private (Guid Session, long Epoch)? _domain;
    private readonly LinkedList<string> _back = [], _forward = [];
    public string Scope { get; private set; } = AnalysisTree.RootKey;
    public string? Focus { get; private set; }
    public string? BackScope => _back.Last?.Value;
    public string? ForwardScope => _forward.Last?.Value;
    public ImmutableHashSet<ulong> Marks => _marks;
    public ImmutableHashSet<ulong> Basket => _basket;
    /// <summary>Revision updates can retain IDs; an epoch/session change cannot silently retag retained references.</summary>
    public void BindIdentity(SessionIdentity identity)
    {
        var domain = (identity.SessionId, identity.Epoch);
        if (_domain is { } prior && prior != domain && (_marks.Count != 0 || _basket.Count != 0))
            throw new InvalidOperationException("Retained marks or basket entries belong to another session or epoch. Review and clear them before changing observations.");
        if (_domain is { } previous && previous != domain) ResetNavigation();
        _domain = domain;
    }
    public void SetFocus(string key) => Focus = key;
    public void ResetNavigation(string scope = AnalysisTree.RootKey)
    { ArgumentNullException.ThrowIfNull(scope); Scope = scope; Focus = null; _back.Clear(); _forward.Clear(); }
    private static void Push(LinkedList<string> history, string scope)
    {
        history.AddLast(scope);
        if (history.Count > AnalysisLimits.NavigationHistory) history.RemoveFirst();
    }
    public void Navigate(string scope) { if (scope == Scope) return; Push(_back, Scope); _forward.Clear(); Scope = scope; }
    public void Back() { if (_back.Last is { } previous) { _back.RemoveLast(); Push(_forward, Scope); Scope = previous.Value; } }
    public void Forward() { if (_forward.Last is { } next) { _forward.RemoveLast(); Push(_back, Scope); Scope = next.Value; } }
    public void ToggleMark(ulong id)
    {
        if (id == 0) throw new ArgumentException("A real observation id is required.", nameof(id));
        if (_marks.Contains(id)) _marks = _marks.Remove(id);
        else if (_marks.Count >= AnalysisLimits.ReviewPaths) throw new InvalidOperationException("Refine the marked selection to at most 100,000 paths.");
        else _marks = _marks.Add(id);
    }
    public void MarkCaptured(IEnumerable<ulong> ids)
    {
        _marks = CaptureBounded(_marks, ids, "marked selection");
    }
    /// <summary>Replace an active range over a retained bounded baseline, without partial changes on refusal.</summary>
    public void MarkRangeCaptured(ImmutableHashSet<ulong> baseline, IEnumerable<ulong> ids)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.Contains(0) || baseline.Count > AnalysisLimits.ReviewPaths) throw new ArgumentException("A bounded set of real observation IDs is required.", nameof(baseline));
        _marks = CaptureBounded(baseline, ids, "marked selection");
    }
    public void ClearMarks() => _marks = [];
    public void StageCaptured(IEnumerable<ulong> ids)
    {
        _basket = CaptureBounded(_basket, ids, "review basket");
    }
    private static ImmutableHashSet<ulong> CaptureBounded(ImmutableHashSet<ulong> prior, IEnumerable<ulong> ids, string kind)
    {
        var result = prior.ToBuilder();
        foreach (ulong id in ids)
        {
            if (id == 0) throw new ArgumentException("A real observation id is required.", nameof(ids));
            result.Add(id);
            if (result.Count > AnalysisLimits.ReviewPaths) throw new InvalidOperationException($"Refine the {kind} to at most 100,000 paths.");
        }
        return result.ToImmutable();
    }
    public void RemoveFromBasket(ulong id) => _basket = _basket.Remove(id);
    public void ClearBasket() => _basket = [];
    public AnalysisSelection Capture(AnalysisSnapshot snapshot)
    {
        BindIdentity(snapshot.Identity);
        return new(snapshot.Identity.SessionId, snapshot.Identity.Epoch, snapshot.Identity.Revision, _marks.Order().ToImmutableArray());
    }
}
