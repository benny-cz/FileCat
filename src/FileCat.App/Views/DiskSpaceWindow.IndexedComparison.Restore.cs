using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    internal Task AddExternalIndexedComparisonSessionAsync(AppServices services, string path, CancellationToken cancellation)
    {
        AnalysisInventoryInput.ValidateChosenPath(path, comparison: true);
        return AddPreparedIndexedComparisonSessionAsync(services, async token =>
        {
            AnalysisSavedIndexedComparison? imported = null;
            try
            {
                await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, token);
                imported = await prepared.LoadExternalComparisonAsync(path, token); return imported;
            }
            catch { if (imported is not null) await imported.DisposeAsync(); throw; }
        }, cancellation);
    }

    internal Task AddSavedIndexedComparisonSessionAsync(AppServices services, string name, CancellationToken cancellation)
    {
        AnalysisPrivateInventoryStore.ValidateComparisonName(name);
        return AddPreparedIndexedComparisonSessionAsync(services, async token =>
        {
            AnalysisSavedIndexedComparison? imported = null;
            try
            {
                await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, token);
                imported = await prepared.LoadSavedComparisonAsync(name, token); return imported;
            }
            catch { if (imported is not null) await imported.DisposeAsync(); throw; }
        }, cancellation, name);
    }

    internal async Task AddPreparedIndexedComparisonSessionAsync(AppServices services,
        Func<CancellationToken, Task<AnalysisSavedIndexedComparison>> load, CancellationToken cancellation, string? savedName = null)
    {
        Dispatcher.UIThread.VerifyAccess(); ObjectDisposedException.ThrowIf(_closed, this);
        if (AnalysisIndexedComparison.IndexCapacityRefusal is { } refusal) throw new InvalidOperationException(refusal);
        AnalysisSessionAdmission.Slot? admission = _admissionPool.Acquire();
        if (_pendingIndexed++ == 0) _indexedIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AnalysisSavedIndexedComparison? saved = null; AnalysisDatasetLease? a = null, b = null;
        AnalysisIndexedComparison? result = null; AnalysisIndexedComparison.Query? query = null; IndexedComparisonView? view = null;
        Exception? unconfirmed = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _indexedLifetime.Token);
        try
        {
            saved = await load(linked.Token);
            a = await saved.Previous.AcquireAsync(linked.Token); b = await saved.Current.AcquireAsync(linked.Token);
            var metadata = saved;
            // Retained leases own the complete graphs. Confirm the independent
            // import-owner close before transferring this admission to a tab.
            await saved.DisposeAsync(); saved = null;
            result = await b.CompareIndexedAsync(a, metadata.View.Metric, metadata.StructuralRoots, linked.Token);
            query = await IndexedComparisonView.BuildQueryAsync(result, metadata.View, linked.Token, metadata.PreviousRootDisplayIds, metadata.CurrentRootDisplayIds);
            var page = await IndexedComparisonView.ReadPageAsync(result, 0, linked.Token, query);
            linked.Token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_closed, this);
            view = new(this, admission, a, b, result, metadata.StructuralRoots, metadata.PreviousLabel, page,
                static (old, now, metric, roots, token) => now.CompareIndexedAsync(old, metric, roots, token), services,
                metadata.View, query, metadata.PreviousRootDisplayIds, metadata.CurrentRootDisplayIds);
            a = b = null; result = null; query = null; admission = null;
            await view.RestoreAsync(metadata.View, linked.Token);
            if (savedName is not null) view.RememberSavedComparison(metadata.ComparisonId, savedName, metadata.View);
            linked.Token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_closed, this);
            AddView(view); view = null;
        }
        catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error))
        { unconfirmed = error; view?.RememberUnconfirmedClose(error); RecordIndexedCloseFailure(error); throw; }
        finally
        {
            try
            {
                var close = ClosePreparedAsync();
                try { if (admission is not null) await admission.CloseAfterAsync(close); else await close; }
                catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { RecordIndexedCloseFailure(error); throw; }
            }
            finally { if (--_pendingIndexed == 0) { _indexedIdle.TrySetResult(); if (_closed) _indexedLifetime.Dispose(); } }
        }
        async Task ClosePreparedAsync()
        {
            Exception? failure = unconfirmed;
            try { if (view is not null) { view.Dispose(); await view.CloseCompletion; } }
            catch (Exception error) { failure = AnalysisViewWork.PreserveCloseFailure(failure, new AnalysisCloseUnconfirmedException(error)); }
            try { await AnalysisIndexedComparison.CloseAsync(query, result, b, a, saved); }
            catch (Exception error) { failure = AnalysisViewWork.PreserveCloseFailure(failure, new AnalysisCloseUnconfirmedException(error)); }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed partial class IndexedComparisonView
    {
        internal void RememberUnconfirmedClose(Exception error) => _work.RecordUnconfirmedClose(error);
        internal async Task RestoreAsync(AnalysisComparisonView view, CancellationToken token)
        {
            if (_result is null) throw new InvalidOperationException("The comparison is unavailable.");
            _chartChoice.SelectedIndex = (int)view.Chart; _chartSide.SelectedIndex = view.PreviousChart ? 1 : 0;
            if (view.Chart != AnalysisComparisonChartMode.Table)
            {
                _chartOwner = await _result.BuildChartAsync(view.PreviousChart, token);
                _chartRoots = await _chartOwner.ReadRootsAsync(token);
                _chartProjection = await _chartOwner.LoadAsync(view.ChartScope, token);
                _chartRoot.ItemsSource = _chartRoots.Select(root => root.Label).ToArray();
                _chartRoot.SelectedIndex = _chartRoots.Length == 0 ? -1 : 0;
                _chartMode = (int)view.Chart; DrawChart();
            }
            if (view.FocusNode != 0)
            {
                var row = await _result.FindRowForPathAsync(view.PreviousFocus, view.FocusNode, token)
                    ?? throw new InvalidDataException("The saved comparison focus is absent from its captured accounting rows.");
                bool visible = await LocateComparisonRowAsync(row, token); await InspectRowAsync(row, token);
                await SelectFocusedMemberAsync(view.PreviousFocus, view.FocusNode, token);
                if (!visible) _status.Text = "Saved focus is outside the applied table filter; both captured sides are shown in the inspector. The filter is kept.";
            }
            token.ThrowIfCancellationRequested(); Refresh();
        }
        private async Task SelectFocusedMemberAsync(bool previous, ulong pathId, CancellationToken token)
        {
            var result = _result ?? throw new InvalidOperationException("The comparison is unavailable.");
            var side = previous ? _before : _after;
            if (side.Entity is null) throw new InvalidDataException("The saved comparison focus has no recorded accounting side.");
            ulong offset = await result.FindMemberOffsetAsync(side.Previous, side.Entity.Id, pathId, token);
            var page = await result.ReadMembersAsync(side.Previous, side.Entity.Id, offset, PageSize, token);
            var selected = page.Rows.FirstOrDefault(path => path.Id == pathId)
                ?? throw new InvalidDataException("The saved comparison alias is absent from its captured member page.");
            string location = await new AnalysisReportLineage(side.Previous ? _previousSource : _currentSource).ResolveAsync(selected, token);
            token.ThrowIfCancellationRequested();
            _publishing = true;
            try { ShowMembers(side, page); side.Members.SelectedItem = selected; }
            finally { _publishing = false; }
            ShowLocatedPath(side, selected, location);
            if (_selected is { } row) FocusChart(row, _before.Page, _after.Page);
        }

    }
}
