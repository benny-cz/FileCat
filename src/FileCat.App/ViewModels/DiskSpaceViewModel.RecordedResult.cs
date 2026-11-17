using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    private string? _capturedPreviewNotice;
    internal bool IsIndexedRuntimePreview => !_disposed && _indexedStorage && _session is not null && !_session.UsesMemoryFallback;
    internal string? RuntimePreviewNotice => _capturedPreviewNotice ?? (IsIndexedRuntimePreview
        ? "Live preview of a bounded subset. Marks and selection apply to this displayed revision. Open the recorded result after scanning to explore every recorded entry."
        : null);
    internal bool CanOpenRecordedResult => RecordedResultUnavailableReason is null;
    internal string? RecordedResultUnavailableReason => _disposed ? "This analysis is closed."
        : !IsIndexedRuntimePreview || _acquisition is null ? "This view has no pending full recorded result."
        : _actualWork.HasUnconfirmedClose || _bindingCloseError is not null || _session!.SourceCloseFailures != 0
            ? "Source cleanup could not be confirmed. Preserve this result before restarting FileCat."
        : IsPreparing || IsRescanning || !_session!.Completion.IsCompletedSuccessfully || _session.DatasetDescriptor is null
            ? "Wait for the scan and its source handles to finish closing."
        : _building || _interaction != 0 ? "Finish the current view update or chart interaction first."
        : Tree is null || _publishedView is null ? "Wait for the preview revision to be displayed."
        : null;

    internal async Task<AnalysisDatasetLease> AcquireRecordedDatasetAsync(CancellationToken cancellation)
    {
        if (RecordedResultUnavailableReason is { } reason) throw new InvalidOperationException(reason);
        return await _session!.AcquireDatasetAsync(cancellation) ?? throw new InvalidOperationException("The full recorded result is unavailable.");
    }

    // Called only after the full view has finished its fallible native preparation. No source
    // factory is decoded from observations. The caller either publishes or rolls back this move.
    internal RecordedResultHandoff DetachRecordedResultOwner()
    {
        if (RecordedResultUnavailableReason is { } reason) throw new InvalidOperationException(reason);
        var preview = CaptureDisplayedRevision();
        try { return new(this, preview); }
        catch { preview.Dispose(); throw; }
    }

    internal void RecordedResultPublished() => SetStatus("The recorded result opened in another tab. This captured preview, its marks and review basket were kept.");

    internal sealed class RecordedResultHandoff
    {
        private readonly DiskSpaceViewModel _model;
        private readonly AnalysisSession _session;
        private readonly AnalysisSnapshot? _observation;
        private readonly AnalysisSnapshotLease? _capturedOwner;
        private readonly string? _notice;
        private readonly AnalysisPhase _phase;
        private bool _settled;
        internal AnalysisIndexedAcquisition Acquisition { get; }

        internal RecordedResultHandoff(DiskSpaceViewModel model, AnalysisSnapshotLease preview)
        {
            _model = model; Acquisition = model._acquisition!; _session = model._session!;
            _observation = model._observation; _capturedOwner = model._capturedOwner; _notice = model._capturedPreviewNotice; _phase = model.Phase;
            model._projectionCancel?.Cancel(); ++model._generation;
            model._pending?.Lease?.Dispose(); model._pending?.Order.Dispose(); model._pending = null;
            ObjectDisposedException.ThrowIf(model._disposed, model);
            model._capturedOwner = preview; model._observation = preview.Snapshot;
            model._acquisition = null; model._session = null; model.Dirty = false;
            model.Phase = preview.Snapshot.Phase is AnalysisPhase.Scanning or AnalysisPhase.Refreshing or AnalysisPhase.Paused or AnalysisPhase.Pausing or AnalysisPhase.Ready
                ? AnalysisPhase.Stopped : preview.Snapshot.Phase;
            model._capturedPreviewNotice = "Captured preview. Its displayed subset and review references remain unchanged; the full recorded result has its own tab.";
        }

        internal void Commit() { _settled = true; }

        internal bool TryRollback()
        {
            if (_settled) return false; _settled = true;
            if (_model._disposed) return false; // The prepared view must close the moved owner.
            _model._capturedOwner?.Dispose(); _model._capturedOwner = _capturedOwner; _model._observation = _observation;
            _model._acquisition = Acquisition; _model._session = _session; _model.Phase = _phase;
            _model._capturedPreviewNotice = _notice;
            return true;
        }
    }
}
