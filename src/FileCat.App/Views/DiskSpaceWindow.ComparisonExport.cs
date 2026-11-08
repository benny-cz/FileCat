using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private async Task ExportComparisonBundleAsync()
        {
            if (_disposed || _persistenceBusy || _comparisonChartBusy || _comparisonChartUnapplied || !Model.CanExportComparison || _actualWork.HasUnconfirmedClose || Model.HistoryServices is not { } services) return;
            using var actual = _actualWork.Enter(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Model.HistoryLifetime, _window._indexedLifetime.Token);
            _persistenceBusy = true; RefreshComparisonPersistence();
            try
            {
                using var captured = Model.CaptureComparison(forExport: true);
                await using var bundle = AnalysisCapturedComparisonBundle.Capture(services.Io, captured.Previous, captured.Current, captured.View,
                    captured.Roots, captured.PreviousLabel, captured.PreviousRootIds, captured.CurrentRootIds);
                var previous = captured.Previous.Snapshot; var current = captured.Current.Snapshot;
                string observed = "Previous: " + ExportObservation(previous.Identity, previous.Coverage, previous.Taint) + "\nCurrent: " + ExportObservation(current.Identity, current.Coverage, current.Taint);
                var options = new ReportOptions(AnalysisReportFormat.Markdown, AnalysisReportScope.EntireResult, false) { ComparisonBundle = true };
                var status = await _window.ExportExternalInventoryAsync(services, observed, cancel.Token,
                    (prepared, folder, name, token) => prepared.ExportComparisonAsync(bundle, folder, name, token), _actualWork.RecordUnconfirmedClose, options);
                if (!_disposed && status is not null) Model.SetStatus(status);
            }
            catch (OperationCanceledException) { if (!_disposed) Model.SetStatus("Comparison export canceled. No successful full output is claimed."); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or AggregateException or OverflowException or FormatException)
            { _actualWork.RecordUnconfirmedClose(error); if (!_disposed) Model.SetStatus("Comparison export or captured-data release failed; inspect any existing output before retrying: " + AnalysisNames.Display(error.Message, 600)); }
            finally { _persistenceBusy = false; if (!_disposed) RefreshComparisonPersistence(); }
        }
    }
}
