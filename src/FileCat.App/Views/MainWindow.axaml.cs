using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Views;

public partial class MainWindow : Window, IViewActions, IWorkspaceViewportProvider, IPanelResizeActions, IBreadcrumbActions
{
    // Screen readers follow a list's focused row and, on Linux, hear focus after window activation.
    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new FileCat.App.Controls.DescendantFocusWindowAutomationPeer(this);

    private readonly MainViewModel _vm;
    private readonly Dictionary<PanelViewModel, PanelView> _panelViews = new();
    private PanelView? _pointerFocusOwner;
    private long _pointerFocusAt;
    private readonly OverlayDialogService _dialogs;
    private readonly DispatcherTimer _notificationTimer;
    /// <summary>
    /// The text a key that ran a command would also type, swallowed if it arrives next; null when none is pending.
    /// macOS types into a text field without raising key-down, so a plain flag set by F7 or ⌘L swallowed the first
    /// character typed into the field the command had just focused.
    /// </summary>
    private string? _suppressTextInput;
    private bool _forceClose;
    private Task? _interruptedStartup;
    private readonly CommandSearchBar _commandSearch;
    private readonly WindowPlacement? _savedPlacement;
    private WindowPlacement? _normalPlacement;
    private readonly DispatcherTimer _normalPlacementTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private bool _placementTrackingReady;
    private bool _toolbarRefreshPending;
    private sealed record ToolbarPresentationState(CommandAvailability Availability, string Title, bool Checked);

    public MainWindow() : this(null!, null) { }

