using FileCat.Core.Analysis;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.App.Services;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    internal static async Task<ReportOptions?> ReviewComparisonReportOptionsAsync(IDialogService dialogs, CancellationToken cancellation, bool chartAvailable = false)
    {
        var format = new ComboBox { ItemsSource = new[] { "Markdown (.md)", "HTML (.html)", "Spreadsheet-safe CSV (.csv)", "Current comparison chart · SVG (.svg)", "Current comparison chart · PNG (.png)" }, SelectedIndex = 0 };
        var scope = new ComboBox { ItemsSource = new[] { "Captured comparison query, cohort and ranking", "Entire unfiltered comparison" }, SelectedIndex = 0 };
        var redact = new CheckBox { Content = "Omit names, paths, source identifiers and query text", IsChecked = true };
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var size = new ComboBox { ItemsSource = new[] { "960 × 720", "1280 × 960", "1600 × 1200" }, SelectedIndex = 1, IsVisible = false };
        AutomationProperties.SetName(size, "Comparison chart image dimensions");
        AutomationProperties.SetName(format, "Comparison report format"); AutomationProperties.SetName(scope, "Comparison report scope");
        ReportOptions Read() => new(format.SelectedIndex < 3 ? (AnalysisReportFormat)format.SelectedIndex : AnalysisReportFormat.Markdown,
            scope.SelectedIndex == 1 ? AnalysisReportScope.EntireResult : AnalysisReportScope.FilteredView, redact.IsChecked == true)
        { ComparisonReport = true, ChartFormat = format.SelectedIndex >= 3 ? (AnalysisChartFormat)(format.SelectedIndex - 3) : null,
            ChartWidth = size.SelectedIndex switch { 0 => 960, 2 => 1600, _ => 1280 }, ChartHeight = size.SelectedIndex switch { 0 => 720, 2 => 1200, _ => 960 } };
        void Update()
        {
            bool chart = format.SelectedIndex >= 3; size.IsVisible = chart; scope.IsEnabled = !chart;
            if (chart && scope.SelectedIndex != 0) scope.SelectedIndex = 0;
            if (format.SelectedIndex >= 0 && scope.SelectedIndex >= 0) detail.Text = Read().Description + (chart && !chartAvailable ? " Select an available comparison Treemap or Sunburst before exporting an image." : "");
        }
        format.SelectionChanged += (_, _) => Update(); scope.SelectionChanged += (_, _) => Update(); size.SelectionChanged += (_, _) => Update(); redact.IsCheckedChanged += (_, _) => Update(); Update();
        var closer = new DialogCloser();
        using var close = cancellation.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => closer.Close(false)));
        return await dialogs.ShowCustomAsync("Choose comparison report", new StackPanel { Spacing = 8, Children = { format, scope, size, redact, detail } },
            [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Choose destination…", true)
                { IsAvailable = () => format.SelectedIndex is >= 0 and <= 4 && scope.SelectedIndex is 0 or 1 && (format.SelectedIndex < 3 || chartAvailable) }], format, closer: closer) is true ? Read() : null;
    }
    private sealed partial class SessionView
    {
        private async Task ExportComparisonReportAsync()
        {
            if (_disposed || _persistenceBusy || _comparisonChartBusy || _comparisonChartUnapplied || !Model.CanExportComparison || _actualWork.HasUnconfirmedClose || Model.HistoryServices is not { } services) return;
            using var actual = _actualWork.Enter(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Model.HistoryLifetime, _window._indexedLifetime.Token);
            _persistenceBusy = true; RefreshComparisonPersistence();
            try
            {
                using var captured = Model.CaptureComparison(forExport: true);
                var comparison = Model.Comparison!;
                var chartData = _comparisonChartData;
                var chart = captured.View.Chart == AnalysisComparisonChartMode.Table ? null :
                    (captured.View.Chart == AnalysisComparisonChartMode.Sunburst ? (Controls.DiskSpace.AnalysisChartControl?)_comparisonSunburst : _comparisonTreemap)?.CaptureExportView();
                var options = await ReviewComparisonReportOptionsAsync(_window._dialogs, cancel.Token, chart is not null && chartData is not null);
                if (options is null) return;
                if (options.ChartFormat is { } chartFormat)
                {
                    if (chart is null || chartData is null) throw new InvalidOperationException("Choose an available comparison chart first.");
                    await using var owner = captured.Retain();
                    await using var scopeReport = await chartData.CaptureReportAsync(services.Io, captured.View.ChartScope, cancel.Token);
                    var previous = captured.Previous.Snapshot; var current = captured.Current.Snapshot;
                    var context = new AnalysisComparisonChartContext(previous.Identity, current.Identity, captured.View.PreviousChart,
                        previous.Coverage, current.Coverage, previous.Freshness, current.Freshness, captured.Taint, comparison.Compatibility, Model.ComparisonIsStructural);
                    await using var image = await AnalysisCapturedChart.CaptureComparisonAsync(services.Io, scopeReport, owner, chart, context,
                        chartFormat, options.ChartWidth, options.ChartHeight, options.RedactPaths, cancel.Token);
                    var chartStatus = await _window.ExportExternalInventoryAsync(services,
                        "Previous: " + ExportObservation(previous.Identity, previous.Coverage, previous.Taint) + "\nCurrent: " + ExportObservation(current.Identity, current.Coverage, current.Taint), cancel.Token,
                        (prepared, folder, name, token) => prepared.ExportChartAsync(image, folder, name, options.RedactPaths, token), _actualWork.RecordUnconfirmedClose, options);
                    if (!_disposed && chartStatus is not null) Model.SetStatus(chartStatus);
                    return;
                }
                await using var report = await AnalysisCapturedComparisonReport.CaptureAsync(services.Io, captured.Previous, captured.Current,
                    comparison, captured.View, options.Format, options.Scope == AnalysisReportScope.EntireResult, cancel.Token, captured.PreviousRootIds, captured.CurrentRootIds);
                string observed = "Previous: " + ExportObservation(captured.Previous.Snapshot.Identity, captured.Previous.Snapshot.Coverage, captured.Previous.Snapshot.Taint) +
                    "\nCurrent: " + ExportObservation(captured.Current.Snapshot.Identity, captured.Current.Snapshot.Coverage, captured.Current.Snapshot.Taint) +
                    "\nCaptured accounting rows: " + report.Rows.ToString("N0") + (report.DetailLimited ? " of " + report.MatchingRows.ToString("N0") + "; detail limited" : "");
                var status = await _window.ExportExternalInventoryAsync(services, observed, cancel.Token,
                    (prepared, folder, name, token) => prepared.ExportComparisonReportAsync(report, folder, name, options.RedactPaths, token), _actualWork.RecordUnconfirmedClose, options);
                if (!_disposed && status is not null) Model.SetStatus(status);
            }
            catch (OperationCanceledException) { if (!_disposed) Model.SetStatus("Comparison report canceled. No successful full output is claimed."); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or AggregateException or OverflowException or FormatException)
            { _actualWork.RecordUnconfirmedClose(error); if (!_disposed) Model.SetStatus("Comparison report was not exported: " + AnalysisNames.Display(error.Message, 600)); }
            finally { _persistenceBusy = false; if (!_disposed) RefreshComparisonPersistence(); }
        }
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
