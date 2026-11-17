using System.Collections.Immutable;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    // Internal until the indexed acquisition/profile-quota and complete product gates are qualified.
    internal Task<AnalysisDatasetExplorer> AddIndexedSessionAsync(AnalysisDatasetLease source, AnalysisMetric metric = AnalysisMetric.OnDisk,
        AnalysisFilterRequest? filter = null, CancellationToken cancellation = default, AppServices? services = null, string? historyNotice = null) =>
        AddPreparedIndexedSessionAsync(token => source.RetainAsync(token), metric, filter, null, cancellation, services, historyNotice: historyNotice);

    internal async Task<AnalysisDatasetExplorer> AddPreparedIndexedSessionAsync(Func<CancellationToken, Task<AnalysisDatasetLease>> prepareSource,
        AnalysisMetric metric, AnalysisFilterRequest? filter, AnalysisSavedView? saved, CancellationToken cancellation, AppServices? services = null,
        Func<AnalysisInventoryReceipt?>? openedInventory = null, string? historyNotice = null, IAsyncDisposable? additionalOwner = null,
        AnalysisSessionAdmission.Slot? reservedAdmission = null, AnalysisIndexedAcquisition? runtimeAcquisition = null,
        Func<AnalysisDatasetLease, CancellationToken, Task<AnalysisSavedView?>>? prepareView = null, Func<ulong?>? restoredFocus = null,
        Func<DiskSpaceViewModel.RecordedResultHandoff>? runtimeHandoff = null, Func<string?>? preparedViewNotice = null,
        AnalysisSelection? reviewedMarks = null)
    {
        AnalysisSessionAdmission.Slot? admission = reservedAdmission;
        try
        {
            Dispatcher.UIThread.VerifyAccess(); ObjectDisposedException.ThrowIf(_closed, this);
            if (runtimeHandoff is not null && (additionalOwner is not null || runtimeAcquisition is not null))
                throw new ArgumentException("Choose either immediate or transactional runtime ownership.");
            admission ??= _admissionPool.Acquire();
        }
        catch
        {
            var close = additionalOwner?.DisposeAsync().AsTask() ?? Task.CompletedTask;
            if (admission is not null) await admission.CloseAfterAsync(close); else await close;
            throw;
        }
        if (_pendingIndexed++ == 0) _indexedIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AnalysisDatasetLease? retained = null; AnalysisDatasetExplorer? explorer = null;
        Exception? unconfirmed = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _indexedLifetime.Token);
        try
        {
            retained = await prepareSource(linked.Token);
            if (prepareView is not null) saved = await prepareView(retained, linked.Token);
            string? restoreNotice = preparedViewNotice?.Invoke(); ulong scope = 0, offset = 0; int skipped = 0;
            if (saved is not null)
            {
                AnalysisSavedCatalog.ValidateView(saved); metric = saved.Metric;
                string? root = saved.Root;
                if (root is not null && !retained.Descriptor.Roots.Any(r => r.Id == root))
                { root = null; restoreNotice = "The saved location is absent. Showing all captured locations."; }
                filter = new(saved.Query, saved.CaseSensitive, saved.ShowUnknown, root) { RootDisplayIds = saved.RootDisplayIds };
                if (saved.Grouping == AnalysisGroupingDimension.FolderHierarchy)
                {
                    var restored = await RestoreIndexedScopeAsync(retained, saved.Scope, linked.Token);
                    scope = restored.Scope; skipped = restored.Skipped; string? scopeNotice = restored.Notice;
                    if (scopeNotice is not null) restoreNotice = restoreNotice is null ? scopeNotice : restoreNotice + " " + scopeNotice;
                }
            }
            explorer = await AnalysisDatasetExplorer.CreateAsync(retained, metric, filter, linked.Token);
            if (saved is not null && scope != 0)
            {
                await explorer.InspectAsync(scope, maximum: 1, cancellation: linked.Token);
                if (explorer.Inspection?.Row.IsVisible != true)
                { scope = offset = 0; skipped = 0; restoreNotice = (restoreNotice is null ? "" : restoreNotice + " ") + "The saved folder is absent from the matching observations. Showing the captured top level."; }
            }
            if (skipped != 0)
            {
                AnalysisDatasetExplorer? other = null;
                try { other = await explorer.CreateScopeAsync(scope, skipped, linked.Token); }
                catch (ArgumentOutOfRangeException)
                { restoreNotice = (restoreNotice is null ? "" : restoreNotice + " ") + "The saved Other subset is absent. Showing its captured parent; no selection was restored."; }
                if (other is not null) { var previous = explorer; explorer = other; await previous.DisposeAsync(); }
            }
            ulong? focus = restoredFocus?.Invoke();
            if (focus is { } focused && (saved is null || saved.Grouping == AnalysisGroupingDimension.FolderHierarchy))
            {
                if (await explorer.InspectAsync(focused, maximum: 1, cancellation: linked.Token) && explorer.Inspection?.Row.IsVisible == true)
                {
                    var position = await explorer.LocateChildAsync(focused, saved is null ? AnalysisHierarchySort.Metric : SavedIndexedSort(saved.Sort), saved?.SortDescending ?? true, linked.Token);
                    if (position.ParentId == scope) offset = position.Offset / 128 * 128;
                    else focus = null;
                }
                else focus = null;
            }
            await Task.WhenAll(explorer.LoadPageAsync(scope, saved is null ? AnalysisHierarchySort.Metric : SavedIndexedSort(saved.Sort), offset,
                cancellation: linked.Token, descending: saved?.SortDescending ?? true), explorer.LoadChartAsync(scope, linked.Token));
            linked.Token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_closed, this);
            var view = await IndexedSessionView.CreateAsync(this, retained, explorer, filter ?? new(""), admission, saved, restoreNotice, linked.Token, services);
            DiskSpaceViewModel.RecordedResultHandoff? transfer = null;
            try
            {
                view._historyOwner = additionalOwner; additionalOwner = null;
                view._runtimeAcquisition = runtimeAcquisition;
                if (reviewedMarks is not null)
                {
                    var identity = retained.Descriptor.Identity;
                    if (reviewedMarks.SessionId != identity.SessionId || reviewedMarks.Epoch != identity.Epoch || reviewedMarks.Revision != identity.Revision ||
                        reviewedMarks.Kind != AnalysisSelectionKind.VisibleMatches || reviewedMarks.NodeIds.Length > AnalysisLimits.ReviewPaths)
                        throw new InvalidOperationException("Reviewed marks belong to another captured result or exceed its review limit.");
                    view.SetReviewedMarks(reviewedMarks);
                }
                view.SetHistoryNotice(historyNotice);
                if (openedInventory?.Invoke() is { } receipt) view.RememberOpenedInventory(receipt);
                view.RefreshRuntimeRescanControls();
                if (focus is { } restored) await view.RestoreRuntimeFocusAsync(restored);
                linked.Token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_closed, this);
                if (runtimeHandoff is not null)
                {
                    transfer = runtimeHandoff();
                    view._historyOwner = transfer.Acquisition; view._runtimeAcquisition = transfer.Acquisition;
                    view.RefreshRuntimeRescanControls();
                }
                linked.Token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_closed, this);
                AddView(view);
                transfer?.Commit();
            }
            catch
            {
                if (transfer?.TryRollback() == true) { view._historyOwner = null; view._runtimeAcquisition = null; }
                retained = null; explorer = null; admission = null; view.Dispose(); await view.CloseCompletion; throw;
            }
            retained = null; admission = null; var result = explorer; explorer = null; return result;
        }
        catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { unconfirmed = error; throw; }
        finally
        {
            try
            {
                Task close = ClosePreparedAsync();
                try { if (admission is not null) await admission.CloseAfterAsync(close); else await close; }
                catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { RecordIndexedCloseFailure(error); throw; }
            }
            finally
            {
                if (--_pendingIndexed == 0) { _indexedIdle.TrySetResult(); if (_closed) _indexedLifetime.Dispose(); }
            }
            async Task ClosePreparedAsync()
            {
                Exception? failure = unconfirmed;
                void Remember(Exception error) => failure = failure is null ? error : new AggregateException(failure, error);
                try { if (explorer is not null) await explorer.DisposeAsync(); } catch (Exception error) { Remember(error as AnalysisCloseUnconfirmedException ?? new AnalysisCloseUnconfirmedException(error)); }
                try { if (retained is not null) await retained.DisposeAsync(); } catch (Exception error) { Remember(error as AnalysisCloseUnconfirmedException ?? new AnalysisCloseUnconfirmedException(error)); }
                try { if (additionalOwner is not null) await additionalOwner.DisposeAsync(); } catch (Exception error) { Remember(error as AnalysisCloseUnconfirmedException ?? new AnalysisCloseUnconfirmedException(error)); }
                if (failure is not null)
                {
                    if (failure is not AnalysisCloseUnconfirmedException && AnalysisCloseUnconfirmedException.IsPresent(failure))
                        throw new AnalysisCloseUnconfirmedException(failure);
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }
        }
    }

    private static bool IndexedExpectedError(Exception error) => AnalysisCloseUnconfirmedException.IsPresent(error) ||
        error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException or OverflowException or OperationCanceledException or KeyNotFoundException;

    private sealed partial class IndexedSessionView : StackPanel, IAnalysisSessionView
    {
        private readonly DiskSpaceWindow _window;
        private readonly AnalysisSessionAdmission.Slot _admission;
        private readonly AnalysisViewWork _actualWork = new();
        private readonly AnalysisDatasetLease _source;
        internal IAsyncDisposable? _historyOwner;
        private AnalysisDatasetExplorer _explorer;
        private AnalysisExplorerPage? _shownPage;
        private AnalysisIndexedChart? _shownChart;
        private AnalysisExplorerInspection? _shownInspection;
        private readonly ComboBox _metric = new() { ItemsSource = new[] { "File size", "Unique file size", "On disk — reported", "Files" }, MinWidth = 180 };
        private readonly ComboBox _chartMode = new() { ItemsSource = new[] { "Treemap", "Sunburst", "Table" }, SelectedIndex = 0 };
        private readonly ComboBox _color = AnalysisColorLegend.CreateSelector();
        private readonly AnalysisColorLegend _legend = new();
        private readonly ComboBox _sort = new() { ItemsSource = new[] { "Active metric", "File paths", "File size", "Name", "Metadata state" }, SelectedIndex = 0 };
        private readonly CheckBox _descending = new() { Content = "Descending", IsChecked = true };
        private readonly ComboBox _roots = new() { MinWidth = 160 };
        private readonly TextBox _query = new() { PlaceholderText = "Search within observations…", MinWidth = 200, MaxLength = AnalysisLimits.QueryBytes };
        private readonly CheckBox _case = new() { Content = "Case sensitive" }, _unknown = new() { Content = "Inspect uncertain query results" };
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap }, _summary = new() { TextWrapping = TextWrapping.Wrap }, _range = new(), _inspectorStatus = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _historyNotice = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        internal void SetHistoryNotice(string? notice) { _historyNotice.Text = notice; _historyNotice.IsVisible = notice is not null; }
        private readonly TextBox _details = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180, MaxHeight = 420 };
        private readonly Grid _body = new() { ColumnSpacing = 12, RowSpacing = 12 };
        private readonly StackPanel _main = new() { Spacing = 8 }, _inspector = new() { Spacing = 8 };
        private bool? _wide;
        private readonly StackPanel _breadcrumbs = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        private readonly StackPanel _buckets = new() { Spacing = 4 };
        private readonly ListBox _list = new() { MinHeight = 220, MaxHeight = 420, Focusable = true };
        private readonly TreemapControl _treemap = new() { Height = 300 };
        private readonly SunburstControl _sunburst = new() { Height = 300, IsVisible = false };
        private readonly Button _previous, _next, _parent, _back, _forward;
        private readonly Button[] _evidence = new Button[6];
        private readonly List<(ulong Parent, ulong Offset, int Skipped)> _past = [], _future = [];
        private readonly List<ulong> _pageOffsets = [];
        private CancellationTokenSource? _buildCancel, _navigationCancel;
        private Task _buildTask = Task.CompletedTask;
        private TaskCompletionSource? _interactionEnd;
        private bool _interacting;
        private int _interactionDepth;
        private double? _interactionStatusHeight;
        private ulong _scope, _offset, _focus;
        private string? _chartFocusKey;
        private int _navigation, _inspection;
        private bool _disposed, _building, _again, _publishing;
        private string? _rootId;
        public bool HasTemporaryWork => _persistenceBusy || _runtimeRescanCancel is not null || _selection.Marks.Count != 0 || _selection.Basket.Count != 0 ||
            !_revisionSaved && _source.Descriptor.Origin == AnalysisOrigin.LiveSession && _source.Descriptor.Persistence == AnalysisPersistence.Temporary;
        public bool IsComparison => false;

        internal IndexedSessionView(DiskSpaceWindow window, AnalysisDatasetLease source, AnalysisDatasetExplorer explorer, AnalysisFilterRequest filter,
            AnalysisSessionAdmission.Slot admission, AnalysisSavedView? saved = null, string? restoreNotice = null,
            AnalysisDatasetGroupExplorer? groups = null, GroupPage? groupedPage = null, AppServices? services = null)
        {
            _services = services;
            _window = window; _source = source; _explorer = explorer; _rootId = filter.RootId; _admission = admission;
            _selection.BindIdentity(source.Descriptor.Identity);
            _grouping = groups; _groupPage = groupedPage; _groupKey = groupedPage?.Scope?.Definition.Key;
            Spacing = 10; Margin = new Thickness(10);
            _metric.SelectedIndex = (int)explorer.Metric; _query.Text = filter.Text; _case.IsChecked = filter.CaseSensitive; _unknown.IsChecked = filter.UnknownOnly;
            _scope = explorer.Page?.ParentId ?? 0;
            _offset = groupedPage?.Offset ?? explorer.Page?.Children.Offset ?? 0;
            if (saved is not null)
            {
                _sort.SelectedIndex = (int)SavedIndexedSort(saved.Sort); _descending.IsChecked = saved.SortDescending;
                _chartMode.SelectedIndex = saved.Chart == "Sunburst" ? 1 : saved.Chart == "Table" ? 2 : 0;
                _color.SelectedIndex = (int)saved.ColorDimension;
                _group.SelectedIndex = (int)saved.Grouping;
            }
            _roots.ItemsSource = new[] { "All captured locations" }.Concat(source.Descriptor.Roots.Select(root => AnalysisNames.Display(root.Label, 120) + " [" + AnalysisNames.Display(filter.RootDisplayIds?.GetValueOrDefault(root.Id) ?? root.Id, 64) + "]")).ToArray();
            _roots.SelectedIndex = filter.RootId is null ? 0 : 1 + Array.FindIndex(source.Descriptor.Roots.ToArray(), root => root.Id == filter.RootId);
            AutomationProperties.SetName(_query, "Filter indexed observations"); AutomationProperties.SetName(_metric, "Indexed metric");
            AutomationProperties.SetName(_roots, "Indexed captured location"); AutomationProperties.SetName(_sort, "Indexed table order");
            AutomationProperties.SetName(_descending, "Indexed descending table order");
            AutomationProperties.SetName(_chartMode, "Indexed chart mode"); AutomationProperties.SetAutomationId(_status, "Indexed analysis status");
            AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
            AutomationProperties.SetName(_details, "Captured metadata inspector"); AutomationProperties.SetLiveSetting(_inspectorStatus, AutomationLiveSetting.Polite);
            var toolbar = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            toolbar.Children.Add(_metric); toolbar.Children.Add(_group); toolbar.Children.Add(_chartMode); toolbar.Children.Add(_color); toolbar.Children.Add(_sort); toolbar.Children.Add(_descending); toolbar.Children.Add(_roots);
            AutomationProperties.SetName(_group, "Indexed grouping");
            var filters = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            filters.Children.Add(_query); filters.Children.Add(_case); filters.Children.Add(_unknown);
            filters.Children.Add(ActionButton("Apply filter", () => _ = RebuildAsync(), "Apply this query to the captured observations."));
            filters.Children.Add(ActionButton("Clear filter", () => { _query.Text = ""; _unknown.IsChecked = false; _ = RebuildAsync(); }, "Clear the query and uncertain-results cohort."));
            var navigation = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            AddIndexedPersistenceControls(navigation);
            AddRuntimeRescanControls(navigation);
            _back = ActionButton("Back", () => _ = HistoryAsync(false), "Return to the preceding analysis scope.");
            _forward = ActionButton("Forward", () => _ = HistoryAsync(true), "Return to the next analysis scope.");
            _parent = ActionButton("Parent", () => _ = ParentAsync(), "Explore the preceding group level or captured parent directory.");
            navigation.Children.Add(_back); navigation.Children.Add(_forward); navigation.Children.Add(_parent);
            string provenance = source.Descriptor.Origin == AnalysisOrigin.ImportedInventory
                ? "Offline imported observations · unverified. Recorded locations are not connected."
                : "Captured observations.";
            if (source.Descriptor.Taint.HasFlag(AnalysisTaint.PrivilegedDerived)) provenance += " Privileged-derived metadata.";
            provenance += " File metadata can be sensitive.";
            var provenanceLabel = new TextBlock { Text = provenance, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(provenanceLabel, "Analysis provenance: " + provenance);
            Children.Add(provenanceLabel); Children.Add(toolbar); Children.Add(filters); Children.Add(_summary); Children.Add(_status); Children.Add(navigation); Children.Add(_legend);
            Children.Add(_historyNotice);
            Children.Add(new ScrollViewer { Content = _breadcrumbs, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
            _body.Children.Add(_main); _body.Children.Add(_inspector); Children.Add(_body);
            _main.Children.Add(_treemap); _main.Children.Add(_sunburst); _main.Children.Add(_buckets);
            var pageBar = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            _previous = ActionButton("Previous entries", () => _ = PreviousPageAsync(), "Show the preceding page of captured entries.");
            _next = ActionButton("Next entries", () => _ = NextPageAsync(), "Show the next page of captured entries.");
            pageBar.Children.Add(_previous); pageBar.Children.Add(_next); pageBar.Children.Add(_range); _main.Children.Add(pageBar);
            AutomationProperties.SetName(_list, "Indexed analysis entries");
            Avalonia.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
            _list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
                { Setters = { new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) } });
            _list.ItemTemplate = new FuncDataTemplate<object>((row, _) =>
            {
                var item = DisplayItem(row); if (item is null) return new TextBlock();
                var grid = new Grid { ColumnDefinitions = new("3*,2*,2*,2*,2*"), MinHeight = 32, Margin = new Thickness(6, 3) };
                string[] cells = [item.Name, DiskSpaceViewModel.ValueText(item, _grouping?.Metric ?? _explorer.Metric), DiskSpaceViewModel.LogicalText(item),
                    DiskSpaceViewModel.FilePathsText(item), DiskSpaceViewModel.StatusText(item)];
                for (int column = 0; column < cells.Length; column++)
                {
                    var text = new TextBlock { Text = cells[column], TextWrapping = column == 0 ? TextWrapping.NoWrap : TextWrapping.Wrap,
                        TextTrimming = column == 0 ? TextTrimming.CharacterEllipsis : TextTrimming.None, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                    Grid.SetColumn(text, column); grid.Children.Add(text);
                }
                bool? stacked = null;
                void FitRow()
                {
                    if (grid.Bounds.Width <= 0) return;
                    bool compact = grid.Bounds.Width < 40 * ((TextBlock)grid.Children[0]).FontSize;
                    if (stacked == compact) return; stacked = compact;
                    grid.ColumnDefinitions = new(compact ? "*,*" : "3*,2*,2*,2*,2*");
                    grid.RowDefinitions = new(compact ? "Auto,Auto,Auto" : "Auto");
                    string[] labels = ["", "Active metric: ", "File size: ", "File paths: ", "State: "];
                    for (int column = 0; column < cells.Length; column++)
                    {
                        var text = (TextBlock)grid.Children[column];
                        Grid.SetRow(text, compact && column > 0 ? 1 + (column - 1) / 2 : 0);
                        Grid.SetColumn(text, compact ? column == 0 ? 0 : (column - 1) % 2 : column);
                        Grid.SetColumnSpan(text, compact && column == 0 ? 2 : 1);
                        text.Text = (compact ? labels[column] : "") + cells[column];
                        text.Margin = new Thickness(0, 0, 12, compact ? 4 : 0);
                    }
                }
                grid.SizeChanged += (_, _) => FitRow();
                bool fitQueued = false;
                grid.Children[0].PropertyChanged += (_, change) =>
                {
                    // Changing row layout/text inside inherited-font propagation can mutate
                    // Avalonia's property-store traversal. Relayout after that traversal ends.
                    if (change.Property != TextBlock.FontSizeProperty || fitQueued) return;
                    fitQueued = true; Dispatcher.UIThread.Post(() => { fitQueued = false; FitRow(); }, DispatcherPriority.Loaded);
                };
                string? explanation = DiskSpaceViewModel.EntryExplanation(item);
                AutomationProperties.SetName(grid, string.Join("; ", cells)); ToolTip.SetTip(grid, new RichTip(item.Name, detail: string.Join(" · ", cells.Skip(1)) + (explanation is null ? "" : "\n" + explanation))); return MarkedRow(item, grid);
            });
            _main.Children.Add(new TextBlock { Text = "Mark · Name · Active metric · File size · File paths · State", TextWrapping = TextWrapping.Wrap }); _main.Children.Add(_list);
            AddSelectionControls();
            _exploreFolder = ActionButton("Explore selected folder", () => { if (_list.SelectedItem is AnalysisHierarchyRow { Path.Kind: AnalysisKind.Directory } row) _ = NavigateAsync(row.Path.Id); }, "Open the selected folder within this analysis.");
            _main.Children.Add(_exploreFolder);
            _exploreGroup = ActionButton("Explore selected group", ExploreGroup, "Open the captured members of the selected accounting group.");
            _main.Children.Add(_exploreGroup);
            _main.Children.Add(ActionButton("Focus inspector", () => { _details.BringIntoView(); _details.Focus(); }, "Read and copy captured metadata with the keyboard."));
            _inspector.Children.Add(new TextBlock { Text = "Inspector", FontWeight = FontWeight.Bold }); _inspector.Children.Add(_inspectorStatus); _inspector.Children.Add(_details);
            _inspector.Children.Add(_showCapturedFolder);
            var evidence = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            string[] captions = ["First aliases", "More aliases", "First facts", "More facts", "First issues", "More issues"];
            for (int index = 0; index < captions.Length; index++)
            {
                int action = index; _evidence[index] = ActionButton(captions[index], () => _ = EvidenceAsync(action), "Page captured inspector evidence; totals may exceed the displayed page."); evidence.Children.Add(_evidence[index]);
            }
            _inspector.Children.Add(evidence); AddGroupEvidence();
            _inspector.Children.Add(new TextBlock { Text = "These observations do not authorize filesystem changes.", TextWrapping = TextWrapping.Wrap });
            _list.SelectionChanged += (_, _) => SelectEntry();
            _list.DoubleTapped += (_, args) =>
            {
                if (args.KeyModifiers != KeyModifiers.None || args.Source is Control source &&
                    (source is CheckBox || source.GetVisualAncestors().OfType<CheckBox>().Any())) return;
                OpenEntry();
            };
            _metric.SelectionChanged += (_, _) => { if (!_publishing) _ = RebuildAsync(); };
            _group.SelectionChanged += (_, _) => { if (!_publishing) _ = RebuildAsync(); };
            _roots.SelectionChanged += (_, _) => { if (!_publishing) { _rootId = _roots.SelectedIndex > 0 ? _source.Descriptor.Roots[_roots.SelectedIndex - 1].Id : null; _ = RebuildAsync(); } };
            _sort.SelectionChanged += (_, _) => OrderChanged();
            _descending.IsCheckedChanged += (_, _) => OrderChanged();
            _chartMode.SelectionChanged += (_, _) => UpdateCharts();
            _color.SelectionChanged += (_, _) => UpdateCharts();
            _treemap.Activated += ActivateChart; _sunburst.Activated += ActivateChart;
            _treemap.InteractionChanged += Interaction; _sunburst.InteractionChanged += Interaction;
            PropertyChanged += (_, change) => { if (change.Property == BoundsProperty) LayoutMode(Bounds.Width); };
            LayoutMode(window.Width);
            Publish(explorer);
            if (restoreNotice is not null) _status.Text += " " + restoreNotice;
        }
        private void LayoutMode(double width)
        {
            bool wide = width >= 900; if (_wide == wide) return; _wide = wide;
            _body.ColumnDefinitions = new(wide ? "3*,2*" : "*"); _body.RowDefinitions = new(wide ? "Auto" : "Auto,Auto");
            Grid.SetColumn(_inspector, wide ? 1 : 0); Grid.SetRow(_inspector, wide ? 0 : 1);
        }
        private void Interaction(bool active)
        {
            if (_disposed) return;
            if (active)
            {
                if (_interactionDepth++ == 0)
                {
                    _interacting = true; _interactionEnd = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    // Status text can wrap differently while the query waits to publish.
                    // Keep its rendered space until release so the pressed row stays put.
                    if (_status.Bounds.Height > 0)
                    { _interactionStatusHeight = _status.Height; _status.Height = _status.Bounds.Height; }
                }
            }
            else if (_interactionDepth > 0 && --_interactionDepth == 0)
            {
                _interacting = false;
                if (_interactionStatusHeight is { } height) { _status.Height = height; _interactionStatusHeight = null; }
                _interactionEnd?.TrySetResult(); _interactionEnd = null;
            }
            _color.IsEnabled = !_interacting;
        }
        private Task WaitForInteractionAsync(CancellationToken cancellation) => _interacting && _interactionEnd is { } end ? end.Task.WaitAsync(cancellation) : Task.CompletedTask;
        public ScrollViewer CreateViewport() => new() { Content = this, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        private void ActivateChart(string key, bool mark, bool enter)
        {
            if (_disposed || _window._dialogs.IsOpen) return;
            _chartFocusKey = key;
            _basketInspection = null;
            if (_grouping is not null) { ActivateGroupChart(key, enter, mark); return; }
            if (_shownChart is not { } chart) return;
            if (chart.Remainders.TryGetValue(key, out var remainder))
            {
                if (enter) _ = remainder.GeometryScope ? NavigateAsync(remainder.Parent, geometrySkipped: checked((int)remainder.Offset)) : NavigateAsync(remainder.Parent, remainder.Offset);
                else FocusOtherChartItem(); return;
            }
            var aggregate = AnalysisSavedCatalog.SplitAggregationScope(key);
            if (aggregate.Skipped != 0 && (aggregate.Scope == AnalysisTree.RootKey ||
                chart.Items.TryGetValue(aggregate.Scope, out var capturedParent) && capturedParent is { Kind: AnalysisKind.Directory, NodeId: not null }))
            {
                ulong parent = aggregate.Scope == AnalysisTree.RootKey ? 0 : chart.Items[aggregate.Scope].NodeId!.Value;
                if (enter) _ = NavigateAsync(parent, geometrySkipped: aggregate.Skipped); else FocusOtherChartItem(); return;
            }
            if (!chart.Items.TryGetValue(key, out var item) || item.NodeId is not { } id)
            {
                ++_inspection; _focus = 0; _shownInspection = null;
                foreach (var button in _evidence) button.IsEnabled = false;
                _inspectorStatus.Text = "Captured accounting bucket.";
                _details.Text = "This accounting group is not a filesystem entry. Explore its parent in the table to inspect the captured file paths.";
                RefreshSelection(); UpdateCharts(); return;
            }
            if (mark) ToggleMark(id);
            if (enter && item.Kind == AnalysisKind.Directory) _ = NavigateAsync(id, geometrySkipped: 0); else _ = InspectAsync(id);
        }
        private void FocusOtherChartItem()
        {
            ++_inspection; _focus = 0; _shownInspection = null; _groupFocus = null;
            foreach (var button in _evidence) button.IsEnabled = false;
            _inspectorStatus.Text = "Other small items — captured accounting subset.";
            _details.Text = "This bucket summarizes captured items and is not a file path. Press Enter or double-click to explore its exact subset. Marks and the review basket remain separate.";
            RefreshSelection(); UpdateCharts();
        }
        private Task RebuildAsync(CapturedFolderRequest? capturedFolder = null)
        {
            _persistenceNotice = null;
            if (_disposed || _actualWork.HasUnconfirmedClose) return Task.CompletedTask; _again = true; _buildCancel?.Cancel();
            _requestedFolder = capturedFolder;
            return _building ? _buildTask : _buildTask = BuildLoopAsync();
        }
        private void OrderChanged()
        {
            if (_publishing) return;
            _pageOffsets.Clear();
            // A pending query may already own a complete page waiting for a captured gesture.
            // Rebuild that pending request too; an order change must not publish its older page.
            if (_building) _ = RebuildAsync(); else _ = NavigateAsync(_scope, record: false);
        }
        private async Task BuildLoopAsync()
        {
            using var actualWork = _actualWork.Enter();
            _building = true;
            try
            {
                while (_again && !_disposed)
                {
                    _again = false; using var cancel = new CancellationTokenSource(); _buildCancel = cancel;
                    AnalysisDatasetExplorer? next = null; AnalysisDatasetGroupExplorer? nextGroups = null;
                    _status.Text = "Applying the query to captured observations…";
                    try
                    {
                        var capturedFolder = _requestedFolder;
                        var metric = capturedFolder?.Metric ?? (AnalysisMetric)Math.Max(0, _metric.SelectedIndex);
                        var request = capturedFolder?.Filter ?? DraftFilter();
                        var dimension = capturedFolder is null ? (AnalysisGroupingDimension)Math.Max(0, _group.SelectedIndex) : AnalysisGroupingDimension.FolderHierarchy;
                        ulong initialScope = 0, initialOffset = 0, initialFocus = 0; string? navigationNotice = null;
                        GroupPage? nextPage = null;
                        if (dimension == AnalysisGroupingDimension.FolderHierarchy)
                        {
                            next = await AnalysisDatasetExplorer.CreateAsync(_source, metric, request, cancel.Token);
                            if (capturedFolder is not null)
                            {
                                if (capturedFolder.Identity != _source.Descriptor.Identity) throw new InvalidOperationException("This entry belongs to another captured revision.");
                                AnalysisPathMetadata? path = null;
                                try { path = await _source.ReadPathAsync(capturedFolder.Path, cancel.Token); }
                                catch (ArgumentOutOfRangeException) { navigationNotice = "The selected entry is absent from this captured revision. Showing the captured top level."; }
                                if (path is not null)
                                {
                                    await next.InspectAsync(path.Id, maximum: 1, cancellation: cancel.Token);
                                    if (next.Inspection?.Row.IsVisible != true)
                                    {
                                        await next.DisposeAsync(); next = null; request = new("");
                                        next = await AnalysisDatasetExplorer.CreateAsync(_source, metric, request, cancel.Token);
                                        navigationNotice = "Cleared the display filters to show this captured entry. No live location was opened.";
                                    }
                                    initialScope = path.Kind == AnalysisKind.Directory ? path.Id : path.ParentId; initialFocus = path.Id;
                                    if (path.Kind != AnalysisKind.Directory) initialOffset = (await next.LocateChildAsync(path.Id, capturedFolder.Sort, capturedFolder.Descending, cancel.Token)).Offset;
                                }
                            }
                            await Task.WhenAll(next.LoadPageAsync(initialScope, capturedFolder?.Sort ?? (AnalysisHierarchySort)_sort.SelectedIndex, initialOffset,
                                cancellation: cancel.Token, descending: capturedFolder?.Descending ?? _descending.IsChecked == true), next.LoadChartAsync(initialScope, cancel.Token));
                        }
                        else
                        {
                            // Grouping owns its query/accounting. Keep the existing hierarchy only
                            // as an immutable metadata inspector; rebuilding both old/new pairs
                            // would needlessly exceed the graph-consumer budget during replacement.
                            nextGroups = await AnalysisDatasetGroupExplorer.CreateAsync(_source, metric, dimension, request, cancel.Token);
                            nextPage = await LoadGroupedPageAsync(nextGroups, null, 0, GroupSort(), _descending.IsChecked == true, cancel.Token);
                        }
                        if (_interacting) _status.Text = "Query ready. Finish the current interaction to display it.";
                        await WaitForInteractionAsync(cancel.Token);
                        cancel.Token.ThrowIfCancellationRequested(); if (_disposed) return;
                        ++_navigation; ++_inspection; _navigationCancel?.Cancel(); _focus = initialFocus; _chartFocusKey = null; _scope = initialScope; _offset = initialOffset;
                        _past.Clear(); _future.Clear(); _groupPast.Clear(); _groupFuture.Clear(); _pageOffsets.Clear();
                        _groupKey = _groupFocus = null; _selectedGroupedItem = null;
                        var old = next is null ? null : _explorer; var oldGroups = _grouping;
                        if (next is not null) { _explorer = next; next = null; }
                        _grouping = nextGroups; nextGroups = null; _groupPage = nextPage;
                        bool retainedDraft = capturedFolder is not null && ApplyCapturedFolderControls(capturedFolder, request);
                        try
                        {
                            if (capturedFolder is not null) _basketInspection = null;
                            Publish(_explorer);
                            if (capturedFolder is not null)
                            {
                                _status.Text = navigationNotice ?? "Showing the entry in its captured folder. No live location was opened.";
                                if (retainedDraft) _status.Text += " A newer filter draft is retained; use Apply filter to display it.";
                                if (ReferenceEquals(_requestedFolder, capturedFolder)) _requestedFolder = null;
                            }
                        }
                        finally { try { if (oldGroups is not null) await oldGroups.DisposeAsync(); } finally { if (old is not null) await old.DisposeAsync(); } }
                    }
                    catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
                    catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!_disposed && !cancel.IsCancellationRequested) _status.Text = "The previous view remains displayed. " + AnalysisNames.Display(error.Message, 500); }
                    finally
                    {
                        try { try { if (nextGroups is not null) await nextGroups.DisposeAsync(); } finally { if (next is not null) await next.DisposeAsync(); } }
                        catch (Exception error) { RememberViewCloseFailure(error); throw; }
                        finally { if (ReferenceEquals(_buildCancel, cancel)) _buildCancel = null; }
                    }
                }
            }
            finally { _building = false; }
        }
        private async Task<bool> NavigateAsync(ulong parent, ulong offset = 0, bool record = true, AnalysisHierarchySort? sort = null,
            bool? descending = null, CancellationToken cancellation = default, int? geometrySkipped = null, Action? onPublished = null)
        {
            if (_grouping is not null) return await NavigateGroupAsync(_groupKey, offset, record);
            if (_disposed || _actualWork.HasUnconfirmedClose) return false;
            CancelRequestedFolder();
            using var actualWork = _actualWork.Enter();
            int version = ++_navigation; ++_inspection; _navigationCancel?.Cancel();
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _selectionLifetime.Token); _navigationCancel = cancel;
            var original = _explorer; int skipped = geometrySkipped ?? (parent == _scope ? original.GeometrySkipped : 0);
            var old = (_scope, _offset, original.GeometrySkipped); AnalysisDatasetExplorer? candidate = null; bool published = false;
            _status.Text = "Loading captured entries…";
            try
            {
                if (skipped != original.GeometrySkipped || original.GeometrySkipped != 0 && parent != original.GeometryParent)
                    candidate = await original.CreateScopeAsync(parent, skipped, cancel.Token);
                var explorer = candidate ?? original;
                bool[] loaded = await Task.WhenAll(explorer.LoadPageAsync(parent, sort ?? (AnalysisHierarchySort)_sort.SelectedIndex, offset,
                    cancellation: cancel.Token, descending: descending ?? _descending.IsChecked == true), explorer.LoadChartAsync(parent, cancel.Token));
                await WaitForInteractionAsync(cancel.Token);
                if (_disposed || cancel.IsCancellationRequested || version != _navigation || !ReferenceEquals(original, _explorer) || loaded.Any(done => !done)) return false;
                if (record && old != (parent, offset, skipped)) { Push(_past, old); _future.Clear(); }
                ++_inspection;
                if (parent != _scope || skipped != original.GeometrySkipped) { _pageOffsets.Clear(); _focus = 0; _chartFocusKey = null; }
                _scope = parent; _offset = offset; bool replaced = candidate is not null;
                if (replaced) { _explorer = explorer; candidate = null; }
                published = true;
                try { onPublished?.Invoke(); Publish(explorer); }
                finally { if (replaced) await original.DisposeAsync(); }
                return !_disposed && version == _navigation && ReferenceEquals(explorer, _explorer);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return false; }
            catch (Exception error) when (Expected(error))
            {
                RememberViewCloseFailure(error);
                if (!_disposed && version == _navigation) _status.Text = (published ? "The scope is displayed, but the previous captured view did not close successfully. " : "The previous view remains displayed. ") + AnalysisNames.Display(error.Message, 500);
                return false;
            }
            finally
            {
                try { if (candidate is not null) await candidate.DisposeAsync(); }
                catch (Exception error) { RememberViewCloseFailure(error); throw; }
                finally { if (ReferenceEquals(_navigationCancel, cancel)) _navigationCancel = null; }
            }
        }
        private async Task HistoryAsync(bool forward)
        {
            if (_grouping is not null) { await GroupHistoryAsync(forward); return; }
            var source = forward ? _future : _past; if (source.Count == 0) return;
            var target = source[^1]; int count = source.Count; var previous = (_scope, _offset, _explorer.GeometrySkipped);
            await NavigateAsync(target.Parent, target.Offset, record: false, geometrySkipped: target.Skipped, onPublished: () =>
            {
                // History belongs to the displayed scope. Publish it before waiting for the
                // previous owner's actual close, so a fresh Back/Forward sees the new stacks.
                if (source.Count == count && source[^1] == target)
                { source.RemoveAt(source.Count - 1); Push(forward ? _past : _future, previous); }
            });
        }
        private async Task NextPageAsync()
        {
            ulong? next = _grouping is not null ? _groupPage is { HasMore: true } grouped ? grouped.NextOffset : null : _shownPage is { Children.HasMore: true } page ? page.Children.NextOffset : null;
            if (next is { } offset) { ulong previous = _offset; if (await NavigateAsync(_scope, offset, false)) { if (_pageOffsets.Count == AnalysisLimits.NavigationHistory) _pageOffsets.RemoveAt(0); _pageOffsets.Add(previous); Buttons(); } }
        }
        private async Task PreviousPageAsync()
        { if (_offset != 0) { ulong previous = _pageOffsets.Count > 0 ? _pageOffsets[^1] : 0; if (await NavigateAsync(_scope, previous, false)) { if (_pageOffsets.Count > 0) _pageOffsets.RemoveAt(_pageOffsets.Count - 1); Buttons(); } } }
        private void Publish(AnalysisDatasetExplorer explorer)
        {
            _exploreGroup.IsVisible = _grouping is not null;
            _exploreFolder.IsVisible = _grouping is null;
            _groupEvidence.IsVisible = _grouping is not null;
            if (_grouping is not null && _groupPage is { } grouped) { PublishGroup(_grouping, grouped); return; }
            _groupCharges.IsEnabled = _firstCharges.IsEnabled = _moreCharges.IsEnabled = false;
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
            _breadcrumbs.Children.Clear(); _breadcrumbs.Children.Add(ActionButton("All locations", () => _ = NavigateAsync(0, geometrySkipped: 0), "Explore captured locations."));
            if (!page.AncestorsComplete) _breadcrumbs.Children.Add(new TextBlock { Text = "…" });
            foreach (var path in page.Ancestors.AddRange(page.Scope is null ? [] : new[] { page.Scope.Path }))
            { ulong id = path.Id; _breadcrumbs.Children.Add(ActionButton(AnalysisNames.DisplayName(path, 80), () => _ = NavigateAsync(id, geometrySkipped: 0), "Explore this captured directory.")); }
            if (page.GeometrySkipped != 0)
            {
                _breadcrumbs.Children.Add(new TextBlock { Text = "Other small items", TextWrapping = TextWrapping.Wrap });
                _status.Text = "Exact Other subset. " + _status.Text;
                _range.Text += " · exact geometry subset";
            }
            AppendPersistenceNotice();
            _details.Text = "Choose a table entry or chart item to inspect its captured metadata. Related evidence is paged.";
            _inspectorStatus.Text = "No entry selected.";
            _buckets.Children.Clear();
            foreach (var bucket in page.Buckets)
            {
                var bucketItem = AnalysisIndexedChart.Bucket(bucket, explorer.Metric);
                string title = bucket.Kind == AnalysisHierarchyBucketKind.SharedFiles ? "Shared hard-linked files" : "Directory allocation";
                string amount = bucket.Totals.Missing.Count != 0 && bucket.Totals.Known == 0 && bucket.Totals.IdentityUnverified == 0 ? "Unknown" :
                    DiskSpaceViewModel.Bytes(explorer.Metric == AnalysisMetric.OnDisk ? checked(bucket.Totals.Known + bucket.Totals.IdentityUnverified) : bucket.Totals.Known);
                var button = ActionButton(title + " · " + amount, () =>
                {
                    ++_inspection; _focus = 0; _chartFocusKey = null; foreach (var control in _evidence) control.IsEnabled = false;
                    _details.Text = title + $": {bucket.Totals.Known} known bytes; {bucket.Totals.IdentityUnverified} identity-unverified path bytes; {bucket.Objects:N0} observed objects; {bucket.ObservedAliases:N0} observed aliases; {bucket.Charges:N0} charges. This accounting bucket is not a file path. " +
                        (bucket.Kind == AnalysisHierarchyBucketKind.SharedFiles ? "Shared charges remain at their original common ancestor. The matching paths remain available in the table." : "Directory metadata allocation is separate from file contents and file-path counts.") +
                        (bucket.ReferenceLogical is null ? "" : "\nMatching reference paths: " + DiskSpaceViewModel.FilePathsText(bucketItem) +
                            "\nFile size: " + DiskSpaceViewModel.LogicalText(bucketItem, true)) +
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
        {
            RefreshIndexedPersistence();
            RefreshSelection();
            if (_grouping is not null) { GroupButtons(); return; }
            _previous.IsEnabled = _offset > 0; _next.IsEnabled = _shownPage?.Children.HasMore == true; _parent.IsEnabled = _parent.IsVisible = _scope != 0 || _explorer.GeometrySkipped != 0;
            _back.IsEnabled = _past.Count > 0; _forward.IsEnabled = _future.Count > 0; _back.IsVisible = _forward.IsVisible = _past.Count != 0 || _future.Count != 0; }
        private void UpdateCharts()
        {
            _treemap.IsVisible = _chartMode.SelectedIndex == 0; _sunburst.IsVisible = _chartMode.SelectedIndex == 1;
            _legend.IsVisible = _chartMode.SelectedIndex != 2;
            IAnalysisChartData? chart = _grouping is not null ? _groupPage?.Chart : _shownChart;
            if (chart is null) return;
            string scope = _grouping is not null ? _groupPage!.Chart.Scope : _shownChart!.Scope;
            string? focus = _chartFocusKey ?? (_focus == 0 ? _groupFocus : _grouping is not null && _selectedGroupedItem?.NodeId == _focus ? _selectedGroupedItem.Key : AnalysisTree.Key(_focus));
            var color = (AnalysisColorDimension)Math.Clamp(_color.SelectedIndex, 0, 2);
            _legend.SetView(color, chart.ObservedUtc);
            _treemap.SetView(chart, scope, focus, _selection.Marks, color); _sunburst.SetView(chart, scope, focus, _selection.Marks, color);
        }
        internal async Task InspectAsync(ulong path, ulong alias = 0, ulong fact = 0, ulong issue = 0)
        {
            if (_disposed || _actualWork.HasUnconfirmedClose) return;
            using var actualWork = _actualWork.Enter();
            if (_disposed) return; int version = ++_inspection; _focus = path; RefreshSelection(); var explorer = _explorer;
            foreach (var button in _evidence) button.IsEnabled = false;
            _inspectorStatus.Text = "Loading captured metadata…";
            try
            {
                if (!await explorer.InspectAsync(path, alias, fact, issue, maximum: 32) || _disposed || version != _inspection || !ReferenceEquals(explorer, _explorer)) return;
                var detail = explorer.Inspection!; _shownInspection = detail;
                var item = _selectedGroupedItem?.NodeId == path ? _selectedGroupedItem : AnalysisIndexedChart.Row(detail.Row, explorer.Metric);
                var metric = _grouping?.Metric ?? explorer.Metric;
                var text = new System.Text.StringBuilder();
                text.AppendLine((detail.AncestorsComplete ? "" : "… / ") + string.Join(" / ", detail.Ancestors.Select(p => AnalysisNames.DisplayName(p, 128)).Append(AnalysisNames.DisplayName(detail.Row.Path, 256))));
                bool basketOnly = _basketInspection == path;
                if (basketOnly)
                {
                    string length = detail.Object is { LogicalLength: { IsKnown: true } measured } ? measured.Value!.Value.ToString(CultureInfo.InvariantCulture) + " bytes" : detail.Object?.LogicalLength.State.ToString() ?? "Unknown";
                    text.AppendLine("Captured file size: " + (detail.Row.Path.Kind != AnalysisKind.File ? "Not a file" : length));
                    text.AppendLine("Captured " + _referenceInspectionLabel + " entry. Its metadata is independent of the current query; no amount assigned by the current group is shown.");
                }
                else
                {
                    text.AppendLine("Active metric: " + DiskSpaceViewModel.ValueText(item, metric, true) + " · File size: " + DiskSpaceViewModel.LogicalText(item, true));
                    if (_grouping is not null) text.AppendLine("Active metric is the amount assigned to this entry in the displayed accounting group. File size describes the captured path; shared charges are shown once in their accounting group.");
                }
                text.AppendLine($"Metadata: {item.MetadataState}; origin: {explorer.Descriptor.Origin}; coverage: {explorer.Descriptor.Coverage}; observed revision: {explorer.Descriptor.Identity.Revision}.");
                text.AppendLine("Name encoding: " + detail.Row.Path.NameEncoding + "; captured path ID: " + path);
                if (!basketOnly && DiskSpaceViewModel.EntryExplanation(item) is { } explanation) text.AppendLine(explanation);
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
            catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!_disposed && version == _inspection) { _inspectorStatus.Text = "Inspector unavailable."; _details.Text = AnalysisNames.Display(error.Message, 500); } }
        }
        private Task EvidenceAsync(int action)
        {
            if (_shownInspection is not { } detail || _focus == 0) return Task.CompletedTask;
            return InspectAsync(_focus, action switch { 0 => 0, 1 => detail.Aliases.NextOffset, _ => detail.Aliases.Offset },
                action switch { 2 => 0, 3 => detail.Facts.NextOffset, _ => detail.Facts.Offset }, action switch { 4 => 0, 5 => detail.Issues.NextOffset, _ => detail.Issues.Offset });
        }
        public void HandleKeys(KeyEventArgs args)
        {
            if (_disposed || args.Handled || _window._dialogs.IsOpen || _query.IsKeyboardFocusWithin) return;
            if (args.Key == Key.Space && _list.IsKeyboardFocusWithin && _window.FocusManager?.GetFocusedElement() is not CheckBox)
            { if (_focus != 0) ToggleMark(_focus); args.Handled = true; return; }
            if (args.Key == Key.Enter && _list.IsKeyboardFocusWithin)
            { OpenEntry(); args.Handled = true; }
            else if (args.Key == Key.Enter && _grouping is not null && (_treemap.IsKeyboardFocusWithin || _sunburst.IsKeyboardFocusWithin) && _groupPage is { } grouped)
            {
                string? key = _focus == 0 ? _groupFocus : AnalysisTree.Key(_focus);
                key ??= grouped.Chart.ChildrenOf(grouped.Chart.Scope).FirstOrDefault()?.Key;
                if (key is not null) ActivateGroupChart(key, true); args.Handled = true;
            }
            else if (args.Key == Key.Back && (_list.IsKeyboardFocusWithin || _treemap.IsKeyboardFocusWithin || _sunburst.IsKeyboardFocusWithin))
            { _ = ParentAsync(); args.Handled = true; }
        }
        private static void Push(List<(ulong Parent, ulong Offset, int Skipped)> history, (ulong Parent, ulong Offset, int Skipped) value)
        { if (history.Count == AnalysisLimits.NavigationHistory) history.RemoveAt(0); history.Add(value); }
        private static bool Expected(Exception error) => IndexedExpectedError(error);
        private void RememberViewCloseFailure(Exception error)
        {
            _actualWork.RecordUnconfirmedClose(error);
            if (AnalysisCloseUnconfirmedException.IsPresent(error)) _window.RecordIndexedCloseFailure(error);
        }
        internal Task CloseCompletion { get; private set; } = Task.CompletedTask;
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; ++_navigation; ++_inspection; _buildCancel?.Cancel(); _navigationCancel?.Cancel();
            _services?.AnalysisSavedReferences.Untrack(this);
            _memberCaptureCancel?.Cancel(); _selectionLifetime.Cancel(); _selection.ClearMarks(); _selection.ClearBasket();
            _markVisibilityCancel?.Cancel(); _visibilityMarks = null; _markVisibility = null; _visibilityOwner = null;
            CancelTableRange();
            _inventoryCancel?.Cancel(); _inventoryCloser?.Close(false);
            _runtimeRescanCancel?.Cancel(); StopRuntimeRescan();
            var actualWork = _actualWork.CloseAsync();
            _treemap.Clear(); _sunburst.Clear(); _list.ItemsSource = null; _details.Text = ""; _breadcrumbs.Children.Clear(); _buckets.Children.Clear();
            _shownPage = null; _shownChart = null; _shownInspection = null;
            _groupPage = null; _selectedGroupedItem = null; _chargePage = null;
            CloseCompletion = CloseAsync(actualWork);
            _window.TrackIndexedClose(CloseCompletion, _admission);
        }
        private async Task CloseAsync(Task actualWork)
        {
            // Explorer disposal cancels its inspection/page/chart lanes. View continuations
            // must also unwind before their admission can be reused in another window.
            try { await _buildTask; }
            finally { try { await _explorer.DisposeAsync(); } finally { try { await actualWork; } finally { try { if (_grouping is not null) await _grouping.DisposeAsync(); } finally { try { await _source.DisposeAsync(); } finally { try { if (_historyOwner is not null) await _historyOwner.DisposeAsync(); } finally { _selectionLifetime.Dispose(); } } } } } }
        }
    }
}
