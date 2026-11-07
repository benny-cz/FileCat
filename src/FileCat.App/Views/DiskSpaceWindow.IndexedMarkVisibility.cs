using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class IndexedSessionView
    {
        private ImmutableHashSet<ulong>? _visibilityMarks;
        private object? _visibilityOwner;
        private string? _visibilityScope;
        private AnalysisSelectionVisibility? _markVisibility;
        private string? _visibilityError;
        private CancellationTokenSource? _markVisibilityCancel;
        private int _visibilityGeneration;
        private bool _visibilityRunning, _visibilityAgain;
        private void ScheduleMarkVisibility()
        {
            object owner = _grouping is { } groups ? groups : _explorer;
            string scope = _grouping is not null ? _groupKey ?? AnalysisTree.RootKey : AnalysisTree.Key(_scope);
            if (!ReferenceEquals(_visibilityMarks, _selection.Marks) || !ReferenceEquals(_visibilityOwner, owner) || _visibilityScope != scope)
            {
                _visibilityMarks = _selection.Marks; _visibilityOwner = owner; _visibilityScope = scope;
                _markVisibility = null; _visibilityError = null; _visibilityGeneration++; _markVisibilityCancel?.Cancel();
                _visibilityAgain = _selection.Marks.Count != 0;
                if (_visibilityAgain && !_visibilityRunning && !_actualWork.HasUnconfirmedClose) _ = CountMarkVisibilityAsync();
            }
            RefreshMarkVisibilityText();
        }
        private void RefreshMarkVisibilityText()
        {
            if (_disposed) return;
            _recountMarks.IsVisible = _visibilityError is not null;
            _recountMarks.IsEnabled = !_actualWork.HasUnconfirmedClose;
            string prefix = $"{_selection.Marks.Count:N0} marked · {_selection.Basket.Count:N0} in review basket.";
            if (_selection.Marks.Count == 0) { _selectionSummary.Text = prefix; return; }
            if (_markVisibility is not { } counts)
            { _selectionSummary.Text = prefix + (_visibilityError is null ? " Counting marked entries in this scope and filter…" : " Mark scope counts unavailable: " + _visibilityError); return; }
            int page = _list.Items.Cast<object>().Select(row => row is AnalysisHierarchyRow folder ? (ulong?)folder.Path.Id : row is AnalysisDatasetGroupRow member ? member.Member.Path.Id : null)
                .Where(id => id.HasValue && _selection.Marks.Contains(id.Value)).Distinct().Count();
            _selectionSummary.Text = prefix + $" {counts.InScope:N0} in this scope · {page:N0} on this page · {counts.Elsewhere:N0} elsewhere · " +
                $"{counts.HiddenByFilter:N0} marked items hidden by filters" + (counts.Missing == 0 ? "." : $" · {counts.Missing:N0} absent from this captured revision.");
        }
        private async Task CountMarkVisibilityAsync()
        {
            using var actualWork = _actualWork.Enter(); _visibilityRunning = true;
            try
            {
                while (_visibilityAgain && !_disposed && !_actualWork.HasUnconfirmedClose)
                {
                    _visibilityAgain = false; var marks = _visibilityMarks!; var owner = _visibilityOwner!;
                    int generation = _visibilityGeneration; ulong folder = _scope; string? groupKey = _groupKey;
                    using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token); _markVisibilityCancel = cancel;
                    try
                    {
                        var counts = owner is AnalysisDatasetGrouping groups ? await groups.ClassifyMarksAsync(marks, groupKey, cancel.Token) :
                            await ((AnalysisDatasetExplorer)owner).ClassifyMarksAsync(marks, folder, cancel.Token);
                        cancel.Token.ThrowIfCancellationRequested(); if (_disposed || generation != _visibilityGeneration) continue;
                        if (counts.Identity != _source.Descriptor.Identity) throw new InvalidOperationException("These mark counts belong to another captured revision.");
                        _markVisibility = counts; _visibilityError = null; RefreshMarkVisibilityText();
                    }
                    catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
                    catch (Exception error) when (Expected(error))
                    {
                        RememberViewCloseFailure(error);
                        if (!_disposed && generation == _visibilityGeneration) { _visibilityError = AnalysisNames.Display(error.Message, 240); RefreshMarkVisibilityText(); }
                    }
                    finally { if (ReferenceEquals(_markVisibilityCancel, cancel)) _markVisibilityCancel = null; }
                }
            }
            finally { _visibilityRunning = false; }
        }
    }
}
