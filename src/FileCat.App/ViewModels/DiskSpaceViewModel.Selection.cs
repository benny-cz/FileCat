using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    private string? _tableAnchor;
    private Guid _tableAnchorSession;
    private long _tableAnchorEpoch;
    private string? _tableAnchorScope;
    private AnalysisOrderedRows? _rangeOrderOwner;
    private ImmutableHashSet<ulong>? _rangeBaseline, _rangePublishedMarks;

    public void SetTableAnchor(string key)
    {
        _tableAnchor = key; _tableAnchorSession = Tree?.Snapshot.Identity.SessionId ?? Guid.Empty;
        _tableAnchorEpoch = Tree?.Snapshot.Identity.Epoch ?? 0; _tableAnchorScope = Selection.Scope;
        _rangeOrderOwner = null; _rangeBaseline = _rangePublishedMarks = null;
    }

    private void RebindTableRangeOrder(AnalysisTree tree, AnalysisOrderedRows next)
    {
        if (_rangeOrderOwner is null || !ReferenceEquals(_rangeOrderOwner, _orderedRows) || Tree is not { } before ||
            before.Snapshot.Identity.SessionId != tree.Snapshot.Identity.SessionId || before.Snapshot.Identity.Epoch != tree.Snapshot.Identity.Epoch) return;
        var rows = next.Rows;
        if (Rows.Length != rows.Length) return;
        for (int i = 0; i < rows.Length; i++) if (Rows[i].Key != rows[i].Key || Rows[i].NodeId != rows[i].NodeId) return;
        _rangeOrderOwner = next;
    }

    /// <summary>Marks real rows in the currently published order. Synthetic members are never implicitly expanded.</summary>
    public bool FocusTableRange(string target, bool additive = false)
    {
        if (_disposed || Tree is not { } tree || _orderedRows is not { } owner) return false;
        var rows = Rows; int end = -1, anchor = -1;
        bool sameScope = _tableAnchorSession == tree.Snapshot.Identity.SessionId && _tableAnchorEpoch == tree.Snapshot.Identity.Epoch && _tableAnchorScope == Selection.Scope;
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i].Key == target) end = i;
            if (sameScope && rows[i].Key == _tableAnchor) anchor = i;
        }
        if (end < 0) return false;
        if (anchor < 0) anchor = end;
        int first = Math.Min(anchor, end), last = Math.Max(anchor, end), real = 0, synthetic = 0;
        var baseline = !additive && ReferenceEquals(owner, _rangeOrderOwner) && ReferenceEquals(Selection.Marks, _rangePublishedMarks)
            ? _rangeBaseline! : Selection.Marks;
        IEnumerable<ulong> Members()
        {
            for (int i = first; i <= last; i++)
                if (rows[i].NodeId is { } id) { real++; yield return id; }
                else synthetic++;
        }
        try { Selection.MarkRangeCaptured(baseline, Members()); }
        catch (InvalidOperationException error) { SetStatus(error.Message); return false; }
        _tableAnchor = rows[anchor].Key; _tableAnchorSession = tree.Snapshot.Identity.SessionId;
        _tableAnchorEpoch = tree.Snapshot.Identity.Epoch; _tableAnchorScope = Selection.Scope;
        // The owner clears its reference array when released; do not retain a separately copied order.
        _rangeOrderOwner = owner; _rangeBaseline = baseline; _rangePublishedMarks = Selection.Marks;
        Selection.SetFocus(target);
        SetStatus($"Marked rows: {real:N0}. Existing marks outside this range are retained." +
            (synthetic == 0 ? "" : $" Accounting groups not expanded: {synthetic:N0}. Use Mark current members to include their observed files."));
        return true;
    }
}
