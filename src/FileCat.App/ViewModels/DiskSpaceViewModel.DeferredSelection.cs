using FileCat.Core.Analysis;

namespace FileCat.App.ViewModels;

public enum AnalysisDeferredSelectionState { None, WaitingForScan, Materializing, Ready, Unavailable }

/// <summary>Captured review data. It grants no source binding or filesystem authority.</summary>
internal sealed class AnalysisDeferredSelectionReview(Guid request, AnalysisSelection selection, AnalysisSavedView view,
    string scopeLabel, AnalysisSnapshotLease owner) : IDisposable
{
    internal Guid Request { get; } = request;
    internal AnalysisSelection Selection { get; } = selection;
    internal AnalysisSavedView View { get; } = view;
    internal string ScopeLabel { get; } = scopeLabel;
    internal AnalysisSnapshot Snapshot => owner.Snapshot;
    internal AnalysisNode Node(ulong id)
    {
        var nodes = Snapshot.Nodes; int first = 0, last = nodes.Length - 1;
        while (first <= last)
        {
            int at = first + (last - first) / 2; var node = nodes[at];
            if (node.Id == id) return node;
            if (node.Id < id) first = at + 1; else last = at - 1;
        }
        throw new InvalidDataException("The reviewed path is absent from its captured revision.");
    }
    internal AnalysisDeferredSelectionReview Retain() => new(Request, Selection, View, ScopeLabel, owner.Retain());
    public void Dispose() => owner.Dispose();
}

public sealed partial class DiskSpaceViewModel
{
    private sealed class DeferredRequest(AnalysisSession session, SessionIdentity identity, AnalysisSavedView view, string label) : IDisposable
    {
        internal readonly Guid Id = Guid.NewGuid();
        internal readonly AnalysisSession Session = session;
        internal readonly SessionIdentity Identity = identity;
        internal readonly AnalysisSavedView View = view;
        internal readonly string Label = label;
        internal readonly CancellationTokenSource Cancellation = new();
        private bool _closed;
        internal void Cancel() { if (!_closed) Cancellation.Cancel(); }
        public void Dispose() { if (_closed) return; _closed = true; Cancellation.Dispose(); }
    }
    private DeferredRequest? _deferredRequest, _deferredActive;
    private AnalysisDeferredSelectionReview? _deferredReady;
    private bool _deferredWorkerRunning;
    public AnalysisDeferredSelectionState DeferredSelectionState { get; private set; }
    public string? DeferredSelectionText { get; private set; }
    public bool HasDeferredSelection => _deferredRequest is not null;
    public bool CanReviewDeferredSelection => !_disposed && _deferredReady is not null;
    public int DeferredSelectionCount => _deferredReady?.Selection.NodeIds.Length ?? 0;
    public bool CanRequestDeferredSelection => DeferredSelectionUnavailableReason is null;
    public string? DeferredSelectionUnavailableReason => _disposed ? "This analysis is closed."
        : _actualWork.HasUnconfirmedClose ? "Captured storage cleanup is unconfirmed."
        : IsIndexedRuntimePreview ? "This preview contains a bounded subset. Open the recorded result after the scan finishes to review matching entries."
        : _session is null || !Running || _session.Completion.IsCompleted ? "Available while this analysis is scanning or paused. Captured inventories have no future scan results."
        : Tree is null || _publishedView is null ? "Wait for a displayed observation revision."
        : null;

