using FileCat.Core.Analysis;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.ViewModels;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    internal static async Task<ReportOptions?> ReviewComparisonReportOptionsAsync(IDialogService dialogs, CancellationToken cancellation)
    {
        var format = new ComboBox { ItemsSource = new[] { "Markdown (.md)", "HTML (.html)", "Spreadsheet-safe CSV (.csv)" }, SelectedIndex = 0 };
        var scope = new ComboBox { ItemsSource = new[] { "Captured comparison query, cohort and ranking", "Entire unfiltered comparison" }, SelectedIndex = 0 };
        var redact = new CheckBox { Content = "Omit names, paths, source identifiers and query text", IsChecked = true };
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(format, "Comparison report format"); AutomationProperties.SetName(scope, "Comparison report scope");
        ReportOptions Read() => new((AnalysisReportFormat)format.SelectedIndex, scope.SelectedIndex == 1 ? AnalysisReportScope.EntireResult : AnalysisReportScope.FilteredView, redact.IsChecked == true) { ComparisonReport = true };
        void Update() { if (format.SelectedIndex >= 0 && scope.SelectedIndex >= 0) detail.Text = Read().Description; }
        format.SelectionChanged += (_, _) => Update(); scope.SelectionChanged += (_, _) => Update(); redact.IsCheckedChanged += (_, _) => Update(); Update();
        var closer = new DialogCloser();
        using var close = cancellation.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => closer.Close(false)));
        return await dialogs.ShowCustomAsync("Choose comparison report", new StackPanel { Spacing = 8, Children = { format, scope, redact, detail } },
            [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Choose destination…", true)
                { IsAvailable = () => format.SelectedIndex is >= 0 and <= 2 && scope.SelectedIndex is 0 or 1 }], format, closer: closer) is true ? Read() : null;
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
                var options = await ReviewComparisonReportOptionsAsync(_window._dialogs, cancel.Token);
                if (options is null) return;
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
