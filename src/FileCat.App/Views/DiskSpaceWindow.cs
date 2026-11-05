using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;
using FileCat.Core.Commands;

namespace FileCat.App.Views;

/// <summary>The modeless analysis host. Main-panel navigation never supplies operation targets here.</summary>
public sealed partial class DiskSpaceWindow : Window
{
    private readonly TabControl _tabs = new();
    private readonly List<TabItem> _items = [];
    private readonly List<SessionView> _sessions = [];
    private readonly Panel _overlay = new() { IsVisible = false };
    private readonly OverlayDialogService _dialogs;
    private readonly Action? _newAnalysis;
    private readonly Func<Keymap> _keymap;
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private bool _forceClose, _askingClose;
    private long _nextTabNumber;
    public int SessionCount => _sessions.Count;
    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new DescendantFocusWindowAutomationPeer(this);
    public DiskSpaceWindow(Action? newAnalysis = null, Func<Keymap>? keymap = null)
    {
        var defaults = new Keymap(CommandRegistry.CreateDefault());
        _keymap = keymap ?? (() => defaults);
        _newAnalysis = newAnalysis; Title = "Disk space — FileCat"; Width = 1180; Height = 780; MinWidth = 360; MinHeight = 360;
        WindowFit.Attach(this);
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.ico"))); } catch (IOException) { }
        _dialogs = new(_overlay, () => _tabs);
        var header = new WrapPanel { ItemSpacing = 8, Margin = new Thickness(10, 8) };
        header.Children.Add(ActionButton("New analysis", () => _newAnalysis?.Invoke(), "Choose another captured scope in a new analysis tab."));
        header.Children.Add(ActionButton("Close analysis", () => _ = CloseTabAsync(), "Close the selected analysis. Active work and review references need a preserving choice."));
        header.Children.Add(ActionButton("Continue in FileCat", Hide, "Hide this window and keep its sessions in the running FileCat instance."));
        AutomationProperties.SetLiveSetting(_notice, AutomationLiveSetting.Polite);
        var windowHeader = new StackPanel { Children = { header, _notice } };
        var dock = new DockPanel(); DockPanel.SetDock(windowHeader, Dock.Top); dock.Children.Add(windowHeader); dock.Children.Add(_tabs);
        Content = ThemeLayers.Over(new Grid { Children = { dock, _overlay } }, dock);
        ToolTipNames.Install();
        // Overlays are siblings of the tabs, so review-context bindings belong to the window route.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            int selected = _tabs.SelectedIndex;
            if (selected >= 0 && selected < _sessions.Count) _sessions[selected].HandleKeys(e);
        }, RoutingStrategies.Bubble);
        Closing += (_, e) =>
        {
            if (_forceClose) return;
            if (_sessions.Any(s => s.Model.HasTemporaryWork))
            {
                e.Cancel = true;
                if (!_askingClose) _ = ConfirmCloseAsync();
            }
        };
        Closed += (_, _) => { _dialogs.CancelAll(); foreach (var session in _sessions) session.Dispose(); _sessions.Clear(); };
    }
    public void AddSession(DiskSpaceViewModel model, IReadOnlyList<string>? roots = null)
    {
        if (_sessions.Count >= AnalysisLimits.Sessions) throw new InvalidOperationException("Eight analyses are open. Close an analysis before adding another.");
        var view = new SessionView(this, model, roots ?? []);
        _notice.IsVisible = false;
        var item = new TabItem { Header = (model.Comparison is null ? "Analysis " : "Comparison ") + checked(++_nextTabNumber), Content = view.CreateViewport() };
        _sessions.Add(view); _items.Add(item); _tabs.ItemsSource = _items.ToArray(); _tabs.SelectedItem = item;
    }
    private async Task ConfirmCloseAsync()
    {
        _askingClose = true;
        try
        {
            if (await _dialogs.ConfirmAsync("Close disk space?", "Stop active scans and discard temporary analyses, comparisons and unsaved review references?", "Stop and close", true, "Keep open"))
            { _forceClose = true; Close(); }
        }
        finally { _askingClose = false; }
    }
    private async Task CloseTabAsync()
    {
        int at = _tabs.SelectedIndex;
        if (at < 0 || at >= _sessions.Count) return;
        var view = _sessions[at];
        if (view.Model.HasTemporaryWork &&
            !await _dialogs.ConfirmAsync(view.Model.Comparison is null ? "Close analysis?" : "Close comparison?",
                view.Model.Comparison is null ? "Stop this metadata scan and discard its temporary results and unsaved review references?" : "Discard this temporary comparison and its display settings? The saved inventories remain available.",
                view.Model.Comparison is null ? "Stop and close" : "Discard comparison", true, "Keep open")) return;
        if (_forceClose || (at = _sessions.IndexOf(view)) < 0) return;
        view.Dispose(); _sessions.RemoveAt(at); _items.RemoveAt(at); _tabs.ItemsSource = _items.ToArray();
        _notice.IsVisible = false;
        if (_items.Count > 0) _tabs.SelectedIndex = Math.Min(at, _items.Count - 1);
    }
    public void ForceClose() { _forceClose = true; Close(); }
    public void ExplainSessionLimit() { _notice.Text = "Eight analyses are open. Close an analysis before adding another."; _notice.IsVisible = true; }
    private static Button ActionButton(string text, Action action, string help)
    {
        var button = new Button { Content = text }; button.Click += (_, _) => action();
        ToolTip.SetTip(button, new RichTip(text, detail: help)); AutomationProperties.SetHelpText(button, help); return button;
    }
    private sealed partial class SessionView : Grid, IDisposable
    {
        private readonly DiskSpaceWindow _window;
        public DiskSpaceViewModel Model { get; }
        private readonly StackPanel _setup = new() { Spacing = 6, Margin = new Thickness(10) };
        private readonly ScrollViewer _setupViewport;
        private readonly StackPanel _roots = new() { Spacing = 5 };
        private readonly List<TextBox> _rootEditors = [];
        private readonly TextBlock _resolved = new() { TextWrapping = TextWrapping.Wrap };
        private readonly Button _start = new() { Content = "Start analysis", Classes = { "primary" } };
        private readonly ComboBox _components = new() { ItemsSource = new[] { "Standard allocation", "Detailed allocation (partial component support)" }, MinWidth = 250 };
        private readonly TextBox _query = new() { PlaceholderText = "Search within observations…", MinWidth = 180, MaxLength = AnalysisLimits.QueryBytes };
        private readonly CheckBox _case = new() { Content = "Case sensitive" };
        private readonly ComboBox _metric = new() { ItemsSource = new[] { "File size", "Unique file size", "On disk — reported", "Files" }, SelectedIndex = 2, MinWidth = 155 };
        private readonly ComboBox _mode = new() { ItemsSource = new[] { "Treemap", "Sunburst", "Table" }, SelectedIndex = 0, MinWidth = 125 };
        private readonly ComboBox _rootFilter = new() { MinWidth = 140, MaxWidth = 240 };
        private readonly Button _unknown = new() { Content = "Inspect uncertain results", IsVisible = false };
        private ImmutableArray<RootBinding> _shownRoots = [];
        private sealed record RootChoice(string? Id, string Label) { public override string ToString() => Label; }
        private readonly Button _pause = new() { Content = "Pause" }, _stop = new() { Content = "Stop" };
        private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontSize = 18 };
        private readonly TextBlock _scope = new() { TextWrapping = TextWrapping.Wrap }, _coverage = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap }, _error = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _basket = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _inspector = new() { TextWrapping = TextWrapping.Wrap };
        private readonly StackPanel _inspectorPanel = new() { Spacing = 8, Margin = new Thickness(10) };
        private readonly ListBox _list = new() { SelectionMode = SelectionMode.Single, Focusable = true };
        private readonly TreemapControl _treemap = new(); private readonly SunburstControl _sunburst = new();
        private readonly Grid _explorer = new() { RowDefinitions = new RowDefinitions("3*,Auto,2*"), ColumnDefinitions = new ColumnDefinitions("*,300"), MinHeight = 240 };
        private readonly Grid _chartHost = new(); private readonly ScrollViewer _inspectorScroll;
        private readonly Grid _table = new() { RowDefinitions = new RowDefinitions("Auto,*") };
        private readonly Grid _tableHeader = new() { Margin = new Thickness(8, 3, 24, 3) };
        private readonly Button _inspect;
        private ScrollViewer? _viewport;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer _filterTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private bool _updating, _disposed;
        private ImmutableArray<AnalysisTreeItem> _shownRows;
        public SessionView(DiskSpaceWindow window, DiskSpaceViewModel model, IReadOnlyList<string> roots)
        {
            _window = window; Model = model;
            _query.Text = Model.Query; _case.IsChecked = Model.CaseSensitive; _metric.SelectedIndex = (int)Model.Metric;
            _mode.SelectedIndex = Model.Chart switch { "Sunburst" => 1, "Table" => 2, _ => 0 };
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto");
            _setup.Children.Add(new TextBlock { Text = "Understand this storage. Choose what to measure.", FontSize = 20 });
            _setup.Children.Add(_roots);
            foreach (string root in roots) AddRoot(root);
            if (_rootEditors.Count == 0) AddRoot("");
            var choose = new WrapPanel { ItemSpacing = 8 };
            choose.Children.Add(ActionButton("Add location", () => { if (_rootEditors.Count < AnalysisLimits.Roots) AddRoot(""); }, "Add one explicit filesystem location, up to 32."));
            choose.Children.Add(ActionButton("Choose folders…", () => _ = ChooseFoldersAsync(), "Choose source folders without changing the main panels."));
            choose.Children.Add(ActionButton("Choose files…", () => _ = ChooseFilesAsync(), "Choose explicit file paths. Hard links remain separate paths but share verified object charges."));
            _setup.Children.Add(choose);
            _components.SelectedIndex = (int)Model.Components;
            _components.SelectionChanged += (_, _) => Model.Components = (AnalysisComponentPreset)Math.Clamp(_components.SelectedIndex, 0, 1);
            AutomationProperties.SetName(_components, "Allocation components");
            ToolTip.SetTip(_components, new RichTip("Allocation components", detail: "Standard records reported file allocation. Detailed also records supported directory self-allocation. " + AnalysisComponents.UnmeasuredExplanation));
            _setup.Children.Add(_components);
            _setup.Children.Add(new TextBlock { Text = "Stay within each filesystem. Links and cloud contents are not followed. Detailed adds directory allocation; separate streams, forks and xattrs are not measured.", TextWrapping = TextWrapping.Wrap });
            _setup.Children.Add(_resolved); _setup.Children.Add(_start);
            _setupViewport = new ScrollViewer { Content = _setup, MaxHeight = 250, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            Children.Add(_setupViewport);
            var toolbar = new StackPanel { Spacing = 6, Margin = new Thickness(10, 4) };
            var nav = new WrapPanel { ItemSpacing = 7, LineSpacing = 5 };
            nav.Children.Add(ActionButton("Back", Model.Back, "Return to the previous analysis scope."));
            nav.Children.Add(ActionButton("Forward", Model.Forward, "Return to the next analysis scope."));
            nav.Children.Add(ActionButton("Up", Model.Parent, "Inspect the parent scope without changing the captured sources."));
            nav.Children.Add(_mode); nav.Children.Add(_metric); nav.Children.Add(_pause); nav.Children.Add(_stop);
            _inspect = ActionButton("Inspect selection", () => _ = InspectAsync(), "Open full selection details and actions, including on a narrow screen.");
            nav.Children.Add(_inspect);
            _save = ActionButton("Save…", () => _ = SaveAsync(), "Save the displayed analysis, including all captured paths and metadata. The current display filter does not reduce the saved inventory.");
            _openSaved = ActionButton("Open saved…", () => _ = OpenSavedAsync(), "Open a named inventory from this private profile as inert observations. No source is reconnected or scanned.");
            nav.Children.Add(_save); nav.Children.Add(_openSaved);
            _compare = ActionButton("Compare…", () => _ = CompareAsync(), "Compare the entire captured revision with a saved inventory. Opening and comparison remain offline.");
            nav.Children.Add(_compare);
            var filters = new WrapPanel { ItemSpacing = 8, LineSpacing = 5, Children = { _query, _case } };
            filters.Children.Add(_rootFilter); filters.Children.Add(_unknown);
            filters.Children.Add(ActionButton("Clear filter", () => { Model.ShowUnknownFilterResults = false; _query.Text = ""; }, "Remove the display query; scan boundaries remain captured."));
            toolbar.Children.Add(nav); toolbar.Children.Add(_scope); toolbar.Children.Add(_summary); toolbar.Children.Add(_coverage); toolbar.Children.Add(filters); toolbar.Children.Add(_error);
            Grid.SetRow(toolbar, 1); Children.Add(toolbar);
            _chartHost.Children.Add(_treemap); _chartHost.Children.Add(_sunburst); _explorer.Children.Add(_chartHost);
            var splitter = new GridSplitter { Height = 5, ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetRow(splitter, 1); _explorer.Children.Add(splitter);
            _list.ItemTemplate = new FuncDataTemplate<AnalysisTreeItem>((item, _) => item is null ? new Border() : Row(item));
            _list.SelectionChanged += (_, _) => { if (!_updating && _list.SelectedItem is AnalysisTreeItem item) Model.Focus(item.Key); };
            _list.DoubleTapped += (_, _) => { if (_list.SelectedItem is AnalysisTreeItem item) ActivateItem(item.Key, false, true); };
            bool tablePress = false;
            _list.PointerPressed += (_, _) => { if (!tablePress) { tablePress = true; Model.Interaction(true); } };
            void EndTablePress() { if (tablePress) { tablePress = false; Model.Interaction(false); } }
            _list.PointerReleased += (_, _) => EndTablePress(); _list.PointerCaptureLost += (_, _) => EndTablePress();
            string[] headings = ["Mark", "Name", "Active metric", "File size", "Files", "State"];
            for (int i = 0; i < headings.Length; i++) { var h = new TextBlock { Text = headings[i], TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(h, i); _tableHeader.Children.Add(h); }
            _tableHeader.SizeChanged += (_, _) => FitColumns(_tableHeader);
            _table.Children.Add(_tableHeader); Grid.SetRow(_list, 1); _table.Children.Add(_list);
            Grid.SetRow(_table, 2); _explorer.Children.Add(_table);
            _inspectorPanel.Children.Add(new TextBlock { Text = "Selection inspector", FontSize = 18 }); _inspectorPanel.Children.Add(_inspector);
            _inspectorPanel.Children.Add(Unavailable("Preview", new AnalysisCapabilities().For(AnalysisVerb.Preview).Reason));
            _inspectorPanel.Children.Add(Unavailable("Reveal in panel", "A historical path is not a current source binding. Explicit live rebinding is required."));
            _inspectorPanel.Children.Add(ActionButton("Copy path", () => _ = CopyPathAsync(), "Copy the selected path as literal text; it is not opened or executed."));
            _inspectorPanel.Children.Add(ActionButton("Mark current members", Model.MarkCurrentMembers, "Capture this group's current real members. Future observations are not selected."));
            _inspectorPanel.Children.Add(ActionButton("Add to review", Model.StageSelection, "Stage inert observation references. Unsupported actions remain skipped; no files change."));
            _inspectorScroll = new ScrollViewer { Content = _inspectorPanel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            Grid.SetColumn(_inspectorScroll, 1); Grid.SetRowSpan(_inspectorScroll, 3); _explorer.Children.Add(_inspectorScroll);
            Grid.SetRow(_explorer, 2); Children.Add(_explorer);
            var bottom = new WrapPanel { ItemSpacing = 10, Margin = new Thickness(10, 5), Children = { _basket } };
            bottom.Children.Add(ActionButton("Review cleanup", () => _ = ReviewAsync(), "Review the captured references and their supported actions. Expected free-space gain is unknown."));
            Grid.SetRow(bottom, 3); Children.Add(bottom); Grid.SetRow(_status, 4); _status.Margin = new Thickness(10, 3, 10, 8); Children.Add(_status);
            _start.Click += async (_, _) => await Model.StartAsync(_rootEditors.Select(e => e.Text ?? "").ToArray());
            _pause.Click += (_, _) => Model.PauseOrResume(); _stop.Click += (_, _) => Model.Stop();
            _mode.SelectionChanged += (_, _) => { Model.Chart = _mode.SelectedIndex switch { 1 => "Sunburst", 2 => "Table", _ => "Treemap" }; Refresh(); };
            _metric.SelectionChanged += (_, _) => { if (_updating) return; Model.Metric = (AnalysisMetric)Math.Max(0, _metric.SelectedIndex); _ = Model.RebuildAsync(true); };
            _query.TextChanged += (_, _) => { if (_updating) return; _filterTimer.Stop(); _filterTimer.Start(); };
            _case.IsCheckedChanged += (_, _) => { Model.CaseSensitive = _case.IsChecked == true; _ = Model.RebuildAsync(true); };
            _rootFilter.SelectionChanged += (_, _) => { if (!_updating && _rootFilter.SelectedItem is RootChoice choice) { Model.RootFilter = choice.Id; _ = Model.RebuildAsync(true); } };
            _unknown.Click += (_, _) => { Model.ShowUnknownFilterResults = !Model.ShowUnknownFilterResults; _ = Model.RebuildAsync(true); };
            ToolTip.SetTip(_rootFilter, new RichTip("Filter captured location", detail: "Choose one captured binding. Covered locations refer to their observed subtree, without scanning it twice."));
            ToolTip.SetTip(_unknown, new RichTip("Uncertain query results", detail: "Missing metadata may leave a predicate unknown. Inspect that explicit cohort separately from confirmed matches."));
            _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); Model.Query = _query.Text ?? ""; _ = Model.RebuildAsync(true); };
            foreach (var chart in new AnalysisChartControl[] { _treemap, _sunburst })
            { chart.Activated += ActivateItem; chart.InteractionChanged += Model.Interaction; }
            _timer.Tick += async (_, _) => { if (IsEffectivelyVisible) await Model.RebuildAsync(); };
            AttachedToVisualTree += (_, _) => { _timer.Start(); _ = Model.RebuildAsync(true); };
            DetachedFromVisualTree += (_, _) => _timer.Stop();
            Model.Changed += Refresh;
            AutomationProperties.SetName(_list, "Ranked analysis entries"); AutomationProperties.SetName(_query, "Filter observations");
            AutomationProperties.SetName(_rootFilter, "Filter captured location"); AutomationProperties.SetName(_unknown, "Inspect uncertain query results");
            AutomationProperties.SetName(_status, "Analysis status"); AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
            PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) Refresh(); };
            if (Model.Comparison is { } comparison) ShowComparison(comparison);
            Refresh();
        }
        public ScrollViewer CreateViewport()
        {
            Height = 690;
            _viewport = new ScrollViewer { Name = "AnalysisPageScroll", Content = this, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            _viewport.LayoutUpdated += (_, _) =>
            {
                if (_disposed || _viewport is null) return;
                if (Model.Comparison is not null)
                {
                    double comparisonHeight = Math.Max(_viewport.Bounds.Height, ComparisonMinimumHeight);
                    if (double.IsFinite(comparisonHeight) && Math.Abs(Height - comparisonHeight) > .5) Height = comparisonHeight;
                    return;
                }
                // Auto rows get their full wrapped height. The inner explorer always receives a finite viewport,
                // preserving list virtualization while the page itself scrolls on short or large-text displays.
                double minimum = Children.Where(c => c != _explorer).Sum(c => c.DesiredSize.Height) + _explorer.MinHeight;
                double height = Math.Max(_viewport.Bounds.Height, minimum);
                if (double.IsFinite(height) && Math.Abs(Height - height) > .5) Height = height;
            };
            return _viewport;
        }
        private void ActivateItem(string key, bool toggleMark, bool open)
        {
            Model.Focus(key, toggleMark, open);
            if (open && Bounds.Width < 900 && Model.Tree?.Items.GetValueOrDefault(key)?.Kind == AnalysisKind.File) _ = InspectAsync();
        }
        private static Control Unavailable(string title, string reason)
        {
            var wrapper = new Border { Focusable = true, Child = new Button { Content = title, IsEnabled = false } };
            ToolTip.SetTip(wrapper, new RichTip(title, detail: reason)); AutomationProperties.SetName(wrapper, title); AutomationProperties.SetHelpText(wrapper, reason); return wrapper;
        }
        private void AddRoot(string path)
        {
            if (_rootEditors.Count >= AnalysisLimits.Roots) return;
            var box = new TextBox { Text = path, MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Stretch };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; row.Children.Add(box);
            var remove = ActionButton("Remove", () => { if (_rootEditors.Count > 1) { _rootEditors.Remove(box); _roots.Children.Remove(row); } }, "Remove this location before starting.");
            Grid.SetColumn(remove, 1); row.Children.Add(remove); _roots.Children.Add(row); _rootEditors.Add(box);
            AutomationProperties.SetName(box, "Analysis location " + _rootEditors.Count);
        }
        private async Task ChooseFoldersAsync()
        {
            var folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Analyze folders", AllowMultiple = true });
            foreach (var folder in folders) if (folder.TryGetLocalPath() is { } path) AddChosenLocation(path);
        }
        private async Task ChooseFilesAsync()
        {
            var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Analyze files", AllowMultiple = true });
            foreach (var file in files) if (file.TryGetLocalPath() is { } path) AddChosenLocation(path);
        }
        private void AddChosenLocation(string path)
        {
            if (_rootEditors.FirstOrDefault(e => string.IsNullOrWhiteSpace(e.Text)) is { } blank) blank.Text = path;
            else if (_rootEditors.Count < AnalysisLimits.Roots) AddRoot(path);
            else Model.SetStatus("32 locations are captured. Remove a location before adding another.");
        }
        private Control Row(AnalysisTreeItem item)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*,125,115,60,80"), Margin = new Thickness(4, 3) };
            row.SizeChanged += (_, _) => FitColumns(row);
            var mark = new CheckBox { IsChecked = item.NodeId is { } id && Model.Selection.Marks.Contains(id), IsEnabled = item.NodeId.HasValue, Margin = new Thickness(0, 0, 6, 0) };
            mark.Click += (_, e) => { Model.Focus(item.Key, true); e.Handled = true; };
            void UpdateMark() => mark.IsChecked = item.NodeId is { } id && Model.Selection.Marks.Contains(id);
            mark.AttachedToVisualTree += (_, _) => Model.Changed += UpdateMark; mark.DetachedFromVisualTree += (_, _) => Model.Changed -= UpdateMark;
            row.Children.Add(mark);
            string[] values = [AnalysisNames.Display(item.Name), DiskSpaceViewModel.ValueText(item, Model.Tree?.Projection.Metric ?? Model.Metric), DiskSpaceViewModel.LogicalText(item), item.Files.ToString("N0"), DiskSpaceViewModel.StatusText(item)];
            for (int i = 0; i < values.Length; i++) { var text = new TextBlock { Text = values[i], TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(text, i + 1); row.Children.Add(text); }
            ToolTip.SetTip(row, new RichTip(AnalysisNames.Display(item.Name, 240), detail: $"Known active value: {DiskSpaceViewModel.ValueText(item, Model.Tree?.Projection.Metric ?? Model.Metric, true)}. File size: {DiskSpaceViewModel.LogicalText(item, true)}. Files: {item.Files}.", hints: [item.Unknown ? "Some measurements or namespace entries are unavailable." : "Observed metadata, not an immutable filesystem snapshot."]));
            AutomationProperties.SetName(row, string.Join("; ", values)); return row;
        }
        private static void FitColumns(Grid row)
        {
            bool wide = row.Bounds.Width >= 750;
            row.ColumnDefinitions = new ColumnDefinitions(wide ? "32,*,125,115,60,80" : "32,*,115,0,0,0");
            foreach (var child in row.Children) if (Grid.GetColumn(child) >= 3) child.IsVisible = wide;
        }
        private void Refresh()
        {
            if (Model.Comparison is not null) return;
            if (_disposed) return; _updating = true;
            try
            {
                _setupViewport.IsVisible = Model.Phase == AnalysisPhase.Ready; _roots.IsEnabled = !Model.IsPreparing; _start.IsEnabled = !Model.IsPreparing;
                _components.IsEnabled = !Model.IsPreparing && Model.Phase == AnalysisPhase.Ready;
                _resolved.Text = Model.ResolvedTargets; _status.Text = Model.Status; _error.Text = Model.FilterError; _error.IsVisible = Model.FilterError is not null;
                _pause.IsEnabled = Model.Running; _stop.IsEnabled = Model.Running || Model.IsPreparing;
                _save.IsEnabled = Model.CanSave && !_persistenceBusy;
                _openSaved.IsEnabled = Model.HasPrivateStorage && !_persistenceBusy;
                _compare.IsEnabled = Model.CanCompare && !_persistenceBusy;
                _stop.Content = Model.IsPreparing ? "Stop binding" : "Stop";
                _pause.Content = Model.Phase is AnalysisPhase.Paused or AnalysisPhase.Pausing ? "Resume" : "Pause";
                var tree = Model.Tree;
                if (tree is not null && !_shownRoots.Equals(tree.Snapshot.Roots))
                {
                    _shownRoots = tree.Snapshot.Roots;
                    var choices = new[] { new RootChoice(null, "All locations") }.Concat(_shownRoots.Select(r => new RootChoice(r.Id, AnalysisNames.Display(r.Label, 80, true) + " [" + r.Id + "]"))).ToArray();
                    _rootFilter.ItemsSource = choices; _rootFilter.SelectedItem = choices.FirstOrDefault(c => c.Id == Model.RootFilter);
                }
                _rootFilter.IsVisible = tree is not null && tree.Snapshot.Roots.Length > 1;
                _unknown.IsVisible = tree is not null && (tree.Projection.UnknownFilterNodes.Count > 0 || tree.Projection.IncludesUnknownFilterResults);
                _unknown.Content = tree?.Projection.IncludesUnknownFilterResults == true ? "Show confirmed matches" : $"Inspect {tree?.Projection.UnknownFilterNodes.Count ?? 0:N0} uncertain results";
                _scope.Text = tree is null ? "Captured scope appears here after Start." : AnalysisNames.Display(tree.NameOf(Model.Selection.Scope));
                _summary.Text = Model.ScopeSummary;
                _coverage.Text = tree is null ? "Unknown, unavailable and zero remain separate." :
                    (tree.Snapshot.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "Traversal complete under the captured policy" : "Traversal has gaps") +
                    (tree.Snapshot.Freshness == AnalysisFreshness.Observed ? ". Observed metadata over time." : ". The source may have changed or become unavailable.") +
                    " Reported allocation can share physical blocks; expected free-space gain is unknown.";
                if (tree is not null)
                {
                    var totals = tree.Projection.Totals;
                    ulong issueCount = tree.Snapshot.IssueTotals.Count > 0 ? tree.Snapshot.IssueTotals.Values.Aggregate(0ul, (a, b) => checked(a + b)) : (ulong)tree.Snapshot.Issues.Length;
                    _coverage.Text += $" Missing measurements: {totals.Missing.Values.Aggregate(0ul, (a, b) => checked(a + b)):N0}. Traversal boundaries: {tree.BoundaryCount:N0}; issues: {issueCount:N0}.";
                    _coverage.Text += tree.Snapshot.Policy.ComponentPreset == "detailed"
                        ? " Detailed: directory allocation requested; extra component coverage unknown."
                        : " Standard: reported allocation; extra component coverage unknown.";
                    ToolTip.SetTip(_coverage, new RichTip("Measurement coverage", detail: tree.ComponentCoverageDescription));
                    AutomationProperties.SetHelpText(_coverage, tree.ComponentCoverageDescription);
                    if (!totals.CompatibleBasis) _coverage.Text += " These locations report different allocation components. Choose a location for its On disk view; File size and Files remain available together.";
                    if (tree.Projection.IncludesUnknownFilterResults) _coverage.Text += " Showing uncertain filter results, not confirmed matches.";
                    else if (tree.Projection.UnknownFilterNodes.Count > 0) _coverage.Text += $" {tree.Projection.UnknownFilterNodes.Count:N0} uncertain filter results are excluded from confirmed matches.";
                    if (tree.Projection.FilterText != Model.Query) _coverage.Text += " The previous valid filter remains displayed while the new query is applied.";
                    if (tree.Projection.Metric == AnalysisMetric.UniqueFileSize && totals.IdentityUnverified > 0)
                        _coverage.Text += $" {totals.IdentityUnverified} path bytes have unverified identities and are excluded from unique chart area.";
                }
                if (!_shownRows.Equals(Model.Rows)) { _shownRows = Model.Rows; _list.ItemsSource = Model.Rows; }
                _list.SelectedItem = Model.Rows.FirstOrDefault(r => r.Key == Model.Selection.Focus);
                if (tree is not null)
                {
                    _treemap.SetView(tree, Model.Selection.Scope, Model.Selection.Focus, Model.Selection.Marks);
                    _sunburst.SetView(tree, Model.Selection.Scope, Model.Selection.Focus, Model.Selection.Marks);
                    var selected = tree.Items.GetValueOrDefault(Model.Selection.Focus ?? "");
                    _inspector.Text = selected is null ? "Choose an entry in either chart or the table. Marks and the review basket are separate." :
                        AnalysisNames.Display(selected.Name) + "\n" + Model.FocusedDisplayPath() + "\n" + DiskSpaceViewModel.StatusText(selected) +
                        $"\n\nActive metric: {DiskSpaceViewModel.ValueText(selected, tree.Projection.Metric, true)}\nFile size: {DiskSpaceViewModel.LogicalText(selected, true)}\nFile paths: {selected.Files}\n" +
                        (selected.NameEvidence is { } evidence ? evidence + "\n" : "") +
                        (selected.IdentityUnverified > 0 ? $"Identity-unverified path bytes: {selected.IdentityUnverified}. Aliases may be counted more than once.\n" : "") +
                        (selected.SharedElsewhere ? "Shared objects are charged once at their common parent. Removing one alias does not promise free space.\n" : "") +
                        (selected.Unknown ? "Some requested measurements or namespace entries are unknown.\n" : "") + "\nObservations do not authorize current filesystem operations.";
                }
                _basket.Text = $"Review basket: {Model.Selection.Basket.Count:N0} references · expected free-space gain unknown";
                if (tree is not null)
                {
                    int hidden = Model.Selection.Marks.Count(id => !tree.Items.TryGetValue(AnalysisTree.Key(id), out var marked) || !marked.Visible);
                    if (hidden > 0) _basket.Text += $" · {hidden:N0} marked items hidden by filters";
                }
                bool tableOnly = _mode.SelectedIndex == 2 || Bounds.Width < 640;
                _chartHost.IsVisible = !tableOnly; _treemap.IsVisible = _mode.SelectedIndex == 0; _sunburst.IsVisible = _mode.SelectedIndex == 1;
                _explorer.RowDefinitions[0].Height = tableOnly ? new GridLength(0) : new GridLength(3, GridUnitType.Star);
                _explorer.RowDefinitions[1].Height = tableOnly ? new GridLength(0) : GridLength.Auto;
                _inspectorScroll.IsVisible = Bounds.Width >= 900; _explorer.ColumnDefinitions[1].Width = Bounds.Width >= 900 ? new GridLength(300) : new GridLength(0);
                _inspect.IsVisible = Bounds.Width < 900; _inspect.IsEnabled = tree is not null;
            }
            finally { _updating = false; }
        }
        private async Task CopyPathAsync()
        {
            if (Model.FocusedPath() is { } path && _window.Clipboard is { } clipboard) await clipboard.SetTextAsync(path);
            else Model.SetStatus("A lossless literal path is unavailable for this selection. Raw source names remain in its observations.");
        }
        private async Task InspectAsync()
        {
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(new TextBlock { Text = _inspector.Text, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(ActionButton("Copy path", () => _ = CopyPathAsync(), "Copy the literal observation path."));
            body.Children.Add(ActionButton("Mark current members", Model.MarkCurrentMembers, "Capture current real members; future observations are not selected."));
            body.Children.Add(ActionButton("Add to review", Model.StageSelection, "Stage inert references for explicit review."));
            body.Children.Add(Unavailable("Preview", new AnalysisCapabilities().For(AnalysisVerb.Preview).Reason));
            Model.Interaction(true);
            try { await _window._dialogs.ShowCustomAsync("Selection inspector", body, [new DialogButton("Close details", true, IsDefault: true, IsCancel: true)]); }
            finally { Model.Interaction(false); }
        }
        private async Task ReviewAsync()
        {
            var captured = Model.Selection.Basket.Order().ToImmutableArray(); var tree = Model.Tree;
            string paths = tree is null ? "" : string.Join(Environment.NewLine, captured.Take(200).Select(id => AnalysisNames.Display(tree.Items.GetValueOrDefault(AnalysisTree.Key(id))?.Name ?? "Missing observation")));
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(new TextBlock { Text = $"0 included · {captured.Length:N0} skipped. Expected free-space gain: unknown.", TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = new AnalysisCapabilities().For(AnalysisVerb.Recycle).Reason, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new ScrollViewer { MaxHeight = 260, Content = new TextBlock { Text = paths, TextWrapping = TextWrapping.Wrap } });
            body.Children.Add(Unavailable("Recycle selected entries", "A qualified live binding and a fresh exact-entry review are required. These references remain skipped."));
            Model.Interaction(true);
            try { await _window._dialogs.ShowCustomAsync("Review cleanup — keep files", body, [new DialogButton("Cancel / Keep files", false, IsDefault: true, IsCancel: true)]); }
            finally { Model.Interaction(false); }
        }
        public void HandleKeys(KeyEventArgs e)
        {
            if (e.Handled || KeyMapper.ToChord(e.Key, e.KeyModifiers) is not { } chord) return;
            var keymap = _window._keymap();
            string? Resolve(CommandContext context) => keymap.Resolve(chord, context) ??
                (OperatingSystem.IsMacOS() && KeyMapper.MacCommandAlias(chord) is { } alias ? keymap.Resolve(alias, context) : null);
            if (_window._dialogs.IsOpen)
            {
                if (Resolve(CommandContext.DiskSpaceReview) == CommandIds.DiskSpaceReviewCancel)
                { _window._dialogs.CancelAll(); e.Handled = true; }
                return;
            }
            string? command = Resolve(CommandContext.DiskSpace);
            bool editor = _window.FocusManager?.GetFocusedElement() is TextBox;
            if (command == CommandIds.DiskSpaceClose) { _window.Close(); e.Handled = true; return; }
            if (Model.Comparison is not null)
            {
                if (command == CommandIds.DiskSpaceFilter) { _comparisonQuery?.Focus(); e.Handled = true; }
                return;
            }
            if (command == CommandIds.DiskSpaceFilter) { _query.Focus(); e.Handled = true; return; }
            if (editor) return;
            bool handled = true;
            switch (command)
            {
                case CommandIds.DiskSpaceReview: _ = ReviewAsync(); break;
                case CommandIds.DiskSpaceBack: Model.Back(); break;
                case CommandIds.DiskSpaceForward: Model.Forward(); break;
                case CommandIds.DiskSpaceParent: Model.Parent(); break;
                case CommandIds.DiskSpacePreview: Model.SetStatus(new AnalysisCapabilities().For(AnalysisVerb.Preview).Reason); break;
                case CommandIds.DiskSpaceCopy: Model.SetStatus(new AnalysisCapabilities().For(AnalysisVerb.Copy).Reason); break;
                case CommandIds.DiskSpaceMove: Model.SetStatus(new AnalysisCapabilities().For(AnalysisVerb.Move).Reason); break;
                case CommandIds.DiskSpaceMark when _list.IsKeyboardFocusWithin || _treemap.IsKeyboardFocusWithin || _sunburst.IsKeyboardFocusWithin: Model.ToggleFocusedMark(); break;
                case CommandIds.DiskSpaceEnter when _list.IsKeyboardFocusWithin || _treemap.IsKeyboardFocusWithin || _sunburst.IsKeyboardFocusWithin:
                    if (Model.Selection.Focus is { } focus) ActivateItem(focus, false, true);
                    break;
                default: handled = false; break;
            }
            e.Handled = handled;
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; _timer.Stop(); _filterTimer.Stop(); Model.Changed -= Refresh;
            _treemap.Clear(); _sunburst.Clear(); _list.ItemsSource = null; ReleaseComparisonView(); Model.Dispose();
        }
    }
}
