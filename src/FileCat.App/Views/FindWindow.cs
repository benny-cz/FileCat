using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Views;

/// <summary>
/// Find files (Alt+F7) as Salamander has it (plan §11, SEARCH-002): a window of its own that leaves the panels free, so
/// a search runs while you work and several can run at once. The criteria are above; the found items are below in a
/// panel's list, which acts on the original items with the panels' own commands (F3, F4, F5, F6, F8, …). A new search
/// can replace the found items, keep only those it matches too, remove those it matches, or add its matches.
/// </summary>
public sealed class FindWindow : Window, IViewActions
{
    private static readonly List<FindWindow> s_open = [];

    /// <summary>
    /// Panel commands that act on the found items (their shortcuts are the panels'); Enter opens, Space shows the item
    /// in the active panel, and Ctrl+H hides it from the list.
    /// </summary>
    private static readonly HashSet<string> ListCommands =
    [
        CommandIds.View, CommandIds.ViewAlternate, CommandIds.Edit, CommandIds.HexEdit, CommandIds.Copy, CommandIds.Move,
        CommandIds.Delete, CommandIds.DeletePermanent, CommandIds.Properties, CommandIds.UserMenu, CommandIds.OpenWithSystem,
        CommandIds.Reveal, CommandIds.CopyNames, CommandIds.CopyPaths, CommandIds.CopyUncPaths, CommandIds.CopyToClipboard,
        CommandIds.CutToClipboard, CommandIds.MarkToggleDown, CommandIds.MarkAll, CommandIds.MarkNone, CommandIds.MarkInvert,
        CommandIds.MarkInvertAll, CommandIds.MarkSelectMask, CommandIds.MarkUnselectMask, CommandIds.MarkSameExt,
        CommandIds.UnmarkSameExt, CommandIds.MarkSameName, CommandIds.UnmarkSameName, CommandIds.MarkRestore, CommandIds.Checksum,
        CommandIds.Pack, CommandIds.Attributes, CommandIds.AddToWorkingSet, CommandIds.RemoveFromSet, CommandIds.CompareFiles,
        CommandIds.ApplyCommand, CommandIds.BulkRename, CommandIds.SortName, CommandIds.SortExtension, CommandIds.SortTime,
        CommandIds.SortSize, CommandIds.SortNone, CommandIds.Refresh,
    ];

    private readonly MainViewModel _vm;
    private readonly AppServices _services;
    private readonly OverlayDialogService _dialogs;
    private readonly Panel _overlay = new() { IsVisible = false };
    // The found items live in a tab of a panel outside the workspace, so the panels' commands work on them.
    private readonly PanelViewModel _panel;
    private readonly FileListControl _list = new() { IsActivePanel = true };
    private readonly TextBlock _placeholder = new()
    {
        Text = "Found items appear here. Enter searches; Ctrl+I keeps only the items a new search finds too, Ctrl+S removes them, and Ctrl+W adds its finds.",
        Classes = { "muted" },
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 560,
        TextAlignment = TextAlignment.Center,
    };
    private TabViewModel? _tab;
    private CommandScope? _scope;

