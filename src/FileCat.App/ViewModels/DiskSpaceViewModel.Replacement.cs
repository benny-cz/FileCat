using FileCat.Core.Analysis;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    internal async Task<Services.AnalysisOwnedReplacementCapture> PrepareSavedReplacementAsync(Guid previous, AnalysisSnapshotLease captured, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HasPrivateStorage || IsSaving || Tree?.Snapshot.Identity != captured.Snapshot.Identity || _publishedView is not { } shown)
            throw new InvalidOperationException("The displayed revision or private storage changed. Capture the view again before saving.");
        var view = shown with { Chart = Chart, Scope = Selection.Scope, ColorDimension = ColorDimension };
        IsSaving = true;
        try
        {
            return await Services.AnalysisOwnedStoreAccess.PrepareReplacementAsync(_services!, previous, captured.Snapshot, view, _actualWork, Closed, cancellation);
        }
        catch { Closed(); throw; }
        void Closed() { IsSaving = false; if (!_disposed) Changed?.Invoke(); }
    }
    internal Task<AnalysisSavedEntry?> FindSavedReplacementAsync(string name, CancellationToken cancellation)
    {
        if (!HasPrivateStorage) throw new InvalidOperationException("Private inventory storage is unavailable.");
        return Services.AnalysisOwnedStoreAccess.FindReplacementTargetAsync(_services!, name, cancellation);
    }
    internal Task<Services.AnalysisOwnedRecoveryCapture> PrepareSavedRecoveryAsync(Guid operation, CancellationToken cancellation)
    {
        if (!HasPrivateStorage) throw new InvalidOperationException("Private inventory storage is unavailable.");
        return Services.AnalysisOwnedStoreAccess.PrepareRecoveryAsync(_services!, operation, _actualWork, cancellation);
    }
    internal void RememberReplacement(AnalysisSnapshotLease captured, AnalysisInventoryReceipt receipt)
    {
        _savedRevision = captured.Snapshot.Identity; _savedInventoryId = receipt.InventoryId; SavedInventoryName = receipt.Name;
        _savedCopyLost = false;
        if (!_disposed) Changed?.Invoke();
    }
}
