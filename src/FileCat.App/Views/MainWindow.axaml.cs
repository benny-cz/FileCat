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
    private readonly MainViewModel _vm;
    private readonly Dictionary<PanelViewModel, PanelView> _panelViews = new();
    private readonly OverlayDialogService _dialogs;
    private readonly DispatcherTimer _notificationTimer;
    private bool _suppressTextInput;
    private bool _forceClose;
    private readonly CommandSearchBar _commandSearch;

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
            // Another panel, or another tab in it: the title follows (and the tab's own title as it moves on).
            if (e.PropertyName is nameof(WorkspaceViewModel.ActivePanel) or nameof(WorkspaceViewModel.ActiveTab)) UpdateTitle();
        };
        if (vm.Services.Icons.Native is { } native) native.IconsLoaded += () =>
        {
            foreach (var pv in _panelViews.Values) pv.List.InvalidateVisual();
        };
        BuildMenu();
        BuildToolbar();
        // A new theme draws the menus' and the toolbar's icons in its colors.
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
        // Switches the keyboard flips too (Ctrl+H) show their state on the toolbar.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.ShowHiddenItems) or nameof(MainViewModel.ShowToolbar) or nameof(MainViewModel.ShowDriveButtons))
                foreach (var button in Toolbar.Children.OfType<Button>())
                    if (button.Tag is string id && CheckedState(id) is { } on)
                    {
                        button.Classes.Set("checked", on);
                        ToolTip.SetTip(button, ToolbarTip(id));
                        Avalonia.Automation.AutomationProperties.SetName(button, TitleOf(id, _vm.Services.Commands.Get(id)?.Title ?? id));
                    }
        };
        _commandSearch = new CommandSearchBar(CommandSearchBox, MenuRow, vm.CommandSearchEntries, () => vm.Services.History.RecentCommands,
            id =>
            {
                FocusActivePanel();
                vm.RunFromSearch(id);
            },
            vm.ChangeShortcutAsync, FocusActivePanel);
        MenuRow.SizeChanged += (_, _) => FitCommandSearch();
        WorkspaceHost.SizeChanged += (_, _) => UpdateLayoutStrip();
        MainMenu.SizeChanged += (_, _) => FitCommandSearch();
        if (placement is { Width: > 200, Height: > 200 })
        {
            Width = placement.Width;
            Height = placement.Height;
            if (placement.Maximized) WindowState = WindowState.Maximized;
        }

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AltChords.Attach(this);
        // Moving a panel by dragging its number: the handle keeps the pointer, and its events reach the window.
        AddHandler(PointerMovedEvent, OnPanelDragMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPanelDragReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, e) =>
        {
            if (_panelDrag is { } drag && ReferenceEquals(e.Pointer, drag.Pointer)) EndPanelDrag();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        // Dragging a tab along a tab strip, or to another panel's.
        AttachTabDrag();
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnPreviewTextInput, RoutingStrategies.Tunnel);
        CommandLine.AddHandler(KeyDownEvent, OnCommandLineKeyDown, RoutingStrategies.Tunnel);
        Deactivated += (_, _) =>
        {
            _vm.UpdateKeyBar(KeyMods.None);
            ThemeAnimation.SetActive(false);
        };
        Activated += (_, _) =>
        {
            ThemeAnimation.SetActive(true);
            if (!WindowsContextMenu.IsOpenOrRecentlyClosed)
                foreach (var panel in _panelViews.Values) panel.List.RefreshGitStatuses();
        };
        // The steampunk canopy ends above the panels; its lower rail begins below them.
        LayoutUpdated += (_, _) =>
        {
            var bands = (MenuRow.Bounds.Bottom, WorkspaceHost.Bounds.Bottom + WorkspaceHost.Margin.Bottom);
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
        // Windows says at once when a volume or the medium in a drive arrives or goes (WM_DEVICECHANGE), which the drive
        // watcher's own check every two seconds would otherwise find a moment later, or (for a medium) not at all.
        if (OperatingSystem.IsWindows())
            Win32Properties.AddWndProcHookCallback(this, (IntPtr _, uint message, IntPtr wParam, IntPtr lParam, ref bool _) =>
            {
                const uint WM_DEVICECHANGE = 0x0219;
                const int DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004, DBT_DEVTYP_VOLUME = 2;
                if (message == WM_DEVICECHANGE && (wParam == DBT_DEVICEARRIVAL || wParam == DBT_DEVICEREMOVECOMPLETE) && lParam != IntPtr.Zero &&
                    System.Runtime.InteropServices.Marshal.ReadInt32(lParam, 4) == DBT_DEVTYP_VOLUME)
                    vm.Services.Drives.Check(mediaChanged: true);
                return IntPtr.Zero;
            });
        Opened += (_, _) =>
        {
            if (OperatingSystem.IsWindows() && TryGetPlatformHandle()?.Handle is { } hwnd)
            {
                Platform.Windows.WindowsPlatform.SetOwnerWindow(hwnd);
                // The running operation on the taskbar button, for a minimized window too (release issue I30).
                _vm.Operations.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(OperationCenterViewModel.TaskbarState) or nameof(OperationCenterViewModel.TaskbarFraction))
                        Platform.Windows.TaskbarProgress.Set(hwnd, _vm.Operations.TaskbarState, _vm.Operations.TaskbarFraction);
                };
            }
            // The first frame is the light part (menus, toolbar, key bar, the startup mark) in the theme's colors; the
            // panels, the heavy part, are built right after it, so a slow start never shows a blank window.
            Dispatcher.UIThread.Post(() =>
            {
                _panelsDeferred = false;
                RebuildPanels();
                Dispatcher.UIThread.Post(FocusActivePanel, DispatcherPriority.Loaded);
            }, DispatcherPriority.Background);
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

    private Core.Platform.ProcessAccount? _account;
    private TabViewModel? _titleTab;

    /// <summary>
    /// The active tab's place, FileCat, and the account it runs as with its rights (elevated on Windows it starts with
    /// "Administrator: "). It follows the tab as it goes elsewhere: before, only another panel's activation changed it.
    /// </summary>
    private void UpdateTitle()
    {
        var tab = _vm.Workspace.ActiveTab;
        if (!ReferenceEquals(tab, _titleTab))
        {
            if (_titleTab is not null) _titleTab.PropertyChanged -= OnTitleTabChanged;
            _titleTab = tab;
            if (tab is not null) tab.PropertyChanged += OnTitleTabChanged;
        }
        _account ??= _vm.Services.Shell.Account;
        Title = _account.WindowTitle(tab?.Title);
    }

    private void OnTitleTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.Title)) UpdateTitle();
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
        // A panel's own location button: that panel is the source, and its menu opens.
        v.LocationMenuRequested += () =>
        {
            _vm.Workspace.Activate(p);
            _vm.Execute(CommandIds.LocationMenuSource);
        };
        v.MiddleClick += row => OpenRowInNewTab(p, row);
        v.MoveRequested += (press, handle) => BeginPanelDrag(p, press, handle);
        v.TabDragRequested += (press, tab, handle) => BeginTabDrag(p, tab, press, handle);
        AttachDragDrop(v, p);
        _panelViews[p] = v;
        return v;
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
        foreach (var (header, entries) in MainMenuModel.Layout)
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
            if (entry is MainMenuModel.Submenu sub)
            {
                var submenu = new MenuItem { Header = sub.Header };
                // A submenu shows the icon of its first command.
                if (sub.Items.OfType<string>().Select(CommandIcons.Get).FirstOrDefault(i => i is not null) is { } first)
                    submenu.Icon = new Image { Source = first, Width = 16, Height = 16 };
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
            var mi = new MenuItem { Header = TitleOf(id, def.Title), Tag = id };
            if (chord.Key is not null && KeyMapper.ToGesture(chord) is { } g) mi.InputGesture = g;
            if (CommandIcons.Get(id) is { } icon) mi.Icon = new Image { Source = icon, Width = 16, Height = 16 };
            mi.Click += (_, _) =>
            {
                FocusActivePanel();
                _vm.RememberCommand(id);
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
                    // A switch says what choosing it does now: "Hide hidden and system items" while they show.
                    if (CheckedState(cid) is not null && _vm.Services.Commands.Get(cid) is { } switchDef) c.Header = TitleOf(cid, switchDef.Title);
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
        if ((_panelDrag is not null || _tabDrag is not null) && e.Key == Key.Escape)
        {
            // Esc cancels moving a panel, or a tab.
            EndPanelDrag();
            EndTabDrag();
            e.Handled = true;
            return;
        }
        if (KeyMapper.IsModifierKey(e.Key))
        {
            _vm.UpdateKeyBar(KeyMapper.ToMods(e.KeyModifiers));
            return;
        }
        if (_dialogs.IsOpen || ActivePanelView()?.List.IsRenaming == true) return;
        // The command search keeps its keys: Enter runs, F2 changes a shortcut, Esc returns to the panel.
        if (CommandSearchBox.IsFocused) return;
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
        // With Alt held, after Avalonia's access-key handler saw it (or releasing Alt opens the menu bar).
        AltChords.Handle(this, e);
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
            case var _ when TypesText(e.Key, e.KeyModifiers):
                // A letter, digit, or punctuation extends the search: its text follows the key press, and only while
                // the key press is not handled (Windows drops the text of a handled one).
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

    /// <summary>
    /// Whether a key types a character: letters, digits (the Czech number row's accented letters too), and punctuation,
    /// with Shift or none; Ctrl+Alt as well, which is AltGr on Windows keyboards ("@" on a Czech one).
    /// </summary>
    private static bool TypesText(Key key, KeyModifiers modifiers)
    {
        var command = modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta);
        if (command != 0 && command != (KeyModifiers.Control | KeyModifiers.Alt)) return false;
        return key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9 or Key.Decimal
            or >= Key.OemSemicolon and <= Key.OemBackslash;
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

    private CommandLineCompletion.Cycle? _completionCycle;
    private bool _completing;

    /// <summary>
    /// Reads the folder off the UI thread (a network folder may be slow; after 3 s nothing is completed) and completes
    /// the command line unless it was edited meanwhile.
    /// </summary>
    private async Task CompleteCommandLineAsync(string text, int caret, string folder, bool backwards)
    {
        _completing = true;
        try
        {
            var cycle = _completionCycle;
            var work = Task.Run(() => CommandLineCompletion.Complete(text, caret, folder, backwards, cycle));
            if (await Task.WhenAny(work, Task.Delay(3000)) != work || work.Result is not { } done || CommandLine.Text != text) return;
            CommandLine.Text = done.Text;
            CommandLine.CaretIndex = done.Caret;
            _completionCycle = done.Cycle;
        }
        finally
        {
            _completing = false;
        }
    }

    private void OnCommandLineKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab && !KeyMapper.IsModifierKey(e.Key)) _completionCycle = null;
        if (e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == 0 && !string.IsNullOrEmpty(CommandLine.Text) &&
            _vm.ActiveTab?.Location is { IsFileSystem: true } here)
        {
            // Shell-style: the word before the caret, completed from the panel's folder; Tab again for the next name.
            e.Handled = true;
            if (!_completing) _ = CompleteCommandLineAsync(CommandLine.Text, CommandLine.CaretIndex, here.Path, (e.KeyModifiers & KeyModifiers.Shift) != 0);
            return;
        }
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

    public bool FocusCommandSearch() => CommandSearchBox.IsEffectivelyVisible && CommandSearchBox.Focus(NavigationMethod.Tab);

    /// <summary>The command search in the menu bar (tests read what it lists).</summary>
    internal CommandSearchBar CommandSearch => _commandSearch;

    /// <summary>
    /// The command search takes the room the menus leave, up to 300 DIP; in a window too narrow for it, Ctrl+Shift+P
    /// opens the same search in a dialog.
    /// </summary>
    private void FitCommandSearch()
    {
        double room = MenuRow.Bounds.Width - MainMenu.Bounds.Width - CommandSearchBox.Margin.Left - CommandSearchBox.Margin.Right;
        bool fits = room >= 120;
        if (CommandSearchBox.IsVisible != fits) CommandSearchBox.IsVisible = fits;
        double width = Math.Min(room, 300);
        if (fits && (double.IsNaN(CommandSearchBox.Width) || Math.Abs(CommandSearchBox.Width - width) > 0.5)) CommandSearchBox.Width = width;
        // The shortcut shows in the box where it fits, and always in its tooltip.
        string? gesture = _vm.Services.Keymap.GetGestureText(CommandIds.Palette);
        string placeholder = gesture is not null && width >= 280 ? $"Search commands ({gesture})" : "Search commands";
        if (CommandSearchBox.PlaceholderText != placeholder) CommandSearchBox.PlaceholderText = placeholder;
        ToolTip.SetTip(CommandSearchBox, "Type what you want to do: any command, by its name or other words for it"
            + (gesture is null ? string.Empty : $". {gesture} comes here from anywhere."));
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

    /// <summary>From the keyboard (Shift+F10, the Menu key): the menu opens at the focused item.</summary>
    public void ShowContextMenu() => ActivePanelView()?.ShowContextMenu(atFocus: true);

    public void ShowNotification(string message, bool isError = false) => _vm.Notify(message, isError);

    public void ReloadChrome()
    {
        BuildMenu();
        BuildToolbar(); // tooltips name the shortcuts, which may have changed
        FitCommandSearch(); // its hint names the shortcut
    }

    private void OnThemeChanged()
    {
        CommandIcons.Clear();
        BuildMenu();
        BuildToolbar();
    }

    /// <summary>A switch's title as choosing it would act now; every other command keeps its own.</summary>
    private string TitleOf(string id, string title) => id switch
    {
        CommandIds.ToggleHidden => _vm.ShowHiddenItems ? "Hide hidden and system items" : "Show hidden and system items",
        CommandIds.ToggleToolbar => _vm.ShowToolbar ? "Hide the toolbar" : "Show the toolbar",
        CommandIds.ToggleDriveButtons => _vm.ShowDriveButtons ? "Hide the place buttons" : "Show the place buttons",
        _ => title,
    };

    /// <summary>A toolbar button's tooltip: what it does now and its key, then what it takes and where its result goes.</summary>
    private string ToolbarTip(string id)
    {
        var def = _vm.Services.Commands.Get(id);
        var chord = _vm.Services.Keymap.GetChords(id).FirstOrDefault();
        return TitleOf(id, def?.Title ?? id) + (chord.Key is null ? "" : $" ({chord})") + (def?.Description is { Length: > 0 } d ? "\n" + d : "");
    }

    /// <summary>Commands that switch something on and off: their toolbar buttons show the state, and their titles say what choosing them does.</summary>
    private bool? CheckedState(string id) => id switch
    {
        CommandIds.ToggleHidden => _vm.ShowHiddenItems,
        CommandIds.ToggleToolbar => _vm.ShowToolbar,
        CommandIds.ToggleDriveButtons => _vm.ShowDriveButtons,
        _ => null,
    };

    /// <summary>
    /// The toolbar (D-49): the commands used most, grouped as Salamander groups its top toolbar. A button runs its command
    /// as the key would (the panel keeps the keyboard), and its tooltip names the key.
    /// </summary>
    private void BuildToolbar()
    {
        Toolbar.Children.Clear();
        foreach (var id in MainMenuModel.Toolbar)
        {
            if (id == "-")
            {
                Toolbar.Children.Add(new Border { Classes = { "toolbarSeparator" } });
                continue;
            }
            if (_vm.Services.Commands.Get(id) is not { } def || CommandIcons.Get(id) is not { } icon) continue;
            var button = new Button { Classes = { "toolbar" }, Content = new Image { Source = icon, Width = 16, Height = 16 }, Tag = id };
            ToolTip.SetTip(button, ToolbarTip(id));
            Avalonia.Automation.AutomationProperties.SetName(button, TitleOf(id, def.Title));
            if (CheckedState(id) == true) button.Classes.Add("checked");
            button.Click += (_, _) =>
            {
                FocusActivePanel();
                _vm.RememberCommand(id);
                _vm.Execute(id);
            };
            Toolbar.Children.Add(button);
        }
    }

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
