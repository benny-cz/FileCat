using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FileCat.App.Services;
using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.ViewModels;

public sealed partial class KeyBarItem : ObservableObject
{
    public KeyBarItem(int number) => Number = number;

    public int Number { get; }
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private string? _commandId;
    [ObservableProperty] private bool _isEnabled = true;
}

/// <summary>Root view model: workspace, key bar, command line, and command dispatch (plan §4.4).</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly Dictionary<string, CancellationTokenSource> _sizing = new(StringComparer.Ordinal);

    public MainViewModel(AppServices services)
    {
        Services = services;
        Workspace = new WorkspaceViewModel(services);
        AttachRemoteInteraction();
        _drives = services.Drives.Listen(change => services.Ui.Post(() => OnDrivesChanged(change)));
        for (int i = 1; i <= 12; i++) KeyBar.Add(new KeyBarItem(i));
        UpdateKeyBar(KeyMods.None);
        Workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(WorkspaceViewModel.ActivePanel) or nameof(WorkspaceViewModel.ActiveTab))
            {
                OnPropertyChanged(nameof(CommandLinePrompt));
                FollowActiveListing();
                UpdateKeyBar(_mods);
            }
        };
        FollowActiveListing();
    }

    private Core.Listing.ListingModel? _keyBarListing;
    private bool _keyBarPending;

    /// <summary>
    /// The key bar follows the active listing: F3, F5, F8 and the rest apply only to a focused or marked item, so their
    /// state changes as focus moves off "..", marks come and go, and a listing loads.
    /// </summary>
    private void FollowActiveListing()
    {
        var listing = Workspace.ActiveTab?.Listing;
        if (ReferenceEquals(listing, _keyBarListing)) return;
        if (_keyBarListing is not null) _keyBarListing.Changed -= OnActiveListingChanged;
        _keyBarListing = listing;
        if (listing is not null) listing.Changed += OnActiveListingChanged;
    }

    private void OnActiveListingChanged(object? sender, Core.Listing.ListingChange change)
    {
        const Core.Listing.ListingChange relevant = Core.Listing.ListingChange.Focus | Core.Listing.ListingChange.Marks |
                                                    Core.Listing.ListingChange.Reset | Core.Listing.ListingChange.State;
        if ((change & relevant) == 0 || _keyBarPending) return;
        // A burst (holding an arrow key) updates the bar once.
        _keyBarPending = true;
        Services.Ui.Post(() =>
        {
            _keyBarPending = false;
            // The tab may have closed meanwhile (with its listing).
            if (_keyBarListing is { IsDisposed: false }) UpdateKeyBar(_mods);
        });
    }

    public AppServices Services { get; }
    public WorkspaceViewModel Workspace { get; }
    public ObservableCollection<KeyBarItem> KeyBar { get; } = [];
    /// <summary>The main window's dialogs, or a Find window's while a command runs there (<see cref="ExecuteInAsync"/>).</summary>
    public IDialogService Dialogs
    {
        get => Scope?.Dialogs ?? _dialogs;
        set => _dialogs = value;
    }

    /// <summary>The main window, or a Find window while a command runs there.</summary>
    public IViewActions View
    {
        get => Scope?.View ?? _view;
        set => _view = value;
    }

    private IDialogService _dialogs = null!;
    private IViewActions _view = null!;

    [ObservableProperty] private string _commandLineText = string.Empty;
    [ObservableProperty] private string? _notification;
    [ObservableProperty] private bool _notificationIsError;
    [ObservableProperty] private bool _showKeyBar = true;
    [ObservableProperty] private bool _showCommandLine = true;
    [ObservableProperty] private bool _showToolbar = true;
    [ObservableProperty] private bool _showDriveButtons = true;
    /// <summary>Hidden and system items are shown (the setting, observable for the toolbar's switch).</summary>
    [ObservableProperty] private bool _showHiddenItems = true;

    private KeyMods _mods;

    public string CommandLinePrompt => (Workspace.ActiveTab?.DisplayPath ?? string.Empty) + ">";

    public TabViewModel? ActiveTab => Scope?.Tab ?? Workspace.ActiveTab;

    public void Initialize(WorkspaceState? state)
    {
        ShowKeyBar = Services.Settings.ShowFunctionKeyBar;
        ListFontSize = Services.Settings.FontSize;
        ShowCommandLine = Services.Settings.ShowCommandLine;
        ShowToolbar = Services.Settings.ShowToolbar;
        ShowDriveButtons = Services.Settings.ShowDriveButtons;
        ShowHiddenItems = Services.Settings.ShowHidden;
        QuietConnect = state is not null;
        _ = RefreshDriveButtonsAsync();
        Workspace.LoadState(state);
        if (Services.SettingsStatus == StateLoadStatus.NewerSchemaReadOnly)
            Notify("Settings were written by a newer FileCat and are opened read-only; changes will not be saved.", true);
        else if (Services.SettingsStatus is StateLoadStatus.RecoveredFromBackup or StateLoadStatus.CorruptUsingDefaults)
            Notify("Settings were damaged; FileCat restored a usable copy and kept the damaged file for inspection.", true);
    }

    public void UpdateKeyBar(KeyMods mods)
    {
        _mods = mods;
        var bar = Services.Keymap.GetFunctionKeyBar(mods, CommandContext.Panel);
        for (int i = 0; i < 12; i++)
        {
            KeyBar[i].Label = bar[i].Label;
            KeyBar[i].CommandId = bar[i].CommandId;
            KeyBar[i].IsEnabled = bar[i].CommandId is not null && GetAvailability(bar[i].CommandId!).Enabled;
        }
    }

    public void Notify(string message, bool isError = false)
    {
        if (Scope?.View is { } scoped)
        {
            // A command in a Find window reports there.
            scoped.ShowNotification(message, isError);
            if (isError) AppLog.Warn("User notification: " + message);
            return;
        }
        Notification = message;
        NotificationIsError = isError;
        if (isError) AppLog.Warn("User notification: " + message);
    }

    public void ClearNotification() => Notification = null;

    internal void CancelSizing(string key)
    {
        if (_sizing.Remove(key, out var cts)) cts.Cancel();
    }

    /// <summary>Esc stops folder sizing started in this tab; true when something was running.</summary>
    internal bool CancelSizing(TabViewModel tab)
    {
        var prefix = tab.GetHashCode() + "|";
        var keys = _sizing.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var k in keys) CancelSizing(k);
        if (keys.Count > 0) Notify($"Stopped sizing {Formatters.Plural(keys.Count, "folder", "folders")}; the sizes shown so far are lower bounds.");
        return keys.Count > 0;
    }

    internal void CancelAllSizing()
    {
        foreach (var c in _sizing.Values) c.Cancel();
        _sizing.Clear();
    }

    internal CancellationToken BeginSizing(string key)
    {
        CancelSizing(key);
        var cts = new CancellationTokenSource();
        _sizing[key] = cts;
        return cts.Token;
    }

    internal void EndSizing(string key, CancellationToken token)
    {
        if (_sizing.TryGetValue(key, out var cts) && cts.Token == token) _sizing.Remove(key);
    }

    /// <summary>Last known window placement, reused by autosave.</summary>
    public WindowPlacement? LastPlacement { get; set; }

    public void SaveWorkspace(WindowPlacement? placement)
    {
        placement ??= LastPlacement;
        LastPlacement = placement;
        try
        {
            JsonFileStore.Save(Services.Paths.WorkspaceFile, Workspace.ToState(placement), StateJsonContext.Default.WorkspaceState);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Saving workspace failed", ex);
        }
        Services.SaveHistory();
        Services.SaveSettings();
    }

    /// <summary>Opens locations forwarded by a second launch or passed on the command line.</summary>
    public void OpenArguments(StartupOptions options, bool initial = false)
    {
        var panel = Workspace.ActivePanel;
        if (panel is null) return;
        void Open(string text, PanelViewModel? into)
        {
            if (!Services.Providers.TryParse(text, null, out var loc) || loc is null)
            {
                Notify($"Could not open \"{text}\".", true);
                return;
            }
            // A file argument opens its folder with the file focused.
            string? focus = null;
            if (loc.IsFileSystem && File.Exists(loc.Path))
            {
                focus = Path.GetFileName(loc.Path);
                loc = Location.FileSystem(Path.GetDirectoryName(loc.Path)!);
            }
            var target = into ?? panel;
            // At startup the requested location replaces the default tab instead of adding one next to it.
            if (initial && target.Tabs.Count == 1 && !target.Tabs[0].IsLocked) target.ActiveTab?.Navigate(loc, focus, record: false);
            else target.OpenTab(loc, focus);
        }
        if (options.LeftLocation is { } l && Workspace.Panels.Count > 0) Open(l, Workspace.Panels[0]);
        if (options.RightLocation is { } r && Workspace.Panels.Count > 1) Open(r, Workspace.Panels[1]);
        foreach (var loc in options.Locations) Open(loc, null);
    }
}
