using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    internal sealed record ReportOptions(AnalysisReportFormat Format, AnalysisReportScope Scope, bool RedactPaths)
    {
        internal string Extension => Format switch { AnalysisReportFormat.Markdown => ".md", AnalysisReportFormat.Html => ".html", _ => ".csv" };
        internal string Description => (Scope switch
        {
            AnalysisReportScope.FilteredView => "Export the captured filtered folder's immediate real rows in the displayed order. Table pagination does not limit this export.",
            AnalysisReportScope.SelectedRows => "Export the marked real paths from the captured result. The summary describes the unfiltered source scope; marked rows are not added together.",
            _ => Format == AnalysisReportFormat.Csv ? "Export every real namespace row in the entire unfiltered captured result. Folder rollups overlap and must not be added together."
                : "Export a summary of the entire unfiltered result and up to 5,000 root-level real rows. This is explicitly limited detail; choose full-result CSV for every row."
        }) + (Format != AnalysisReportFormat.Csv && Scope != AnalysisReportScope.EntireResult ? " Human reports include at most 5,000 rows; use CSV for all rows." : "") +
            (RedactPaths ? " Names, displayed locations and source identifiers are omitted throughout the report." : " Sensitive displayed names and locations are included.") +
            " Synthetic accounting buckets stay in the summary and are excluded from this namespace table. File contents and operation approvals are excluded.";
    }

    internal static async Task<ReportOptions?> ReviewReportOptionsAsync(ViewModels.IDialogService dialogs, bool folderView, int marked, CancellationToken cancellation)
    {
        var format = new ComboBox { ItemsSource = new[] { "Markdown (.md)", "HTML (.html)", "Spreadsheet-safe CSV (.csv)" }, SelectedIndex = 0 };
        var scopes = new List<(string Label, AnalysisReportScope Scope)>();
        if (folderView) scopes.Add(("Captured filtered folder", AnalysisReportScope.FilteredView));
        if (marked != 0) scopes.Add(($"Marked real paths ({marked:N0})", AnalysisReportScope.SelectedRows));
        scopes.Add(("Entire unfiltered result", AnalysisReportScope.EntireResult));
        var scope = new ComboBox { ItemsSource = scopes.Select(value => value.Label).ToArray(), SelectedIndex = 0 };
        var redact = new CheckBox { Content = "Omit names, locations and source identifiers", IsChecked = true };
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(format, "Report format"); AutomationProperties.SetName(scope, "Report scope");
        ReportOptions Read() => new((AnalysisReportFormat)format.SelectedIndex, scopes[scope.SelectedIndex].Scope, redact.IsChecked == true);
        void Update() { if (format.SelectedIndex >= 0 && scope.SelectedIndex >= 0) detail.Text = Read().Description; }
        format.SelectionChanged += (_, _) => Update(); scope.SelectionChanged += (_, _) => Update(); redact.IsCheckedChanged += (_, _) => Update(); Update();
        var body = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "Choose the captured report to share. These choices do not change the analysis or its marks.", TextWrapping = TextWrapping.Wrap },
            format, scope, redact, detail } };
        if (!folderView) body.Children.Add(new TextBlock { Text = "To export the current filtered folder, first switch to folder hierarchy. Group and small-item scopes are not substituted with another scope.", TextWrapping = TextWrapping.Wrap });
        var closer = new ViewModels.DialogCloser();
        using var close = cancellation.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => closer.Close(false)));
        var choice = await dialogs.ShowCustomAsync("Choose report scope", body,
            [new ViewModels.DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new ViewModels.DialogButton("Choose destination…", true)
                { IsAvailable = () => format.SelectedIndex >= 0 && scope.SelectedIndex >= 0 && (Read().Scope != AnalysisReportScope.SelectedRows || Read().Format == AnalysisReportFormat.Csv || marked <= AnalysisLimits.ChartLeaves) }], format, closer: closer);
        return choice is true ? Read() : null;
    }

    private sealed partial class IndexedSessionView
    {
        private readonly Button _exportReport = new() { Content = "Export report…" };
        private async Task ExportReportAsync()
        {
            if (_disposed || _persistenceBusy || !HasInventoryStore) return;
            using var actual = _actualWork.Enter(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token);
            _persistenceBusy = true; Interaction(true); RefreshIndexedPersistence();
            try
            {
                var view = CapturePublishedView(); var marks = _selection.Marks.Order().ToImmutableArray();
                await using var source = await _source.RetainAsync(cancel.Token);
                var options = await ReviewReportOptionsAsync(_window._dialogs, _grouping is null && _explorer.OtherScopeKey is null, marks.Length, cancel.Token);
                if (options is null) return; cancel.Token.ThrowIfCancellationRequested();
                var filter = new AnalysisFilterRequest(view.Query, view.CaseSensitive, view.ShowUnknown, view.Root) { RootDisplayIds = view.RootDisplayIds };
                var captureTask = source.CaptureReportAsync(view.Metric, options.Format, options.Scope, filter, _scope, marks,
                    _shownPage?.Scope is { } shownFolder ? AnalysisNames.DisplayName(shownFolder.Path) : "All captured locations",
                    SavedIndexedSort(view.Sort), view.SortDescending, cancel.Token);
                var closer = new ViewModels.DialogCloser();
                try
                {
                    if (!captureTask.IsCompleted)
                    {
                        var dismissed = _window._dialogs.ShowCustomAsync("Preparing captured report", new TextBlock { Text = "Preparing the immutable report scope. Cancel waits for outstanding captured-data work to stop.", TextWrapping = TextWrapping.Wrap },
                            [new ViewModels.DialogButton("Cancel", false, IsDefault: true, IsCancel: true)], closer: closer);
                        if (await Task.WhenAny(captureTask, dismissed) == dismissed) cancel.Cancel();
                    }
                    await using var report = await captureTask; closer.Close(null);
                    var descriptor = source.Descriptor;
                    var status = await _window.ExportExternalInventoryAsync(_services!, ExportObservation(descriptor.Identity, descriptor.Coverage, descriptor.Taint), cancel.Token,
                        (prepared, folder, name, token) => prepared.ExportReportAsync(report, folder, name, options.RedactPaths, token), RememberViewCloseFailure, options);
                    if (!_disposed && status is not null) PersistenceStatus = status;
                }
                finally { closer.Close(null); }
            }
            catch (OperationCanceledException) { if (!_disposed) PersistenceStatus = "Report canceled. No successful full output is claimed."; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or AggregateException or OverflowException or FormatException)
            { RememberViewCloseFailure(error); if (!_disposed) PersistenceStatus = "Report or captured-data release failed; inspect any existing output before retrying: " + AnalysisNames.Display(error.Message, 600); }
            finally { _persistenceBusy = false; Interaction(false); if (!_disposed) RefreshIndexedPersistence(); }
        }
    }
    private sealed partial class SessionView
    {
        private readonly Button _exportReport = new() { Content = "Export report…" };
        private async Task ExportReportAsync()
        {
            if (_disposed || _persistenceBusy || !Model.CanExport || _actualWork.HasUnconfirmedClose || Model.HistoryServices is not { } services) return;
            using var actual = _actualWork.Enter(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Model.HistoryLifetime, _window._indexedLifetime.Token);
            _persistenceBusy = true; Model.Interaction(true); Refresh();
            try
            {
                using var revision = Model.CaptureDisplayedRevision(); var tree = Model.Tree!; var view = Model.PublishedReportView;
                string folder = Model.Selection.Scope; var marks = Model.Selection.Marks.Order().ToImmutableArray();
                var options = await ReviewReportOptionsAsync(_window._dialogs, tree.GroupingDimension == AnalysisGroupingDimension.FolderHierarchy && !folder.StartsWith("other:", StringComparison.Ordinal), marks.Length, cancel.Token);
                if (options is null) return; cancel.Token.ThrowIfCancellationRequested();
                var captureTask = AnalysisCapturedMemoryReport.CaptureAsync(revision, tree, services.Io, options.Format, options.Scope, folder, marks, view.Sort, view.SortDescending, cancel.Token,
                    context: new(view.Query, view.CaseSensitive, view.ShowUnknown, view.Root));
                var closer = new ViewModels.DialogCloser();
                try
                {
                    if (!captureTask.IsCompleted)
                    {
                        var dismissed = _window._dialogs.ShowCustomAsync("Preparing captured report", new TextBlock { Text = "Preparing the immutable report scope. Cancel waits for outstanding captured-data work to stop.", TextWrapping = TextWrapping.Wrap },
                            [new ViewModels.DialogButton("Cancel", false, IsDefault: true, IsCancel: true)], closer: closer);
                        if (await Task.WhenAny(captureTask, dismissed) == dismissed) cancel.Cancel();
                    }
                    await using var report = await captureTask; closer.Close(null);
                    var snapshot = revision.Snapshot;
                    var status = await _window.ExportExternalInventoryAsync(services, ExportObservation(snapshot.Identity, snapshot.Coverage, snapshot.Taint), cancel.Token,
                        (prepared, destination, name, token) => prepared.ExportReportAsync(report, destination, name, options.RedactPaths, token), _actualWork.RecordUnconfirmedClose, options);
                    if (!_disposed && status is not null) Model.SetStatus(status);
                }
                finally { closer.Close(null); }
            }
            catch (OperationCanceledException) { if (!_disposed) Model.SetStatus("Report canceled. No successful full output is claimed."); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or AggregateException or OverflowException or FormatException)
            { _actualWork.RecordUnconfirmedClose(error); if (!_disposed) Model.SetStatus("Report or captured-data release failed; inspect any existing output before retrying: " + AnalysisNames.Display(error.Message, 600)); }
            finally { _persistenceBusy = false; Model.Interaction(false); if (!_disposed) Refresh(); }
        }
    }
}