    public MainWindow(MainViewModel vm, WindowPlacement? placement)
    {
        ReadabilityPolicy.ApplyDensity(vm.Services.Settings.RowDensity);
        InitializeComponent();
        _savedPlacement = placement;
        _vm = vm;
        DataContext = vm;
        UpdateNotificationLiveSettings();
        LiveRegions.Watch(RestoredFocusAnnouncement);
        Classes.Set("comfortable", ReadabilityPolicy.Comfortable(vm.Services.Settings.RowDensity));
        _dialogs = new OverlayDialogService(OverlayHost, () => ActivePanelView()?.List, list =>
        {
            if (!OperatingSystem.IsMacOS()) return;
            RestoredFocusAnnouncement.Text = "Focus returned to " + list.DescribeFocus();
            LiveRegions.Repeat(RestoredFocusAnnouncement);
        });
        vm.Dialogs = _dialogs;
        vm.View = this;
        // The user's first key press or click ends the quiet startup: connections may ask questions from then on.
        AddHandler(KeyDownEvent, (_, _) => vm.QuietConnect = false, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => vm.QuietConnect = false, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, RecordPointerFocusIntent, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, _) => ClearPointerFocusIntent(), RoutingStrategies.Tunnel, handledEventsToo: true);
        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _notificationTimer.Tick += (_, _) => TryExpireNotification();
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.CommandAvailabilityChanged += OnCommandAvailabilityChanged;
        if (vm.CurrentNotification is { IsError: false })
        {
            _notificationTimer.Interval = TimeSpan.FromSeconds(6);
            _notificationTimer.Start();
        }
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
        ToolbarMore.Click += (_, _) => OpenToolbarMore();
        ToolTip.SetTip(ToolbarMore, "More toolbar commands, grouped by task. All commands also remain in the menus and command search.");
        BuildMenu();
        BuildToolbar();
        MenuInteractionTrace.Install(this, MainMenu);
        // A new theme draws the menus' and the toolbar's icons in its colors.
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) =>
        {
            _normalPlacementTimer.Stop();
            ThemeManager.ThemeChanged -= OnThemeChanged;
            vm.CommandAvailabilityChanged -= OnCommandAvailabilityChanged;
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        };
        _commandSearch = new CommandSearchBar(CommandSearchBox, MenuRow, vm.CommandSearchEntries, () => vm.Services.History.RecentCommands,
            id =>
            {
                FocusActivePanel();
                vm.RunFromSearch(id);
            },
            vm.ChangeShortcutAsync, FocusActivePanel,
            e =>
            {
                if (KeyMapper.ToChord(e.Key, e.KeyModifiers) is not { } chord) return false;
                string? command = vm.Services.Keymap.Resolve(chord, CommandContext.Panel);
                if (command is null && OperatingSystem.IsMacOS() && KeyMapper.MacCommandAlias(chord) is { } alias)
                    command = vm.Services.Keymap.Resolve(alias, CommandContext.Panel);
                return command == CommandIds.Help;
            },
            () => vm.Services.Keymap.GetGestureText(CommandIds.Help),
            vm.ShowKeyboardReferenceForSearchAsync);
        MenuRow.SizeChanged += (_, _) => FitCommandSearch();
        OverlayHost.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty) FitCommandSearch();
        };
        WorkspaceHost.SizeChanged += (_, _) => UpdateLayoutStrip();
        MainMenu.SizeChanged += (_, _) => FitCommandSearch();
        ApplySavedPlacement();
        _normalPlacementTimer.Tick += (_, _) =>
        {
            _normalPlacementTimer.Stop();
            RememberNormalPlacement();
        };
        SizeChanged += (_, _) => QueueNormalPlacement();
        PositionChanged += (_, _) => QueueNormalPlacement();
        PropertyChanged += (_, change) =>
        {
            if (change.Property != WindowStateProperty) return;
            if (WindowState == WindowState.Normal) QueueNormalPlacement();
            else _normalPlacementTimer.Stop();
        };
        Opened += (_, _) =>
        {
            if (_normalPlacement is null) ApplySavedPlacement(); // Some backends report screens only after opening.
            _placementTrackingReady = true;
            if (WindowState == WindowState.Normal) QueueNormalPlacement();
        };

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
            EndPanelResize(restore: false, returnFocus: false);
            _vm.ReconcileKeyBar(KeyMods.None);
            ThemeAnimation.SetActive(false);
        };
        Activated += (_, _) =>
        {
            // A popup or another top-level window can receive the modifier's release instead of this window.
            _vm.ReconcileKeyBar(KeyMods.None);
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
            _interruptedStartup = LoadInterruptedAsync();
        };
    }

    /// <summary>Jobs whose journal has no end are shown as interrupted, never silently resumed (plan §9.3).</summary>
    private async Task LoadInterruptedAsync()
    {
        try
        {
            var dir = _vm.Services.Paths.JournalDirectory;
            var interrupted = await Task.Run(() => Core.Jobs.JournalRecovery.Scan(dir));
            if (!_vm.EditSessionsActive) return;
            var messages = new List<string>();
            if (interrupted.Count > 0)
            {
                _vm.Operations.LoadInterrupted(interrupted);
                _vm.Operations.IsOpen = true;
                messages.Add($"{Formatters.Plural(interrupted.Count, "operation was", "operations were")} interrupted when FileCat last closed. Review them in the operations pane.");
            }
            try
            {
                var reviewed = await Task.Run(() => RecoveryReviewStore.Load(dir));
                if (!_vm.EditSessionsActive) return;
                _vm.Operations.LoadReviewedRecovery(reviewed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { messages.Add("Reviewed recovery reports could not be loaded: " + Formatters.SafeName(ex.Message)); }
            int hex = await Task.Run(() => Platform.Windows.HexSaveJournal.Pending(_vm.Services.Paths.HexRecoveryDirectory).Count);
            if (!_vm.EditSessionsActive) return;
            if (hex > 0) messages.Add($"{Formatters.Plural(hex, "hex save was", "hex saves were")} interrupted. Finish or roll back with Tools → Recover interrupted hex save.");
            if (await _vm.RestoreEditSessionsAsync() is { } sessions) messages.Add(sessions);
            if (!_vm.EditSessionsActive) return;
            if (messages.Count > 0) _vm.Notify(string.Join(" ", messages), true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { if (_vm.EditSessionsActive) _vm.Notify($"Cannot review interrupted work: {ex.Message}", true); }
    }

    private void AttachDragDrop(PanelView view, PanelViewModel panel)
    {
        DragDrop.SetAllowDrop(view, true);
        DropTargetAdmission.Offer? offered = null;
        view.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            (offered, _) = DropTargetAdmission.Check(panel, e.DataTransfer.Contains(DataFormat.File),
                (e.KeyModifiers & KeyModifiers.Shift) != 0);
            e.DragEffects = offered?.Effect ?? DragDropEffects.None;
            e.Handled = true;
        });
        view.AddHandler(DragDrop.DragLeaveEvent, (_, _) => offered = null);
        view.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var previous = offered;
            offered = null;
            bool move = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            e.Handled = true;
            e.DragEffects = DragDropEffects.None;
            var (current, refusal) = DropTargetAdmission.Check(panel, e.DataTransfer.Contains(DataFormat.File), move);
            if (current is null)
            {
                _vm.Notify(refusal!, true);
                return;
            }
            if (previous is not null && !DropTargetAdmission.SameTarget(previous, current))
            {
                _vm.Notify("The drop target changed during the drag. Drop again onto the folder you want to use.", true);
                return;
            }
            IReadOnlyList<string>? paths;
            string? pathRefusal;
            try
            {
                var files = e.DataTransfer.TryGetFiles()?.ToArray();
                (paths, pathRefusal) = DropTargetAdmission.CompletePaths(
                    e.DataTransfer.GetItems(DataFormat.File).Count(), files?.Select(f => f.TryGetLocalPath()).ToArray() ?? []);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _vm.Notify("The dropped file payload could not be read. No items were accepted; copy them to a local folder and use F5.", true);
                return;
            }
            if (paths is null)
            {
                _vm.Notify(pathRefusal!, true);
                return;
            }
            e.DragEffects = current.Effect;
            await _vm.DropFilesAsync(panel, current, paths);
        });
        view.List.DragRequested += async (_, press) =>
        {
            var tab = panel.ActiveTab;
            if (tab is null) return;
            var sel = tab.Listing.GetSelection();
            DragExport.Prepared? prepared;
            string? refusal;
            try
            {
                async Task<IStorageItem?> Convert(string path, bool folder) => folder
                    ? await StorageProvider.TryGetFolderFromPathAsync(path)
                    : await StorageProvider.TryGetFileFromPathAsync(path);
                (prepared, refusal) = await DragExport.PrepareAsync(sel, _vm.Services.Providers,
                    _vm.Services.Platform.FileOperations, _vm.Services.Paths.TempDirectory, Convert, CancellationToken.None);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
            {
                _vm.Notify("The complete selection could not be prepared for dragging: " + Formatters.SafeName(ex.Message), true);
                return;
            }
            finally
            {
                ItemSources.Release(sel);
            }
            if (prepared is null)
            {
                if (refusal is not null) _vm.Notify(refusal, true);
                return;
            }
            using (prepared)
            {
                if (!ReferenceEquals(panel.ActiveTab, tab) || tab.Listing.IsDisposed)
                {
                    _vm.Notify("The source tab changed before dragging began. Select the complete scope again.");
                    return;
                }
                try
                {
                    var transfer = new DataTransfer();
                    foreach (var item in prepared.Items) transfer.Add(DataTransferItem.CreateFile(item));
                    var effect = await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
                    if (effect != DragDropEffects.None) prepared.Handoff();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
                {
                    _vm.Notify("Dragging could not start: " + Formatters.SafeName(ex.Message), true);
                }
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ShowHiddenItems) or nameof(MainViewModel.ShowToolbar) or nameof(MainViewModel.ShowDriveButtons))
            ScheduleToolbarRefresh();
        if (e.PropertyName == nameof(MainViewModel.CurrentNotification))
        {
            UpdateNotificationLiveSettings();
            _notificationTimer.Stop();
            if (_vm.CurrentNotification is { IsError: false })
            {
                _notificationTimer.Interval = TimeSpan.FromSeconds(6);
                _notificationTimer.Start();
            }
        }
    }

    /// <summary>
    /// Each notification is announced exactly once, after it is laid out. Its text changes before the hidden
    /// notification bar has templated it into the window, where no screen reader can hear a live change, and a
    /// repeated notice keeps the same text, so both texts stay silent until the shown one is raised explicitly.
    /// </summary>
    private void UpdateNotificationLiveSettings()
    {
        AutomationProperties.SetLiveSetting(NotificationInfoText, AutomationLiveSetting.Off);
        AutomationProperties.SetLiveSetting(NotificationErrorText, AutomationLiveSetting.Off);
        if (_vm.CurrentNotification is { } notice)
            Dispatcher.UIThread.Post(() => AnnounceNotification(notice), DispatcherPriority.Background);
    }

    private void AnnounceNotification(UserNotification notice)
    {
        if (!ReferenceEquals(_vm.CurrentNotification, notice)) return;
        var text = notice.IsError ? NotificationErrorText : NotificationInfoText;
        if (TopLevel.GetTopLevel(text) is null)
        {
            // Still not templated into the window: announce once it is.
            void Attached(object? sender, VisualTreeAttachmentEventArgs e)
            {
                text.AttachedToVisualTree -= Attached;
                AnnounceNotification(notice);
            }
            text.AttachedToVisualTree += Attached;
            return;
        }
        AutomationProperties.SetLiveSetting(text, notice.IsError ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        var peer = ControlAutomationPeer.CreatePeerForElement(text);
        if (peer.GetName() is { Length: > 0 } name)
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, string.Empty, name);
    }

    private void OnCommandAvailabilityChanged(object? sender, EventArgs e) => ScheduleToolbarRefresh();

    private void ScheduleToolbarRefresh()
    {
        if (_toolbarRefreshPending) return;
        _toolbarRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _toolbarRefreshPending = false;
            if (!IsVisible) return;
            foreach (var wrapper in Toolbar.Children.OfType<Border>())
                if (wrapper.Child is Button button && button.Tag is string id)
                    UpdateToolbarButton(button, wrapper, id);
        });
    }

    internal bool NotificationTimerEnabled => _notificationTimer.IsEnabled;
    internal TimeSpan NotificationTimerInterval => _notificationTimer.Interval;

    internal void TryExpireNotification(DateTimeOffset? now = null)
    {
        _notificationTimer.Stop();
        if (_vm.CurrentNotification is not { IsError: false } current) return;
        if (NotificationBorder.IsPointerOver || NotificationBorder.IsKeyboardFocusWithin || _dialogs.IsOpen)
        {
            _notificationTimer.Interval = TimeSpan.FromSeconds(6);
            _notificationTimer.Start();
            return;
        }
        var remaining = current.LastAt.AddSeconds(6) - (now ?? DateTimeOffset.UtcNow);
        if (remaining > TimeSpan.Zero)
        {
            _notificationTimer.Interval = remaining;
            _notificationTimer.Start();
            return;
        }
        _vm.ClearNotification(current.Id);
    }

    private void OnNotificationCloseClicked(object? sender, RoutedEventArgs e) => _vm.ClearNotification();

    private async void OnNotificationCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.Notification is not { } message || Clipboard is not { } clipboard) return;
        try { await clipboard.SetTextAsync(message); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _vm.Notify("The notification could not be copied: " + ex.Message, true);
        }
    }

    private void OnNotificationHistoryClicked(object? sender, RoutedEventArgs e) =>
        _vm.Execute(CommandIds.RecentNotifications);

    private void OnNotificationRestoreWorkspaceClicked(object? sender, RoutedEventArgs e) =>
        _vm.Execute(CommandIds.RestorePreviousWorkspace);

    private Core.Platform.ProcessAccount? _account;
    private TabViewModel? _titleTab;

    /// <summary>
    /// The active tab's place and the account it runs as with its rights; Windows puts FileCat first.
    /// It follows the tab as it goes elsewhere: before, only another panel's activation changed it.
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

    private void RecordPointerFocusIntent(object? sender, PointerPressedEventArgs e)
    {
        // A physical panel press outranks a late accessibility focus request for
        // an editor in the previously active panel. A fresh press or key starts
        // a new user intent; the short guard never changes ordinary navigation.
        _pointerFocusOwner = !_dialogs.IsOpen && e.Source is Visual source &&
                             source.FindAncestorOfType<PanelView>(includeSelf: true) is { } owner &&
                             owner.PointerActivates(e.Source) ? owner : null;
        _pointerFocusAt = Stopwatch.GetTimestamp();
    }

    private void ClearPointerFocusIntent()
    {
        _pointerFocusOwner = null;
    }

    internal bool ShouldRejectStalePanelAccessibilityFocus(Visual editor)
    {
        if (_dialogs.IsOpen || _pointerFocusOwner is not { } owner || !owner.IsEffectivelyVisible ||
            Stopwatch.GetElapsedTime(_pointerFocusAt) > TimeSpan.FromMilliseconds(250) ||
            editor.FindAncestorOfType<PanelView>(includeSelf: true) is not { } requested ||
            ReferenceEquals(requested, owner)) return false;
        return true;
    }

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
        _vm.ReconcileKeyBar(KeyMapper.ToMods(KeyMapper.IsModifierKey(e.Key)
            ? KeyMapper.AfterModifierKey(e.Key, e.KeyModifiers, pressed: false) : e.KeyModifiers));
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        _suppressTextInput = null;
        // An ordinary key reports the held modifiers even if a popup consumed their releases.
        if (!KeyMapper.IsModifierKey(e.Key)) _vm.ReconcileKeyBar(KeyMapper.ToMods(e.KeyModifiers));
        if ((_panelDrag is not null || _tabDrag is not null) && e.Key == Key.Escape)
        {
            // Esc cancels moving a panel, or a tab.
            EndPanelDrag();
            EndTabDrag();
            e.Handled = true;
            return;
        }
        if (!_dialogs.IsOpen && HandlePanelResizeKey(e)) return;
        if (KeyMapper.IsModifierKey(e.Key))
        {
            _vm.ReconcileKeyBar(KeyMapper.ToMods(KeyMapper.AfterModifierKey(e.Key, e.KeyModifiers, pressed: true)));
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
        _suppressTextInput = TypedText(e);
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
            _suppressTextInput = TypedText(e);
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

    /// <summary>The text a key press may also type; none for ⌘ or Ctrl chords, which type nothing (Ctrl+Alt is AltGr).</summary>
    private static string? TypedText(KeyEventArgs e)
    {
        var chord = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta);
        return (chord & KeyModifiers.Meta) != 0 || chord == KeyModifiers.Control || string.IsNullOrEmpty(e.KeySymbol) ? null : e.KeySymbol;
    }

    private bool HandleEscape(TabViewModel? tab)
    {
        if (_vm.Notification is not null)
        {
            _vm.ClearNotification();
            return true;
        }
        if (tab is null) return false;
        if (tab.CancelAnalysis()) return true;
        if (_vm.CancelSizing(tab)) return true;
        if (tab.CancelPendingNavigation()) return true;
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
        if (_suppressTextInput is { } typed)
        {
            _suppressTextInput = null;
            if (e.Text == typed)
            {
                e.Handled = true;
                return;
            }
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

    public void FocusPanelFilter() => ActivePanelView()?.FocusFilterBox();

    public bool OpenAncestorMenu() => ActivePanelView()?.OpenAncestorMenu() == true;

    public void CloseWhenIdle()
    {
        // Re-enter the ordinary close gate: the user may have opened an editor
        // or started new work since choosing to exit after the old job.
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
        // Keep the invoking field out of the native accessibility tree while
        // an in-window modal owns focus; VoiceOver otherwise follows its stale
        // text-field cursor instead of the dialog's focused item.
        bool fits = room >= 120 && !OverlayHost.IsVisible;
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
        ReadabilityPolicy.ApplyDensity(_vm.Services.Settings.RowDensity);
        Classes.Set("comfortable", ReadabilityPolicy.Comfortable(_vm.Services.Settings.RowDensity));
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
    private Controls.RichTip ToolbarTip(string id, CommandAvailability availability)
    {
        var def = _vm.Services.Commands.Get(id);
        var chord = _vm.Services.Keymap.GetChords(id).FirstOrDefault();
        return new Controls.RichTip(TitleOf(id, def?.Title ?? id), chord.Key is null ? null : chord.ToString(), def?.Description,
            availability.Enabled ? null : ["Unavailable here: " + (availability.Reason ?? "This command is not available in the current context.")]);
    }

    /// <summary>The wrapper remains hoverable when its button is disabled; the button exposes the reason to assistive technology.</summary>
    private void UpdateToolbarButton(Button button, Border wrapper, string id)
    {
        var availability = _vm.GetAvailability(id);
        string title = TitleOf(id, _vm.Services.Commands.Get(id)?.Title ?? id);
        var state = new ToolbarPresentationState(availability, title, CheckedState(id) == true);
        if (Equals(wrapper.Tag, state)) return;
        wrapper.Tag = state;
        button.IsEnabled = availability.Enabled;
        button.Classes.Set("checked", state.Checked);
        Avalonia.Automation.AutomationProperties.SetName(button, title);
        var tip = ToolbarTip(id, availability);
        ToolTip.SetTip(button, tip);
        // A disabled child may not receive hover events. Give its enabled parent a separate visual instance.
        ToolTip.SetTip(wrapper, availability.Enabled ? null : ToolbarTip(id, availability));
        Avalonia.Automation.AutomationProperties.SetHelpText(button, tip.Text);
        Avalonia.Automation.AutomationProperties.SetHelpText(wrapper, tip.Text);
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
    /// The focused toolbar uses a stable primary order and a fixed More menu. The classic arrangement remains available.
    /// A button runs its existing command as its shortcut would, and its tooltip names the current shortcut.
    /// </summary>
    private void BuildToolbar()
    {
        Toolbar.Children.Clear();
        bool classic = _vm.Services.Settings.ClassicToolbar;
        ToolbarMore.IsVisible = !classic;
        ToolbarMore.ContextMenu = classic ? null : BuildToolbarMore();
        ScrollViewer.SetHorizontalScrollBarVisibility(ToolbarScroll,
            classic ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        foreach (var id in classic ? MainMenuModel.Toolbar : MainMenuModel.PrimaryToolbar)
        {
            if (id == "-")
            {
                Toolbar.Children.Add(new Border { Classes = { "toolbarSeparator" } });
                continue;
            }
            if (_vm.Services.Commands.Get(id) is null || CommandIcons.Get(id) is not { } icon) continue;
            var image = new Image { Source = icon, Width = 16, Height = 16 };
            Control content = image;
            if (!classic && PrimaryToolbarLabel(id) is { } label)
                content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 4,
                    Children = { image, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } },
                };
            var button = new Button { Classes = { "toolbar" }, Content = content, Tag = id };
            button.Click += (_, _) =>
            {
                FocusActivePanel();
                _vm.RememberCommand(id);
                _vm.Execute(id);
            };
            var wrapper = new Border { Child = button, Background = Avalonia.Media.Brushes.Transparent };
            Toolbar.Children.Add(wrapper);
            UpdateToolbarButton(button, wrapper, id);
        }
    }

    private static string? PrimaryToolbarLabel(string id) => id switch
    {
        CommandIds.Copy => "Copy",
        CommandIds.Move => "Move",
        CommandIds.MakeDirectory => "New folder",
        CommandIds.Delete => "Delete",
        CommandIds.FindFiles => "Find",
        CommandIds.Operations => "Jobs",
        _ => null,
    };

    private void OpenToolbarMore()
    {
        ToolbarMore.ContextMenu?.Open(ToolbarMore);
    }

    private ContextMenu BuildToolbarMore()
    {
        var groups = new List<Control>();
        foreach (var section in MainMenuModel.SecondaryToolbar)
        {
            var owner = new MenuItem { Header = section.Header };
            owner.ItemsSource = BuildItems(owner, section.Commands);
            groups.Add(owner);
        }
        var menu = new ContextMenu { ItemsSource = groups };
        menu.Closed += (_, _) => _vm.ReconcileKeyBar(KeyMods.None);
        return menu;
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

    protected override void OnClosed(EventArgs e)
    {
        _vm.StopEditSessions();
        base.OnClosed(e);
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
        if (WindowState == WindowState.Normal) RememberNormalPlacement();
        var normal = _normalPlacement ?? new WindowPlacement
        {
            Width = Bounds.Width,
            Height = Bounds.Height,
            X = Position.X,
            Y = Position.Y,
        };
        var p = new WindowPlacement
        {
            Width = normal.Width,
            Height = normal.Height,
            X = normal.X,
            Y = normal.Y,
            Maximized = WindowState == WindowState.Maximized,
        };
        _vm.SaveWorkspace(p);
    }

    private void ApplySavedPlacement()
    {
        if (WindowPlacementPolicy.Restore(_savedPlacement, DisplayAreas()) is not { } restored) return;
        _normalPlacement = new WindowPlacement
        {
            X = restored.X, Y = restored.Y, Width = restored.Width, Height = restored.Height,
        };
        MinWidth = Math.Min(MinWidth, restored.Width);
        MinHeight = Math.Min(MinHeight, restored.Height);
        Width = restored.Width;
        Height = restored.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint((int)restored.X, (int)restored.Y);
        if (restored.Maximized) WindowState = WindowState.Maximized;
    }

    private IReadOnlyList<DisplayWorkArea> DisplayAreas()
    {
        var all = Screens.All;
        var primary = Screens.Primary;
        return all.OrderBy(screen => !ReferenceEquals(screen, primary))
            .Select(screen => new DisplayWorkArea(screen.WorkingArea.X, screen.WorkingArea.Y,
                screen.WorkingArea.Width, screen.WorkingArea.Height, screen.Scaling)).ToArray();
    }

    private void QueueNormalPlacement()
    {
        if (!_placementTrackingReady || WindowState != WindowState.Normal) return;
        _normalPlacementTimer.Stop();
        _normalPlacementTimer.Start();
    }

    private void RememberNormalPlacement()
    {
        if (!_placementTrackingReady || WindowState != WindowState.Normal ||
            !double.IsFinite(Bounds.Width) || !double.IsFinite(Bounds.Height) ||
            Bounds.Width < 200 || Bounds.Height < 150) return;
        _normalPlacement = new WindowPlacement
        {
            X = Position.X, Y = Position.Y, Width = Bounds.Width, Height = Bounds.Height,
        };
    }
}
