using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Views;

public partial class MainWindow : Window, IViewActions
{
    /// <summary>A menu's own submenu (the Registry's commands, dimmed everywhere else, stay out of the File menu's way).</summary>
    private sealed record Submenu(string Header, string[] Items);

    private static readonly (string Header, object[] Items)[] MenuLayout =
    [
        ("_File", [CommandIds.View, CommandIds.ViewAlternate, CommandIds.Edit, CommandIds.EditNew, CommandIds.HexEdit, CommandIds.EditSessions, "-", CommandIds.Copy, CommandIds.Duplicate,
            CommandIds.Move, CommandIds.Rename, CommandIds.MakeDirectory, CommandIds.Delete, CommandIds.DeletePermanent, "-",
            CommandIds.Pack, CommandIds.Unpack, CommandIds.TestArchive, "-",
            CommandIds.Checksum, CommandIds.VerifyChecksums, CommandIds.Attributes, CommandIds.CreateLink, CommandIds.BulkRename, CommandIds.ApplyCommand,
            CommandIds.AddToWorkingSet, CommandIds.RemoveFromSet,
            new Submenu("_Registry", [CommandIds.RegistryExport, CommandIds.RegistryImport, CommandIds.RegistrySaveData, CommandIds.RegistryLoadData,
                CommandIds.RegistryWritable, CommandIds.RegistryView]), "-",
            CommandIds.Undo, CommandIds.Properties, CommandIds.Reveal, CommandIds.OpenWithSystem, "-", CommandIds.Exit]),
        ("_Mark", [CommandIds.MarkToggleDown, CommandIds.MarkToggle, CommandIds.MarkSelectMask, CommandIds.MarkUnselectMask,
            CommandIds.MarkInvert, CommandIds.MarkInvertAll, CommandIds.MarkAll, CommandIds.MarkNone, "-", CommandIds.MarkSameExt,
            CommandIds.UnmarkSameExt, CommandIds.MarkSameName, CommandIds.UnmarkSameName, "-", CommandIds.MarkRestore, CommandIds.UnmarkHidden, "-",
            CommandIds.CopyNames, CommandIds.CopyPaths, CommandIds.CopyUncPaths]),
        ("_Navigate", [CommandIds.Parent, CommandIds.Enter, CommandIds.Root, CommandIds.Back, CommandIds.Forward, CommandIds.Home, "-",
            CommandIds.GoTo, CommandIds.LocationMenuLeft, CommandIds.LocationMenuRight, CommandIds.FindFolder, CommandIds.FolderHistory,
            CommandIds.FileHistory, CommandIds.Bookmarks, CommandIds.WorkingSets, "-", CommandIds.Refresh, CommandIds.ToggleHidden]),
        ("_Commands", [CommandIds.FindFiles, CommandIds.CompareDirectories, CommandIds.CompareFiles, CommandIds.FlatView, CommandIds.QuickFilter, CommandIds.FindDeleted, "-",
            CommandIds.CommandLineFocus, CommandIds.InsertName, CommandIds.InsertPath, CommandIds.OpenTerminal, CommandIds.UserMenu, "-",
            CommandIds.CopyToClipboard, CommandIds.CutToClipboard, CommandIds.PasteFromClipboard, "-",
            CommandIds.ConnectNetworkDrive, CommandIds.DisconnectNetworkDrive, "-", CommandIds.SftpConnect, CommandIds.SftpDisconnect]),
        ("_Panels", [CommandIds.SwitchPanel, CommandIds.SwitchPanelBack, CommandIds.SwapPanels, CommandIds.OpenInTarget, CommandIds.TargetToSource,
            CommandIds.QuickView, "-", CommandIds.AddPanel, CommandIds.ClosePanel, CommandIds.FocusPanelPicker, CommandIds.ChooseTarget,
            CommandIds.MaximizePanel, "-", CommandIds.NewTab, CommandIds.CloseTab, CommandIds.NextTab, CommandIds.PreviousTab, CommandIds.ReopenTab,
            CommandIds.TabList, CommandIds.DuplicateTab, CommandIds.CopyTabToTarget, CommandIds.LockTab, CommandIds.OpenInNewTab,
            CommandIds.OpenInNewTargetTab]),
        ("_View", [CommandIds.SortName, CommandIds.SortExtension, CommandIds.SortTime, CommandIds.SortSize, CommandIds.SortNone, "-",
            CommandIds.ColumnProfilePrefix + "0", CommandIds.ColumnProfilePrefix + "1", CommandIds.ColumnProfilePrefix + "2", "-",
            CommandIds.AnalyzeFolder, CommandIds.ColumnProfilePrefix + "3", CommandIds.ColumnProfilePrefix + "4", "-", CommandIds.ThemePick, CommandIds.ThemeCycle]),
        ("_Tools", [CommandIds.Operations, CommandIds.Palette, CommandIds.Settings, "-", CommandIds.SaveWorkspace, CommandIds.LoadWorkspace, "-",
            CommandIds.DiagnosticsExport, CommandIds.HexRecovery]),
        ("_Help", [CommandIds.Help, CommandIds.CheckUpdates, CommandIds.About]),
    ];

