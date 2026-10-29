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

    /// <summary>The whole command title, where the bar shows a short label or trims a long one.</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>The key with its modifiers and the whole title, and why the command is unavailable here.</summary>
    [ObservableProperty] private string? _tip;
}

/// <summary>Root view model: workspace, key bar, command line, and command dispatch (plan §4.4).</summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// Raised with what a screen reader should hear after a keyboard tab switch. The focused list keeps its row, so
    /// Orca only said "Go to parent location" and never which of several same-named tabs it now was.
    /// </summary>
    public event Action<string>? TabAnnounced;

    private void AnnounceTab(PanelViewModel? panel)
    {
        if (panel?.ActiveTab is { } tab && panel.Tabs.Count > 1)
            TabAnnounced?.Invoke($"Tab {panel.Tabs.IndexOf(tab) + 1} of {panel.Tabs.Count}: {tab.TabAccessibleName}");
    }

    /// <summary>Folder counts running, by tab and name, each with what ends it in its tab (status line, Count).</summary>
    private readonly Dictionary<string, (CancellationTokenSource Cts, Action? Ended)> _sizing = new(StringComparer.Ordinal);

    public MainViewModel(AppServices services)
    {
        Services = services;
        Workspace = new WorkspaceViewModel(services);
        AttachRemoteInteraction();
        _drives = services.Drives.Listen(change => services.Ui.Post(() => OnDrivesChanged(change)));
        services.Saved += OnStateSaved;
        services.StateSaveFailed += message => services.Ui.Post(() => Notify(message, true));
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
    private TabViewModel? _availabilityTab;
    private bool _keyBarPending;

    /// <summary>Signals that command presentations should refresh from the current, cached context.</summary>
    public event EventHandler? CommandAvailabilityChanged;

    /// <summary>
    /// The key bar follows the active listing: F3, F5, F8 and the rest apply only to a focused or marked item, so their
    /// state changes as focus moves off "..", marks come and go, and a listing loads.
    /// </summary>
    private void FollowActiveListing()
    {
        var tab = Workspace.ActiveTab;
        var listing = tab?.Listing;
        if (ReferenceEquals(listing, _keyBarListing) && ReferenceEquals(tab, _availabilityTab)) return;
        if (_keyBarListing is not null) _keyBarListing.Changed -= OnActiveListingChanged;
        if (_availabilityTab is not null) _availabilityTab.PropertyChanged -= OnAvailabilityTabChanged;
        _keyBarListing = listing;
        _availabilityTab = tab;
        if (listing is not null) listing.Changed += OnActiveListingChanged;
        if (tab is not null) tab.PropertyChanged += OnAvailabilityTabChanged;
    }

    private void OnAvailabilityTabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TabViewModel.CanGoBack) or nameof(TabViewModel.CanGoForward))
            UpdateKeyBar(_mods);
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
    private const int MaxRecentNotifications = 100;
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(30);
    private readonly List<UserNotification> _recentNotifications = [];
    private long _nextNotificationId;
    private UserNotification? _currentNotification;

    public UserNotification? CurrentNotification
    {
        get => _currentNotification;
        private set
        {
            var previous = _currentNotification;
            if (!SetProperty(ref _currentNotification, value)) return;
            bool messageChanged = !string.Equals(previous?.Message, value?.Message, StringComparison.Ordinal);
            if (messageChanged)
                OnPropertyChanged(nameof(Notification));
            if (messageChanged || previous?.IsError != value?.IsError)
                OnPropertyChanged(nameof(NotificationAnnouncement));
            if (previous?.IsError != value?.IsError)
            {
                OnPropertyChanged(nameof(NotificationIsError));
                OnPropertyChanged(nameof(NotificationIsInfo));
            }
            if (previous?.ActionCommandId != value?.ActionCommandId)
                OnPropertyChanged(nameof(NotificationRestoreWorkspace));
            OnPropertyChanged(nameof(NotificationRepeatText));
        }
    }

    public string? Notification => CurrentNotification?.Message;
    // Mac's native live-region bridge requires a non-null announcement name even while a
    // notification is being dismissed. Include severity so a same-text error is still distinct.
    public string NotificationAnnouncement => CurrentNotification is { } notice
        ? (notice.IsError ? "Error: " : "Notice: ") + notice.Message
        : "FileCat notification";
    public bool NotificationIsError => CurrentNotification?.IsError == true;
    public bool NotificationIsInfo => CurrentNotification is { IsError: false };
    public bool NotificationRestoreWorkspace => CurrentNotification?.ActionCommandId == CommandIds.RestorePreviousWorkspace;
    public string? NotificationRepeatText => CurrentNotification is { Occurrences: > 1 } current ? current.OccurrenceLabel : null;
    public IReadOnlyList<UserNotification> RecentNotifications => _recentNotifications.ToArray();
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
        ComfortableDensity = ReadabilityPolicy.Comfortable(Services.Settings.RowDensity);
        ShowCommandLine = Services.Settings.ShowCommandLine;
        ShowToolbar = Services.Settings.ShowToolbar;
        ShowDriveButtons = Services.Settings.ShowDriveButtons;
        ShowHiddenItems = Services.Settings.ShowHidden;
        QuietConnect = state is not null;
        _ = RefreshDriveButtonsAsync();
        Workspace.LoadState(state);
        if (WorkspaceReadOnly)
            Notify(WorkspaceUnavailable
                ? "The saved window layout cannot be read safely. The default layout is shown and the original file will not be overwritten."
                : WorkspaceOversized
                    ? "The saved window layout exceeds FileCat's safe input limit. The default layout is shown and the original file will not be overwritten."
                    : "The window layout was saved by a newer FileCat; this one starts with the default layout and does not save over it.", true);
        if (Services.SettingsStatus == StateLoadStatus.UnavailableReadOnly)
            Notify("Settings cannot be read safely. A backup or defaults are shown read-only; the original file was left untouched.", true);
        if (Services.SettingsStatus == StateLoadStatus.OversizedReadOnly)
            Notify("Settings exceed FileCat's safe input limit. A backup or defaults are shown read-only; the original file was left untouched.", true);
        else if (Services.SettingsStatus == StateLoadStatus.NewerSchemaReadOnly)
            Notify("Settings were written by a newer FileCat and are opened read-only; changes will not be saved.", true);
        else if (Services.SettingsStatus is StateLoadStatus.RecoveredFromBackup or StateLoadStatus.CorruptUsingDefaults)
            Notify("Settings were damaged; FileCat restored a usable copy and kept the damaged file for inspection.", true);
        if (Services.HistoryStatus == StateLoadStatus.UnavailableReadOnly)
            Notify("History cannot be read safely. It is shown from a backup or defaults and will not be saved over the original.", true);
        else if (Services.HistoryStatus == StateLoadStatus.OversizedReadOnly)
            Notify("History exceeds FileCat's safe input limit. It is shown from a backup or defaults and will not be saved over the original.", true);
    }

    public void UpdateKeyBar(KeyMods mods)
    {
        _mods = mods;
        var bar = Services.Keymap.GetFunctionKeyBar(mods, CommandContext.Panel);
        for (int i = 0; i < 12; i++)
        {
            var key = KeyBar[i];
            var availability = bar[i].CommandId is { } id ? GetAvailability(id) : default;
            string? title = bar[i].CommandId is { } command ? Services.Commands.Get(command)?.Title : null;
            string chord = new KeyChord("F" + (i + 1), mods).ToDisplayString();
            key.Label = bar[i].Label;
            key.CommandId = bar[i].CommandId;
            key.IsEnabled = bar[i].CommandId is not null && availability.Enabled;
            // A key without a command in this layer still says which key it is (unnamed, readers said "Button").
            key.Title = title ?? chord + ": no command";
            key.Tip = title is null ? null : chord + ": " + title +
                (availability.Enabled ? string.Empty : Environment.NewLine + "Unavailable here: " +
                    (availability.Reason ?? "This command is not available in the current context."));
        }
        CommandAvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Use for input-state reconciliation; ordinary keys need not rebuild twelve buttons.</summary>
    internal void ReconcileKeyBar(KeyMods mods)
    {
        if (_mods != mods) UpdateKeyBar(mods);
    }

    public void Notify(string message, bool isError = false, string? actionCommandId = null)
    {
        var now = DateTimeOffset.UtcNow;
        int previous = _recentNotifications.FindLastIndex(item => item.Message == message && item.IsError == isError &&
            item.ActionCommandId == actionCommandId && now - item.LastAt <= RepeatWindow);
        UserNotification current;
        if (previous >= 0)
        {
            current = _recentNotifications[previous] with { LastAt = now, Occurrences = _recentNotifications[previous].Occurrences + 1 };
            _recentNotifications.RemoveAt(previous);
        }
        else current = new UserNotification(++_nextNotificationId, message, isError, actionCommandId, now, now);
        _recentNotifications.Add(current);
        if (_recentNotifications.Count > MaxRecentNotifications) _recentNotifications.RemoveAt(0);
        if (Scope?.View is { } scoped)
        {
            // A command in a Find window reports there.
            scoped.ShowNotification(message, isError);
            if (isError) AppLog.Warn("User notification: " + message);
            return;
        }
        CurrentNotification = current;
        if (isError) AppLog.Warn("User notification: " + message);
    }

    public void ClearNotification(long? expectedId = null)
    {
        if (expectedId is null || CurrentNotification?.Id == expectedId) CurrentNotification = null;
    }

    /// <summary>
    /// Stops a count. It ends in its tab at once: a call it is waiting on (a slow disk, a dead share) may take a while to
    /// return, and until then the tab would go on saying it counts and refusing Count.
    /// </summary>
    internal void CancelSizing(string key)
    {
        if (!_sizing.Remove(key, out var sizing)) return;
        sizing.Cts.Cancel();
        sizing.Ended?.Invoke();
    }

    /// <summary>Stops one count, not a count of the same name started since.</summary>
    internal void CancelSizing(string key, CancellationToken token)
    {
        if (_sizing.TryGetValue(key, out var sizing) && sizing.Cts.Token == token)
        {
            _sizing.Remove(key);
            sizing.Cts.Cancel();
            sizing.Ended?.Invoke();
        }
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
        var all = _sizing.Values.ToList();
        _sizing.Clear();
        foreach (var (cts, ended) in all)
        {
            cts.Cancel();
            ended?.Invoke();
        }
    }

    internal CancellationToken BeginSizing(string key)
    {
        CancelSizing(key);
        var cts = new CancellationTokenSource();
        _sizing[key] = (cts, null);
        return cts.Token;
    }

    /// <summary>What ends the count of <paramref name="key"/> in its tab, when it is stopped rather than finished.</summary>
    internal void OnSizingStopped(string key, CancellationToken token, Action ended)
    {
        if (_sizing.TryGetValue(key, out var sizing) && sizing.Cts.Token == token) _sizing[key] = (sizing.Cts, ended);
    }

    internal void EndSizing(string key, CancellationToken token)
    {
        if (_sizing.TryGetValue(key, out var sizing) && sizing.Cts.Token == token) _sizing.Remove(key);
    }

    /// <summary>Last known window placement, reused by autosave.</summary>
    public WindowPlacement? LastPlacement { get; set; }

    /// <summary>
    /// The window layout is newer or too large to read safely: this one starts with the default layout and
    /// never saves over the original — not on exit, not in the minute's autosave (plan §19.1).
    /// </summary>
    public bool WorkspaceReadOnly { get; private set; }
    private bool WorkspaceOversized { get; set; }
    private bool WorkspaceUnavailable { get; set; }

    /// <summary>
    /// The saved window layout, or null for the default one: none saved yet, damaged beyond its backup, written by a
    /// newer FileCat (then <see cref="WorkspaceReadOnly"/>), or a reset the user asked for. Read even for a reset, so that
    /// a reset never replaces a newer FileCat's layout either.
    /// </summary>
    public WorkspaceState? LoadSavedWorkspace(bool reset = false)
    {
        var state = JsonFileStore.Load(Services.Paths.WorkspaceFile, StateJsonContext.Default.WorkspaceState,
            WorkspaceState.CurrentSchema, () => new WorkspaceState(), out var status);
        if (status is StateLoadStatus.CorruptUsingDefaults) AppLog.Warn("Workspace was corrupt; using the default layout.");
        WorkspaceOversized = status == StateLoadStatus.OversizedReadOnly;
        WorkspaceUnavailable = status == StateLoadStatus.UnavailableReadOnly;
        WorkspaceReadOnly = status is StateLoadStatus.NewerSchemaReadOnly or StateLoadStatus.OversizedReadOnly or StateLoadStatus.UnavailableReadOnly;
        return WorkspaceReadOnly || reset ? null : state;
    }

    public void SaveWorkspace(WindowPlacement? placement)
    {
        placement ??= LastPlacement;
        LastPlacement = placement;
        if (WorkspaceReadOnly) return;
        try
        {
            JsonFileStore.Save(Services.Paths.WorkspaceFile, Workspace.ToState(placement), StateJsonContext.Default.WorkspaceState);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Services.ReportSaveFailure("window layout", ex);
        }
        Services.SaveHistory();
        Services.SaveSettings();
    }

    /// <summary>Opens locations forwarded by a second launch or passed on the command line.</summary>
    public void OpenArguments(StartupOptions options, bool initial = false)
    {
        // A named workspace replaces the panels and tabs first; the locations and a list then open in it (plan §19.1).
        if (options.Workspace is { Length: > 0 } workspace)
        {
            _ = OpenArgumentsInWorkspaceAsync(workspace, options);
            return;
        }
        OpenLocations(options, initial);
    }

    private async Task OpenArgumentsInWorkspaceAsync(string workspace, StartupOptions options)
    {
        await OpenNamedWorkspaceAsync(workspace);
        OpenLocations(options, initial: false);
    }

    private void OpenLocations(StartupOptions options, bool initial)
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
        if (options.ListFile is { Length: > 0 } list) OpenListFile(list);
    }

    /// <summary>A list file (--list, Total Commander's LOADLIST) opens as a result set in a new tab; reading it may take a while.</summary>
    private void OpenListFile(string file)
    {
        string path;
        try { path = Path.GetFullPath(file); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Notify($"Could not open the list \"{file}\": {ex.Message}", true);
            return;
        }
        var set = Services.ResultSets.Create($"List: {Path.GetFileName(path)}", $"Paths listed in {path}");
        set.FullFolders = true;
        OpenResultSet(set, null);
        _ = Task.Run(() =>
        {
            string? problem = null;
            Core.Search.ListFile.Outcome? outcome = null;
            try { outcome = Core.Search.ListFile.Load(path, set, CancellationToken.None); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problem = ex.Message; }
            set.IsComplete = true;
            set.NotifyChanged();
            Services.Ui.Post(() =>
            {
                if (outcome is null)
                {
                    Notify($"Could not read the list {path}: {problem}", true);
                    return;
                }
                var left = new List<string>();
                if (outcome.Missing > 0) left.Add($"{outcome.Missing:N0} named nothing that exists");
                if (outcome.Network > 0) left.Add($"{outcome.Network:N0} network, linked, or unsafe paths were left out (list loading does not follow them; open trusted paths directly)");
                if (outcome.Truncated) left.Add($"the list stops at {Core.Search.ListFile.MaxItems:N0} items");
                Notify($"Opened {Formatters.Plural(outcome.Added, "item", "items")} from {Path.GetFileName(path)}" + (left.Count > 0 ? "; " + string.Join("; ", left) + "." : "."), left.Count > 0);
            });
        });
    }
}
