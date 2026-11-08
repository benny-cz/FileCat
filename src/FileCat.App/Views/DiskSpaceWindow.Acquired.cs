using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

internal sealed record AcquiredAnalysisView(AnalysisDatasetExplorer? Indexed, DiskSpaceViewModel? Memory);

public sealed partial class DiskSpaceWindow
{
    // Completed-acquisition handoff used by development/native qualification. Public command and
    // indexed admission remain gated by the full source/package/resource qualification packet.
    internal async Task<AcquiredAnalysisView> AddAcquiredSessionAsync(AnalysisSession session, AppServices? memoryServices = null,
        CancellationToken cancellation = default)
    {
        Dispatcher.UIThread.VerifyAccess(); ObjectDisposedException.ThrowIf(_closed, this);
        if (!session.Completion.IsCompletedSuccessfully) throw new InvalidOperationException("Wait for the acquisition to finish before transferring its result.");
        cancellation.ThrowIfCancellationRequested();
        var history = memoryServices is null ? null : await memoryServices.AnalysisHistory.CaptureCompletedAsync(session);
        cancellation.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_closed, this);
        await using var dataset = await session.AcquireDatasetAsync(cancellation);
        if (dataset is not null) return new(await AddIndexedSessionAsync(dataset, cancellation: cancellation, services: memoryServices, historyNotice: history?.Detail), null);
        cancellation.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_closed, this);
        AnalysisSnapshotLease? captured = session.AcquireSnapshot(); DiskSpaceViewModel? model = null;
        try
        {
            model = new(captured, memoryServices, session.UsesMemoryFallback); captured = null;
            model.HistoryNotice = history?.Detail;
            AddSession(model); var published = model; model = null;
            return new(null, published);
        }
        finally { model?.Dispose(); captured?.Dispose(); }
    }
}