    private readonly TextBox _names = new() { PlaceholderText = "* (a word finds names containing it; masks: *.cs;*.txt|*test*)" };
    private readonly TextBox _lookIn = new() { PlaceholderText = "Folders to search, separated by semicolons" };
    private readonly TextBox _text = new() { PlaceholderText = "Text inside files (optional)" };
    private readonly CheckBox _subfolders = new() { Content = "Subfolders", IsChecked = true };
    private readonly CheckBox _hidden = new() { Content = "Hidden items" };
    private readonly CheckBox _matchCase = new() { Content = "Match case" };
    private readonly CheckBox _wholeWords = new() { Content = "Whole words" };
    private readonly CheckBox _regex = new() { Content = "Regular expression" };
    private readonly CheckBox _hex = new() { Content = "Hex" };
    private readonly Button _find = new() { Content = "Find", Classes = { "primary" } };
    private readonly Button _stop = new() { Content = "Stop", IsEnabled = false };
    private readonly Button _skip = new() { Content = "Skip current folder", IsEnabled = false };
    private readonly Button _logButton = new() { Content = "Log", IsEnabled = false };
    private readonly TextBlock _status = new() { Classes = { "small", "muted" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _error = new() { Classes = { "error" }, IsVisible = false, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _message = new() { Classes = { "small" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Panel _lookInHost = new();
    private readonly PathCompletion _completion;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    // Searching within a result set (Find in a panel that shows one): its items are the scope.
    private readonly ResultSet? _withinSet;
    private readonly IReadOnlyList<(ItemRef Item, string Relative)>? _within;

    private ResultSet? _set;           // the found items
    private ResultSet? _refineScratch; // a refining search's own matches
    private RefineMode _mode;
    private int _appended;
    private SearchSession? _session;
    private CancellationTokenSource? _cts;
    private DateTime _lastRefresh;
    private int _shownCount = -1;
    private bool _closed;

    /// <summary>Open Find windows, oldest first.</summary>
    public static IReadOnlyList<FindWindow> OpenWindows => s_open;

    /// <summary>Opens Find for <paramref name="root"/>, or within a result set's items.</summary>
    public static FindWindow Open(MainViewModel vm, string root, ResultSet? within)
    {
        var window = new FindWindow(vm, root, within);
        s_open.Add(window);
        window.Closed += (_, _) => s_open.Remove(window);
        window.Show();
        return window;
    }

    private FindWindow(MainViewModel vm, string root, ResultSet? within)
    {
        _vm = vm;
        _services = vm.Services;
        _panel = new PanelViewModel(vm.Workspace, vm.Services);
        _withinSet = within;
        _within = within?.Snapshot();
        Title = within is null ? "Find files" : "Find within results";
        Width = 1040; Height = 720; MinWidth = 640; MinHeight = 420;
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.ico"))); } catch (Exception) { }
        _dialogs = new OverlayDialogService(_overlay, () => _tab is null ? _names : _list);
        _list.FontSize = vm.ListFontSize;
        AutomationName(_list, "Found items");
        AutomationName(_names, "Names");
        AutomationName(_lookIn, within is null ? "Look in" : "Search within");
        AutomationName(_text, "Containing");
        AutomationName(_logButton, "Search log");

        var history = _services.History;
        _names.Text = history.SearchNames.FirstOrDefault() ?? "*";
        _lookIn.Text = _within is null ? root : $"{within!.Title} ({_within.Count:N0} items)";
        _lookIn.IsReadOnly = _within is not null;
        _hidden.IsChecked = _services.Settings.ShowHidden;
        HistoryKeys(_names, () => history.SearchNames);
        HistoryKeys(_text, () => history.SearchTexts);
        if (_within is null) HistoryKeys(_lookIn, () => history.SearchFolders);
        _hex.IsCheckedChanged += (_, _) => UpdateContentOptions();
        _regex.IsCheckedChanged += (_, _) => UpdateContentOptions();
        UpdateContentOptions();

        var root_ = new Grid();
        var dock = new DockPanel();
        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        dock.Children.Add(menu);
        var criteria = BuildCriteria();
        _completion = new PathCompletion(_lookIn, _lookInHost, () => _services.Settings.ShowHidden, () => root);
        _completion.Chosen += path =>
        {
            _lookIn.Text = path;
            _lookIn.CaretIndex = path.Length;
        };
        DockPanel.SetDock(criteria, Dock.Top);
        dock.Children.Add(criteria);
        var statusBar = new DockPanel { Margin = new Thickness(10, 4, 10, 6) };
        DockPanel.SetDock(_logButton, Dock.Right);
        statusBar.Children.Add(_logButton);
        statusBar.Children.Add(_message);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        dock.Children.Add(statusBar);
        var results = new Border
        {
            Margin = new Thickness(8, 4, 8, 0),
            BorderThickness = new Thickness(1),
            Child = new Panel { Children = { _list, _placeholder } },
            [!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("FcBorder"),
        };
        _list.IsVisible = false;
        dock.Children.Add(results);
        root_.Children.Add(dock);
        root_.Children.Add(_overlay);
        Content = root_;

        _find.Click += (_, _) => StartSearch(RefineMode.Replace);
        _stop.Click += (_, _) => Stop();
        _skip.Click += (_, _) => _session?.SkipCurrentFolder();
        _logButton.Click += (_, _) => _ = ShowLogAsync();
        _timer.Tick += (_, _) => OnTick();
        _list.OpenRequested += (_, _) => OpenFocused();
        _list.ContextMenuRequested += (_, point) => ShowListMenu(point);
        _list.DragRequested += async (_, press) => await DragAsync(press);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _names.Focus();
            _names.SelectAll();
        }, DispatcherPriority.Input);
        Closing += (_, _) => _dialogs.CancelAll();
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _cts?.Cancel();
            _tab?.Dispose();
        };
    }

    private static void AutomationName(Control control, string name) => Avalonia.Automation.AutomationProperties.SetName(control, name);

    // ---- Layout ------------------------------------------------------------------------------------------------

    private Control BuildCriteria()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(10, 8, 10, 4) };
        int row = 0;
        void Row(string label, Control input, Control? extra = null)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 10, 3) };
            Grid.SetRow(text, row);
            Grid.SetRow(input, row);
            Grid.SetColumn(input, 1);
            input.Margin = new Thickness(0, 3);
            grid.Children.Add(text);
            grid.Children.Add(input);
            if (extra is not null)
            {
                Grid.SetRow(extra, row);
                Grid.SetColumn(extra, 2);
                extra.Margin = new Thickness(6, 3, 0, 3);
                grid.Children.Add(extra);
            }
            row++;
        }
        Row("Named:", _names, HistoryButton(_names, () => _services.History.SearchNames, "Earlier names"));
        var lookInButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (_within is null)
        {
            lookInButtons.Children.Add(HistoryButton(_lookIn, () => _services.History.SearchFolders, "Earlier folders"));
            var local = new Button { Content = "Local drives" };
            var all = new Button { Content = "All drives" };
            ToolTip.SetTip(local, "Searches every local drive");
            ToolTip.SetTip(all, "Searches every local and network drive");
            local.Click += (_, _) => _lookIn.Text = string.Join("; ", Drives(network: false));
            all.Click += (_, _) => _lookIn.Text = string.Join("; ", Drives(network: true));
            lookInButtons.Children.Add(local);
            lookInButtons.Children.Add(all);
        }
        _lookInHost.Children.Add(_lookIn);
        Row(_within is null ? "Look in:" : "Search within:", _lookInHost, lookInButtons);
        Row("Containing:", _text, HistoryButton(_text, () => _services.History.SearchTexts, "Earlier texts"));
        var options = new WrapPanel { ItemSpacing = 12, LineSpacing = 4 };
        foreach (var box in new[] { _subfolders, _hidden, _matchCase, _wholeWords, _regex, _hex }) options.Children.Add(box);
        ToolTip.SetTip(_hex, "The text is bytes: hex pairs (4D 5A) and text in quotes (\"MZ\")");
        ToolTip.SetTip(_wholeWords, "No letter, digit, or underscore right before or after the text");
        if (_within is not null) _subfolders.IsEnabled = false;
        Row(string.Empty, options);
        var actions = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _find, _stop, _skip } };
        ToolTip.SetTip(_find, "Enter · Ctrl+I keeps only items found again · Ctrl+S removes them · Ctrl+W adds the new finds");
        ToolTip.SetTip(_stop, "Esc");
        DockPanel.SetDock(buttons, Dock.Left);
        actions.Children.Add(buttons);
        _status.Margin = new Thickness(12, 0, 0, 0);
        actions.Children.Add(_status);
        return new StackPanel { Children = { grid, actions, _error } };
    }

    /// <summary>A small button that lists a field's history; choosing an entry puts it in the field.</summary>
    private static Button HistoryButton(TextBox box, Func<IReadOnlyList<string>> history, string name)
    {
        var button = new Button { Content = "▾", Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Stretch };
        AutomationName(button, name);
        ToolTip.SetTip(button, name + " (Up and Down in the box go through them too)");
        button.Click += (_, _) =>
        {
            var flyout = new MenuFlyout();
            var entries = history();
            if (entries.Count == 0) flyout.Items.Add(new MenuItem { Header = "(nothing yet)", IsEnabled = false });
            foreach (var entry in entries.Take(25))
            {
                var item = new MenuItem { Header = entry };
                item.Click += (_, _) =>
                {
                    box.Text = entry;
                    box.CaretIndex = entry.Length;
                    box.Focus();
                };
                flyout.Items.Add(item);
            }
            flyout.ShowAt(box);
        };
        return button;
    }

    /// <summary>Up and Down go through a field's history, as in prompts.</summary>
    private static void HistoryKeys(TextBox box, Func<IReadOnlyList<string>> history)
    {
        int index = -1;
        box.KeyDown += (_, e) =>
        {
            var h = history();
            if (h.Count == 0 || e.KeyModifiers != KeyModifiers.None) return;
            if (e.Key == Key.Up && index < h.Count - 1) index++;
            else if (e.Key == Key.Down && index > 0) index--;
            else return;
            box.Text = h[index];
            box.CaretIndex = box.Text.Length;
            e.Handled = true;
        };
    }

    private static IEnumerable<string> Drives(bool network)
    {
        if (!OperatingSystem.IsWindows()) return ["/"];
        // The type is known without touching the drive (a sleeping disk or a lost share would hold the window).
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable || network && d.DriveType == DriveType.Network)
            .Select(d => d.Name);
    }

    private void UpdateContentOptions()
    {
        bool hex = _hex.IsChecked == true;
        _matchCase.IsEnabled = !hex;
        _wholeWords.IsEnabled = !hex;
        _regex.IsEnabled = !hex;
        _text.PlaceholderText = hex ? "Bytes: hex pairs (4D 5A 90) and text in quotes (\"MZ\")" : "Text inside files (optional)";
    }

    // ---- Menus -------------------------------------------------------------------------------------------------

    private Menu BuildMenu()
    {
        var menu = new Menu();
        menu.Items.Add(Submenu("_Files", FileItems()));
        menu.Items.Add(Submenu("_Edit", [
            CommandItem(CommandIds.MarkAll, "Select all"), CommandItem(CommandIds.MarkNone, "Unselect all"),
            CommandItem(CommandIds.MarkInvert, "Invert selection"), CommandItem(CommandIds.MarkSelectMask, "Select…"),
            CommandItem(CommandIds.MarkUnselectMask, "Unselect…"), new Separator(),
            ActionItem("Hide selected items", "Ctrl+H", HideSelected, () => _tab is not null),
            ActionItem("Hide duplicate names", null, HideDuplicateNames, () => _tab is not null),
        ]));
        menu.Items.Add(Submenu("F_ind", [
            ActionItem("Find", "Enter", () => StartSearch(RefineMode.Replace)),
            ActionItem("Keep only items found again", "Ctrl+I", () => StartSearch(RefineMode.Intersect), () => _set is not null),
            ActionItem("Remove items found again", "Ctrl+S", () => StartSearch(RefineMode.Subtract), () => _set is not null),
            ActionItem("Add new finds", "Ctrl+W", () => StartSearch(RefineMode.Append), () => _set is not null),
            new Separator(),
            ActionItem("Stop", "Escape", Stop, () => IsSearching),
            ActionItem("Skip current folder", null, () => _session?.SkipCurrentFolder(), () => IsSearching),
            new Separator(),
            ActionItem("Show in panel", null, ShowInPanel, () => _set is not null),
            ActionItem("Search log…", null, () => _ = ShowLogAsync(), () => _session?.Log.Count > 0),
        ]));
        menu.Items.Add(Submenu("_View", [
            CommandItem(CommandIds.SortName), CommandItem(CommandIds.SortExtension), CommandItem(CommandIds.SortTime),
            CommandItem(CommandIds.SortSize), CommandItem(CommandIds.SortNone), new Separator(), CommandItem(CommandIds.Refresh, "Check the items again"),
        ]));
        var logOnErrors = new MenuItem
        {
            Header = "Show the log after a search that could not read something",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _services.Settings.SearchLogOnErrors,
        };
        logOnErrors.Click += (_, _) =>
        {
            _services.Settings.SearchLogOnErrors = !_services.Settings.SearchLogOnErrors;
            logOnErrors.IsChecked = _services.Settings.SearchLogOnErrors;
            _services.SaveSettings();
        };
        menu.Items.Add(Submenu("_Options", [logOnErrors]));
        return menu;
    }

    private List<Control> FileItems() =>
    [
        ActionItem("Open", "Enter", OpenFocused, () => _tab is not null),
        ActionItem("Show in the active panel", "Space", () => FocusInPanel(enter: false), () => _tab is not null),
        CommandItem(CommandIds.View), CommandItem(CommandIds.ViewAlternate), CommandItem(CommandIds.Edit), new Separator(),
        CommandItem(CommandIds.Copy), CommandItem(CommandIds.Move), CommandItem(CommandIds.Delete), CommandItem(CommandIds.DeletePermanent),
        new Separator(),
        CommandItem(CommandIds.Properties), CommandItem(CommandIds.UserMenu), CommandItem(CommandIds.Checksum), CommandItem(CommandIds.Pack),
        new Separator(),
        CommandItem(CommandIds.CopyNames), CommandItem(CommandIds.CopyPaths), CommandItem(CommandIds.CopyToClipboard),
        CommandItem(CommandIds.AddToWorkingSet),
    ];

    private static MenuItem Submenu(string header, List<Control> items)
    {
        var top = new MenuItem { Header = header, ItemsSource = items };
        top.SubmenuOpened += (_, _) =>
        {
            foreach (var item in items.OfType<MenuItem>())
                if (item.Tag is Func<(bool Enabled, string? Reason)> state)
                {
                    var (enabled, reason) = state();
                    item.IsEnabled = enabled;
                    ToolTip.SetTip(item, enabled ? null : reason);
                }
        };
        return top;
    }

    private MenuItem ActionItem(string header, string? gesture, Action run, Func<bool>? available = null)
    {
        var item = new MenuItem { Header = header };
        if (gesture is not null) item.InputGesture = KeyGesture.Parse(gesture);
        item.Click += (_, _) => run();
        if (available is not null) item.Tag = new Func<(bool, string?)>(() => (available(), null));
        return item;
    }

    private MenuItem CommandItem(string id, string? header = null)
    {
        var def = _services.Commands.Get(id);
        var item = new MenuItem { Header = header ?? def?.Title ?? id };
        var chord = _services.Keymap.GetChords(id).FirstOrDefault();
        if (chord.Key is not null && KeyMapper.ToGesture(chord) is { } gesture) item.InputGesture = gesture;
        item.Click += (_, _) => Run(id);
        item.Tag = new Func<(bool, string?)>(() =>
        {
            if (_scope is null) return (false, "Search first: these act on the items found.");
            var availability = _vm.GetAvailability(id, _scope);
            return (availability.Enabled, availability.Reason);
        });
        return item;
    }

    private void ShowListMenu(Point point)
    {
        var menu = new ContextMenu { ItemsSource = FileItems().Concat([new Separator(), ActionItem("Hide selected items", "Ctrl+H", HideSelected)]).ToList() };
        foreach (var item in menu.Items.OfType<MenuItem>())
            if (item.Tag is Func<(bool Enabled, string? Reason)> state) item.IsEnabled = state().Enabled;
        menu.Open(_list);
    }

    // ---- Searching ---------------------------------------------------------------------------------------------

    private bool IsSearching => _session is { Finished: false };

    /// <summary>The criteria the window shows.</summary>
    internal SearchCriteria Criteria => new()
    {
        Names = _names.Text ?? "*",
        LookIn = _within is null ? _lookIn.Text ?? string.Empty : string.Empty,
        Subfolders = _subfolders.IsChecked == true,
        IncludeHidden = _hidden.IsChecked == true,
        Text = _text.Text ?? string.Empty,
        MatchCase = _matchCase.IsChecked == true,
        WholeWords = _wholeWords.IsChecked == true,
        Regex = _regex.IsChecked == true,
        Hex = _hex.IsChecked == true,
    };

    /// <summary>Starts a search; a refining one combines its matches with the items found so far when it finishes.</summary>
    internal void StartSearch(RefineMode mode)
    {
        ShowError(null);
        if (mode != RefineMode.Replace && _set is null) mode = RefineMode.Replace;
        var criteria = Criteria;
        var ignored = _services.Settings.SearchIgnoredFolders.Where(f => f.Enabled).Select(f => f.Folder).ToList();
        if (!criteria.TryBuildQuery(DateTime.UtcNow, ignored, _within, out var query, out var error))
        {
            ShowError(error);
            return;
        }
        var history = _services.History;
        AppServices.RememberText(history.SearchNames, string.IsNullOrWhiteSpace(criteria.Names) ? "*" : criteria.Names);
        if (criteria.Text.Length > 0) AppServices.RememberText(history.SearchTexts, criteria.Text);
        if (_within is null) AppServices.RememberText(history.SearchFolders, criteria.LookIn.Trim());
        _cts?.Cancel();
        var found = _services.ResultSets.Create(TitleOf(criteria), query!.Describe());
        found.FullFolders = true;
        _mode = mode;
        _appended = 0;
        if (mode == RefineMode.Replace)
        {
            _set = found;
            _refineScratch = null;
            ShowSet(found);
            Title = (_within is null ? "Find: " : "Find within results: ") + found.Title[("Search ".Length)..];
        }
        else
        {
            _refineScratch = found;
            _set!.Provenance += mode switch
            {
                RefineMode.Intersect => "; kept only those also matching " + query.Describe(),
                RefineMode.Subtract => "; removed those matching " + query.Describe(),
                _ => "; added " + query.Describe(),
            };
        }
        _session = new SearchSession(query, found);
        _cts = new CancellationTokenSource();
        var session = _session;
        var token = _cts.Token;
        _ = Task.Run(() => session.Run(token), token);
        _message.Text = string.Empty;
        UpdateButtons();
        _timer.Start();
        OnTick();
    }

    private static string TitleOf(SearchCriteria c)
    {
        var what = c.Text.Length > 0 ? $"\"{c.Text}\"" : string.IsNullOrWhiteSpace(c.Names) ? "*" : c.Names;
        return $"Search {what}";
    }

    private void ShowSet(ResultSet set)
    {
        var location = ResultSetProvider.LocationOf(set);
        if (_tab is null)
        {
            _tab = _panel.OpenTab(location);
            _scope = new CommandScope(_tab, _dialogs, this);
            _list.Tab = _tab;
            _list.IsVisible = true;
            _placeholder.IsVisible = false;
        }
        else
        {
            _tab.Navigate(location, record: false);
        }
        _shownCount = 0;
        _lastRefresh = DateTime.UtcNow;
    }

    private void Stop() => _cts?.Cancel();

    private void OnTick()
    {
        if (_session is not { } session || _set is null) return;
        bool finished = session.Finished;
        if (_mode == RefineMode.Append && _refineScratch is { } scratch)
        {
            // New finds join the list as they come.
            var snapshot = scratch.Snapshot();
            if (snapshot.Count > _appended)
            {
                _set.AddRange(snapshot.Skip(_appended));
                _appended = snapshot.Count;
            }
        }
        if (finished)
        {
            _timer.Stop();
            if (_mode is RefineMode.Intersect or RefineMode.Subtract && _refineScratch is { } refine)
            {
                if (refine.IsComplete)
                {
                    var kept = Refine.Combine(_set.Snapshot(), refine.Snapshot(), _mode).Select(k => k.Item).ToHashSet();
                    _set.Remove(_set.Snapshot().Select(s => s.Item).Where(i => !kept.Contains(i)).ToList());
                }
                else
                {
                    _message.Text = "The search stopped before it finished, so the list was left as it was.";
                }
            }
            Refresh(force: true);
            UpdateButtons();
            if (session.Log.Count > 0 && _services.Settings.SearchLogOnErrors && !_closed) _ = ShowLogAsync();
        }
        else
        {
            Refresh(force: false);
        }
        UpdateStatus();
    }

    /// <summary>Shows new finds, at most about once a second while a search runs (each look checks every item again).</summary>
    private void Refresh(bool force)
    {
        if (_tab is null || _set is null) return;
        int count = _set.Count;
        if (!force && (count == _shownCount || DateTime.UtcNow - _lastRefresh < TimeSpan.FromMilliseconds(900))) return;
        _shownCount = count;
        _lastRefresh = DateTime.UtcNow;
        _tab.Refresh();
    }

    private void UpdateStatus()
    {
        if (_session is not { } s) return;
        int notSearched = s.Log.Count;
        string state = s.Finished
            ? (_refineScratch is { IsComplete: false } || _refineScratch is null && _set is { IsComplete: false } ? "Stopped" : "Finished")
            : $"Searching {s.CurrentFolder}";
        string verb = _mode switch
        {
            RefineMode.Intersect => " matched again",
            RefineMode.Subtract => " to remove",
            RefineMode.Append => " new",
            _ => " found",
        };
        _status.Text = state + $" · {s.FoldersVisited:N0} folders" + (s.FilesExamined > 0 ? $" · {s.FilesExamined:N0} files read" : string.Empty)
            + $" · {s.Matches:N0}{verb}" + (_mode != RefineMode.Replace ? $" · {_set?.Count ?? 0:N0} in the list" : string.Empty)
            + (notSearched > 0 ? $" · {notSearched:N0} not searched (see the log)" : string.Empty)
            + (s.RegexTimedOut ? " · some regular expression matches timed out" : string.Empty);
        _logButton.IsEnabled = notSearched > 0;
        _logButton.Content = notSearched > 0 ? $"Log ({notSearched:N0})" : "Log";
    }

    private void UpdateButtons()
    {
        bool searching = IsSearching;
        _find.IsEnabled = true;
        _stop.IsEnabled = searching;
        _skip.IsEnabled = searching && _within is null;
    }

    private void ShowError(string? error)
    {
        _error.Text = error ?? string.Empty;
        _error.IsVisible = error is not null;
    }

    // ---- The search log -----------------------------------------------------------------------------------------

    /// <summary>What was not searched; Enter shows the folder in the active panel.</summary>
    private async Task ShowLogAsync()
    {
        if (_session is not { } session || _dialogs.IsOpen) return;
        var log = session.Log;
        if (log.Count == 0) return;
        var items = log.Select(e => new ChoiceItem(e.Path, e.Kind switch
        {
            SearchLogKind.Inaccessible => "Not accessible" + (e.Detail is null ? string.Empty : ": " + e.Detail),
            SearchLogKind.Ignored => "On the ignore list",
            SearchLogKind.Skipped => "Skipped while searching",
            _ => "No longer exists",
        })).ToList();
        var r = await _dialogs.ChooseAsync(new ChoiceOptions("Search log", items) { Hint = "What was not searched · Enter shows it in the active panel · Esc closes" });
        if (r.Index < 0) return;
        var entry = log[r.Index];
        string folder = Directory.Exists(entry.Path) ? entry.Path : Path.GetDirectoryName(entry.Path) ?? entry.Path;
        string? name = Directory.Exists(entry.Path) ? null : Path.GetFileName(entry.Path);
        ShowInMainPanel(Location.FileSystem(folder), name);
    }

    // ---- Acting on found items --------------------------------------------------------------------------------

    private bool TryFocused(out ItemRef item)
    {
        item = null!;
        if (_tab is null || !_tab.Listing.TryGetFocused(out var e) || e.Kind == EntryKind.Parent) return false;
        item = _tab.Listing.GetItemRef(_tab.Listing.FocusedStoreIndex);
        return true;
    }

    /// <summary>Runs a panel command on the found items; its dialogs open in this window.</summary>
    internal void Run(string id)
    {
        if (_scope is null) return;
        _ = _vm.ExecuteInAsync(_scope, id);
    }

    /// <summary>Enter: a folder opens in the active panel; a file opens as the system opens it.</summary>
    private void OpenFocused()
    {
        if (!TryFocused(out var item)) return;
        if (item.IsContainer) FocusInPanel(enter: true);
        else Run(CommandIds.OpenWithSystem);
    }

    /// <summary>Space: the active panel shows the item's folder with the item focused (or, entering, the folder itself).</summary>
    private void FocusInPanel(bool enter)
    {
        if (!TryFocused(out var item)) return;
        if (enter && item.IsContainer && item.FileSystemPath is { } folder) ShowInMainPanel(Location.FileSystem(folder), null);
        else ShowInMainPanel(item.Parent, item.Name);
    }

    private void ShowInMainPanel(Location location, string? focusName)
    {
        var tab = _vm.Workspace.ActiveTab;
        if (tab is null) return;
        tab.Navigate(location, focusName);
        if (_vm.View.TopLevel is Window main) main.Activate();
        _vm.View.FocusActivePanel();
    }

    /// <summary>The found items as a result set in the active panel (the set stays shared with this window).</summary>
    private void ShowInPanel()
    {
        if (_set is null) return;
        _vm.OpenResultSet(_set, IsSearching ? _cts : null);
        if (_vm.View.TopLevel is Window main) main.Activate();
        _vm.View.FocusActivePanel();
    }

    /// <summary>Removes the selected items from the list; nothing is deleted.</summary>
    private void HideSelected()
    {
        if (_tab is null || _set is null) return;
        var selection = _tab.Listing.GetSelection();
        int hidden = _set.Remove(selection);
        _tab.Refresh();
        ShowNotification($"{Formatters.Plural(hidden, "item was", "items were")} hidden from the list. Nothing was deleted.");
    }

    /// <summary>Keeps the first item of each name and hides the others.</summary>
    private void HideDuplicateNames()
    {
        if (_tab is null || _set is null) return;
        var seen = new HashSet<string>(Core.FileSystem.PathUtil.SafetyComparer);
        var repeats = _set.Snapshot().Select(s => s.Item).Where(i => !seen.Add(i.Name)).ToList();
        int hidden = _set.Remove(repeats);
        _tab.Refresh();
        ShowNotification(hidden == 0 ? "No name is listed twice." : $"{Formatters.Plural(hidden, "item with a repeated name was", "items with repeated names were")} hidden. Nothing was deleted.");
    }

    private async Task DragAsync(PointerPressedEventArgs press)
    {
        if (_tab is null) return;
        var selection = _tab.Listing.GetSelection();
        var paths = selection.Select(s => s.FileSystemPath).OfType<string>().Take(10_000).ToList();
        ItemSources.Release(selection);
        if (paths.Count == 0) return;
        var transfer = new DataTransfer();
        foreach (var p in paths)
        {
            Avalonia.Platform.Storage.IStorageItem? item = Directory.Exists(p)
                ? await StorageProvider.TryGetFolderFromPathAsync(p)
                : await StorageProvider.TryGetFileFromPathAsync(p);
            if (item is not null) transfer.Add(DataTransferItem.CreateFile(item));
        }
        await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    // ---- Keys --------------------------------------------------------------------------------------------------

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dialogs.IsOpen || KeyMapper.IsModifierKey(e.Key)) return;
        if (_completion.Shown.Count > 0 && e.Key is Key.Escape or Key.Enter or Key.Up or Key.Down or Key.Tab) return;
        var mods = e.KeyModifiers;
        if (mods == KeyModifiers.Control)
        {
            var refine = e.Key switch
            {
                Key.I => RefineMode.Intersect,
                Key.S => RefineMode.Subtract,
                Key.W => RefineMode.Append,
                _ => (RefineMode?)null,
            };
            if (refine is { } mode)
            {
                e.Handled = true;
                StartSearch(mode);
                return;
            }
        }
        if (e.Key == Key.Escape && mods == KeyModifiers.None)
        {
            e.Handled = true;
            // Esc stops a search; with none running, it closes the window.
            if (IsSearching) Stop();
            else Close();
            return;
        }
        bool inList = _tab is not null && (_list.IsFocused || _list.IsKeyboardFocusWithin);
        if (!inList)
        {
            // Enter in the criteria searches (unless it chooses a suggested folder).
            if (e.Key == Key.Enter && mods == KeyModifiers.None)
            {
                e.Handled = true;
                StartSearch(RefineMode.Replace);
            }
            return;
        }
        switch (e.Key)
        {
            case Key.Enter when mods == KeyModifiers.None:
                e.Handled = true;
                OpenFocused();
                return;
            case Key.Space when mods == KeyModifiers.None:
                e.Handled = true;
                FocusInPanel(enter: false);
                return;
            case Key.H when mods == KeyModifiers.Control:
                e.Handled = true;
                HideSelected();
                return;
        }
        if (KeyMapper.ToChord(e.Key, mods) is not { } chord) return;
        var id = _services.Keymap.Resolve(chord, CommandContext.Panel);
        if (id is null || !ListCommands.Contains(id)) return;
        e.Handled = true;
        Run(id);
    }

    // ---- IViewActions: commands run for the found items report and ask here ------------------------------------

    public void FocusActivePanel()
    {
        if (_tab is not null) _list.Focus();
        else _names.Focus();
    }

    public Task<string?> RenameInlineAsync(PromptOptions options) => Task.FromResult<string?>(null);

    public void FocusPathBox() => _lookIn.Focus();

    public void FocusCommandLine() { }

    public bool FocusCommandSearch() => false;

    public void CloseWhenIdle() { }

    public void OpenMenuBar() { }

    public void ShowContextMenu() => ShowListMenu(default);

    public void ShowNotification(string message, bool isError = false)
    {
        _message.Text = message;
        _message.Classes.Set("error", isError);
    }

    public void ReloadChrome() { }

    IClipboard? IViewActions.Clipboard => Clipboard;

    TopLevel? IViewActions.TopLevel => this;

    // ---- For tests ---------------------------------------------------------------------------------------------

    internal OverlayDialogService Dialogs => _dialogs;

    internal TabViewModel? ResultsTab => _tab;

    internal FileListControl List => _list;

    internal SearchSession? Session => _session;

    /// <summary>The names of the found items, in the order found.</summary>
    internal IReadOnlyList<string> Found => _set?.Snapshot().Select(s => s.Item.Name).ToList() ?? [];

    /// <summary>Whether the last search, and the refining after it, have finished.</summary>
    internal bool IsIdle => _session is null || _session.Finished && !_timer.IsEnabled;

    internal TextBox NamesBox => _names;

    internal string Error => _error.IsVisible ? _error.Text ?? string.Empty : string.Empty;

    internal void OpenLog() => _ = ShowLogAsync();

    internal void SetContent(string text, bool hex)
    {
        _text.Text = text;
        _hex.IsChecked = hex;
    }

    internal string Status => _status.Text ?? string.Empty;

    internal string Message => _message.Text ?? string.Empty;
}
