using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private async Task ShowHistoryAsync()
        {
            if (_disposed || Model.HistoryServices is not { } services) return;
            using var actual = _actualWork.Enter();
            await AnalysisHistoryBrowser.ShowAsync(services, _window._dialogs, Model.HistoryLifetime);
        }
    }
    private sealed partial class IndexedSessionView
    {
        private async Task ShowHistoryAsync()
        {
            if (_disposed || _services is null) return;
            using var actual = _actualWork.Enter();
            await AnalysisHistoryBrowser.ShowAsync(_services, _window._dialogs, _selectionLifetime.Token);
        }
    }
    internal async Task<AnalysisDatasetExplorer> AddHistorySessionAsync(AppServices services, AnalysisHistoryOpenSource source, CancellationToken cancellation)
    {
        try
        {
            return await AddPreparedIndexedSessionAsync(source.AcquireAsync, source.Entry.View.Metric, null, source.Entry.View, cancellation,
                services, historyNotice: "Historical observations · protected from automatic retention while this view is open. Save a named copy to keep them independently.", additionalOwner: source);
        }
        catch { await source.DisposeAsync(); throw; }
    }
}
