using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    // The factory is a process-owned capability supplied by native preparation or a runtime
    // predecessor. A data-only inventory/dataset cannot supply or reconstruct this factory.
    internal async Task<AcquiredAnalysisView> AddOwnedAcquisitionAsync(AppServices services,
        Func<CancellationToken, Task<AnalysisIndexedAcquisition>> prepare, AnalysisSavedView? view = null,
        CancellationToken cancellation = default,
        Func<AnalysisDatasetLease, CancellationToken, Task<AnalysisSavedView?>>? prepareIndexedView = null, Func<ulong?>? restoredFocus = null)
    {
        Dispatcher.UIThread.VerifyAccess(); ObjectDisposedException.ThrowIf(_closed, this);
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(prepare);
        AnalysisSessionAdmission.Slot? admission = _admissionPool.Acquire();
        if (_pendingIndexed++ == 0) _indexedIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _indexedLifetime.Token);
        AnalysisIndexedAcquisition? acquisition = null; DiskSpaceViewModel? model = null;
        try
        {
            acquisition = await prepare(linked.Token);
            var session = acquisition.RuntimeSession;
            using var stop = linked.Token.Register(session.Stop);
            await session.Completion.WaitAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_closed, this);
            if (session.DatasetDescriptor is not null)
            {
                var captured = acquisition; acquisition = null;
                var slot = admission; admission = null;
                var explorer = await AddPreparedIndexedSessionAsync(async token =>
                    await session.AcquireDatasetAsync(token) ?? throw new InvalidOperationException("The completed indexed result is unavailable."),
                    view?.Metric ?? AnalysisMetric.OnDisk, null, view, linked.Token, services,
                    additionalOwner: captured, reservedAdmission: slot, runtimeAcquisition: captured,
                    prepareView: prepareIndexedView, restoredFocus: restoredFocus);
                return new(explorer, null);
            }
            model = DiskSpaceViewModel.AdoptAcquisition(acquisition, services, view); acquisition = null;
            await model.RebuildAsync(true, linked.Token);
            linked.Token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_closed, this);
            if (model.Tree is null) throw new InvalidOperationException("The memory result could not be prepared.");
            var memorySlot = admission; admission = null;
            AddReservedSession(model, [], memorySlot!);
            var published = model; model = null; return new(null, published);
        }
        finally
        {
            Task CloseUnpublishedAsync()
            {
                var closes = new List<Task>();
                if (acquisition is not null) closes.Add(acquisition.DisposeAsync().AsTask());
                if (model is not null) { model.Dispose(); closes.Add(model.CloseCompletion); }
                return Task.WhenAll(closes);
            }
            try
            {
                var close = CloseUnpublishedAsync();
                if (admission is not null) await admission.CloseAfterAsync(close); else await close;
            }
            catch (Exception error)
            { RecordIndexedCloseFailure(error); throw; }
            finally
            {
                if (--_pendingIndexed == 0) { _indexedIdle.TrySetResult(); if (_closed) _indexedLifetime.Dispose(); }
            }
        }
    }

    private sealed partial class IndexedSessionView
    {
        internal AnalysisIndexedAcquisition? _runtimeAcquisition;
    }
}