    private readonly MainViewModel _vm;
    private readonly Dictionary<PanelViewModel, PanelView> _panelViews = new();
    private readonly OverlayDialogService _dialogs;
    private readonly DispatcherTimer _notificationTimer;
    private bool _suppressTextInput;
    private bool _forceClose;

    public MainWindow() : this(null!, null) { }

    public MainWindow(MainViewModel vm, WindowPlacement? placement)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _dialogs = new OverlayDialogService(OverlayHost, () => ActivePanelView()?.List);
        vm.Dialogs = _dialogs;
        vm.View = this;
        // The user's first key press or click ends the quiet startup: connections may ask questions from then on.
        AddHandler(KeyDownEvent, (_, _) => vm.QuietConnect = false, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => vm.QuietConnect = false, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _notificationTimer.Tick += (_, _) =>
        {
            _notificationTimer.Stop();
            vm.ClearNotification();
        };
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.Workspace.LayoutChanged += RebuildPanels;
        vm.Workspace.Panels.CollectionChanged += (_, _) => RebuildPanels();
        vm.Workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.ActivePanel)) UpdateTitle();
        };
        if (vm.Services.Icons.Native is { } native) native.IconsLoaded += () =>
        {
            foreach (var pv in _panelViews.Values) pv.List.InvalidateVisual();
        };
        BuildMenu();
        if (placement is { Width: > 200, Height: > 200 })
        {
            Width = placement.Width;
            Height = placement.Height;
            if (placement.Maximized) WindowState = WindowState.Maximized;
        }

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnPreviewTextInput, RoutingStrategies.Tunnel);
        CommandLine.AddHandler(KeyDownEvent, OnCommandLineKeyDown, RoutingStrategies.Tunnel);
        Deactivated += (_, _) =>
        {
            _vm.UpdateKeyBar(KeyMods.None);
            ThemeAnimation.SetActive(false);
        };
        Activated += (_, _) => ThemeAnimation.SetActive(true);
        // The steampunk glass fills the space above the panels and below them.
        LayoutUpdated += (_, _) =>
        {
            var bands = (MainMenu.Bounds.Bottom, WorkspaceHost.Bounds.Bottom + WorkspaceHost.Margin.Bottom);
            if (Backdrop.GlassBands != bands)
            {
                Backdrop.GlassBands = bands;
                Backdrop.InvalidateVisual();
            }
        };
        _ = vm.Operations;
        // Unsaved hex edits hold sign-out like running jobs (the static event is unsubscribed when this window closes).
        Action editorsChanged = vm.RefreshSessionActivity;
        HexEditorWindow.UnsavedStateChanged += editorsChanged;
        Closed += (_, _) => HexEditorWindow.UnsavedStateChanged -= editorsChanged;
        Opened += (_, _) =>
        {
            if (OperatingSystem.IsWindows() && TryGetPlatformHandle()?.Handle is { } hwnd)
                Platform.Windows.WindowsPlatform.SetOwnerWindow(hwnd);
            RebuildPanels();
            Dispatcher.UIThread.Post(FocusActivePanel, DispatcherPriority.Loaded);
            _ = LoadInterruptedAsync();
        };
    }

    /// <summary>Jobs whose journal has no end are shown as interrupted, never silently resumed (plan §9.3).</summary>
    private async Task LoadInterruptedAsync()
    {
        var dir = _vm.Services.Paths.JournalDirectory;
        var interrupted = await Task.Run(() => Core.Jobs.JournalRecovery.Scan(dir));
        var messages = new List<string>();
        if (interrupted.Count > 0)
        {
            _vm.Operations.LoadInterrupted(interrupted);
            _vm.Operations.IsOpen = true;
            messages.Add($"{Formatters.Plural(interrupted.Count, "operation was", "operations were")} interrupted when FileCat last closed. Review them in the operations pane.");
        }
        int hex = await Task.Run(() => Platform.Windows.HexSaveJournal.Pending(_vm.Services.Paths.HexRecoveryDirectory).Count);
        if (hex > 0) messages.Add($"{Formatters.Plural(hex, "hex save was", "hex saves were")} interrupted. Finish or roll back with Tools → Recover interrupted hex save.");
        if (_vm.RestoreEditSessions() is { } sessions) messages.Add(sessions);
        if (messages.Count > 0) _vm.Notify(string.Join(" ", messages), true);
    }

    private const int MaxDragItems = 5000;

    private void AttachDragDrop(PanelView view, PanelViewModel panel)
    {
        DragDrop.SetAllowDrop(view, true);
        view.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
                ? (e.KeyModifiers & KeyModifiers.Shift) != 0 ? DragDropEffects.Move : DragDropEffects.Copy
                : DragDropEffects.None;
        });
        view.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files is null) return;
            var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
            bool move = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            e.Handled = true;
            await _vm.DropFilesAsync(panel, paths, move);
        });
        view.List.DragRequested += async (_, press) =>
        {
            var tab = panel.ActiveTab;
            if (tab is null) return;
            var sel = tab.Listing.GetSelection();
            if (sel.Count > MaxDragItems)
            {
                // Other applications cannot take a huge list well, and building it would stall the UI.
                ItemSources.Release(sel);
                _vm.Notify($"Too many items to drag ({sel.Count:N0}). Copy or move them with F5 or F6 instead.");
                return;
            }
            var paths = sel.Select(s => s.FileSystemPath).Where(p => p is not null).Cast<string>().ToList();
            if (paths.Count == 0)
            {
                // Archive members and files in disk images are copied into a private folder first (plan §4.2); the rest is
                // explained. Staging is capped so it finishes while the button is still down.
                var (staged, refusal) = await Task.Run(() => Core.Operations.DragStaging.Stage(sel, _vm.Services.Providers,
                    _vm.Services.Platform.FileOperations, _vm.Services.Paths.TempDirectory, CancellationToken.None));
                ItemSources.Release(sel);
                if (refusal is not null) _vm.Notify(refusal, true);
                if (staged is null) return;
                paths = [.. staged];
            }
            else
            {
                ItemSources.Release(sel);
            }
            var transfer = new DataTransfer();
            foreach (var p in paths)
            {
                Avalonia.Platform.Storage.IStorageItem? item = Directory.Exists(p)
                    ? await StorageProvider.TryGetFolderFromPathAsync(p)
                    : await StorageProvider.TryGetFileFromPathAsync(p);
                if (item is not null) transfer.Add(DataTransferItem.CreateFile(item));
            }
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Notification) && _vm.Notification is not null)
        {
            _notificationTimer.Stop();
            _notificationTimer.Interval = TimeSpan.FromSeconds(_vm.NotificationIsError ? 12 : 6);
            _notificationTimer.Start();
        }
    }

    private void OnNotificationPressed(object? sender, PointerPressedEventArgs e) => _vm.ClearNotification();

    private void UpdateTitle()
    {
        var tab = _vm.ActiveTab;
        Title = tab is null ? "FileCat" : $"{tab.Title} — FileCat";
    }

    // ---- Panels layout ----------------------------------------------------------------------------------

    private PanelView? ActivePanelView() =>
        _vm.Workspace.ActivePanel is { } p && _panelViews.TryGetValue(p, out var v) ? v : null;

    /// <summary>The active panel's file list (scripted input in the TV-01 benchmark).</summary>
    internal Controls.FileListControl? ActiveList => ActivePanelView()?.List;

    private PanelView GetView(PanelViewModel p)
    {
        if (_panelViews.TryGetValue(p, out var v)) return v;
        v = new PanelView { DataContext = p };
        v.Activated += () => _vm.Workspace.Activate(p);
        v.OpenRequested += () => _vm.Execute(CommandIds.Open);
        v.PathSubmitted += text => NavigateToText(p, text);
        v.LocationMenuRequested += () => _vm.Execute(_vm.Workspace.Panels.Count == 2 && _vm.Workspace.Panels.IndexOf(p) == 1 ? CommandIds.LocationMenuRight : CommandIds.LocationMenuLeft);
        v.MiddleClick += row => OpenRowInNewTab(p, row);
        AttachDragDrop(v, p);
        _panelViews[p] = v;
        return v;
    }

    private void RebuildPanels()
    {
        var ws = _vm.Workspace;
        foreach (var dead in _panelViews.Keys.Where(k => !ws.Panels.Contains(k)).ToList()) _panelViews.Remove(dead);
        WorkspaceHost.Children.Clear();
        WorkspaceHost.ColumnDefinitions.Clear();
        WorkspaceHost.RowDefinitions.Clear();
        var panels = ws.MaximizedPanel is { } max && ws.Panels.Contains(max) ? [max] : ws.Panels.ToList();
        bool rows = ws.Layout == "Rows";
        for (int i = 0; i < panels.Count; i++)
        {
            if (i > 0)
            {
                var splitter = new GridSplitter
                {
                    ResizeDirection = rows ? GridResizeDirection.Rows : GridResizeDirection.Columns,
                    Background = Avalonia.Media.Brushes.Transparent,
                    Focusable = false,
                };
                if (rows)
                {
                    WorkspaceHost.RowDefinitions.Add(new RowDefinition(5, GridUnitType.Pixel));
                    Grid.SetRow(splitter, WorkspaceHost.RowDefinitions.Count - 1);
                }
                else
                {
                    WorkspaceHost.ColumnDefinitions.Add(new ColumnDefinition(5, GridUnitType.Pixel));
                    Grid.SetColumn(splitter, WorkspaceHost.ColumnDefinitions.Count - 1);
                }
                WorkspaceHost.Children.Add(splitter);
            }
            var view = GetView(panels[i]);
            double weight = Math.Max(0.1, panels[i].Size);
            if (rows)
            {
                WorkspaceHost.RowDefinitions.Add(new RowDefinition(weight, GridUnitType.Star) { MinHeight = 140 });
                Grid.SetRow(view, WorkspaceHost.RowDefinitions.Count - 1);
                Grid.SetColumn(view, 0);
            }
            else
            {
                WorkspaceHost.ColumnDefinitions.Add(new ColumnDefinition(weight, GridUnitType.Star) { MinWidth = 260 });
                Grid.SetColumn(view, WorkspaceHost.ColumnDefinitions.Count - 1);
                Grid.SetRow(view, 0);
            }
            WorkspaceHost.Children.Add(view);
        }
        MaximizedStrip.IsVisible = ws.MaximizedPanel is not null;
        if (ws.MaximizedPanel is { } m)
        {
            var target = ws.GetTarget(m);
            MaximizedText.Text = $"Panel {m.Number} maximized · F11 restores" + (target is null ? " · no target panel" : $" · target: panel {target.Number} ({target.ActiveTab?.DisplayPath})");
        }
        UpdateTitle();
    }

    private void NavigateToText(PanelViewModel panel, string text)
    {
        var tab = panel.ActiveTab;
        if (tab is null) return;
        if (_vm.Services.Providers.TryParse(text, tab.Location, out var loc) && loc is not null)
        {
            string? focus = null;
            if (loc.IsFileSystem && File.Exists(loc.Path))
            {
                focus = Path.GetFileName(loc.Path);
                loc = Location.FileSystem(Path.GetDirectoryName(loc.Path)!);
            }
            tab.Navigate(loc, focus);
            FocusActivePanel();
        }
        else
        {
            _vm.Notify($"\"{text}\" is not a location FileCat can open.", true);
        }
    }

    private void OpenRowInNewTab(PanelViewModel panel, int row)
    {
        var tab = panel.ActiveTab;
        if (tab?.Location is null || row >= tab.Listing.VisibleCount) return;
        var e = tab.Listing.GetVisible(row);
        var child = _vm.Services.Providers.For(tab.Location).GetChildLocation(tab.Location, e);
        if (child is not null) panel.OpenTab(child, activate: false);
    }

    // ---- Menu --------------------------------------------------------------------------------------------

    private void BuildMenu()
    {
        var items = new List<MenuItem>();
        foreach (var (header, entries) in MenuLayout)
        {
            var top = new MenuItem { Header = header };
            top.ItemsSource = BuildItems(top, entries);
            items.Add(top);
        }
        MainMenu.ItemsSource = items;
    }

    /// <summary>A menu's items; each time it opens, they say whether they apply here (and why not, in their tooltip).</summary>
    private List<Control> BuildItems(MenuItem owner, IEnumerable<object> entries)
    {
        var children = new List<Control>();
        foreach (var entry in entries)
        {
            if (entry is Submenu sub)
            {
                var submenu = new MenuItem { Header = sub.Header };
                submenu.ItemsSource = BuildItems(submenu, sub.Items);
                children.Add(submenu);
                continue;
            }
            if (entry is not string id) continue;
            if (id == "-")
            {
                children.Add(new Separator());
                continue;
            }
            var def = _vm.Services.Commands.Get(id);
            if (def is null) continue;
            var chord = _vm.Services.Keymap.GetChords(id).FirstOrDefault();
            var mi = new MenuItem { Header = def.Title, Tag = id };
            if (chord.Key is not null && KeyMapper.ToGesture(chord) is { } g) mi.InputGesture = g;
            mi.Click += (_, _) =>
            {
                FocusActivePanel();
                _vm.Execute(id);
            };
            children.Add(mi);
        }
        owner.SubmenuOpened += (_, _) =>
        {
            foreach (var c in children.OfType<MenuItem>())
            {
                if (c.Tag is string cid)
                {
                    var a = _vm.GetAvailability(cid);
                    c.IsEnabled = a.Enabled;
                    ToolTip.SetTip(c, a.Enabled ? null : a.Reason);
                    // Column profiles by their names ("Columns: Details"), which Settings can change.
                    if (cid.StartsWith(CommandIds.ColumnProfilePrefix, StringComparison.Ordinal) &&
                        int.TryParse(cid.AsSpan(CommandIds.ColumnProfilePrefix.Length), out int profile))
                        c.Header = "Columns: " + _vm.Services.Columns.NameOf(profile);
                }
            }
        };
        return children;
    }

    private void OnKeyBarClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            FocusActivePanel();
            _vm.Execute(id);
        }
    }

    // ---- Keyboard routing (plan §4.4) ---------------------------------------------------------------------

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (KeyMapper.IsModifierKey(e.Key)) _vm.UpdateKeyBar(KeyMapper.ToMods(e.KeyModifiers));
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        _suppressTextInput = false;
        if (KeyMapper.IsModifierKey(e.Key))
        {
            _vm.UpdateKeyBar(KeyMapper.ToMods(e.KeyModifiers));
            return;
        }
        if (_dialogs.IsOpen || ActivePanelView()?.List.IsRenaming == true) return;
        if (KeyMapper.ToChord(e.Key, e.KeyModifiers) is not { } chord) return;

        var focused = FocusManager?.GetFocusedElement();
        bool inText = focused is TextBox;
        var tab = _vm.ActiveTab;

        if (!inText && tab is not null && tab.IsQuickSearchActive)
        {
            if (HandleQuickSearchKey(tab, chord, e)) return;
        }

        if (inText)
        {
            // Text fields keep their editing keys; function keys still reach panel commands.
            if (!chord.IsFunctionKey) return;
        }
        else if (chord == new KeyChord("Escape", KeyMods.None))
        {
            if (HandleEscape(tab)) e.Handled = true;
            return;
        }

        var id = _vm.Services.Keymap.Resolve(chord, CommandContext.Panel);
        // macOS: ⌘ works wherever the keymap says Ctrl, and ⌘Q quits, as Mac users expect.
        if (id is null && OperatingSystem.IsMacOS() && KeyMapper.MacCommandAlias(chord) is { } alias)
            id = alias == KeyMapper.MacQuit ? CommandIds.Exit : _vm.Services.Keymap.Resolve(alias, CommandContext.Panel);
        if (id is null) return;
        e.Handled = true;
        _suppressTextInput = true;
        _vm.ClearNotification();
        _vm.Execute(id);
    }

    private bool HandleQuickSearchKey(TabViewModel tab, KeyChord chord, KeyEventArgs e)
    {
        bool handled = true;
        switch (chord.Key)
        {
            case "Escape" when chord.Mods == KeyMods.None:
                tab.EndQuickSearch();
                break;
            case "Back" when chord.Mods == KeyMods.None:
                tab.QuickSearchBackspace();
                break;
            case "Down" when chord.Mods == KeyMods.None:
                tab.QuickSearchCycle(true);
                break;
            case "Up" when chord.Mods == KeyMods.None:
                tab.QuickSearchCycle(false);
                break;
            case "Enter" when chord.Mods == KeyMods.Ctrl:
                tab.QuickSearchCycle(true);
                break;
            case "Enter" when chord.Mods == (KeyMods.Ctrl | KeyMods.Shift):
                tab.QuickSearchCycle(false);
                break;
            case "Space" when chord.Mods is KeyMods.None or KeyMods.Shift:
                // Space extends the search text (arrives as text input).
                return true;
            case "Enter":
                tab.EndQuickSearch();
                handled = false;
                break;
            default:
                // Any other key ends quick search and then acts normally.
                tab.EndQuickSearch();
                handled = false;
                break;
        }
        if (handled)
        {
            e.Handled = true;
            _suppressTextInput = true;
        }
        return handled;
    }

    private bool HandleEscape(TabViewModel? tab)
    {
        if (tab is null) return false;
        if (_vm.Notification is not null)
        {
            _vm.ClearNotification();
            return true;
        }
        if (tab.CancelAnalysis()) return true;
        if (_vm.CancelSizing(tab)) return true;
        if (tab.Listing.State == ListingState.Loading || tab.Listing.IsRefreshing)
        {
            tab.Listing.CancelLoading();
            return true;
        }
        if (tab.Listing.Filter is not null)
        {
            tab.SetFilter(null);
            return true;
        }
        if (tab.ComparisonLabel is not null)
        {
            tab.ComparisonLabel = null;
            return true;
        }
        return false;
    }

    private void OnPreviewTextInput(object? sender, TextInputEventArgs e)
    {
        if (_suppressTextInput)
        {
            _suppressTextInput = false;
            e.Handled = true;
            return;
        }
        if (_dialogs.IsOpen || FocusManager?.GetFocusedElement() is TextBox) return;
        var text = e.Text;
        var tab = _vm.ActiveTab;
        if (tab is null || string.IsNullOrEmpty(text) || text.Any(char.IsControl)) return;
        // Space alone never starts a quick search (it marks).
        if (!tab.IsQuickSearchActive && string.IsNullOrWhiteSpace(text)) return;
        tab.QuickSearchType(text);
        e.Handled = true;
    }

    private void OnCommandLineKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (!string.IsNullOrEmpty(_vm.CommandLineText)) _vm.CommandLineText = string.Empty;
            else FocusActivePanel();
        }
        else if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            e.Handled = true;
            _vm.Execute((e.KeyModifiers & KeyModifiers.Shift) != 0 ? CommandIds.InsertPath : CommandIds.InsertName);
            Dispatcher.UIThread.Post(() => CommandLine.CaretIndex = CommandLine.Text?.Length ?? 0);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _vm.RunCommandLine();
        }
        else if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            _vm.CommandLineHistory(e.Key == Key.Up ? 1 : -1);
            Dispatcher.UIThread.Post(() => CommandLine.CaretIndex = CommandLine.Text?.Length ?? 0);
        }
    }

    // ---- IViewActions ---------------------------------------------------------------------------------------

    public void FocusActivePanel()
    {
        var v = ActivePanelView();
        v?.List.Focus(NavigationMethod.Tab);
    }

    public async Task<string?> RenameInlineAsync(PromptOptions options)
    {
        var inline = ActivePanelView()?.List.BeginRename(options);
        if (inline is not null) return await inline;
        return (await _dialogs.PromptAsync(options))?.Text;
    }

    public void FocusPathBox() => ActivePanelView()?.FocusPathBox();

    public void CloseWhenIdle()
    {
        _forceClose = true;
        SavePlacement();
        Close();
    }

    public void FocusCommandLine()
    {
        CommandLine.Focus();
        CommandLine.CaretIndex = CommandLine.Text?.Length ?? 0;
    }

    public void OpenMenuBar()
    {
        if (MainMenu.Items.Count > 0 && MainMenu.Items[0] is MenuItem first)
        {
            first.Focus();
            first.IsSubMenuOpen = true;
        }
    }

    public void ShowContextMenu() => ActivePanelView()?.ShowContextMenu();

    public void ShowNotification(string message, bool isError = false) => _vm.Notify(message, isError);

    public void ReloadChrome() => BuildMenu();

    IClipboard? IViewActions.Clipboard => Clipboard;

    TopLevel? IViewActions.TopLevel => this;

    // ---- Lifetime ---------------------------------------------------------------------------------------------

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_forceClose || e.Cancel) return;
        if (!_vm.CanCloseImmediately())
        {
            e.Cancel = true;
            _ = ConfirmCloseAsync();
            return;
        }
        SavePlacement();
    }

    private async Task ConfirmCloseAsync()
    {
        if (await _vm.ConfirmExitWithActiveWorkAsync())
        {
            _forceClose = true;
            SavePlacement();
            Close();
        }
    }

    private void SavePlacement()
    {
        var p = new WindowPlacement
        {
            Width = Bounds.Width,
            Height = Bounds.Height,
            X = Position.X,
            Y = Position.Y,
            Maximized = WindowState == WindowState.Maximized,
        };
        _vm.SaveWorkspace(p);
    }
}
