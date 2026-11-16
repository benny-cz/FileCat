using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    internal Task<Services.AnalysisOwnedComparisonMaintenanceCapture> PrepareSavedComparisonMaintenanceAsync(string name, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HasPrivateStorage) throw new InvalidOperationException("Private comparison storage is unavailable.");
        return Services.AnalysisOwnedStoreAccess.PrepareComparisonAsync(_services!, name, _actualWork, cancellation);
    }
    private bool _comparisonReady;
    private AnalysisComparisonView? _savedComparisonView;
    private AnalysisComparisonQueryResult? _restoredComparisonQuery;
    private bool _comparisonDraftChanged;
    private ImmutableDictionary<string, string>? _comparisonPreviousRootIds, _comparisonCurrentRootIds;
    public AnalysisComparisonView ComparisonView { get; private set; } = new();
    public string? SavedComparisonName { get; private set; }
    private Guid? _savedComparisonId;
    public bool CanSaveComparison => !_disposed && _comparisonReady && Comparison is not null && HasPrivateStorage && !IsSaving;
    internal bool CanExportComparison => !_disposed && _comparisonReady && Comparison is not null && _comparisonPrevious is not null && _comparisonCurrent is not null && _services is not null && !_services.Io.IsStopped && !_actualWork.HasUnconfirmedClose && !IsSaving;
    internal bool ComparisonReady => !_disposed && _comparisonReady && Comparison is not null;
    internal bool ComparisonHasTemporaryWork => !_comparisonReady || _comparisonDraftChanged || SavedComparisonName is null || ComparisonView != _savedComparisonView;
    internal void SetComparisonDraftChanged(bool changed) { if (ComparisonReady) _comparisonDraftChanged = changed; }
    internal void SetComparisonView(AnalysisComparisonView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_comparisonReady || Comparison?.Metric != view.Metric) throw new InvalidOperationException("Wait for the requested comparison metric before saving its display settings.");
        AnalysisComparisonView.Validate(view); ComparisonView = view;
    }
    internal AnalysisComparisonQueryResult? TakeRestoredComparisonQuery()
    { var result = _restoredComparisonQuery; _restoredComparisonQuery = null; return result; }

    internal sealed class ComparisonCapture : IDisposable
    {
        internal readonly AnalysisSnapshotLease Previous, Current;
        private readonly AnalysisComparisonResult.ReadLease _rows;
        internal readonly AnalysisComparisonView View;
        internal readonly ImmutableDictionary<string, string>? Roots, PreviousRootIds, CurrentRootIds;
        internal readonly string PreviousLabel;
        internal AnalysisTaint Taint => _rows.Taint;
        internal ComparisonCapture(AnalysisSnapshotLease previous, AnalysisSnapshotLease current, AnalysisComparisonResult.ReadLease rows,
            AnalysisComparisonView view, ImmutableDictionary<string, string>? roots, string label,
            ImmutableDictionary<string, string>? previousRootIds, ImmutableDictionary<string, string>? currentRootIds)
        { Previous = previous; Current = current; _rows = rows; View = view; Roots = roots; PreviousLabel = label; PreviousRootIds = previousRootIds; CurrentRootIds = currentRootIds; }
        internal ComparisonCapture Retain()
        {
            AnalysisSnapshotLease? before = null, now = null; AnalysisComparisonResult.ReadLease? rows = null;
            try
            { before = Previous.Retain(); now = Current.Retain(); rows = _rows.Retain(); return new(before, now, rows, View, Roots, PreviousLabel, PreviousRootIds, CurrentRootIds); }
            catch { rows?.Dispose(); now?.Dispose(); before?.Dispose(); throw; }
        }
        public void Dispose() { try { _rows.Dispose(); } finally { try { Previous.Dispose(); } finally { Current.Dispose(); } } }
    }
    internal ComparisonCapture CaptureComparison(bool forExport = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (forExport ? !CanExportComparison : !CanSaveComparison) throw new InvalidOperationException(forExport ? "Wait for an available captured comparison before exporting." : "Wait for an available comparison and private profile storage before saving.");
        AnalysisSnapshotLease? before = null, now = null; AnalysisComparisonResult.ReadLease? rows = null;
        try
        {
            before = _comparisonPrevious!.Retain(); now = _comparisonCurrent!.Retain(); rows = Comparison!.Retain();
            return new(before, now, rows, ComparisonView, _comparisonRootMapping, ComparisonPreviousName ?? "", _comparisonPreviousRootIds, _comparisonCurrentRootIds);
        }
        catch { rows?.Dispose(); now?.Dispose(); before?.Dispose(); throw; }
    }
    public async Task<AnalysisComparisonReceipt> SaveComparisonNewAsync(string name, CancellationToken cancellation = default)
    { using var captured = CaptureComparison(); return await SaveComparisonNewAsync(name, captured, cancellation); }
    internal async Task<AnalysisComparisonReceipt> SaveComparisonNewAsync(string name, ComparisonCapture captured, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); using var actualWork = _actualWork.Enter();
        if (!HasPrivateStorage || IsSaving) throw new InvalidOperationException("Private comparison storage is unavailable or already saving.");
        AnalysisPrivateInventoryStore.ValidateComparisonName(name); using var copy = captured.Retain();
        string path = _services!.Paths.AnalysisSnapshotsDirectory; IsSaving = true; SetStatus("Saving both captured inputs and the applied comparison view…");
        try
        {
            var receipt = await RunPrivateIoAsync(path, token =>
            {
                using var store = OpenPrivateStore(path);
                return store.SaveComparisonNew(name, Guid.NewGuid(), copy.Previous.Snapshot, copy.Current.Snapshot, copy.View, copy.Roots, copy.PreviousLabel, token, copy.PreviousRootIds, copy.CurrentRootIds);
            }, cancellation);
            SavedComparisonName = receipt.Name; _savedComparisonView = copy.View; _savedComparisonId = receipt.ComparisonId;
            SetStatus("Saved " + AnalysisNames.Display(receipt.Name, 240) + ". Both captured inventories and the applied view are private; no source is connected.");
            return receipt;
        }
        catch (AnalysisPublicationUncertainException) { SetStatus("The comparison name was published, but durability is uncertain. Keep the file and inspect it before retrying."); throw; }
        catch (OperationCanceledException) { SetStatus("Comparison save canceled. No successful save is claimed."); throw; }
        catch { SetStatus("Comparison was not saved. Existing files are preserved; choose another name or retry."); throw; }
        finally { IsSaving = false; if (!_disposed) Changed?.Invoke(); }
    }
    public async Task<ImmutableArray<string>> ReadSavedComparisonNamesAsync(CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); using var actualWork = _actualWork.Enter();
        if (!HasPrivateStorage) throw new InvalidOperationException("Private comparison storage is unavailable.");
        string path = _services!.Paths.AnalysisSnapshotsDirectory;
        return await RunPrivateIoAsync(path, token => { using var store = OpenPrivateStore(path); return store.ComparisonNames(token); }, cancellation);
    }
    public async Task<DiskSpaceViewModel> OpenSavedComparisonAsync(string name, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); using var actualWork = _actualWork.Enter();
        if (!HasPrivateStorage) throw new InvalidOperationException("Private comparison storage is unavailable.");
        AnalysisPrivateInventoryStore.ValidateComparisonName(name); string path = _services!.Paths.AnalysisSnapshotsDirectory;
        using var saved = await RunPrivateIoAsync(path, token => { using var store = OpenPrivateStore(path); return store.LoadComparison(name, token); }, cancellation);
        return await RestoreComparisonAsync(_services!, saved, name, cancellation);
    }
    internal static async Task<DiskSpaceViewModel> ImportExternalComparisonAsync(Services.AppServices services, string path, CancellationToken cancellation)
    {
        using var saved = await Services.AnalysisIndexedAcquisition.ReadExternalComparisonAsync(services, path, cancellation);
        return await RestoreComparisonAsync(services, saved, null, cancellation);
    }
    private static async Task<DiskSpaceViewModel> RestoreComparisonAsync(Services.AppServices services, AnalysisSavedComparison saved,
        string? name, CancellationToken cancellation)
    {
        AnalysisSnapshotLease? before = null, now = null; AnalysisComparisonResult? result = null; DiskSpaceViewModel? model = null;
        try
        {
            before = saved.Previous.Retain(); now = saved.Current.Retain(); var previous = before.Snapshot; var current = now.Snapshot;
            result = await Task.Run(() => AnalysisComparison.Build(previous, current, saved.View.Metric, saved.StructuralRoots, cancellation), CancellationToken.None);
            cancellation.ThrowIfCancellationRequested();
            model = new DiskSpaceViewModel(services)
            {
                _comparisonPrevious = before,
                _comparisonCurrent = now,
                _comparisonRootMapping = saved.StructuralRoots,
                _comparisonPreviousRootIds = saved.PreviousRootDisplayIds,
                _comparisonCurrentRootIds = saved.CurrentRootDisplayIds,
                _comparisonReady = true,
                Comparison = result,
                ComparisonPreviousName = saved.PreviousLabel,
                ComparisonView = saved.View,
                _savedComparisonView = saved.View,
                SavedComparisonName = name,
                _savedComparisonId = name is null ? null : saved.ComparisonId,
                Metric = saved.View.Metric,
                Chart = "Table",
                Phase = AnalysisPhase.Stopped,
                Status = name is null ? "Imported comparison · offline observations. Sources are disconnected; differences are unverified metadata observations."
                    : "Saved offline comparison. Sources are disconnected; differences are metadata observations."
            };
            before = now = null; result = null;
            if (!string.IsNullOrWhiteSpace(saved.View.Query))
                model._restoredComparisonQuery = await model.QueryComparisonAsync(saved.View.Query, saved.View.CaseSensitive, saved.View.QuerySide, cancellation);
            if (saved.View.FocusNode != 0 && model.Comparison!.Rows.Count(row => (saved.View.PreviousFocus ? row.Previous : row.Current)?.NodeIds.Contains(saved.View.FocusNode) == true) != 1)
                throw new InvalidDataException("Saved comparison focus does not identify one accounting row.");
            if (saved.View.Chart != AnalysisComparisonChartMode.Table)
            {
                using var chart = await model.BuildComparisonChartAsync(saved.View.PreviousChart, cancellation);
                if (!chart.CanExplore(saved.View.ChartScope) || saved.View.ChartScope.StartsWith("other:", StringComparison.Ordinal) && chart.ChildrenOf(saved.View.ChartScope).IsEmpty)
                    throw new InvalidDataException("Saved comparison chart scope is unavailable in its captured input.");
            }
            cancellation.ThrowIfCancellationRequested(); var success = model; model = null; return success;
        }
        finally { model?.Dispose(); result?.Dispose(); before?.Dispose(); now?.Dispose(); }
    }
}
