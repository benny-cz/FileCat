using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    private readonly CancellationTokenSource _selectionLifetime = new();
    private CancellationTokenSource? _markCountCancel, _matchingCaptureCancel;
    private AnalysisTree? _countTree;
    private ImmutableHashSet<ulong>? _countMarks;
    private string? _countScope;
    private bool _countRunning, _countAgain;
    private int _countGeneration;
    public AnalysisSelectionVisibility? MarkVisibility { get; private set; }
    public int ListedMarks { get; private set; }
    public string? MarkVisibilityError { get; private set; }
    public bool IsCapturingMatches { get; private set; }
    internal AnalysisSnapshotLease? RetainDisplayedOwner() => _lease?.Retain();
    private async Task CloseSelectionWorkAsync(Task actual)
    { try { await actual; } finally { _selectionLifetime.Dispose(); } }
    public void ClearMarks()
    {
        _matchingCaptureCancel?.Cancel(); Selection.ClearMarks();
        _rangeOrderOwner = null; _rangeBaseline = _rangePublishedMarks = null; Changed?.Invoke();
    }

    public void ScheduleMarkVisibility(bool retry = false)
    {
        if (_disposed) return;
        if (!retry && ReferenceEquals(_countTree, Tree) && ReferenceEquals(_countMarks, Selection.Marks) && _countScope == Selection.Scope) return;
        _countTree = Tree; _countMarks = Selection.Marks; _countScope = Selection.Scope; _countGeneration++;
        MarkVisibility = null; ListedMarks = 0; MarkVisibilityError = null; _markCountCancel?.Cancel();
        if (_actualWork.HasUnconfirmedClose) { _countAgain = false; MarkVisibilityError = "Captured storage cleanup is unconfirmed."; return; }
        _countAgain = Tree is not null && Selection.Marks.Count != 0;
        if (_countAgain && !_countRunning) _ = CountMarksAsync();
    }
    private async Task CountMarksAsync()
    {
        using var actual = _actualWork.Enter(); _countRunning = true;
        try
        {
            while (_countAgain && !_disposed)
            {
                _countAgain = false; var tree = _countTree!; var marks = _countMarks!; string scope = _countScope!; int generation = _countGeneration;
                using var owner = _lease?.Retain();
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token); _markCountCancel = cancel;
                try
                {
                    var result = await Task.Run(() =>
                    {
                        var counts = tree.ClassifyMarks(marks, scope, cancel.Token);
                        int listed = tree.ChildrenOf(scope).Where(item => item.NodeId is { } id && marks.Contains(id)).Select(item => item.NodeId).Distinct().Count();
                        return (counts, listed);
                    }, cancel.Token);
                    cancel.Token.ThrowIfCancellationRequested(); if (_disposed || generation != _countGeneration) continue;
                    MarkVisibility = result.counts; ListedMarks = result.listed; Changed?.Invoke();
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException or OverflowException)
                { if (!_disposed && generation == _countGeneration) { MarkVisibilityError = AnalysisNames.Display(error.Message, 240); Changed?.Invoke(); } }
                finally { if (ReferenceEquals(_markCountCancel, cancel)) _markCountCancel = null; }
            }
        }
        finally { _countRunning = false; }
    }
    public async Task MarkMatchingFilesAsync()
    {
        if (_disposed || IsCapturingMatches || Tree is not { } tree) return;
        if (_actualWork.HasUnconfirmedClose) { SetStatus("Captured storage cleanup is unconfirmed. Matching-file capture is unavailable."); return; }
        using var actual = _actualWork.Enter(); using var owner = _lease?.Retain();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token); _matchingCaptureCancel = cancel;
        string scope = Selection.Scope; IsCapturingMatches = true;
        try
        {
            SetStatus("Capturing matching files in this displayed scope and revision…");
            var captured = await Task.Run(() => tree.CaptureMatchingFiles(scope, cancel.Token), cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            if (Tree is not { } current || current.Snapshot.Identity.SessionId != captured.SessionId || current.Snapshot.Identity.Epoch != captured.Epoch)
                throw new InvalidOperationException("The captured matches belong to another session or epoch. No matches were added.");
            Selection.MarkCaptured(captured.NodeIds);
            SetStatus($"Marked {captured.NodeIds.Length:N0} matching file paths from captured revision {captured.Revision:N0}. Existing marks are retained; future entries are not selected.");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { SetStatus("Matching-file capture canceled. No captured matches were added."); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or OverflowException)
        { SetStatus(AnalysisNames.Display(error.Message, 500)); }
        finally { if (ReferenceEquals(_matchingCaptureCancel, cancel)) _matchingCaptureCancel = null; IsCapturingMatches = false; if (!_disposed) Changed?.Invoke(); }
    }
}
