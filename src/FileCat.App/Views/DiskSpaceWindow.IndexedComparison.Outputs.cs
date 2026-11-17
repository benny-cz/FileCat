using Avalonia.Controls;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class IndexedComparisonView
    {
        private Button _exportReport = null!;
        private void AddOutputs()
        {
            AddBundleControls();
            _exportReport = Button("Export comparison report…", () => _ = ExportReportAsync(),
                "Export the applied query/cohort/order or entire comparison as Markdown, HTML or spreadsheet-safe CSV. A displayed chart can be exported as SVG or PNG. Names and paths are omitted by default.");
            Children.Add(_exportReport);
        }
        private void RefreshOutputs(bool usable)
        {
            bool enabled = usable && !_chartInteracting && _result is not null && _services is { Io.IsStopped: false };
            _exportReport.IsEnabled = _saveBundle.IsEnabled = _exportBundle.IsEnabled = enabled;
            _openBundle.IsEnabled = enabled && _window.CanAddSession;
        }
        private Task ExportReportAsync() => RunAsync("Exporting captured comparison observations…", async token =>
        {
            if (_result is null || _services is not { } services) return;
            var result = _result; var view = _applied;
            AnalysisChartControl? chart = _chartProjection is null || _chartOwner is null || _chartMode == 0 ? null :
                (_chartMode == 2 ? (AnalysisChartControl)_chartSunburst : _chartTreemap).CaptureExportView();
            var options = await ReviewComparisonReportOptionsAsync(_window._dialogs, token, chart is not null); if (options is null) return;
            string observations = "Previous: " + ExportObservation(_previousSource.Descriptor.Identity, _previousSource.Descriptor.Coverage, result.Taint) +
                "\nCurrent: " + ExportObservation(_currentSource.Descriptor.Identity, _currentSource.Descriptor.Coverage, result.Taint);
            if (options.ChartFormat is { } format)
            {
                if (chart is null || _chartOwner is null || _chartProjection is null) throw new InvalidOperationException("Apply an available comparison chart first.");
                await using var captured = await _chartOwner.CaptureScopeAsync(_chartProjection, token);
                await using var image = await AnalysisCapturedChart.CaptureComparisonAsync(services.Io, captured, chart, format,
                    options.ChartWidth, options.ChartHeight, options.RedactPaths, token);
                var status = await _window.ExportExternalInventoryAsync(services, observations, token,
                    (prepared, folder, name, stop) => prepared.ExportChartAsync(image, folder, name, options.RedactPaths, stop), _work.RecordUnconfirmedClose, options);
                if (!_disposed && status is not null) _status.Text = status;
            }
            else
            {
                await using var report = await result.CaptureReportAsync(view, options.Format, _queryResult, options.Scope == AnalysisReportScope.EntireResult, token);
                string described = observations + "\nCaptured accounting rows: " + report.Rows.ToString("N0") +
                    (report.DetailLimited ? " of " + report.MatchingRows.ToString("N0") + "; detail limited" : "");
                var status = await _window.ExportExternalInventoryAsync(services, described, token,
                    (prepared, folder, name, stop) => prepared.ExportComparisonReportAsync(report, folder, name, options.RedactPaths, stop), _work.RecordUnconfirmedClose, options);
                if (!_disposed && status is not null) _status.Text = status;
            }
        });
    }
}
