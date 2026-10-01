using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using FileCat.Core.State;
using FileCat.Platform.Windows;

namespace FileCat.App.ViewModels;

/// <summary>
/// One tab: location, navigation history, view settings, quick search, and its listing (plan §4.1).
/// A tab is a viewport; it never owns jobs or edit sessions.
/// </summary>
public sealed partial class TabViewModel : ObservableObject, IDisposable
{
    private const int MaxHistory = 50;
    private readonly List<Location> _back = [];
    private readonly List<Location> _forward = [];
    private int _columnProfile;
    private long _freeBytes = -1;
    /// <summary>The tab's own order while it shows a change journal (which reads newest first).</summary>
    private SortSpec? _sortOutsideJournal;
    private string? _freeBytesDevice;
    private bool _disposed;

    public TabViewModel(AppServices services, PanelViewModel panel)
    {
        Services = services;
        Panel = panel;
        Listing = new ListingModel(services.Providers, services.Io, services.Ui, services.Paths.ListingScratchDirectory, indexBudget: services.ListingIndexes)
        {
            ShowHidden = services.Settings.ShowHidden,
            Sort = new SortSpec(SortField.Name, false, !services.Settings.DirectoriesFirst, !services.Settings.NaturalSort),
        };
        Listing.Changed += OnListingChanged;
        services.Columns.Changed += OnProfilesChanged;
        services.Metadata.ValuesChanged += OnMetadataValuesChanged;
    }

    /// <summary>New values (a pool thread): the folder's checksum sum may have moved.</summary>
    private void OnMetadataValuesChanged()
    {
        if (_hasSidecars) Services.Ui.Post(() => { if (!_disposed) RefreshVerificationSummary(); });
    }

    public AppServices Services { get; }
    public PanelViewModel Panel { get; set; }
    public ListingModel Listing { get; }
    public Location? Location => Listing.Location;
    public IReadOnlyList<Location> BackHistory => _back;
    public IReadOnlyList<Location> ForwardHistory => _forward;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _displayPath = string.Empty;
    [ObservableProperty] private string _statusLeft = string.Empty;
    [ObservableProperty] private string _statusRight = string.Empty;
    /// <summary>
    /// With items marked, what they are and their size ("Marked 1 of 2 folders, 2 of 3 files · 2,39 MB"), shown first
    /// in the mark color; <see cref="StatusLeft"/> then holds the rest of the line. Empty with nothing marked.
    /// </summary>
    [ObservableProperty] private string _statusMarked = string.Empty;
    /// <summary>Marked folders whose size is not counted, none being counted: the status line offers to count them.</summary>
    [ObservableProperty] private bool _canCountMarked;

    /// <summary>Folders being counted in this tab now (Space, or Count in the status line); the main view model keeps it.</summary>
    public int SizingFolders { get; set; }
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private bool _returnToRoot;
    [ObservableProperty] private string? _quickSearch;
    [ObservableProperty] private bool _quickSearchNoMatch;
    [ObservableProperty] private string? _comparisonLabel;
    [ObservableProperty] private string? _banner;
    /// <summary>The filter's mask as typed, or null when every item shows (the panel's filter box shows it).</summary>
    [ObservableProperty] private string? _filterText;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isActiveTab;

    /// <summary>Saved root of a locked tab (the "return to root" variant).</summary>
    public Location? LockedRoot { get; set; }

    public bool IsQuickSearchActive => QuickSearch is not null;

    public event EventHandler? ColumnsChanged;

