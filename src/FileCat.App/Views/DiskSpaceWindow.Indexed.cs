using System.Collections.Immutable;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    // Internal until the indexed acquisition/profile-quota and complete product gates are qualified.
    internal async Task<AnalysisDatasetExplorer> AddIndexedSessionAsync(AnalysisDatasetLease source, AnalysisMetric metric = AnalysisMetric.OnDisk,
        AnalysisFilterRequest? filter = null, CancellationToken cancellation = default)
    {
        Dispatcher.UIThread.VerifyAccess(); ObjectDisposedException.ThrowIf(_closed, this);
        if (_sessions.Count + _pendingIndexed + _indexedClosures.Count >= AnalysisLimits.Sessions) throw new InvalidOperationException("Eight analyses are open or closing. Wait for an analysis to close before adding another.");
        if (_pendingIndexed++ == 0) _indexedIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AnalysisDatasetLease? retained = null; AnalysisDatasetExplorer? explorer = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _indexedLifetime.Token);
        try
        {
            retained = await source.RetainAsync(linked.Token);
            explorer = await AnalysisDatasetExplorer.CreateAsync(retained, metric, filter, linked.Token);
            await Task.WhenAll(explorer.LoadPageAsync(cancellation: linked.Token), explorer.LoadChartAsync(cancellation: linked.Token));
            linked.Token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_closed, this);
            var view = new IndexedSessionView(this, retained, explorer, filter ?? new(""));
            AddView(view); retained = null; var result = explorer; explorer = null; return result;
        }
        finally
        {
            try { if (explorer is not null) await explorer.DisposeAsync(); }
            finally
            {
                try { if (retained is not null) await retained.DisposeAsync(); }
                finally { if (--_pendingIndexed == 0) { _indexedIdle.TrySetResult(); if (_closed) _indexedLifetime.Dispose(); } }
            }
        }
    }

    private sealed class IndexedSessionView : StackPanel, IAnalysisSessionView
    {
        private readonly DiskSpaceWindow _window;
        private readonly AnalysisDatasetLease _source;
        private AnalysisDatasetExplorer _explorer;
        private AnalysisExplorerPage? _shownPage;
        private AnalysisIndexedChart? _shownChart;
        private AnalysisExplorerInspection? _shownInspection;
        private readonly ComboBox _metric = new() { ItemsSource = new[] { "File size", "Unique file size", "On disk — reported", "Files" }, MinWidth = 180 };
        private readonly ComboBox _chartMode = new() { ItemsSource = new[] { "Treemap", "Sunburst", "Table" }, SelectedIndex = 0 };
        private readonly ComboBox _sort = new() { ItemsSource = new[] { "Active metric", "File paths", "File size", "Name", "Metadata state" }, SelectedIndex = 0 };
        private readonly CheckBox _descending = new() { Content = "Descending", IsChecked = true };
        private readonly ComboBox _roots = new() { MinWidth = 160 };
        private readonly TextBox _query = new() { PlaceholderText = "Search within observations…", MinWidth = 200, MaxLength = AnalysisLimits.QueryBytes };
        private readonly CheckBox _case = new() { Content = "Case sensitive" }, _unknown = new() { Content = "Inspect uncertain query results" };
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap }, _summary = new() { TextWrapping = TextWrapping.Wrap }, _range = new(), _inspectorStatus = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBox _details = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180, MaxHeight = 420 };
        private readonly Grid _body = new() { ColumnSpacing = 12, RowSpacing = 12 };
        private readonly StackPanel _main = new() { Spacing = 8 }, _inspector = new() { Spacing = 8 };
        private bool? _wide;
        private readonly StackPanel _breadcrumbs = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        private readonly StackPanel _buckets = new() { Spacing = 4 };
        private readonly ListBox _list = new() { MinHeight = 220, MaxHeight = 420 };
        private readonly TreemapControl _treemap = new() { Height = 300 };
        private readonly SunburstControl _sunburst = new() { Height = 300, IsVisible = false };
        private readonly Button _previous, _next, _parent, _back, _forward;
        private readonly Button[] _evidence = new Button[6];
        private readonly List<(ulong Parent, ulong Offset)> _past = [], _future = [];
        private readonly List<ulong> _pageOffsets = [];
        private CancellationTokenSource? _buildCancel, _navigationCancel;
        private Task _buildTask = Task.CompletedTask;
        private TaskCompletionSource? _interactionEnd;
        private bool _interacting;
        private ulong _scope, _offset, _focus;
        private int _navigation, _inspection;
        private bool _disposed, _building, _again, _publishing;
        private string? _rootId;
        public bool HasTemporaryWork => _source.Descriptor.Persistence == AnalysisPersistence.Temporary;
        public bool IsComparison => false;

        internal IndexedSessionView(DiskSpaceWindow window, AnalysisDatasetLease source, AnalysisDatasetExplorer explorer, AnalysisFilterRequest filter)
        {
            _window = window; _source = source; _explorer = explorer; _rootId = filter.RootId;
            Spacing = 10; Margin = new Thickness(10);
            _metric.SelectedIndex = (int)explorer.Metric; _query.Text = filter.Text; _case.IsChecked = filter.CaseSensitive; _unknown.IsChecked = filter.UnknownOnly;
            _roots.ItemsSource = new[] { "All captured locations" }.Concat(source.Descriptor.Roots.Select(root => AnalysisNames.Display(root.Label, 120) + " [" + AnalysisNames.Display(root.Id, 64) + "]")).ToArray();
            _roots.SelectedIndex = filter.RootId is null ? 0 : 1 + Array.FindIndex(source.Descriptor.Roots.ToArray(), root => root.Id == filter.RootId);
            AutomationProperties.SetName(_query, "Filter indexed observations"); AutomationProperties.SetName(_metric, "Indexed metric");
            AutomationProperties.SetName(_roots, "Indexed captured location"); AutomationProperties.SetName(_sort, "Indexed table order");
            AutomationProperties.SetName(_descending, "Indexed descending table order");
            AutomationProperties.SetName(_chartMode, "Indexed chart mode"); AutomationProperties.SetName(_status, "Indexed analysis status");
            AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
            AutomationProperties.SetName(_details, "Captured metadata inspector"); AutomationProperties.SetLiveSetting(_inspectorStatus, AutomationLiveSetting.Polite);
            var toolbar = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            toolbar.Children.Add(_metric); toolbar.Children.Add(_chartMode); toolbar.Children.Add(_sort); toolbar.Children.Add(_descending); toolbar.Children.Add(_roots);
            var filters = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            filters.Children.Add(_query); filters.Children.Add(_case); filters.Children.Add(_unknown);
            filters.Children.Add(ActionButton("Apply filter", () => _ = RebuildAsync(), "Apply this query to the captured observations."));
            filters.Children.Add(ActionButton("Clear filter", () => { _query.Text = ""; _unknown.IsChecked = false; _ = RebuildAsync(); }, "Clear the query and uncertain-results cohort."));
            var navigation = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            _back = ActionButton("Back", () => _ = HistoryAsync(false), "Return to the preceding analysis scope.");
            _forward = ActionButton("Forward", () => _ = HistoryAsync(true), "Return to the next analysis scope.");
            _parent = ActionButton("Parent", () => _ = NavigateAsync(_shownPage?.Scope?.Path.ParentId ?? 0), "Explore the captured parent directory.");
            navigation.Children.Add(_back); navigation.Children.Add(_forward); navigation.Children.Add(_parent);
            Children.Add(toolbar); Children.Add(filters); Children.Add(_summary); Children.Add(_status); Children.Add(navigation);
            Children.Add(new ScrollViewer { Content = _breadcrumbs, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
            _body.Children.Add(_main); _body.Children.Add(_inspector); Children.Add(_body);
            _main.Children.Add(_treemap); _main.Children.Add(_sunburst); _main.Children.Add(_buckets);
            var pageBar = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            _previous = ActionButton("Previous entries", () => _ = PreviousPageAsync(), "Show the preceding page of captured entries.");
            _next = ActionButton("Next entries", () => _ = NextPageAsync(), "Show the next page of captured entries.");
            pageBar.Children.Add(_previous); pageBar.Children.Add(_next); pageBar.Children.Add(_range); _main.Children.Add(pageBar);
            AutomationProperties.SetName(_list, "Indexed analysis entries");
            Avalonia.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            _list.ItemTemplate = new FuncDataTemplate<AnalysisHierarchyRow>((row, _) =>
            {
                if (row is null) return new TextBlock(); var item = AnalysisIndexedChart.Row(row, _explorer.Metric);
                var grid = new Grid { ColumnDefinitions = new("3*,2*,2*,*,2*"), MinWidth = 760, Margin = new Thickness(6, 3) };
                string[] cells = [item.Name, DiskSpaceViewModel.ValueText(item, _explorer.Metric), DiskSpaceViewModel.LogicalText(item), item.Files.ToString("N0"),
                    item.SharedElsewhere ? "Shared at ancestor" : item.Unknown ? "Partial / unknown" : item.MetadataState.ToString()];
                for (int column = 0; column < cells.Length; column++) { var text = new TextBlock { Text = cells[column], TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) }; Grid.SetColumn(text, column); grid.Children.Add(text); }
                AutomationProperties.SetName(grid, string.Join("; ", cells)); ToolTip.SetTip(grid, new RichTip(item.Name, detail: string.Join(" · ", cells.Skip(1)))); return grid;
            });
            _main.Children.Add(new TextBlock { Text = "Name · Active metric · File size · File paths · State", TextWrapping = TextWrapping.Wrap }); _main.Children.Add(_list);
            _main.Children.Add(ActionButton("Explore selected folder", () => { if (_list.SelectedItem is AnalysisHierarchyRow { Path.Kind: AnalysisKind.Directory } row) _ = NavigateAsync(row.Path.Id); }, "Open the selected folder within this analysis."));
            _main.Children.Add(ActionButton("Focus inspector", () => { _details.BringIntoView(); _details.Focus(); }, "Read and copy captured metadata with the keyboard."));
            _inspector.Children.Add(new TextBlock { Text = "Inspector", FontWeight = FontWeight.Bold }); _inspector.Children.Add(_inspectorStatus); _inspector.Children.Add(_details);
            var evidence = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            string[] captions = ["First aliases", "More aliases", "First facts", "More facts", "First issues", "More issues"];
            for (int index = 0; index < captions.Length; index++)
            {
                int action = index; _evidence[index] = ActionButton(captions[index], () => _ = EvidenceAsync(action), "Page captured inspector evidence; totals may exceed the displayed page."); evidence.Children.Add(_evidence[index]);
            }
            _inspector.Children.Add(evidence); _inspector.Children.Add(new TextBlock { Text = "These observations do not authorize filesystem changes.", TextWrapping = TextWrapping.Wrap });
            _list.SelectionChanged += (_, _) => { if (!_publishing && _list.SelectedItem is AnalysisHierarchyRow row) _ = InspectAsync(row.Path.Id); };
            _list.DoubleTapped += (_, _) => { if (_list.SelectedItem is AnalysisHierarchyRow { Path.Kind: AnalysisKind.Directory } row) _ = NavigateAsync(row.Path.Id); };
            _metric.SelectionChanged += (_, _) => _ = RebuildAsync();
            _roots.SelectionChanged += (_, _) => { _rootId = _roots.SelectedIndex > 0 ? _source.Descriptor.Roots[_roots.SelectedIndex - 1].Id : null; _ = RebuildAsync(); };
            _sort.SelectionChanged += (_, _) => OrderChanged();
            _descending.IsCheckedChanged += (_, _) => OrderChanged();
            _chartMode.SelectionChanged += (_, _) => UpdateCharts();
            _treemap.Activated += ActivateChart; _sunburst.Activated += ActivateChart;
            _treemap.InteractionChanged += Interaction; _sunburst.InteractionChanged += Interaction;
            PropertyChanged += (_, change) => { if (change.Property == BoundsProperty) LayoutMode(Bounds.Width); };
            LayoutMode(window.Width);
            Publish(explorer);
        }
        private void LayoutMode(double width)
        {
            bool wide = width >= 900; if (_wide == wide) return; _wide = wide;
            _body.ColumnDefinitions = new(wide ? "3*,2*" : "*"); _body.RowDefinitions = new(wide ? "Auto" : "Auto,Auto");
            Grid.SetColumn(_inspector, wide ? 1 : 0); Grid.SetRow(_inspector, wide ? 0 : 1);
        }
        private void Interaction(bool active)
        {
            if (_disposed) return; _interacting = active;
            if (active) _interactionEnd = new(TaskCreationOptions.RunContinuationsAsynchronously);
            else { _interactionEnd?.TrySetResult(); _interactionEnd = null; }
        }
        private Task WaitForInteractionAsync(CancellationToken cancellation) => _interacting && _interactionEnd is { } end ? end.Task.WaitAsync(cancellation) : Task.CompletedTask;
        public ScrollViewer CreateViewport() => new() { Content = this, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        private void ActivateChart(string key, bool mark, bool enter)
        {
            if (_shownChart is not { } chart) return;
            if (chart.Remainders.TryGetValue(key, out var remainder)) { _ = NavigateAsync(remainder.Parent, remainder.Offset); return; }
            if (!chart.Items.TryGetValue(key, out var item) || item.NodeId is not { } id)
            { _details.Text = "This accounting group is not a filesystem entry. Explore its parent in the table to inspect the captured file paths."; return; }
            if (enter && item.Kind == AnalysisKind.Directory) _ = NavigateAsync(id); else _ = InspectAsync(id);
        }
        private Task RebuildAsync()
        {
            if (_disposed) return Task.CompletedTask; _again = true; _buildCancel?.Cancel();
            return _building ? _buildTask : _buildTask = BuildLoopAsync();
        }
        private void OrderChanged()
        {
            _pageOffsets.Clear();
            // A pending query may already own a complete page waiting for a captured gesture.
            // Rebuild that pending request too; an order change must not publish its older page.
            if (_building) _ = RebuildAsync(); else _ = NavigateAsync(_scope, record: false);
        }
        private async Task BuildLoopAsync()
        {
            _building = true;
            try
            {
                while (_again && !_disposed)
                {
                    _again = false; using var cancel = new CancellationTokenSource(); _buildCancel = cancel;
                    AnalysisDatasetExplorer? next = null; _status.Text = "Applying the query to captured observations…";
                    try
                    {
                        var metric = (AnalysisMetric)Math.Max(0, _metric.SelectedIndex);
                        next = await AnalysisDatasetExplorer.CreateAsync(_source, metric, new(_query.Text ?? "", _case.IsChecked == true, _unknown.IsChecked == true, _rootId), cancel.Token);
                        await Task.WhenAll(next.LoadPageAsync(sort: (AnalysisHierarchySort)_sort.SelectedIndex, cancellation: cancel.Token, descending: _descending.IsChecked == true), next.LoadChartAsync(cancellation: cancel.Token));
                        if (_interacting) _status.Text = "Query ready. Finish the current chart gesture to display it.";
                        await WaitForInteractionAsync(cancel.Token);
                        cancel.Token.ThrowIfCancellationRequested(); if (_disposed) return;
                        ++_navigation; ++_inspection; _navigationCancel?.Cancel(); _focus = 0; _scope = _offset = 0;
                        _past.Clear(); _future.Clear(); _pageOffsets.Clear();
                        var old = _explorer; _explorer = next; next = null; Publish(_explorer); await old.DisposeAsync();
                    }
                    catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
                    catch (Exception error) when (Expected(error)) { if (!_disposed && !cancel.IsCancellationRequested) _status.Text = "The previous view remains displayed. " + AnalysisNames.Display(error.Message, 500); }
                    finally { if (next is not null) await next.DisposeAsync(); if (ReferenceEquals(_buildCancel, cancel)) _buildCancel = null; }
                }
            }
            finally { _building = false; }
        }
        private async Task<bool> NavigateAsync(ulong parent, ulong offset = 0, bool record = true)
        {
            if (_disposed) return false;
            int version = ++_navigation; ++_inspection; _navigationCancel?.Cancel();
            using var cancel = new CancellationTokenSource(); _navigationCancel = cancel; var explorer = _explorer;
            var old = (_scope, _offset); _status.Text = "Loading captured entries…";
            try
            {
                bool[] loaded = await Task.WhenAll(explorer.LoadPageAsync(parent, (AnalysisHierarchySort)_sort.SelectedIndex, offset, cancellation: cancel.Token, descending: _descending.IsChecked == true), explorer.LoadChartAsync(parent, cancel.Token));
                await WaitForInteractionAsync(cancel.Token);
                if (_disposed || version != _navigation || !ReferenceEquals(explorer, _explorer) || loaded.Any(done => !done)) return false;
                if (record && old != (parent, offset)) { Push(_past, old); _future.Clear(); }
                if (parent != _scope) { _pageOffsets.Clear(); _focus = 0; } _scope = parent; _offset = offset; Publish(explorer); return true;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return false; }
            catch (Exception error) when (Expected(error)) { if (!_disposed && version == _navigation) _status.Text = "The previous view remains displayed. " + AnalysisNames.Display(error.Message, 500); return false; }
            finally { if (ReferenceEquals(_navigationCancel, cancel)) _navigationCancel = null; }
        }
        private async Task HistoryAsync(bool forward)
        {
            var source = forward ? _future : _past; if (source.Count == 0) return;
            var target = source[^1]; var previous = (_scope, _offset);
            if (await NavigateAsync(target.Parent, target.Offset, record: false)) { source.RemoveAt(source.Count - 1); Push(forward ? _past : _future, previous); Buttons(); }
        }
        private async Task NextPageAsync()
        { if (_shownPage is { Children.HasMore: true } page) { ulong previous = _offset; if (await NavigateAsync(_scope, page.Children.NextOffset, false)) { if (_pageOffsets.Count == AnalysisLimits.NavigationHistory) _pageOffsets.RemoveAt(0); _pageOffsets.Add(previous); Buttons(); } } }
        private async Task PreviousPageAsync()
        { if (_offset != 0) { ulong previous = _pageOffsets.Count > 0 ? _pageOffsets[^1] : 0; if (await NavigateAsync(_scope, previous, false)) { if (_pageOffsets.Count > 0) _pageOffsets.RemoveAt(_pageOffsets.Count - 1); Buttons(); } } }
        private void Publish(AnalysisDatasetExplorer explorer)
        {
            if (explorer.Page is not { } page) return;
            _shownPage = page; _shownChart = explorer.Chart; _shownInspection = null;
            bool focusTable = _list.IsKeyboardFocusWithin; _publishing = true;
            try { _list.ItemsSource = page.Children.Rows; _list.SelectedItem = page.Children.Rows.FirstOrDefault(row => row.Path.Id == _focus); }
            finally { _publishing = false; }
            if (focusTable) _list.Focus();
            var totals = page.Scope?.Totals ?? explorer.Totals; var logical = page.Scope?.Logical ?? explorer.LogicalTotals;
            _summary.Text = (_metric.Items[ (int)explorer.Metric ]?.ToString() ?? "Measure") + ": " +
                (!totals.CompatibleBasis ? "allocation bases differ; choose a single captured location" : explorer.Metric == AnalysisMetric.Files ? totals.Known.ToString("N0") + " known" : DiskSpaceViewModel.Bytes(explorer.Metric == AnalysisMetric.OnDisk ? checked(totals.Known + totals.IdentityUnverified) : totals.Known) + " known") +
                $" · {logical.Files:N0} matching file paths · File size: {DiskSpaceViewModel.Bytes(logical.KnownBytes)} known" + (logical.UnknownFiles > 0 ? $" · {logical.UnknownFiles:N0} unknown file sizes" : "");
            if (totals.IdentityUnverified > 0) _summary.Text += explorer.Metric == AnalysisMetric.OnDisk
                ? $" · Includes {DiskSpaceViewModel.Bytes(totals.IdentityUnverified)} reported for paths with unverified identities; aliases may repeat."
                : $" · {DiskSpaceViewModel.Bytes(totals.IdentityUnverified)} identity-unverified path bytes are excluded from unique chart area.";
            ulong missing = totals.Missing.Values.Aggregate(0UL, (sum, count) => checked(sum + count));
            if (missing != 0) _summary.Text += $" · {missing:N0} missing measurements.";
            _status.Text = (explorer.Descriptor.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "Complete under captured policy." : "Partial namespace observations; known results so far.") +
                (explorer.Filter is { } filter ? $" {filter.DefinitePaths:N0} definite matches; {filter.UnknownPaths:N0} uncertain results." : "") + (explorer.Chart?.DetailLimited == true ? " Small chart items are summarized; use the table for every entry." : "");
            _range.Text = page.Children.Total == 0 ? "No entries" : $"Entries {page.Children.Offset + 1:N0}–{page.Children.NextOffset:N0} of {page.Children.Total:N0}";
            _range.Text += " · " + (page.Sort switch { AnalysisHierarchySort.FileSize => "File size", AnalysisHierarchySort.Files => "File paths", AnalysisHierarchySort.Name => "Name", AnalysisHierarchySort.MetadataState => "Metadata state", _ => "Active metric" }) +
                (page.Descending ? " · Descending" : " · Ascending");
            _breadcrumbs.Children.Clear(); _breadcrumbs.Children.Add(ActionButton("All locations", () => _ = NavigateAsync(0), "Explore captured locations."));
            if (!page.AncestorsComplete) _breadcrumbs.Children.Add(new TextBlock { Text = "…" });
            foreach (var path in page.Ancestors.AddRange(page.Scope is null ? [] : new[] { page.Scope.Path }))
            { ulong id = path.Id; _breadcrumbs.Children.Add(ActionButton(AnalysisNames.DisplayName(path, 80), () => _ = NavigateAsync(id), "Explore this captured directory.")); }
            _details.Text = "Choose a table entry or chart item to inspect its captured metadata. Related evidence is paged.";
            _inspectorStatus.Text = "No entry selected.";
            _buckets.Children.Clear();
            foreach (var bucket in page.Buckets)
            {
                string title = bucket.Kind == AnalysisHierarchyBucketKind.SharedFiles ? "Shared hard-linked files" : "Directory allocation";
                string amount = bucket.Totals.Missing.Count != 0 && bucket.Totals.Known == 0 && bucket.Totals.IdentityUnverified == 0 ? "Unknown" :
                    DiskSpaceViewModel.Bytes(explorer.Metric == AnalysisMetric.OnDisk ? checked(bucket.Totals.Known + bucket.Totals.IdentityUnverified) : bucket.Totals.Known);
                var button = ActionButton(title + " · " + amount, () =>
                {
                    ++_inspection; _focus = 0; foreach (var control in _evidence) control.IsEnabled = false;
                    _details.Text = title + $": {bucket.Totals.Known} known bytes; {bucket.Totals.IdentityUnverified} identity-unverified path bytes; {bucket.Objects:N0} observed objects; {bucket.ObservedAliases:N0} observed aliases; {bucket.Charges:N0} charges. This accounting bucket is not a file path. " +
                        (bucket.Kind == AnalysisHierarchyBucketKind.SharedFiles ? "Shared charges remain at their original common ancestor. The matching paths remain available in the table." : "Directory metadata allocation is separate from file contents and file-path counts.") +
                        (bucket.Totals.Missing.Count == 0 ? "" : " Missing: " + string.Join(", ", bucket.Totals.Missing.Select(pair => pair.Key + " " + pair.Value)));
                    UpdateCharts();
                }, "Inspect this non-file accounting bucket and its exact measurements.");
                AutomationProperties.SetName(button, "Inspect " + title); _buckets.Children.Add(button);
            }
            foreach (var button in _evidence) button.IsEnabled = false;
            UpdateCharts(); Buttons();
            if (_focus != 0) _ = InspectAsync(_focus);
        }
        private void Buttons()
        { _previous.IsEnabled = _offset > 0; _next.IsEnabled = _shownPage?.Children.HasMore == true; _parent.IsEnabled = _parent.IsVisible = _scope != 0;
            _back.IsEnabled = _past.Count > 0; _forward.IsEnabled = _future.Count > 0; _back.IsVisible = _forward.IsVisible = _past.Count != 0 || _future.Count != 0; }
        private void UpdateCharts()
        {
            _treemap.IsVisible = _chartMode.SelectedIndex == 0; _sunburst.IsVisible = _chartMode.SelectedIndex == 1;
            if (_shownChart is not { } chart) return;
            string? focus = _focus == 0 ? null : AnalysisTree.Key(_focus);
            _treemap.SetView(chart, chart.Scope, focus, ImmutableHashSet<ulong>.Empty); _sunburst.SetView(chart, chart.Scope, focus, ImmutableHashSet<ulong>.Empty);
        }
        private async Task InspectAsync(ulong path, ulong alias = 0, ulong fact = 0, ulong issue = 0)
        {
            if (_disposed) return; int version = ++_inspection; _focus = path; var explorer = _explorer;
            foreach (var button in _evidence) button.IsEnabled = false;
            _inspectorStatus.Text = "Loading captured metadata…";
            try
            {
                if (!await explorer.InspectAsync(path, alias, fact, issue, maximum: 32) || _disposed || version != _inspection || !ReferenceEquals(explorer, _explorer)) return;
                var detail = explorer.Inspection!; _shownInspection = detail; var item = AnalysisIndexedChart.Row(detail.Row, explorer.Metric);
                var text = new System.Text.StringBuilder();
                text.AppendLine((detail.AncestorsComplete ? "" : "… / ") + string.Join(" / ", detail.Ancestors.Select(p => AnalysisNames.DisplayName(p, 128)).Append(AnalysisNames.DisplayName(detail.Row.Path, 256))));
                text.AppendLine("Active metric: " + DiskSpaceViewModel.ValueText(item, explorer.Metric, true) + " · File size: " + DiskSpaceViewModel.LogicalText(item, true));
                text.AppendLine($"Metadata: {item.MetadataState}; origin: {explorer.Descriptor.Origin}; coverage: {explorer.Descriptor.Coverage}; observed revision: {explorer.Descriptor.Identity.Revision}.");
                text.AppendLine("Name encoding: " + detail.Row.Path.NameEncoding + "; captured path ID: " + path);
                if (!detail.Row.Path.RawName.IsDefaultOrEmpty) text.AppendLine("Raw name bytes: " + Convert.ToHexString(detail.Row.Path.RawName.AsSpan(0, Math.Min(256, detail.Row.Path.RawName.Length))) + (detail.Row.Path.RawName.Length > 256 ? "…" : ""));
                text.AppendLine($"Aliases {detail.Aliases.Offset:N0}–{detail.Aliases.NextOffset:N0} of {detail.Aliases.Total:N0}:");
                foreach (var entry in detail.Aliases.Rows) text.AppendLine($"  {entry.Id}: {AnalysisNames.DisplayName(entry, 128)} (parent {entry.ParentId})");
                text.AppendLine($"Storage facts {detail.Facts.Offset:N0}–{detail.Facts.NextOffset:N0} of {detail.Facts.Total:N0}:");
                foreach (var entry in detail.Facts.Rows) text.AppendLine("  " + AnalysisNames.Display(entry.ComponentId, 64) + ": " + (entry.Measurement.IsKnown ? entry.Measurement.Value!.Value.ToString(CultureInfo.InvariantCulture) + " bytes" : entry.Measurement.State.ToString()) + " · " + AnalysisNames.Display(entry.Method, 128) + " · basis " + AnalysisNames.Display(entry.BasisId, 128));
                text.AppendLine($"Issues {detail.Issues.Offset:N0}–{detail.Issues.NextOffset:N0} of {detail.Issues.Total:N0}:");
                foreach (var entry in detail.Issues.Rows) text.AppendLine("  " + entry.State + ": " + AnalysisNames.Display(entry.Code, 256));
                _details.Text = text.ToString(); _inspectorStatus.Text = "Captured metadata ready for " + AnalysisNames.DisplayName(detail.Row.Path, 80) + ".";
                _evidence[0].IsEnabled = alias > 0; _evidence[1].IsEnabled = detail.Aliases.HasMore;
                _evidence[2].IsEnabled = fact > 0; _evidence[3].IsEnabled = detail.Facts.HasMore; _evidence[4].IsEnabled = issue > 0; _evidence[5].IsEnabled = detail.Issues.HasMore;
                UpdateCharts();
            }
            catch (Exception error) when (Expected(error)) { if (!_disposed && version == _inspection) { _inspectorStatus.Text = "Inspector unavailable."; _details.Text = AnalysisNames.Display(error.Message, 500); } }
        }
        private Task EvidenceAsync(int action)
        {
            if (_shownInspection is not { } detail || _focus == 0) return Task.CompletedTask;
            return InspectAsync(_focus, action switch { 0 => 0, 1 => detail.Aliases.NextOffset, _ => detail.Aliases.Offset },
                action switch { 2 => 0, 3 => detail.Facts.NextOffset, _ => detail.Facts.Offset }, action switch { 4 => 0, 5 => detail.Issues.NextOffset, _ => detail.Issues.Offset });
        }
        public void HandleKeys(KeyEventArgs args)
        {
            if (_disposed || args.Handled || _query.IsKeyboardFocusWithin) return;
            if (args.Key == Key.Enter && _list.IsKeyboardFocusWithin && _list.SelectedItem is AnalysisHierarchyRow row)
            { if (row.Path.Kind == AnalysisKind.Directory) _ = NavigateAsync(row.Path.Id); else _ = InspectAsync(row.Path.Id); args.Handled = true; }
            else if (args.Key == Key.Back && (_list.IsKeyboardFocusWithin || _treemap.IsKeyboardFocusWithin || _sunburst.IsKeyboardFocusWithin))
            { _ = NavigateAsync(_shownPage?.Scope?.Path.ParentId ?? 0); args.Handled = true; }
        }
        private static void Push(List<(ulong Parent, ulong Offset)> history, (ulong Parent, ulong Offset) value)
        { if (history.Count == AnalysisLimits.NavigationHistory) history.RemoveAt(0); history.Add(value); }
        private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or FormatException or OverflowException or OperationCanceledException;
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; ++_navigation; ++_inspection; _buildCancel?.Cancel(); _navigationCancel?.Cancel();
            _treemap.Clear(); _sunburst.Clear(); _list.ItemsSource = null; _details.Text = ""; _breadcrumbs.Children.Clear(); _buckets.Children.Clear();
            _shownPage = null; _shownChart = null; _shownInspection = null;
            _window.TrackIndexedClose(CloseAsync());
        }
        private async Task CloseAsync()
        { try { await _buildTask; } finally { try { await _explorer.DisposeAsync(); } finally { await _source.DisposeAsync(); } } }
    }
}
