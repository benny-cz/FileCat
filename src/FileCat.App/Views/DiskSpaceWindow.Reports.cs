using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.Core.Analysis;
using FileCat.App.Services;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    internal sealed record ReportOptions(AnalysisReportFormat Format, AnalysisReportScope Scope, bool RedactPaths)
    {
        internal AnalysisReportViewKind ViewKind { get; init; }
        internal AnalysisChartFormat? ChartFormat { get; init; }
        internal bool ComparisonBundle { get; init; }
        internal bool ComparisonReport { get; init; }
        internal int ChartWidth { get; init; } = 1280;
        internal int ChartHeight { get; init; } = 960;
        internal AnalysisReportFormat CaptureFormat => ChartFormat is null ? Format : AnalysisReportFormat.Csv;
        internal string Extension => ComparisonBundle ? ".fcdsc" : ChartFormat is { } chart ? chart == AnalysisChartFormat.Svg ? ".svg" : ".png" : Format switch { AnalysisReportFormat.Markdown => ".md", AnalysisReportFormat.Html => ".html", _ => ".csv" };
        internal string Description => ComparisonReport && ChartFormat is null ? (Scope == AnalysisReportScope.EntireResult ? "Export the entire unfiltered captured comparison. " : "Export the captured comparison query, cohort and ranking. ") + "Both previous and current value states, observed differences, compatibility, coverage and observation times are included. Shared aliases remain accounting rows; totals describe the full inventories. " + (Format == AnalysisReportFormat.Csv ? "CSV streams all accounting rows in the chosen scope and neutralizes formula-leading text. " : "Human detail is limited to the first 5,000 ranked rows; choose CSV for all rows. ") + (RedactPaths ? "Names, paths, source identifiers and query text are omitted throughout. " : "Sensitive displayed paths and query text are included. ") + "File contents, source connections and operation approvals are excluded." : ComparisonBundle ? "Export both entire captured inventories and the applied comparison metric, query, ranking, focus and chart settings in one self-contained .fcdsc bundle. Filters do not reduce the inventories. Exact raw names, locations, identities and observed metadata are included; this machine bundle cannot omit paths. File contents, current source connections and operation approvals are excluded. Opening the bundle restores offline observations." : ChartFormat is not null ? $"Export the current captured chart at {ChartWidth}×{ChartHeight}, including its metric, scope, observation time, legend and partial-state notice. Small items may be aggregated; this image does not include every table row. " +
            (RedactPaths ? "Names, locations and source identifiers are omitted throughout the image." : "Sensitive displayed names and scope are included.") + (ComparisonReport ? " Both observation intervals, the selected Previous/Current side, comparison limits and growth-color meaning are included. Table filters do not restrict this image." : "") + " Image memory is bounded; choose a smaller image if unavailable. File contents and operation approvals are excluded." : (Scope switch
        {
            AnalysisReportScope.FilteredView => ViewKind switch
            {
                AnalysisReportViewKind.GroupOverview or AnalysisReportViewKind.SmallGroups => "Export the captured accounting-group rows in the displayed order. Reference path statistics are descriptive and are not additional allocation.",
                AnalysisReportViewKind.GroupMembers or AnalysisReportViewKind.SmallGroupMembers => "Export the captured group member paths in the displayed order. Shared reference statistics do not add allocation.",
                AnalysisReportViewKind.SmallItems => "Export the captured Other small-item view. Changing table sort does not change which captured entries are included.",
                _ => "Export the captured filtered folder's immediate real rows in the displayed order."
            } + " Table pagination does not limit this export.",
            AnalysisReportScope.SelectedRows => "Export the marked real paths from the captured result. The summary describes the unfiltered source scope; marked rows are not added together.",
            _ => Format == AnalysisReportFormat.Csv ? "Export every real namespace row in the entire unfiltered captured result. Folder rollups overlap and must not be added together."
                : "Export a summary of the entire unfiltered result and up to 5,000 largest root-level real rows by the active metric. This is explicitly limited detail; choose full-result CSV for every row."
        }) + (Format != AnalysisReportFormat.Csv && Scope != AnalysisReportScope.EntireResult ? " Human reports include at most 5,000 rows; use CSV for all rows." : "") +
            (RedactPaths ? " Names, displayed locations and source identifiers are omitted throughout the report." : " Sensitive displayed names and locations are included.") +
            (Scope == AnalysisReportScope.FilteredView && ViewKind is AnalysisReportViewKind.GroupOverview or AnalysisReportViewKind.SmallGroups
                ? " Accounting groups remain explicitly labelled in this overview." : " Synthetic accounting buckets stay in the summary and are excluded from this namespace table.") + " File contents and operation approvals are excluded.";
    }

    internal static async Task<ReportOptions?> ReviewReportOptionsAsync(ViewModels.IDialogService dialogs, bool folderView, int marked, CancellationToken cancellation, AnalysisReportViewKind viewKind = AnalysisReportViewKind.NamespaceRows)
    {
        var format = new ComboBox { ItemsSource = new[] { "Markdown (.md)", "HTML (.html)", "Spreadsheet-safe CSV (.csv)", "Current chart · SVG (.svg)", "Current chart · PNG (.png)" }, SelectedIndex = 0 };
        var scopes = new List<(string Label, AnalysisReportScope Scope)>();
        if (folderView) scopes.Add((viewKind switch { AnalysisReportViewKind.GroupOverview or AnalysisReportViewKind.SmallGroups => "Captured accounting groups",
            AnalysisReportViewKind.GroupMembers or AnalysisReportViewKind.SmallGroupMembers => "Captured group member paths", AnalysisReportViewKind.SmallItems => "Captured Other small-item view", _ => "Captured filtered folder" }, AnalysisReportScope.FilteredView));
        if (marked != 0) scopes.Add(($"Marked real paths ({marked:N0})", AnalysisReportScope.SelectedRows));
        scopes.Add(("Entire unfiltered result", AnalysisReportScope.EntireResult));
        var scope = new ComboBox { ItemsSource = scopes.Select(value => value.Label).ToArray(), SelectedIndex = 0 };
        var redact = new CheckBox { Content = "Omit names, locations and source identifiers", IsChecked = true };
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var size = new ComboBox { ItemsSource = new[] { "960 × 720", "1280 × 960", "1600 × 1200" }, SelectedIndex = 1, IsVisible = false };
        AutomationProperties.SetName(size, "Chart image dimensions");
        AutomationProperties.SetName(format, "Report format"); AutomationProperties.SetName(scope, "Report scope");
        ReportOptions Read() => new(format.SelectedIndex < 3 ? (AnalysisReportFormat)format.SelectedIndex : AnalysisReportFormat.Markdown, scopes[scope.SelectedIndex].Scope, redact.IsChecked == true)
        { ViewKind = viewKind, ChartFormat = format.SelectedIndex >= 3 ? (AnalysisChartFormat)(format.SelectedIndex - 3) : null,
            ChartWidth = size.SelectedIndex switch { 0 => 960, 2 => 1600, _ => 1280 }, ChartHeight = size.SelectedIndex switch { 0 => 720, 2 => 1200, _ => 960 } };
        void Update()
        {
            bool chart = format.SelectedIndex >= 3; size.IsVisible = chart; scope.IsEnabled = !chart;
            if (chart && folderView && scope.SelectedIndex != 0) scope.SelectedIndex = 0;
            if (format.SelectedIndex >= 0 && scope.SelectedIndex >= 0) detail.Text = Read().Description;
        }
        format.SelectionChanged += (_, _) => Update(); scope.SelectionChanged += (_, _) => Update(); size.SelectionChanged += (_, _) => Update(); redact.IsCheckedChanged += (_, _) => Update(); Update();
        var body = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "Choose the captured report to share. These choices do not change the analysis or its marks.", TextWrapping = TextWrapping.Wrap },
            format, scope, size, redact, detail } };
        if (!folderView) body.Children.Add(new TextBlock { Text = "No current view is available to capture. The other scopes remain explicit choices.", TextWrapping = TextWrapping.Wrap });
        var closer = new ViewModels.DialogCloser();
        using var close = cancellation.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => closer.Close(false)));
        var choice = await dialogs.ShowCustomAsync("Choose report scope", body,
            [new ViewModels.DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new ViewModels.DialogButton("Choose destination…", true)
                { IsAvailable = () => format.SelectedIndex >= 0 && scope.SelectedIndex >= 0 && (Read().ChartFormat is null || folderView) && (Read().Scope != AnalysisReportScope.SelectedRows || Read().Format == AnalysisReportFormat.Csv || marked <= AnalysisLimits.ChartLeaves) }], format, closer: closer);
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
                var chart = (_chartMode.SelectedIndex == 1 ? (Controls.DiskSpace.AnalysisChartControl)_sunburst : _treemap).CaptureExportView();
                var view = CapturePublishedView(); var marks = _selection.Marks.Order().ToImmutableArray();
                await using var source = await _source.RetainAsync(cancel.Token);
                var viewKind = _grouping is null ? _explorer.OtherScopeKey is null ? AnalysisReportViewKind.NamespaceRows : AnalysisReportViewKind.SmallItems
                    : _groupKey is null ? _grouping.OtherScopeKey is null ? AnalysisReportViewKind.GroupOverview : AnalysisReportViewKind.SmallGroups
                    : _grouping.OtherScopeKey is null ? AnalysisReportViewKind.GroupMembers : AnalysisReportViewKind.SmallGroupMembers;
                var options = await ReviewReportOptionsAsync(_window._dialogs, true, marks.Length, cancel.Token, viewKind);
                if (options is null) return; cancel.Token.ThrowIfCancellationRequested();
                var filter = new AnalysisFilterRequest(view.Query, view.CaseSensitive, view.ShowUnknown, view.Root) { RootDisplayIds = view.RootDisplayIds };
                Task<AnalysisCapturedReport> captureTask = Capture();
                async Task<AnalysisCapturedReport> Capture() => options.Scope == AnalysisReportScope.FilteredView
                    ? _grouping is not null ? await source.CaptureViewReportAsync(_grouping, _groupKey, options.CaptureFormat, view.Sort, view.SortDescending, cancel.Token)
                        : await source.CaptureViewReportAsync(_explorer, _scope, options.CaptureFormat, SavedIndexedSort(view.Sort), view.SortDescending, cancel.Token)
                    : await source.CaptureReportAsync(view.Metric, options.CaptureFormat, options.Scope, filter, _scope, marks,
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
                    await using var image = options.ChartFormat is { } imageFormat ? await AnalysisCapturedChart.CaptureAsync(_services!.Io, report, chart ?? throw new InvalidOperationException("No current chart is available to capture."), imageFormat, options.ChartWidth, options.ChartHeight, options.RedactPaths, cancel.Token) : null;
                    var descriptor = source.Descriptor;
                    var status = await _window.ExportExternalInventoryAsync(_services!, ExportObservation(descriptor.Identity, descriptor.Coverage, descriptor.Taint), cancel.Token,
                        (prepared, folder, name, token) => image is null ? prepared.ExportReportAsync(report, folder, name, options.RedactPaths, token) : prepared.ExportChartAsync(image, folder, name, options.RedactPaths, token), RememberViewCloseFailure, options);
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
            if (_disposed || IsComparison || _persistenceBusy || !Model.CanExport || _actualWork.HasUnconfirmedClose || Model.HistoryServices is not { } services) return;
            using var actual = _actualWork.Enter(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Model.HistoryLifetime, _window._indexedLifetime.Token);
            _persistenceBusy = true; Model.Interaction(true); Refresh();
            try
            {
                var chart = (_mode.SelectedIndex == 1 ? (Controls.DiskSpace.AnalysisChartControl)_sunburst : _treemap).CaptureExportView();
                using var revision = Model.CaptureDisplayedRevision(); var tree = Model.Tree!; var view = Model.PublishedReportView;
                string folder = Model.Selection.Scope; var marks = Model.Selection.Marks.Order().ToImmutableArray();
                var scopeInfo = AnalysisSavedCatalog.SplitAggregationScope(folder);
                var viewKind = tree.GroupingDimension == AnalysisGroupingDimension.FolderHierarchy ? scopeInfo.Skipped == 0 ? AnalysisReportViewKind.NamespaceRows : AnalysisReportViewKind.SmallItems
                    : scopeInfo.Scope == AnalysisTree.RootKey ? scopeInfo.Skipped == 0 ? AnalysisReportViewKind.GroupOverview : AnalysisReportViewKind.SmallGroups
                    : scopeInfo.Skipped == 0 ? AnalysisReportViewKind.GroupMembers : AnalysisReportViewKind.SmallGroupMembers;
                var options = await ReviewReportOptionsAsync(_window._dialogs, true, marks.Length, cancel.Token, viewKind);
                if (options is null) return; cancel.Token.ThrowIfCancellationRequested();
                var captureTask = AnalysisCapturedMemoryReport.CaptureAsync(revision, tree, services.Io, options.CaptureFormat, options.Scope, folder, marks, view.Sort, view.SortDescending, cancel.Token,
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
                    await using var image = options.ChartFormat is { } imageFormat ? await AnalysisCapturedChart.CaptureAsync(services.Io, report, chart ?? throw new InvalidOperationException("No current chart is available to capture."), imageFormat, options.ChartWidth, options.ChartHeight, options.RedactPaths, cancel.Token) : null;
                    var snapshot = revision.Snapshot;
                    var status = await _window.ExportExternalInventoryAsync(services, ExportObservation(snapshot.Identity, snapshot.Coverage, snapshot.Taint), cancel.Token,
                        (prepared, destination, name, token) => image is null ? prepared.ExportReportAsync(report, destination, name, options.RedactPaths, token) : prepared.ExportChartAsync(image, destination, name, options.RedactPaths, token), _actualWork.RecordUnconfirmedClose, options);
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
