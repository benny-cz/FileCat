using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Focus, marks, navigation and the inert review basket have independent lifetimes.</summary>
public sealed class AnalysisSelectionState
{
    private ImmutableHashSet<ulong> _marks = [], _basket = [];
    private readonly Stack<string> _back = [], _forward = [];
    public string Scope { get; private set; } = AnalysisTree.RootKey;
    public string? Focus { get; private set; }
    public ImmutableHashSet<ulong> Marks => _marks;
    public ImmutableHashSet<ulong> Basket => _basket;
    public void SetFocus(string key) => Focus = key;
    public void Navigate(string scope) { if (scope == Scope) return; _back.Push(Scope); _forward.Clear(); Scope = scope; }
    public void Back() { if (_back.TryPop(out string? previous)) { _forward.Push(Scope); Scope = previous; } }
    public void Forward() { if (_forward.TryPop(out string? next)) { _back.Push(Scope); Scope = next; } }
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
    public AnalysisSelection Capture(AnalysisSnapshot snapshot) => new(snapshot.Identity.SessionId, snapshot.Identity.Epoch, snapshot.Identity.Revision, _marks.Order().ToImmutableArray());
}