    public int ColumnProfile
    {
        get => _columnProfile;
        set
        {
            _columnProfile = Math.Clamp(value, 0, Services.Columns.Count - 1);
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ColumnSpec[] Columns => Core.Search.ResultSetProvider.IsWorkingSetList(Location)
        ? ColumnProfiles.WorkingSetList
        : Location is { Scheme: Schemes.ResultSet } set && Services.ResultSets.Get(set) is { ShowsGroups: true }
            ? ColumnProfiles.Duplicates
            : Services.Columns.Get(_columnProfile, Location?.Scheme ?? Schemes.FileSystem);

    /// <summary>Stores a dragged column width in the active profile (dedicated layouts keep it for this view only).</summary>
    public bool SetColumnWidth(int column, double width)
    {
        if (ColumnProfileSet.HasFixedLayout(Location?.Scheme ?? Schemes.FileSystem)) return false;
        Services.Columns.SetWidth(_columnProfile, column, width);
        return true;
    }

    private void OnProfilesChanged()
    {
        _columnProfile = Math.Clamp(_columnProfile, 0, Services.Columns.Count - 1);
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The tab's name as the tab strip shows it: control and bidirectional characters escaped, as the file list shows names (§18.3).</summary>
    public string TabHeader => (IsLocked ? "🔒 " : string.Empty) + Formatters.SafeName(Title);

    /// <summary>The path as shown beside the command line, escaped like <see cref="TabHeader"/>; <see cref="DisplayPath"/> stays the real one.</summary>
    public string ShownPath => Formatters.SafeName(DisplayPath);

    partial void OnDisplayPathChanged(string value) => OnPropertyChanged(nameof(ShownPath));

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(TabHeader));

    partial void OnIsLockedChanged(bool value) => OnPropertyChanged(nameof(TabHeader));

    partial void OnQuickSearchChanged(string? value) => OnPropertyChanged(nameof(IsQuickSearchActive));

    // ---- Change watching (plan §8.2): only the visible tab of each panel watches its folder ------------------

    private ChangeMonitor? _monitor;
    private FolderPoller? _poller;
    private RegistryChangeMonitor? _registryMonitor;
    private bool _registryDirty;
    /// <summary>The folder changed while it was being read: it is read again once that read completes.</summary>
    private bool _folderDirty;
    private DateTime _folderStampAtLoad;

    partial void OnIsActiveTabChanged(bool value)
    {
        if (value)
        {
            StartWatching();
            RefreshIfFolderChanged();
        }
        else
        {
            StopWatching();
        }
    }

    private void StartWatching()
    {
        StopWatching();
        if (!IsActiveTab || Location is not { } loc) return;
        if (loc.Scheme == Schemes.Registry && loc.Path.Length > 0)
        {
            _registryMonitor = new RegistryChangeMonitor(loc, () => Services.Ui.Post(() =>
            {
                if (_disposed || Location != loc) return;
                if (Listing.State == ListingState.Complete && !Listing.IsRefreshing) Listing.Refresh();
                else _registryDirty = true;
            }), _ => Services.Ui.Post(() =>
            {
                if (!_disposed && Location == loc) { _registryDirty = true; Banner = "Registry notifications stopped. Reread this key to check for changes."; }
            }));
            return;
        }
        if (!loc.IsFileSystem) return;
        var monitor = new ChangeMonitor(loc.Path, () => Services.Ui.Post(() => OnFolderChanged(loc)));
        _monitor = monitor.IsActive ? monitor : null;
        if (!monitor.IsActive) monitor.Dispose();
        // Changes made on a server are not reported to its mounts (inotify never sees them, some SMB servers send
        // nothing), nor anything in a folder that cannot be watched: their time stamp is read every few seconds too.
        bool watched = monitor.IsActive;
        _ = Task.Run(() => !watched || PathUtil.IsOnNetwork(loc.Path)).ContinueWith(t => Services.Ui.Post(() =>
        {
            if (!t.IsCompletedSuccessfully || !t.Result || _disposed || !IsActiveTab || Location != loc || _poller is not null) return;
            _poller = new FolderPoller(loc.Path, () => Services.Ui.Post(() => OnFolderChanged(loc)), TimeSpan.FromSeconds(3));
        }), TaskScheduler.Default);
    }

    /// <summary>The folder shown changed (a notification, or its time stamp): it is read again.</summary>
    private void OnFolderChanged(Location loc)
    {
        // The report may arrive after the tab closed or moved on (the post outlives the watcher).
        if (_disposed || Location != loc) return;
        if (Listing.State == ListingState.Complete && !Listing.IsRefreshing) Listing.Refresh();
        // A read already under way may have passed what changed: another follows it.
        else _folderDirty = true;
    }

    private void StopWatching()
    {
        _monitor?.Dispose();
        _monitor = null;
        _poller?.Dispose();
        _poller = null;
        _registryMonitor?.Dispose();
        _registryMonitor = null;
        _registryDirty = false;
        _folderDirty = false;
    }

    /// <summary>An inactive tab was not watched: a cheap folder timestamp check decides whether to refresh.</summary>
    private void RefreshIfFolderChanged()
    {
        if (Location is { Scheme: Schemes.Registry } && Listing.State == ListingState.Complete)
        {
            Listing.Refresh();
            return;
        }
        if (Location is not { IsFileSystem: true } loc || Listing.State != ListingState.Complete) return;
        var device = Services.Providers.For(loc).GetDeviceKey(loc);
        var stamp = _folderStampAtLoad;
        _ = Services.Io.Run(device, Core.Threading.IoPriority.Normal, _ => Directory.GetLastWriteTimeUtc(loc.Path)).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && t.Result != stamp)
                Services.Ui.Post(() =>
                {
                    if (Location == loc && !Listing.IsRefreshing) Listing.Refresh();
                });
        }, TaskScheduler.Default);
    }

    private void OnLoadCompleted()
    {
        if (_registryDirty && Location?.Scheme == Schemes.Registry)
        {
            _registryDirty = false;
            Services.Ui.Post(() => { if (!_disposed && !Listing.IsRefreshing) Listing.Refresh(); });
        }
        if (_monitor is not null) _monitor.MinInterval = TimeSpan.FromMilliseconds(Math.Clamp(Listing.LastLoadDuration.TotalMilliseconds * 3, 300, 10_000));
        // Read again what changed during the read, through the monitor's throttle: a folder in constant churn is not
        // reread back to back.
        if (_folderDirty && !Listing.IsRefreshing)
        {
            _folderDirty = false;
            if (_monitor is { } monitor) monitor.Again();
            // Only the time stamp is read here, and it already told of this change: read the folder again now.
            else Services.Ui.Post(() => { if (!_disposed && !Listing.IsRefreshing) Listing.Refresh(); });
        }
        if (Location is { IsFileSystem: true } loc)
        {
            var device = Services.Providers.For(loc).GetDeviceKey(loc);
            _ = Services.Io.Run(device, Core.Threading.IoPriority.Background, _ => Directory.GetLastWriteTimeUtc(loc.Path))
                .ContinueWith(t => { if (t.IsCompletedSuccessfully) _folderStampAtLoad = t.Result; }, TaskScheduler.Default);
        }
    }

    private bool _leaving;

    /// <summary>
    /// The folder shown was deleted or moved by another program while its drive is still there: the panel goes to the
    /// nearest folder that still exists and says why, as Explorer and Total Commander do. A drive that went away, or a
    /// network that dropped, keeps the listing and its banner.
    /// </summary>
    private void LeaveVanishedFolder(Location gone)
    {
        if (_leaving) return;
        _leaving = true;
        _ = Task.Run(() =>
        {
            if (MainViewModel.DriveRootOf(gone) is not { } root || !Directory.Exists(root)) return null;
            for (string? dir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(gone.Path)); dir is not null; dir = Path.GetDirectoryName(dir))
                if (Directory.Exists(dir)) return dir;
            return null;
        }).ContinueWith(t => Services.Ui.Post(() =>
        {
            _leaving = false;
            if (_disposed || Location != gone || !t.IsCompletedSuccessfully || t.Result is not { } parent) return;
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(gone.Path));
            Navigate(Location.FileSystem(LocalFileSystemProvider.NormalizeUserPath(parent)));
            Banner = $"“{name}” is no longer there: another program deleted or moved it. This is the nearest folder that still exists.";
        }), TaskScheduler.Default);
    }

    // ---- Navigation ----------------------------------------------------------------------------------------

    /// <summary>
    /// Navigates this tab. A locked tab keeps its location: navigating away opens a new tab instead
    /// (Total Commander's locked tabs, plan §4.1).
    /// </summary>
    public void Navigate(Location location, string? focusName = null, bool record = true)
    {
        // An SFTP home folder ("~") becomes its absolute path, so the tab, its history, and ".." know where they are.
        if (location.Scheme == Schemes.Sftp && location.Path.StartsWith('~'))
        {
            var sftp = Services.SftpProvider;
            string resolved = sftp.Resolve(location);
            if (!resolved.StartsWith('~'))
            {
                location = location.WithPath(resolved);
            }
            else
            {
                _ = ConnectThenNavigateAsync(location, focusName, record);
                return;
            }
        }
        // A location reached only after some work (a network share mounted first, D-54): that work, then the location it gives.
        if (Services.Providers.IsRegistered(location.Scheme) && Services.Providers.For(location).PrepareAsync(location, CancellationToken.None) is { } preparing)
        {
            _ = PrepareThenNavigateAsync(location, preparing, focusName, record);
            return;
        }
        if (IsLocked && Location is not null && !ReturnToRoot && !Services.Providers.For(location).IsSameLocation(location, Location))
        {
            Panel.OpenTab(location, focusName, activate: true);
            return;
        }
        if (record && Location is not null && Location != location)
        {
            _back.Add(Location);
            if (_back.Count > MaxHistory) _back.RemoveAt(0);
            _forward.Clear();
        }
        EndQuickSearch();
        ComparisonLabel = null;
        Banner = LocationBanner(location);
        // A change journal reads newest first, times mixed with folders; the tab's own order returns when it leaves.
        if (location.Scheme == Schemes.Journal && Location?.Scheme != Schemes.Journal)
        {
            _sortOutsideJournal = Listing.Sort;
            Listing.Sort = Listing.Sort with { Field = SortField.Modified, Descending = true, MixDirectories = true };
        }
        else if (location.Scheme != Schemes.Journal && _sortOutsideJournal is { } kept)
        {
            Listing.Sort = kept;
            _sortOutsideJournal = null;
        }
        Listing.Load(location, focusName);
        if (IsActiveTab) StartWatching();
        Services.RecordFolder(location);
        UpdateTitle();
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Connects in the background (the server's key and password prompts appear meanwhile) to learn the home folder.</summary>
    private async Task PrepareThenNavigateAsync(Location requested, Task<Location> preparing, string? focusName, bool record)
    {
        string what = Services.Providers.Display(requested);
        Banner = $"Opening {what}…";
        Location ready;
        try
        {
            ready = await preparing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or TimeoutException or InvalidOperationException)
        {
            if (!_disposed) Banner = ex is OperationCanceledException ? null : $"{what} could not be opened: {ex.Message}";
            return;
        }
        if (!_disposed) Navigate(ready, focusName, record);
    }

    private async Task ConnectThenNavigateAsync(Location location, string? focusName, bool record)
    {
        try
        {
            await Task.Run(() =>
            {
                using var lease = Services.SftpProvider.Lease(location, CancellationToken.None);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Services.Ui.Post(() => Banner = $"Not connected: {(ex is OperationCanceledException ? "canceled." : ex.Message)}");
            return;
        }
        string resolved = Services.SftpProvider.Resolve(location);
        if (!resolved.StartsWith('~')) Navigate(location.WithPath(resolved), focusName, record);
    }

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    public void GoBack()
    {
        if (_back.Count == 0 || Location is null) return;
        var target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _forward.Add(Location);
        NavigateHistory(target);
    }

    public void GoForward()
    {
        if (_forward.Count == 0 || Location is null) return;
        var target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _back.Add(Location);
        NavigateHistory(target);
    }

    private void NavigateHistory(Location target)
    {
        EndQuickSearch();
        ComparisonLabel = null;
        Banner = LocationBanner(target);
        Listing.Load(target);
        if (IsActiveTab) StartWatching();
        UpdateTitle();
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What a location is and how its keys differ, for places whose rules are not a folder's.</summary>
    private string? LocationBanner(Location location)
    {
        string Key(string id, string fallback) => Services.Keymap.GetGestureText(id) is { Length: > 0 } g ? g.Split(',')[0].Trim() : fallback;
        if (Core.Search.ResultSetProvider.IsWorkingSetList(location))
            return $"Working sets · Enter opens a set · {Key(CommandIds.MakeDirectory, "F7")} creates · {Key(CommandIds.Rename, "F2")} renames · " +
                   $"{Key(CommandIds.Delete, "F8")} deletes a set, never the items it refers to";
        if (Core.Search.ResultSetProvider.IsWorkingSet(location))
            return $"Working set: references to items in other places · F5 from another panel adds · {Key(CommandIds.RemoveFromSet, "Remove from set")} removes " +
                   $"from the set (never deletes) · {Key(CommandIds.Delete, "F8")} deletes the originals";
        return null;
    }

    public void GoUp()
    {
        if (Location is null) return;
        var provider = Services.Providers.For(Location);
        var parent = provider.GetParent(Location);
        if (parent is null) return;
        var name = provider.GetNameInParent(Location);
        Navigate(parent, name);
    }

    public void GoRoot()
    {
        if (Location is null) return;
        var loc = Location;
        var provider = Services.Providers.For(loc);
        Location? last = null;
        string? focus = null;
        for (var p = loc; p is not null; p = Services.Providers.For(p).GetParent(p))
        {
            // Stop at the file-system root rather than climbing into "This PC".
            if (p.Scheme != loc.Scheme && last is not null) break;
            focus = last is null ? null : Services.Providers.For(last).GetNameInParent(last);
            last = p;
        }
        if (last is not null && last != loc) Navigate(last, focus);
        _ = provider;
    }

    public void Refresh()
    {
        ComparisonLabel = null;
        if (Listing.State == ListingState.Failed && Location is not null) Listing.Load(Location);
        else Listing.Refresh();
    }

    /// <summary>Enter: navigate into containers; returns the entry when it should be opened otherwise.</summary>
    public bool TryEnterFocused(out EntryData focused)
    {
        focused = default;
        if (!Listing.TryGetFocused(out var e) || Location is null) return false;
        focused = e;
        // A row about an item elsewhere (a journal entry): its folder, with it under the cursor, while it exists.
        if (e.Tag is ILocatableEntry locatable && e.Kind != EntryKind.Parent)
        {
            if (locatable.Locate() is not { } at) return false;
            Navigate(Location.FileSystem(at.Folder), at.Name);
            return true;
        }
        if (e.Kind == EntryKind.Parent)
        {
            GoUp();
            return true;
        }
        var provider = Services.Providers.For(Location);
        var child = provider.GetChildLocation(Location, e);
        if (child is null) return false;
        Navigate(child);
        return true;
    }

    public void SortBy(SortField field)
    {
        var s = Listing.Sort;
        Listing.Sort = s.Field == field ? s with { Descending = !s.Descending } : s with { Field = field, Descending = field is SortField.Modified or SortField.Size };
    }

    public void SetFilter(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || Mask.TryParse(text, out var all, out _) && all.IsMatchAll)
        {
            // "*" and "*.*" show everything: no filter.
            Listing.Filter = null;
            FilterText = null;
            Banner = null;
            UpdateStatus();
            return;
        }
        if (!Mask.TryParse(text, out var mask, out var error))
        {
            Banner = "Filter: " + error;
            return;
        }
        Listing.Filter = mask;
        FilterText = mask.Text;
        UpdateStatus();
    }

    // ---- Quick search (plan §4.3) ----------------------------------------------------------------------------

    /// <summary>Extends the quick search text; rejects characters that match nothing (focus stays).</summary>
    public bool QuickSearchType(string text)
    {
        var candidate = (QuickSearch ?? string.Empty) + text;
        int match = FindQuickMatch(candidate, 0, forward: true);
        if (match < 0)
        {
            QuickSearchNoMatch = true;
            if (QuickSearch is null) QuickSearch = string.Empty;
            return false;
        }
        QuickSearch = candidate;
        QuickSearchNoMatch = false;
        Listing.SetFocus(match);
        return true;
    }

    public void QuickSearchBackspace()
    {
        if (QuickSearch is null) return;
        if (QuickSearch.Length == 0)
        {
            EndQuickSearch();
            return;
        }
        QuickSearch = QuickSearch[..^1];
        QuickSearchNoMatch = false;
        if (QuickSearch.Length > 0)
        {
            int m = FindQuickMatch(QuickSearch, 0, true);
            if (m >= 0) Listing.SetFocus(m);
        }
    }

    public void QuickSearchCycle(bool forward)
    {
        if (string.IsNullOrEmpty(QuickSearch)) return;
        int start = Listing.FocusedIndex + (forward ? 1 : -1);
        int m = FindQuickMatch(QuickSearch, start, forward);
        if (m >= 0) Listing.SetFocus(m);
    }

    public void EndQuickSearch()
    {
        QuickSearch = null;
        QuickSearchNoMatch = false;
    }

    private int FindQuickMatch(string text, int start, bool forward)
    {
        int count = Listing.VisibleCount;
        if (count == 0 || text.Length == 0) return -1;
        bool wildcard = text.Contains('*') || text.Contains('?');
        bool anywhere = Services.Settings.QuickSearchMatchAnywhere;
        string pattern = wildcard ? text.TrimEnd('*') + "*" : text;
        // A keystroke without a match scans the whole listing (bulk reads, spilled listings included). Typed ASCII
        // compares ordinally, which gives the same answer and is many times faster; other text compares linguistically.
        var comparison = System.Text.Ascii.IsValid(text) ? StringComparison.OrdinalIgnoreCase : StringComparison.CurrentCultureIgnoreCase;
        return Listing.FindVisible(start, forward, name =>
            wildcard ? Wildcard.IsMatch(name, pattern)
            : anywhere ? name.Contains(text, comparison)
            : name.StartsWith(text, comparison));
    }

    public void OnUserMovedFocus()
    {
        if (QuickSearch is not null) EndQuickSearch();
    }

    // ---- Listing notifications ---------------------------------------------------------------------------

    private void OnListingChanged(object? sender, ListingChange change)
    {
        if ((change & ListingChange.State) != 0 || (change & ListingChange.Reset) != 0)
        {
            IsLoading = Listing.State == ListingState.Loading || Listing.IsRefreshing;
            if (Listing.Issues.Count > 0) Banner = Listing.Issues[^1];
            if (Listing.State == ListingState.Complete)
            {
                RequestFreeSpace();
                OnLoadCompleted();
                NoteSidecars();
                if (Listing.LastRefreshError is DirectoryNotFoundException && Location is { IsFileSystem: true } gone) LeaveVanishedFolder(gone);
            }
        }
        // The focused item is described with items marked too (the marked ones are summed on the left).
        if ((change & (ListingChange.Rows | ListingChange.Marks | ListingChange.State | ListingChange.Reset | ListingChange.Focus)) != 0) UpdateStatus();
    }

    public void UpdateTitle()
    {
        if (Location is null) return;
        var provider = Services.Providers.For(Location);
        DisplayPath = provider.GetDisplayPath(Location);
        Title = provider.GetDisplayName(Location);
    }

    private void RequestFreeSpace()
    {
        if (Location is not { IsFileSystem: true } loc) return;
        var device = Services.Providers.For(loc).GetDeviceKey(loc);
        if (device == _freeBytesDevice && _freeBytes >= 0) { UpdateStatus(); return; }
        _freeBytesDevice = device;
        _ = Services.Io.Run(device, Core.Threading.IoPriority.Background, _ =>
        {
            var root = Path.GetPathRoot(loc.Path);
            return string.IsNullOrEmpty(root) ? -1 : new DriveInfo(root).AvailableFreeSpace;
        }).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
                Services.Ui.Post(() =>
                {
                    if (_disposed || Location != loc || _freeBytesDevice != device) return;
                    _freeBytes = t.Result;
                    UpdateStatus();
                });
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// How this location names what it lists: folders and files, keys and values, or things of one kind (drives,
    /// shares, changes), counted together.
    /// </summary>
    private (string DirOne, string DirMany, string FileOne, string FileMany, bool Together) Nouns() => Location?.Scheme switch
    {
        Schemes.Registry => ("key", "keys", "value", "values", false),
        Schemes.Computer => ("item", "items", "", "", true),
        Schemes.Network when Location.Path.Length == 0 => ("computer or server", "computers and servers", "", "", true),
        Schemes.Network => ("share", "shares", "", "", true),
        Schemes.Journal => ("change", "changes", "", "", true),
        Schemes.HiddenData => OperatingSystem.IsWindows()
            ? ("stream or attribute", "streams and attributes", "", "", true)
            : ("attribute", "attributes", "", "", true),
        Schemes.ResultSet when Core.Search.ResultSetProvider.IsWorkingSetList(Location) => ("working set", "working sets", "", "", true),
        _ => ("folder", "folders", "file", "files", false),
    };

    public void UpdateStatus()
    {
        var l = Listing;
        var totals = l.Store.Totals;
        bool fileSystem = Location is { IsFileSystem: true };
        // Counted in the location's own words: keys and values in the Registry, drives in This PC.
        var (dirOne, dirMany, fileOne, fileMany, together) = Nouns();

        // What follows the counts, in both states.
        var rest = new List<string>();
        if (l.State == ListingState.Loading) rest.Add(l.LoadingProgress ?? "loading…");
        else if (l.IsRefreshing) rest.Add(l.LoadingProgress ?? "refreshing…");
        if (l.Filter is not null) rest.Add($"filter \"{l.Filter.Text}\" shows {Math.Max(0, l.VisibleCount - (l.HasParentRow ? 1 : 0))}");
        // Checksums before free space: a failed one matters more, and the end of a long line is what gets cut.
        if (_verificationSummary is { } verified && fileSystem) rest.Add(verified);
        // Free space belongs to folders on disk, not to archives, servers, or lists (the last value would linger there).
        if (_freeBytes >= 0 && fileSystem) rest.Add($"{Formatters.SizeWithUnit(_freeBytes)} free");

        var stats = l.GetMarkStats();
        if (stats.Count == 0)
        {
            var left = together
                ? Formatters.Plural(totals.Directories + totals.Files, dirOne, dirMany)
                : $"{Formatters.Plural(totals.Directories, dirOne, dirMany)}, {Formatters.Plural(totals.Files, fileOne, fileMany)}";
            if (totals.KnownFileBytes > 0) left += $" · {Formatters.SizeWithUnit(totals.KnownFileBytes)}";
            // A location that could not be read has nothing to count ("0 folders, 0 files" would say it is empty).
            if (l.State == ListingState.Failed) left = totals.Directories + totals.Files == 0 ? "Not available" : left + " · listing incomplete";
            StatusMarked = string.Empty;
            StatusLeft = string.Join(" · ", rest.Prepend(left));
            CanCountMarked = false;
        }
        else
        {
            // "Marked 1 of 2 folders, 2 of 3 files": what is marked, out of what is here.
            var what = new List<string>();
            if (together) what.Add($"{stats.Count:N0} of {Formatters.Plural(totals.Directories + totals.Files, dirOne, dirMany)}");
            else
            {
                if (stats.Directories > 0) what.Add($"{stats.Directories:N0} of {Formatters.Plural(totals.Directories, dirOne, dirMany)}");
                if (stats.Files > 0) what.Add($"{stats.Files:N0} of {Formatters.Plural(totals.Files, fileOne, fileMany)}");
            }
            var marked = "Marked " + string.Join(", ", what);
            // A marked folder's size counts once counted (Space counts as it marks); until then it is said, not guessed.
            int unsized = !together && dirOne == "folder" ? stats.UnsizedDirectories : 0;
            string size = Formatters.SizeWithUnit(stats.Bytes);
            if (unsized == 0)
            {
                if (stats.Bytes > 0 || stats.Files > 0) marked += " · " + size;
            }
            else if (SizingFolders > 0)
                marked += " · " + (stats.Bytes > 0 ? $"{size} so far, " : "") + $"counting {Formatters.Plural(unsized, "folder", "folders")}…";
            else if (stats.Bytes > 0 || stats.Files > 0)
                marked += $" · {size}, not counting {Formatters.Plural(unsized, "folder", "folders")}";
            else
                marked += unsized == 1 ? " · size not counted" : " · sizes not counted";
            if (stats.HiddenByFilter > 0) marked += $" · {stats.HiddenByFilter:N0} of them hidden by the filter";
            StatusMarked = marked;
            StatusLeft = rest.Count > 0 ? " · " + string.Join(" · ", rest) : string.Empty;
            CanCountMarked = unsized > 0 && SizingFolders == 0 && fileSystem;
        }
        StatusRight = l.TryGetFocused(out var f) && f.Kind != EntryKind.Parent ? DescribeFocused(f) + FocusedVerification(f, l.FocusedStoreIndex) : string.Empty;
    }

    private static string DescribeFocused(in EntryData e)
    {
        // A drive: what it is and how full ("C: SYSTEM · NTFS · 189 GB free of 952 GB, 80% used").
        if (e.Tag is Core.FileSystem.DriveTag drive)
        {
            var parts = new List<string> { string.IsNullOrEmpty(drive.Label) ? e.Name : $"{e.Name} {drive.Label}" };
            if (!drive.Ready) parts.Add("not ready");
            if (!string.IsNullOrEmpty(drive.Format)) parts.Add(drive.Format);
            if (drive.Ready && drive.TotalBytes > 0)
                parts.Add($"{Formatters.SizeWithUnit(drive.FreeBytes)} free of {Formatters.SizeWithUnit(drive.TotalBytes)}, {100.0 * (drive.TotalBytes - drive.FreeBytes) / drive.TotalBytes:0}% used");
            if (!string.IsNullOrEmpty(drive.RemoteName)) parts.Add(drive.RemoteName);
            return string.Join(" · ", parts);
        }
        if (e.IsContainer) return e.Name;
        return $"{e.Name} · {Formatters.ExactSize(e.Size)}";
    }

    // ---- Cell helpers for special providers -------------------------------------------------------------

    public string GetFolderText(in EntryData e) => e.Tag switch
    {
        Core.Search.ResultTag r => r.Folder ?? r.RelativeFolder,
        _ => string.Empty,
    };

    public string GetKindText(in EntryData e) => e.Tag is IDisplayDetails d ? d.KindText : string.Empty;

    // ---- Metadata columns (plan §10) --------------------------------------------------------------------

    /// <summary>Store indices drawn in the last frame; queued metadata work for other rows is skipped.</summary>
    public volatile HashSet<int> VisibleStoreIndices = [];

    private CancellationTokenSource? _analysis;

    [ObservableProperty] private string? _analysisStatus;

    private bool IsSlowLocation => Location is { IsFileSystem: true } l && (PathUtil.IsUncPath(l.Path) || Services.Providers.For(l).GetDeviceKey(l).StartsWith(@"\\", StringComparison.Ordinal));

    public string GetMetadataText(in EntryData e, int storeIndex, string fieldId, out bool pending)
    {
        pending = false;
        var field = Services.Metadata.Field(fieldId);
        if (field is null || Location is null) return string.Empty;
        // A folder without checksum files or signatures has nothing to verify (search results are asked file by file).
        if (fieldId == Core.Metadata.BuiltInFields.Verified.Id && Location.IsFileSystem && !_hasSidecars) return string.Empty;
        var item = Services.Providers.For(Location).GetItemRef(Location, e);
        if (item.FileSystemPath is not { } path) return string.Empty;
        var store = Listing.Store;
        var device = Services.Providers.For(item.Parent).GetDeviceKey(item.Parent);
        var value = Services.Metadata.Get(fieldId, path, e, device, IsSlowLocation, () => ReferenceEquals(store, Listing.Store) && VisibleStoreIndices.Contains(storeIndex));
        switch (value.State)
        {
            case Core.Metadata.MetadataState.Available:
                return field.Format(value.Value);
            case Core.Metadata.MetadataState.Pending:
                pending = true;
                return "…";
            case Core.Metadata.MetadataState.Failed:
                return "error";
            case Core.Metadata.MetadataState.Unsupported:
                return "—";
            default:
                return string.Empty;
        }
    }

    /// <param name="analyzing">Every value is about to be computed (Analyze folder): no note that the order is partial.</param>
    public void SortByMetadata(string fieldId, bool analyzing = false)
    {
        var s = Listing.Sort;
        bool same = s.Field == SortField.Metadata && s.MetadataId == fieldId;
        Listing.MetadataKeys ??= MetadataKey;
        Listing.Sort = s with { Field = SortField.Metadata, MetadataId = fieldId, Descending = same && !s.Descending };
        if (fieldId == ColumnSpec.FolderSortKey || analyzing) return; // every item's folder is known at once
        var field = Services.Metadata.Field(fieldId);
        Banner = $"Sorted by {field?.Title ?? fieldId} using the values computed so far; items without a value are listed last. Choose View → Analyze folder to compute every value.";
    }

    private IComparable? MetadataKey(EntryStore store, int storeIndex, string fieldId)
    {
        var loc = Location;
        if (loc is null || storeIndex >= store.Count) return null;
        var e = store[storeIndex];
        if (fieldId == ColumnSpec.FolderSortKey) return GetFolderText(e);
        var field = Services.Metadata.Field(fieldId);
        if (field is null || Services.Providers.For(loc).GetItemRef(loc, e).FileSystemPath is not { } path) return null;
        var v = Services.Metadata.Get(fieldId, path, e, Services.Providers.For(loc).GetDeviceKey(loc), IsSlowLocation, () => false);
        return v.State == Core.Metadata.MetadataState.Available ? field.SortKey?.Invoke(v.Value) ?? v.Value as IComparable : null;
    }

    /// <summary>
    /// Explicit analysis (plan §10): computes a metadata field for every item with visible progress and
    /// cancellation, then re-sorts. Until it completes the order is labeled partial.
    /// </summary>
    public async Task AnalyzeAsync(string fieldId)
    {
        if (Location is null) return;
        _analysis?.Cancel();
        var cts = _analysis = new CancellationTokenSource();
        var loc = Location;
        var provider = Services.Providers.For(loc);
        var store = Listing.Store;
        int count = store.Count;
        var field = Services.Metadata.Field(fieldId);
        AnalysisStatus = $"Analyzing {field?.Title}: 0 of {count:N0}…";
        int done = 0;
        try
        {
            await Services.Io.Run(provider.GetDeviceKey(loc), Core.Threading.IoPriority.Background, ct =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
                var last = DateTime.UtcNow;
                for (int i = 0; i < count; i++)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    var e = store[i];
                    if (!e.IsContainer && provider.GetItemRef(loc, e).FileSystemPath is { } path) Services.Metadata.Compute(fieldId, path, e, linked.Token);
                    done = i + 1;
                    if (DateTime.UtcNow - last > TimeSpan.FromMilliseconds(200))
                    {
                        last = DateTime.UtcNow;
                        int d = done;
                        Services.Ui.Post(() => AnalysisStatus = $"Analyzing {field?.Title}: {d:N0} of {count:N0}… (Esc cancels)");
                    }
                }
            }, cts.Token);
            AnalysisStatus = null;
            Banner = $"Sorted by {field?.Title} with every value computed ({count:N0} items).";
            Listing.Resort();
        }
        catch (OperationCanceledException)
        {
            AnalysisStatus = null;
            Banner = $"Analysis canceled after {done:N0} of {count:N0} items; the order remains partial.";
        }
    }

    public bool CancelAnalysis()
    {
        if (_analysis is null || AnalysisStatus is null) return false;
        _analysis.Cancel();
        return true;
    }

    public string GetDetailsText(in EntryData e) => e.Tag is IDisplayDetails d ? d.DetailsText : string.Empty;

    // ---- Persistence ---------------------------------------------------------------------------------------

    public TabState ToState()
    {
        var focus = Listing.TryGetFocused(out var f) && f.Kind != EntryKind.Parent ? f.Name : null;
        return new TabState
        {
            // Working sets outlive the session; search results and other result sets do not. A drive's deleted items are
            // not reopened at startup either: that would ask for administrator approval before anyone asked to scan.
            Location = Location is { Scheme: Schemes.ResultSet } && !Core.Search.ResultSetProvider.IsPersistent(Location) ||
                       Location is { Scheme: Schemes.Recovery } && FileCat.Recovery.RecoveryProvider.IsDevice(Location) ? null : Location,
            Locked = IsLocked,
            ReturnToRoot = ReturnToRoot,
            LockedRoot = LockedRoot,
            SortField = Listing.Sort.Field,
            SortDescending = Listing.Sort.Descending,
            Filter = Listing.Filter?.Text,
            ColumnProfile = ColumnProfile,
            FocusName = focus,
            BackHistory = _back.TakeLast(10).Where(l => (l.Scheme != Schemes.ResultSet || Core.Search.ResultSetProvider.IsPersistent(l)) &&
                                                         !(l.Scheme == Schemes.Recovery && FileCat.Recovery.RecoveryProvider.IsDevice(l))).ToList(),
        };
    }

    public void ApplyState(TabState s)
    {
        IsLocked = s.Locked;
        ReturnToRoot = s.ReturnToRoot;
        LockedRoot = s.LockedRoot;
        _columnProfile = s.ColumnProfile;
        Listing.Sort = Listing.Sort with { Field = s.SortField, Descending = s.SortDescending };
        if (!string.IsNullOrEmpty(s.Filter)) SetFilter(s.Filter);
        _back.Clear();
        _back.AddRange(s.BackHistory);
    }

    public void Dispose()
    {
        _disposed = true;
        StopWatching();
        Listing.Changed -= OnListingChanged;
        Services.Columns.Changed -= OnProfilesChanged;
        Services.Metadata.ValuesChanged -= OnMetadataValuesChanged;
        Listing.Dispose();
    }

    public override string ToString() => Title;

    internal static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
