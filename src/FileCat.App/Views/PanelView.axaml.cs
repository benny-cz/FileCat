using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;

namespace FileCat.App.Views;

public partial class PanelView : UserControl
{
    private static ContextMenu? _openItemMenu;
    private static long _menuRequest;
    private static bool _nativeMenuWarningShown;

    public PanelView()
    {
        InitializeComponent();
        StatusBar.SizeChanged += (_, e) => StatusRightText.MaxWidth = Math.Max(120, e.NewSize.Width * 0.45);
        List.ActivateRequested += (_, _) => Activated?.Invoke();
        List.OpenRequested += (_, _) => OpenRequested?.Invoke();
        List.MiddleClickRequested += (_, row) => MiddleClick?.Invoke(row);
        List.ContextMenuRequested += (_, point) => ShowContextMenu(point: point);
        List.GotFocus += (_, _) => Activated?.Invoke();
        // The number, or the tab strip beside the tabs, moves the whole panel.
        NumberBadge.PointerPressed += (_, e) =>
        {
            MoveRequested?.Invoke(e, NumberBadge);
            e.Handled = true;
        };
        // A tab's button takes its press (it clicks on release), so the strip asks to see handled presses too.
        TabStrip.AddHandler(PointerPressedEvent, OnTabPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        TabHeader.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is null) MoveRequested?.Invoke(e, TabHeader);
        };
        PathBox.GotFocus += (_, _) =>
        {
            Activated?.Invoke();
            Dispatcher.UIThread.Post(PathBox.SelectAll, DispatcherPriority.Input);
        };
        // Typing a path suggests the folders that complete it (not while the box shows where the panel is); its keys
        // come first in OnPathKeyDown.
        _completion = new PathCompletion(PathBox, AddressRow, () => Panel?.Services.Settings.ShowHidden == true, () => Panel?.ActiveTab?.DisplayPath,
            handleKeys: false);
        _completion.Chosen += path => PathSubmitted?.Invoke(path);
        PathBox.LostFocus += (_, _) =>
        {
            RevertPath();
            PathLinks.IsVisible = true;
            PathBox.Classes.Add("linked");
        };
        PathBox.GotFocus += (_, _) =>
        {
            PathLinks.IsVisible = false;
            PathBox.Classes.Remove("linked");
        };
        PathLinks.NavigateRequested += OnPathPartChosen;
        PathLinks.EditRequested += () =>
        {
            Activated?.Invoke();
            FocusPathBox();
        };
        PathLinks.CopyRequested += text => _ = CopyToClipboardAsync(text);
        PathLinks.MenuRequested += ShowPathMenu;
        FilterBox.AddHandler(KeyDownEvent, OnFilterKeyDown, RoutingStrategies.Tunnel);
        FilterBox.GotFocus += (_, _) =>
        {
            Activated?.Invoke();
            Dispatcher.UIThread.Post(FilterBox.SelectAll, DispatcherPriority.Input);
        };
        FilterBox.LostFocus += (_, _) => ShowFilter();
        PathBox.AddHandler(KeyDownEvent, OnPathKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            HookQuickView();
            ShowActiveTab();
            HookTab();
            // The Places tree is built directly, not through bindings. A retired panel must release its
            // buttons and borrowed icons even when hidden; a layout-only detach keeps its live context.
            BuildPlaceButtons();
        };
        // Resizing the panel or adding tabs can make the tabs fit, or not.
        TabScroller.ScrollChanged += (_, _) => UpdateTabOverflow();
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.DataContext is not MainViewModel vm) return;
            _main = vm;
            vm.PlacesChanged += OnPlacesChanged;
            if (vm.Services.Icons.Native is { } native) native.IconsLoaded += OnIconsLoaded;
            ThemeManager.ThemeChanged += OnThemeChanged;
            BuildPlaceButtons(force: true);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_main is not null)
            {
                _main.PlacesChanged -= OnPlacesChanged;
                if (_main.Services.Icons.Native is { } native) native.IconsLoaded -= OnIconsLoaded;
                ThemeManager.ThemeChanged -= OnThemeChanged;
            }
            _main = null;
        };
        // The mouse's back and forward buttons go through this panel's history, wherever in the panel they are pressed.
        AddHandler(PointerPressedEvent, OnHistoryButton, RoutingStrategies.Tunnel, handledEventsToo: true);
        // A press anywhere in a panel makes it the source (and, with two panels, the other one the target).
        AddHandler(PointerPressedEvent, OnAnyPress, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnPlacesChanged() => BuildPlaceButtons();

    /// <summary>
    /// A press anywhere in the panel (its list, tabs, path, place buttons, or status line) makes it the source panel,
    /// and the keyboard follows when it was in another panel. "Set as target" on another panel is the exception: it
    /// changes where F5 and F6 go and the keyboard stays where it is.
    /// </summary>
    private void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        if (Panel is not { } panel) return;
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is { } button &&
            ReferenceEquals(button, RoleButton) && panel.OffersTarget) return;
        if (!panel.IsActive) Activated?.Invoke();
        // After the press is handled: a press that gave the keyboard to something here (the path box, a rename) keeps it.
        Dispatcher.UIThread.Post(() =>
        {
            if (FocusIsInAnotherPanel() is not false) List.Focus();
        }, DispatcherPriority.Input);
    }

    /// <summary>
    /// Whether the keyboard is in another panel of this window: false when it is in this one or elsewhere in the window
    /// (the command line, say), null when nothing has it (a press on something that takes no keyboard clears it).
    /// </summary>
    private bool? FocusIsInAnotherPanel()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focused) return null;
        if (ReferenceEquals(focused, this) || this.IsVisualAncestorOf(focused)) return false;
        return focused.FindAncestorOfType<PanelView>() is not null;
    }

    private void OnHistoryButton(object? sender, PointerPressedEventArgs e)
    {
        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (kind is not (PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton2Pressed)) return;
        e.Handled = true;
        Activated?.Invoke();
        if (Panel?.ActiveTab is not { } tab) return;
        if (kind == PointerUpdateKind.XButton1Pressed)
        {
            if (tab.CanGoBack) tab.GoBack();
        }
        else if (tab.CanGoForward) tab.GoForward();
    }

    private PanelViewModel? _hookedPanel;
    private PanelViewModel? _hookedSource;
    private MainViewModel? _main;

    private string? _placesShown;

    /// <summary>
    /// The place buttons (D-52, D-53): everything the location menu (Alt+F1, Alt+F2) offers, one button each, in its
    /// order and groups: drives with their letters (on Linux and macOS the mount point's name), This PC, phones, working
    /// sets, the Registry, the home and special folders, then bookmarks and saved servers by name. A click opens the
    /// place in this panel, a middle click in a new tab; the right button offers both and a place's other views. They
    /// are made anew only when what they show changed, or their icons did.
    /// </summary>
    private void BuildPlaceButtons(bool force = false)
    {
        if (_main is not { } vm || Panel is null)
        {
            ClearPlaceButtons();
            _placesShown = null;
            return;
        }
        var places = vm.BarPlaces();
        string shown = string.Join("\n", places.Select(p => $"{p.Group}|{p.Title}|{p.Detail}|{p.BarLabel}|{p.Location}|{p.Location?.Session}"));
        if (!force && shown == _placesShown)
        {
            MarkCurrentPlace();
            return;
        }
        _placesShown = shown;
        ClearPlaceButtons();
        PlaceGroup? group = null;
        foreach (var place in places)
        {
            if (group is { } previous && previous != place.Group) DriveButtonsPanel.Children.Add(new Border { Classes = { "placeGap" } });
            group = place.Group;
            DriveButtonsPanel.Children.Add(PlaceButton(place));
        }
        // Last: the places that do not fit the row.
        var more = new Button { Classes = { "drive" }, Content = new TextBlock { Text = "»", FontWeight = Avalonia.Media.FontWeight.Bold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } };
        ToolTip.SetTip(more, "More places: those the row has no room for");
        Avalonia.Automation.AutomationProperties.SetName(more, "More places");
        more.Click += (_, _) => ShowMorePlaces(more);
        DriveButtonsPanel.Children.Add(more);
        MarkCurrentPlace();
    }

    private void ClearPlaceButtons()
    {
        // A detached panel's last composition can still retain its controls. Drop the borrowed images and
        // Place payloads explicitly; the shared icon provider owns the bitmaps, so never dispose them here.
        foreach (var button in DriveButtonsPanel.Children.OfType<Button>())
        {
            if (button.Content is StackPanel content)
                foreach (var image in content.Children.OfType<Image>()) image.Source = null;
            button.Content = null;
            button.Tag = null;
            ToolTip.SetTip(button, null);
            button.Click -= OnPlaceClick;
            button.PointerReleased -= OnPlacePointerReleased;
            button.ContextRequested -= OnPlaceContextRequested;
        }
        DriveButtonsPanel.Children.Clear();
    }

    /// <summary>The » button's menu: the places the row has no room for, in their groups, each with its icon.</summary>
    private void ShowMorePlaces(Button more)
    {
        var items = new List<Control>();
        PlaceGroup? group = null;
        foreach (var place in DriveButtonsPanel.Hidden.Select(c => c.Tag).OfType<Place>())
        {
            if (group is { } previous && previous != place.Group) items.Add(new Separator());
            group = place.Group;
            var item = new MenuItem { Header = place.Title };
            if (place.Icon() is { } image) item.Icon = new Image { Source = image, Width = 16, Height = 16 };
            ToolTip.SetTip(item, place.Detail);
            item.Click += (_, _) => OpenPlace(place, newTab: false);
            items.Add(item);
        }
        if (items.Count > 0) new ContextMenu { ItemsSource = items }.Open(more);
    }

    private Button PlaceButton(Place place)
    {
        var content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 3 };
        if (place.Icon() is { } image) content.Children.Add(new Image { Source = image, Width = 16, Height = 16 });
        if (place.BarLabel is { Length: > 0 } label)
            content.Children.Add(new TextBlock
            {
                Text = label,
                MaxWidth = 120,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
        var button = new Button { Classes = { "drive" }, Content = content, Tag = place };
        var tip = new Controls.RichTip(place.Title, detail: place.Detail, hints:
        [
            place.BarTip,
            place.OpensInPanel ? "Middle-click opens it in a new tab" : null,
            place.Variants.Count > 0 ? "Right-click offers: " + string.Join(", ", place.Variants.Select(v => v.Title)) : null,
        ]);
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, place.Title);
        Avalonia.Automation.AutomationProperties.SetHelpText(button, tip.Text);
        button.Click += OnPlaceClick;
        button.PointerReleased += OnPlacePointerReleased;
        if (place.OpensInPanel) button.ContextRequested += OnPlaceContextRequested;
        return button;
    }

    private void OnPlaceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Place place }) OpenPlace(place, newTab: false);
    }

    private void OnPlacePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Middle || sender is not Button { Tag: Place { OpensInPanel: true } place }) return;
        OpenPlace(place, newTab: true);
        e.Handled = true;
    }

    private void OnPlaceContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Button { Tag: Place place } button) return;
        e.Handled = true;
        PlaceMenu(place).Open(button);
    }

    /// <summary>A place button's menu: here or in a new tab, and the place's other views.</summary>
    private ContextMenu PlaceMenu(Place place)
    {
        var items = new List<Control>();
        void Add(string header, Action act)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => act();
            items.Add(item);
        }
        Add("Open here", () => OpenPlace(place, newTab: false));
        Add("Open in a new tab", () => OpenPlace(place, newTab: true));
        if (place.Variants.Count > 0) items.Add(new Separator());
        foreach (var variant in place.Variants) Add(variant.Title, () => OpenPlace(variant, newTab: false));
        return new ContextMenu { ItemsSource = items };
    }

    private bool _iconsPending;

    /// <summary>The platform's icons load in the background: the buttons take them when they arrive (once per batch).</summary>
    private void OnIconsLoaded()
    {
        if (_iconsPending) return;
        _iconsPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _iconsPending = false;
            BuildPlaceButtons(force: true);
        }, DispatcherPriority.Background);
    }

    private void OnThemeChanged() => Dispatcher.UIThread.Post(() => BuildPlaceButtons(force: true), DispatcherPriority.Background);

    private void OpenPlace(Place place, bool newTab)
    {
        Activated?.Invoke();
        if (_main is { } vm && Panel is { } panel) vm.OpenPlace(panel, place, newTab);
    }

    /// <summary>
    /// The button of where this panel is, outlined: its drive, This PC, phones, the Registry, a server, or a folder
    /// or bookmark it shows exactly.
    /// </summary>
    private void MarkCurrentPlace()
    {
        var at = Panel?.ActiveTab?.Location;
        string? root = MainViewModel.DriveRootOf(at)?.TrimEnd('\\', '/');
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var button in DriveButtonsPanel.Children.OfType<Button>())
        {
            bool current = at is not null && button.Tag is Place { Location: { } place } p && (p.Drive is not null
                ? root is not null && string.Equals(place.Path.TrimEnd('\\', '/'), root, comparison) && (root.Length > 0 || place.Path == "/")
                : place.Scheme switch
                {
                    Core.Resources.Schemes.FileSystem => at.IsFileSystem && string.Equals(Core.FileSystem.PathUtil.NormalizeForCompare(at.Path),
                        Core.FileSystem.PathUtil.NormalizeForCompare(place.Path), Core.FileSystem.PathUtil.SafetyComparison),
                    Core.Resources.Schemes.Computer or Core.Resources.Schemes.Mtp or Core.Resources.Schemes.Registry or Core.Resources.Schemes.Network => at.Scheme == place.Scheme,
                    Core.Resources.Schemes.Sftp or Core.Resources.Schemes.Ftp => at.Scheme == place.Scheme && at.Session == place.Session,
                    _ => at.Equals(place),
                });
            button.Classes.Set("current", current);
        }
    }

    /// <summary>Keeps the quick-view pane attached to the source panel's current tab.</summary>
    private void HookQuickView()
    {
        if (_hookedPanel is not null)
        {
            _hookedPanel.PropertyChanged -= OnPanelPropertyChanged;
            _hookedPanel.Tabs.CollectionChanged -= OnTabsChanged;
        }
        _hookedPanel = Panel;
        if (_hookedPanel is not null)
        {
            _hookedPanel.PropertyChanged += OnPanelPropertyChanged;
            // A tab moved along the strip (dragged, or Ctrl+Shift+PageUp) stays in sight when the tabs do not all fit.
            _hookedPanel.Tabs.CollectionChanged += OnTabsChanged;
        }
        AttachQuickView();
    }

    private void OnTabsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move) ShowActiveTab();
    }

    private void OnPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelViewModel.QuickViewSource)) AttachQuickView();
        else if (e.PropertyName == nameof(PanelViewModel.ActiveTab))
        {
            ShowActiveTab();
            HookTab();
        }
    }

    private TabViewModel? _hookedTab;

    /// <summary>The path line follows the active tab's location.</summary>
    private void HookTab()
    {
        if (_hookedTab is not null) _hookedTab.PropertyChanged -= OnTabPropertyChanged;
        _hookedTab = Panel?.ActiveTab;
        if (_hookedTab is not null) _hookedTab.PropertyChanged += OnTabPropertyChanged;
        UpdatePathLinks();
        ShowFilter();
        MarkCurrentPlace();
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TabViewModel.DisplayPath) or nameof(TabViewModel.Location))
        {
            UpdatePathLinks();
            MarkCurrentPlace();
        }
        else if (e.PropertyName == nameof(TabViewModel.FilterText)) ShowFilter();
    }

    /// <summary>What shows everything: "*.*" on Windows, as Salamander and Total Commander write it; "*" elsewhere.</summary>
    public static string AllItemsMask => OperatingSystem.IsWindows() ? "*.*" : "*";

    /// <summary>The filter box shows the tab's filter (or the mask for everything), and stands out while it filters.</summary>
    private void ShowFilter()
    {
        if (FilterBox.IsFocused) return;
        string? filter = Panel?.ActiveTab?.FilterText;
        FilterBox.Text = filter ?? AllItemsMask;
        FilterBox.Classes.Set("active", filter is not null);
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Panel?.ActiveTab?.SetFilter(FilterBox.Text);
            List.Focus();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            List.Focus();
        }
    }

    /// <summary>
    /// The path with each folder up to it a link: the folders come from the location's parents (so archives, servers,
    /// and the Registry work as folders do), as long as each one's path begins the path shown.
    /// </summary>
    private void UpdatePathLinks()
    {
        var tab = Panel?.ActiveTab;
        string text = tab?.DisplayPath ?? "";
        var segments = new List<PathLine.Segment>();
        if (tab?.Location is { } location && Panel?.Services.Providers is { } providers)
        {
            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            int guard = 0;
            for (var p = location; p is not null && p.Scheme != Core.Resources.Schemes.Computer && guard++ < 256; p = providers.For(p).GetParent(p))
            {
                string shown = providers.For(p).GetDisplayPath(p);
                // A root keeps its separator ("C:\", "/"); a folder's part ends at its name.
                string part = shown.Length > 1 && !shown.EndsWith(":\\", StringComparison.Ordinal) ? shown.TrimEnd('\\', '/') : shown;
                if (part.Length == 0 || !text.StartsWith(part, comparison) || (segments.Count > 0 && part.Length >= segments[0].End)) break;
                segments.Insert(0, new PathLine.Segment(part.Length, p));
            }
            // The location itself always ends at the text's end.
            if (segments.Count > 0 && segments[^1].Target == location && segments[^1].End != text.Length)
                segments[^1] = segments[^1] with { End = text.Length };
        }
        PathLinks.SetPath(text, segments);
    }

    /// <summary>A folder up the path was clicked: the panel goes there, with the folder it came from under the cursor.</summary>
    private void OnPathPartChosen(Core.Resources.Location target, bool newTab)
    {
        Activated?.Invoke();
        if (Panel is not { } panel || panel.ActiveTab is not { } tab) return;
        if (newTab)
        {
            panel.OpenTab(target);
            return;
        }
        var segments = PathLinks.Segments;
        int index = segments.ToList().FindIndex(s => s.Target == target);
        string? focus = index >= 0 && index + 1 < segments.Count ? panel.Services.Providers.For(segments[index + 1].Target).GetNameInParent(segments[index + 1].Target) : null;
        tab.Navigate(target, focus);
        List.Focus();
    }

    private async Task CopyToClipboardAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
        if (TopLevel.GetTopLevel(this)?.DataContext is MainViewModel vm) vm.Notify("Copied " + text);
    }

    /// <summary>The path's context menu: copy the whole path, the part clicked, or the name; go there; edit the path.</summary>
    private void ShowPathMenu(Avalonia.Point point, int segment)
    {
        Activated?.Invoke();
        var segments = PathLinks.Segments;
        if (segments.Count == 0) return;
        int last = segments.Count - 1;
        var items = new List<Control>();
        void Add(string header, string icon, Action run)
        {
            var item = new MenuItem { Header = header, Icon = MenuIconFactory.Create(icon) };
            item.Click += (_, _) => run();
            items.Add(item);
        }
        Add("Copy full path", Core.Commands.CommandIds.CopyPaths, () => _ = CopyToClipboardAsync(PathLinks.Text));
        if (segment >= 0 && segment < last)
            Add($"Copy \u201C{PathLinks.PathUpTo(segment)}\u201D", Core.Commands.CommandIds.CopyPaths, () => _ = CopyToClipboardAsync(PathLinks.PathUpTo(segment)));
        int named = segment >= 0 ? segment : last;
        if (PathLinks.NameOf(named) is { Length: > 0 } name)
            Add($"Copy the name \u201C{name}\u201D", Core.Commands.CommandIds.CopyNames, () => _ = CopyToClipboardAsync(name));
        if (segment >= 0 && segment < last)
        {
            items.Add(new Separator());
            var target = segments[segment].Target;
            Add($"Go to \u201C{PathLinks.NameOf(segment)}\u201D", Core.Commands.CommandIds.GoTo, () => OnPathPartChosen(target, false));
            Add($"Open \u201C{PathLinks.NameOf(segment)}\u201D in a new tab", Core.Commands.CommandIds.NewTab, () => OnPathPartChosen(target, true));
        }
        items.Add(new Separator());
        Add("Edit the path", Core.Commands.CommandIds.Edit, FocusPathBox);
        var menu = new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer };
        menu.Open(PathLinks);
    }

    /// <summary>The path's parts as links (tests).</summary>
    internal PathLine PathLinksControl => PathLinks;

    private void OnSourcePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelViewModel.ActiveTab)) QuickView.Attach(_hookedSource?.ActiveTab);
    }

    /// <summary>The active tab stays in sight when the tabs do not all fit (a new tab opens at the end).</summary>
    private void ShowActiveTab()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Panel?.ActiveTab is { } tab && TabStrip.ContainerFromItem(tab) is { } container) container.BringIntoView();
            UpdateTabOverflow();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateTabOverflow() => TabOverflow.IsVisible = TabScroller.Extent.Width > TabScroller.Viewport.Width + 1;

    private void OnTabOverflowClick(object? sender, RoutedEventArgs e)
    {
        Activated?.Invoke();
        if (TopLevel.GetTopLevel(this)?.DataContext is MainViewModel vm) vm.Execute(Core.Commands.CommandIds.TabList);
    }

    /// <summary>Whether the tab strip offers its "⋯" button, and whether the active tab is in sight (tests).</summary>
    internal (bool Overflowing, bool ActiveTabVisible) TabStripState()
    {
        bool visible = Panel?.ActiveTab is { } tab && TabStrip.ContainerFromItem(tab) is { } c &&
                       c.TranslatePoint(new Avalonia.Point(0, 0), TabScroller) is { } p && p.X >= -0.5 && p.X + c.Bounds.Width <= TabScroller.Viewport.Width + 0.5;
        return (TabOverflow.IsVisible, visible);
    }

    private void AttachQuickView()
    {
        if (_hookedSource is not null) _hookedSource.PropertyChanged -= OnSourcePropertyChanged;
        _hookedSource = Panel?.QuickViewSource;
        if (_hookedSource is not null) _hookedSource.PropertyChanged += OnSourcePropertyChanged;
        QuickView.Attach(_hookedSource?.ActiveTab);
    }

    public event Action? Activated;
    public event Action? OpenRequested;

    /// <summary>A press on the panel's number or tab strip that may start moving the panel (and the element pressed).</summary>
    public event Action<PointerPressedEventArgs, Control>? MoveRequested;

    /// <summary>A left press on a tab that may start dragging it (the tab, and its button).</summary>
    public event Action<PointerPressedEventArgs, TabViewModel, Control>? TabDragRequested;
    public event Action<string>? PathSubmitted;
    public event Action? LocationMenuRequested;
    public event Action<int>? MiddleClick;

    private PanelViewModel? Panel => DataContext as PanelViewModel;

    public void FocusPathBox()
    {
        PathBox.Focus();
        PathBox.SelectAll();
    }

    private void RevertPath() => PathBox.Text = Panel?.ActiveTab?.DisplayPath;

    private readonly PathCompletion _completion;

    /// <summary>The folders suggested for what is typed; empty while none are shown (tests).</summary>
    internal IReadOnlyList<string> PathSuggestionsShown => _completion.Shown;

    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (_completion.HandleKey(e))
        {
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var text = PathBox.Text ?? string.Empty;
            PathSubmitted?.Invoke(text);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            RevertPath();
            List.Focus();
        }
        else if (e.Key == Key.Down && (e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            e.Handled = true;
            RevertPath();
            List.Focus();
        }
    }

    private void OnTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TabViewModel tab } && Panel is { } p)
        {
            p.ActiveTab = tab;
            Activated?.Invoke();
            List.Focus();
        }
    }

    /// <summary>A left press on a tab may start dragging it along the strip, or to another panel's (the window decides).</summary>
    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is { Tag: TabViewModel tab } button
            && e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            TabDragRequested?.Invoke(e, tab, button);
    }

    /// <summary>
    /// Where a tab dropped at <paramref name="point"/> (in <paramref name="relativeTo"/>'s coordinates) goes among this
    /// panel's tabs (as the index it ends up at), and the gap between tabs to mark there; null off the tab strip.
    /// </summary>
    internal (int Index, Rect Gap)? TabDropAt(Point point, Visual relativeTo, TabViewModel dragged)
    {
        if (Panel is not { } panel || !TabHeader.IsEffectivelyVisible || TabScroller.TranslatePoint(default, relativeTo) is not { } origin) return null;
        // Forgiving above and below the strip: a drag rarely stays within its few pixels of height.
        var strip = new Rect(origin, TabScroller.Bounds.Size).Inflate(new Thickness(0, 10));
        if (!strip.Contains(point)) return null;
        int index = 0;
        double gap = origin.X;
        foreach (var tab in panel.Tabs)
        {
            if (ReferenceEquals(tab, dragged)) continue;
            if (TabStrip.ContainerFromItem(tab) is not Control c || c.TranslatePoint(default, relativeTo) is not { } at) continue;
            if (point.X < at.X + c.Bounds.Width / 2)
            {
                gap = at.X;
                break;
            }
            index++;
            gap = at.X + c.Bounds.Width;
        }
        gap = Math.Clamp(gap, origin.X + 1, origin.X + TabScroller.Bounds.Width - 1);
        return (index, new Rect(gap - 1.5, origin.Y + 2, 3, Math.Max(8, TabScroller.Bounds.Height - 4)));
    }

    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Button { Tag: TabViewModel tab } button || Panel is not { } p) return;
        if (e.InitialPressMouseButton == MouseButton.Middle)
        {
            p.CloseTab(tab);
            e.Handled = true;
        }
        else if (e.InitialPressMouseButton == MouseButton.Right)
        {
            BuildTabMenu(p, tab).Open(button);
            e.Handled = true;
        }
    }

    /// <summary>Tab operations following Total Commander (plan §4.1).</summary>
    private ContextMenu BuildTabMenu(PanelViewModel panel, TabViewModel tab)
    {
        var ws = panel.Workspace;
        var items = new List<Control>();
        void Add(string header, string icon, Action action, bool enabled = true, string? gesture = null)
        {
            var mi = new MenuItem { Header = header, Icon = MenuIconFactory.Create(icon), IsEnabled = enabled };
            if (gesture is not null) mi.InputGesture = KeyGesture.Parse(gesture);
            mi.Click += (_, _) =>
            {
                action();
                List.Focus();
            };
            items.Add(mi);
        }
        Add("Close tab", "tab.close", () => panel.CloseTab(tab), panel.Tabs.Count > 1);
        Add("Close other tabs", "tab.closeOthers", () =>
        {
            foreach (var t in panel.Tabs.Where(t => !ReferenceEquals(t, tab) && !t.IsLocked).ToList()) panel.CloseTab(t);
        }, panel.Tabs.Count > 1);
        Add("Duplicate tab", "tab.duplicate", () =>
        {
            if (tab.Location is null) return;
            var dup = panel.OpenTab(tab.Location);
            var s = tab.ToState();
            s.Locked = false;
            dup.ApplyState(s);
        });
        items.Add(new Separator());
        Add(tab.IsLocked && !tab.ReturnToRoot ? "Unlock tab" : "Lock tab (navigation opens new tabs)", "tab.lock", () =>
        {
            bool lockIt = !(tab.IsLocked && !tab.ReturnToRoot);
            tab.IsLocked = lockIt;
            tab.ReturnToRoot = false;
            tab.LockedRoot = lockIt ? tab.Location : null;
        });
        Add(tab.IsLocked && tab.ReturnToRoot ? "Unlock tab" : "Lock tab at root (returns here when revisited)", "tab.lock", () =>
        {
            bool lockIt = !(tab.IsLocked && tab.ReturnToRoot);
            tab.IsLocked = lockIt;
            tab.ReturnToRoot = lockIt;
            tab.LockedRoot = lockIt ? tab.Location : null;
        });
        items.Add(new Separator());
        var target = ws.GetTarget(panel);
        // The last tab moved away leaves a fresh one at the same place behind (a panel keeps a tab).
        Add(target is null ? "Move tab to target panel (no target)" : $"Move tab to panel {target.Number}", "tab.moveTarget", () =>
        {
            if (target is not null) ws.MoveTabToPanel(tab, target);
        }, target is not null);
        Add(target is null ? "Copy tab to target panel (no target)" : $"Copy tab to panel {target.Number}", "tab.copyTarget", () =>
        {
            if (target is not null && tab.Location is { } l) target.OpenTab(l);
        }, target is not null);
        int index = panel.Tabs.IndexOf(tab);
        Add("Move tab left", "tab.left", () => panel.MoveTab(tab, index - 1), index > 0, "Ctrl+Shift+PageUp");
        Add("Move tab right", "tab.right", () => panel.MoveTab(tab, index + 1), index < panel.Tabs.Count - 1, "Ctrl+Shift+PageDown");
        return new ContextMenu { ItemsSource = items };
    }

    private void OnNewTabClick(object? sender, RoutedEventArgs e)
    {
        if (Panel is { ActiveTab.Location: { } loc } p)
        {
            p.OpenTab(loc);
            Activated?.Invoke();
            List.Focus();
        }
    }

    /// <summary>
    /// The role chip: "Set as target" makes this panel the active panel's target without leaving the active panel; on
    /// the active panel it lists the other panels to choose the target from.
    /// </summary>
    private void OnRoleClick(object? sender, RoutedEventArgs e)
    {
        if (Panel is not { } panel || TopLevel.GetTopLevel(this)?.DataContext is not MainViewModel vm) return;
        var ws = panel.Workspace;
        if (panel.OffersTarget && ws.ActivePanel is { } active)
        {
            vm.SetPanelTarget(active, panel);
            return;
        }
        if (!panel.IsActive || ws.Panels.Count <= 2) return;
        var current = ws.GetTarget(panel);
        var items = new List<Control>();
        foreach (var other in ws.Panels.Where(p => !ReferenceEquals(p, panel)))
        {
            var choice = other;
            var item = new MenuItem
            {
                Header = $"Panel {other.Number}: {other.ActiveTab?.DisplayPath}",
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = ReferenceEquals(other, current),
            };
            item.Click += (_, _) => vm.SetPanelTarget(panel, choice);
            items.Add(item);
        }
        new ContextMenu { ItemsSource = items }.Open(RoleButton);
    }

    private void OnLocationMenuClick(object? sender, RoutedEventArgs e)
    {
        Activated?.Invoke();
        LocationMenuRequested?.Invoke();
    }

    private void OnDismissBanner(object? sender, RoutedEventArgs e)
    {
        if (Panel?.ActiveTab is { } t) t.Banner = null;
        List.Focus();
    }

    /// <summary>Count in the status line: this panel's marked folders are counted, and the keyboard stays in its list.</summary>
    private void OnCountFolderSizes(object? sender, RoutedEventArgs e)
    {
        if (Panel?.ActiveTab is { } tab && TopLevel.GetTopLevel(this)?.DataContext is MainViewModel vm) vm.CountFolderSizes(tab);
        List.Focus();
    }

    /// <summary>The item context menu: at the pointer for a right click, at the focused row from the keyboard.</summary>
    public void ShowContextMenu(bool atFocus = false, Avalonia.Point? point = null)
    {
        long request = ++_menuRequest;
        _openItemMenu?.Close();
        _openItemMenu = null;
        _ = ShowContextMenuAsync(atFocus, point, request);
    }

    private async Task ShowContextMenuAsync(bool atFocus, Avalonia.Point? point, long request)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is not MainViewModel vm) return;
        if (OperatingSystem.IsWindows() && Panel?.ActiveTab is { Location.IsFileSystem: true } tab &&
            tab.Listing.MarkedCount <= WindowsContextMenu.MaxItems)
        {
            var paths = tab.Listing.GetSelection().Select(item => item.FileSystemPath).ToArray();
            if (WindowsContextMenu.CanShow(paths))
            {
                var anchor = point ?? (List.FocusedRowBounds() is { } bounds
                    ? new Avalonia.Point(bounds.Left + 12, bounds.Bottom) : new Avalonia.Point(0, 0));
                var screen = List.PointToScreen(anchor);
                var result = await WindowsContextMenu.ShowAsync(paths!, screen.X, screen.Y);
                if (request != _menuRequest) return;
                if (result == WindowsContextMenu.Result.Handled) return;
                if (result == WindowsContextMenu.Result.RecoverImage)
                {
                    vm.RecoverImage(paths[0]!);
                    return;
                }
                if (result == WindowsContextMenu.Result.ActionFailed)
                {
                    vm.Notify("Windows could not complete that action.", true);
                    return;
                }
                if (result == WindowsContextMenu.Result.FileCatActions) atFocus = point is null;
                if (result == WindowsContextMenu.Result.Failed && !_nativeMenuWarningShown)
                {
                    _nativeMenuWarningShown = true;
                    vm.Notify("Windows menu unavailable here; showing FileCat actions.", true);
                }
            }
        }
        if (request != _menuRequest) return;
        var menu = ContextMenuFactory.Build(vm);
        if (atFocus && List.FocusedRowBounds() is { } row)
        {
            menu.Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft;
            menu.PlacementRect = row;
        }
        _openItemMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_openItemMenu, menu)) _openItemMenu = null; };
        menu.Open(List);
    }
}