    public bool RequestSelectionAfterScan()
    {
        if (DeferredSelectionUnavailableReason is { } reason) { SetStatus(reason); return false; }
        var tree = Tree!; var view = _publishedView! with { Scope = Selection.Scope };
        string label = tree.NameOf(view.Scope) + (view.Scope.StartsWith("other:", StringComparison.Ordinal)
            ? " within " + tree.NameOf(tree.ParentOf(view.Scope)) + " using the final observed ranking" : "");
        var request = new DeferredRequest(_session!, tree.Snapshot.Identity, view, label);
        RetireDeferredRequest(); _deferredRequest = request;
        DeferredSelectionState = AnalysisDeferredSelectionState.WaitingForScan;
        DeferredSelectionText = "After the scan completes: matching files in " + AnalysisNames.Display(request.Label, 120) +
            ". The displayed query and scope were captured. Review is required before marks change.";
        Changed?.Invoke();
        if (!_deferredWorkerRunning && !_disposed) _ = MaterializeDeferredSelectionAsync();
        return true;
    }
    public void CancelDeferredSelection()
    {
        if (_disposed) return;
        RetireDeferredRequest(); DeferredSelectionState = AnalysisDeferredSelectionState.None;
        DeferredSelectionText = "Deferred selection canceled. Current marks and the review basket are unchanged.";
        Changed?.Invoke();
    }
    private void RetireDeferredRequest()
    {
        var previous = _deferredRequest; _deferredRequest = null; previous?.Cancel();
        if (!ReferenceEquals(previous, _deferredActive)) previous?.Dispose();
        _deferredReady?.Dispose(); _deferredReady = null;
    }
    private async Task MaterializeDeferredSelectionAsync()
    {
        using var actual = _actualWork.Enter(); _deferredWorkerRunning = true;
        try
        {
            while (!_disposed && _deferredRequest is { } request)
            {
                _deferredActive = request; AnalysisSnapshotLease? owner = null;
                var token = request.Cancellation.Token;
                try
                {
                    await request.Session.Completion.WaitAsync(token);
                    token.ThrowIfCancellationRequested(); if (!ReferenceEquals(request, _deferredRequest) || _disposed) continue;
                    owner = request.Session.AcquireSnapshot(); var snapshot = owner.Snapshot;
                    if (snapshot.Identity.SessionId != request.Identity.SessionId || snapshot.Identity.Epoch != request.Identity.Epoch)
                        throw new InvalidOperationException("The scan session or epoch changed. Request a new deferred selection.");
                    if (snapshot.Phase != AnalysisPhase.Finished)
                        throw new InvalidOperationException("The scan did not complete. No deferred matches were selected; inspect the partial results explicitly.");
                    DeferredSelectionState = AnalysisDeferredSelectionState.Materializing;
                    DeferredSelectionText = "Preparing captured matches for review… Current marks and the review basket remain unchanged.";
                    Changed?.Invoke();
                    var selection = await Task.Run(() =>
                    {
                        ulong previous = 0;
                        foreach (var node in snapshot.Nodes)
                        {
                            token.ThrowIfCancellationRequested();
                            if (node.Id <= previous) throw new InvalidDataException("The completed live observation has invalid path ordering.");
                            previous = node.Id;
                        }
                        var tree = BuildCapturedTree(snapshot, request.View, _zone, token);
                        if (!tree.CanExplore(request.View.Scope))
                            throw new InvalidOperationException("The requested captured scope is absent from the final revision. Choose a scope and capture its current matches.");
                        return tree.CaptureMatchingFiles(request.View.Scope, token) with
                        {
                            FilterSummary = request.View.Query + (request.View.Root is { } root ? " · captured location " + root : "") +
                                (request.View.CaseSensitive ? " · case sensitive" : "") + (request.View.ShowUnknown ? " · uncertain results" : "")
                        };
                    }, token);
                    token.ThrowIfCancellationRequested(); if (!ReferenceEquals(request, _deferredRequest) || _disposed) continue;
                    _deferredReady = new(request.Id, selection, request.View, request.Label, owner); owner = null;
                    DeferredSelectionState = AnalysisDeferredSelectionState.Ready;
                    DeferredSelectionText = $"{selection.NodeIds.Length:N0} captured matching file paths ready for review in " + AnalysisNames.Display(request.Label, 120) +
                        $" · final revision {selection.Revision:N0}" + (snapshot.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "." : " · partial results; unread locations remain unknown.") +
                        " No marks or basket entries were added.";
                    Changed?.Invoke();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error) when (error is InvalidOperationException or InvalidDataException or IOException or FormatException or OverflowException or ArgumentException)
                {
                    if (!_disposed && ReferenceEquals(request, _deferredRequest))
                    {
                        _deferredReady?.Dispose(); _deferredReady = null;
                        DeferredSelectionState = AnalysisDeferredSelectionState.Unavailable;
                        DeferredSelectionText = "Deferred selection unavailable. " + AnalysisNames.Display(error.Message, 400) + " Current marks and the review basket are unchanged.";
                        Changed?.Invoke();
                    }
                }
                finally
                {
                    owner?.Dispose(); if (ReferenceEquals(_deferredActive, request)) _deferredActive = null; request.Dispose();
                }
                if (ReferenceEquals(request, _deferredRequest)) break;
            }
        }
        finally { _deferredWorkerRunning = false; }
    }
    internal AnalysisDeferredSelectionReview CaptureDeferredSelectionReview()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _deferredReady?.Retain() ?? throw new InvalidOperationException("No completed deferred selection is ready for review.");
    }
    internal bool CanApplyDeferredSelection(AnalysisDeferredSelectionReview review) => !_disposed && _deferredReady is { } ready &&
        ready.Request == review.Request && ReferenceEquals(ready.Selection, review.Selection);
    internal void ApplyReviewedDeferredSelection(AnalysisDeferredSelectionReview review)
    {
        if (!CanApplyDeferredSelection(review)) throw new InvalidOperationException("This deferred review was canceled, replaced or already applied. No marks were added.");
        var selection = _deferredReady!.Selection;
        if (Tree is not { } current || current.Snapshot.Identity.SessionId != selection.SessionId || current.Snapshot.Identity.Epoch != selection.Epoch)
            throw new InvalidOperationException("The current observation domain changed. No deferred marks were added.");
        Selection.MarkCaptured(selection.NodeIds); RetireDeferredRequest(); DeferredSelectionState = AnalysisDeferredSelectionState.None;
        DeferredSelectionText = $"Added {selection.NodeIds.Length:N0} reviewed matching file paths from final revision {selection.Revision:N0}. The review basket is unchanged.";
        Changed?.Invoke();
    }
}
