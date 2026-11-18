using System.Globalization;
using Avalonia.Threading;
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
    private int _columnProfile;
    private long _freeBytes = -1;
    private Location? _freeBytesLocation;
    private string? _freeBytesVolume;
    private DateTime _freeBytesCheckedUtc;
    private bool _freeSpaceUnavailable;
    private Location? _freeSpaceTrackedLocation;
    private Location? _freeSpacePendingLocation;
    private DateTime _freeSpacePendingSince;
    private long _freeSpaceRequestId;
    private bool _freeSpaceRefreshAfterPending;
    private bool _forceFreeSpaceOnListingComplete;
    private readonly DispatcherTimer _freeSpaceTimer;
    /// <summary>The tab's own order while it shows a change journal (which reads newest first).</summary>
    private SortSpec? _sortOutsideJournal;
    private bool _disposed;
    private CancellationTokenSource? _pendingNavigation;
    private string? _bannerBeforePendingNavigation;
    private PreparedLocation? _preparedLocation;

    public TabViewModel(AppServices services, PanelViewModel panel)
    {
        Services = services;
        Panel = panel;
        CapacityQuery = FileSystemCapacity.Query;
        _freeSpaceTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => RequestFreeSpace());
        Listing = new ListingModel(services.Providers, services.Io, services.Ui, services.Paths.ListingScratchDirectory, indexBudget: services.ListingIndexes)
        {
            ShowHidden = services.Settings.ShowHidden,
            Sort = new SortSpec(SortField.Name, false, !services.Settings.DirectoriesFirst, !services.Settings.NaturalSort),
        };
        Listing.Changed += OnListingChanged;
        Listing.CaptureMetadataOrder = CaptureMetadataOrder;
        Listing.PreparingStore += PreparingMetadataStore;
        services.Columns.Changed += OnProfilesChanged;
        services.Metadata.ValuesChanged += OnMetadataValuesChanged;
        RefreshSortDescription();
    }

    /// <summary>New values (a pool thread): the folder's checksum sum may have moved.</summary>
    private void OnMetadataValuesChanged()
    {
        Services.Ui.Post(() => { if (!_disposed) CheckMetadataAnalysis(); });
        if (_hasSidecars) Services.Ui.Post(() => { if (!_disposed) RefreshVerificationSummary(); });
    }

    public AppServices Services { get; }
    public PanelViewModel Panel { get; set; }
    public ListingModel Listing { get; }
    internal Func<string, FileSystemCapacitySnapshot?> CapacityQuery { get; set; }
    internal TimeSpan CapacityTtl { get; set; } = TimeSpan.FromSeconds(20);
    internal string? FreeSpaceVolumeIdentity => _freeBytesVolume;
    public Location? Location => Listing.Location;
    /// <summary>Changes at navigation intent, before any provider/connection await; delayed path Paste checks it.</summary>
    internal long NavigationGeneration { get; private set; }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _displayPath = string.Empty;
    [ObservableProperty] private string _statusLeft = string.Empty;
    [ObservableProperty] private string _statusRight = string.Empty;
    /// <summary>Available space for the current file-system volume, shown beside the panel's mask.</summary>
    [ObservableProperty] private string _freeSpaceText = string.Empty;
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
    /// <summary>Validation for the filter field only; location and listing warnings keep their own banner.</summary>
    [ObservableProperty] private string? _filterValidationError;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isActiveTab;
    [ObservableProperty] private bool _pictureView;

    /// <summary>Saved root of a locked tab (the "return to root" variant).</summary>
    private Location? _lockedRoot;
    public Location? LockedRoot
    {
        get => _lockedRoot;
        set
        {
            if (_lockedRoot == value) return;
            _lockedRoot = value;
            OnPropertyChanged(nameof(LockedRoot));
            OnPropertyChanged(nameof(TabLockDescription));
            OnPropertyChanged(nameof(TabTooltip));
            OnPropertyChanged(nameof(TabAccessibleName));
        }
    }

    public bool IsQuickSearchActive => QuickSearch is not null;

    /// <summary>
    /// Raised when the columns this tab shows may have changed: another location, another profile, an edited one.
    /// <see cref="ColumnsChangedEventArgs.WidthsOnly"/> when they are the same columns at other widths.
    /// </summary>
    public event EventHandler<ColumnsChangedEventArgs>? ColumnsChanged;

    private string _sortDescription = string.Empty;

    /// <summary>The current order, including the field and direction, for the panel's accessible Sort button.</summary>
    public string SortDescription => _sortDescription;

    private void RefreshSortDescription()
    {
        var sort = Listing.Sort;
        var column = Columns.FirstOrDefault(c => c.SortField == sort.Field &&
            (sort.Field != SortField.Metadata || c.MetadataId == sort.MetadataId));
        string field = sort.Field == SortField.None ? "Unsorted" :
            column is not null ? Formatters.SafeName(column.DisplayHeader) : sort.Field == SortField.Metadata && sort.MetadataId is { } id
                ? Formatters.SafeName(Services.Metadata.Field(id)?.Title ?? id) : sort.Field.ToString();
        string next = sort.Field == SortField.None ? "Sort files: Unsorted" :
            $"Sort files: {field}, {(sort.Descending ? "descending" : "ascending")}";
        if (_sortDescription == next) return;
        _sortDescription = next;
        OnPropertyChanged(nameof(SortDescription));
    }

    public int ColumnProfile
    {
        get => _columnProfile;
        set
        {
            _columnProfile = Math.Clamp(value, 0, Services.Columns.Count - 1);
            // The columns shown already (the same profile again, or a view with columns of its own, which keeps them until
            // the tab shows a folder): nothing to announce, so the list stays where it is scrolled, with its widths.
            if (ReferenceEquals(_shownColumns, Columns)) return;
            AnnounceColumns();
            RefreshSortDescription();
        }
    }

    public ColumnSpec[] Columns => Core.Search.ResultSetProvider.IsWorkingSetList(Location)
        ? ColumnProfiles.WorkingSetList
        : Location is { Scheme: Schemes.ResultSet } set && Services.ResultSets.Get(set) is { ShowsGroups: true }
            ? ColumnProfiles.Duplicates
            : Services.Columns.Get(_columnProfile, Location?.Scheme ?? Schemes.FileSystem);

    /// <summary>
    /// Whether the column profiles (Alt+0–9, Settings → Columns) set this view's columns: not in a view with a dedicated
    /// layout of its own (results, the Recycle Bin, the registry, recovery, hidden data, the change journal).
    /// </summary>
    public bool UsesColumnProfiles => !ColumnProfileSet.HasFixedLayout(Location?.Scheme ?? Schemes.FileSystem);

    /// <summary>Stores a dragged column width in the active profile (dedicated layouts keep it for this view only).</summary>
    public bool SetColumnWidth(int column, double width)
    {
        if (!UsesColumnProfiles) return false;
        Services.Columns.SetWidth(_columnProfile, column, width);
        return true;
    }

    private void OnProfilesChanged()
    {
        _columnProfile = Math.Clamp(_columnProfile, 0, Services.Columns.Count - 1);
        // Only a view whose own columns changed is told: one with columns of its own is not drawn from the profiles, and
        // an edit to another profile (or a border dragged in a panel on one) leaves this one where it is scrolled, with
        // the widths dragged in it.
        var columns = Columns;
        if (!UsesColumnProfiles || _shownColumns is { } shown && ColumnProfileSet.SameColumns(shown, columns)) return;
        // The same columns at other widths (a border dragged in this list or another one on the profile, a width set in
        // Settings) leave the list where it is scrolled; other columns start it from the first.
        AnnounceColumns(widthsOnly: _shownColumns is { } before && ColumnProfileSet.SameFields(before, columns));
        RefreshSortDescription();
    }

    /// <summary>The columns last announced (<see cref="ColumnsChanged"/>): what the list of this tab shows.</summary>
    private ColumnSpec[]? _shownColumns;

    private void AnnounceColumns(bool widthsOnly = false)
    {
        _shownColumns = Columns;
        ColumnsChanged?.Invoke(this, widthsOnly ? ColumnsChangedEventArgs.Widths : ColumnsChangedEventArgs.Other);
    }

    private string _tabQualifier = string.Empty;
    /// <summary>The shortest location cue needed to distinguish equal titles in this panel.</summary>
    internal string TabQualifier
    {
        get => _tabQualifier;
        set
        {
            if (_tabQualifier == value) return;
            _tabQualifier = value;
            OnPropertyChanged(nameof(TabHeader));
            OnPropertyChanged(nameof(TabAccessibleName));
        }
    }

    /// <summary>The compact, safely escaped tab-strip label.</summary>
    public string TabHeader => (IsLocked ? ReturnToRoot ? "⌂ " : "🔒 " : string.Empty) + Formatters.SafeName(Title) +
        (TabQualifier.Length == 0 ? string.Empty : " · " + Formatters.SafeName(TabQualifier));

    public string TabLockDescription => !IsLocked ? "Unlocked tab" : ReturnToRoot
        ? "Root lock: revisiting returns to " + Formatters.SafeName(LockedRoot?.Path ?? "the saved root")
        : "Navigation lock: opening another location creates a new tab";

    public string TabTooltip => Formatters.SafeName(DisplayPath) + "\n" + TabLockDescription;
    public string TabAccessibleName => TabHeader + ", " + TabLockDescription + ", " + Formatters.SafeName(DisplayPath);

    /// <summary>The path as shown beside the command line, escaped like <see cref="TabHeader"/>; <see cref="DisplayPath"/> stays the real one.</summary>
    public string ShownPath => Formatters.SafeName(DisplayPath);

    partial void OnDisplayPathChanged(string value)
    {
        OnPropertyChanged(nameof(ShownPath));
        OnPropertyChanged(nameof(TabTooltip));
        OnPropertyChanged(nameof(TabAccessibleName));
    }

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(TabHeader));
        OnPropertyChanged(nameof(TabAccessibleName));
    }

    partial void OnIsLockedChanged(bool value) => NotifyLockPresentation();
    partial void OnReturnToRootChanged(bool value) => NotifyLockPresentation();

    private void NotifyLockPresentation()
    {
        OnPropertyChanged(nameof(TabHeader));
        OnPropertyChanged(nameof(TabLockDescription));
        OnPropertyChanged(nameof(TabTooltip));
        OnPropertyChanged(nameof(TabAccessibleName));
    }

    partial void OnQuickSearchChanged(string? value)
    {
        OnPropertyChanged(nameof(IsQuickSearchActive));
        OnPropertyChanged(nameof(ShownQuickSearch));
    }

    // ---- Change watching (plan §8.2): only the visible tab of each panel watches its folder ------------------

    private ChangeMonitor? _monitor;

    /// <summary>How often the shown folder's change notifications overflowed (tests, diagnostics).</summary>
    internal int WatcherOverflows => _monitor?.Overflows ?? 0;

    /// <summary>Whether this tab watches its location now (only the active tab of a panel does; tests).</summary>
    internal bool IsWatching => _monitor is not null || _poller is not null || _registryMonitor is not null;
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
            Services.Metadata.ExpireRefreshable();
            StartWatching();
            RefreshIfFolderChanged();
            _freeSpaceTimer.Start();
            if (Listing.State == ListingState.Complete && !Listing.IsRefreshing) RequestFreeSpace();
        }
        else
        {
            StopWatching();
            _freeSpaceTimer.Stop();
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
        long intent = NavigationGeneration;
        _ = Task.Run(() =>
        {
            if (MainViewModel.DriveRootOf(gone) is not { } root || !Directory.Exists(root)) return null;
            for (string? dir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(gone.Path)); dir is not null; dir = Path.GetDirectoryName(dir))
                if (Directory.Exists(dir)) return dir;
            return null;
        }).ContinueWith(t => Services.Ui.Post(() =>
        {
            _leaving = false;
            if (!OwnsNavigation(intent) || Location != gone || !t.IsCompletedSuccessfully || t.Result is not { } parent) return;
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
    public void Navigate(Location location, string? focusName = null, bool record = true, bool refresh = false) =>
        Navigate(location, focusName, record, refresh, null);

    private void Navigate(Location location, string? focusName, bool record, bool refresh, HistoryJump? jump)
    {
        if (_disposed) return;
        long intent = BeginNavigationIntent();
        if (!OwnsNavigation(intent)) return;
        _loadingHistory = jump;
        // The destination's new tab must own preparation too (including its one password question).
        if (IsLocked && Location is not null && !ReturnToRoot && Services.Providers.IsRegistered(location.Scheme) &&
            !Services.Providers.For(location).IsSameLocation(location, Location))
        {
            Panel.OpenTab(location, focusName, activate: true);
            return;
        }
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
                if (!OwnsNavigation(intent)) return;
                var connection = new CancellationTokenSource();
                BeginPendingNavigation(connection, $"Connecting to {Formatters.SafeName(Services.Providers.Display(location))}…");
                _ = ConnectThenNavigateAsync(location, focusName, record, intent, connection, jump);
                return;
            }
        }
        // A location reached only after some work (a network share mounted first, D-54): that work, then the location it gives.
        if (Services.Providers.IsRegistered(location.Scheme))
        {
            var preparation = new CancellationTokenSource();
            Task<PreparedLocation>? preparing;
            NavigationReads? reads = null;
            try
            {
                reads = CaptureContainerReads(location);
                preparing = Services.Providers.For(location).PrepareNavigationAsync(location, preparation.Token);
                if (preparing is not null && reads is not null) { preparing = KeepContainerReadsAsync(preparing, reads); reads = null; }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                reads?.Dispose();
                preparation.Dispose();
                if (OwnsNavigation(intent))
                    Banner = $"{Formatters.SafeName(Services.Providers.Display(location))} could not be opened: {Formatters.SafeName(ex.Message)}";
                return;
            }
            if (preparing is not null)
            {
                if (!OwnsNavigation(intent))
                {
                    preparation.Cancel();
                    _ = PrepareThenNavigateAsync(location, preparing, focusName, record, intent, preparation, refresh, jump);
                    return;
                }
                BeginPendingNavigation(preparation, $"Opening {Formatters.SafeName(Services.Providers.Display(location))}…");
                _ = PrepareThenNavigateAsync(location, preparing, focusName, record, intent, preparation, refresh, jump);
                return;
            }
            preparation.Dispose();
            if (reads is not null)
            {
                var ready = new PreparedLocation(location, reads);
                bool retained = false;
                try { retained = NavigatePreparedLocation(location, focusName, record, intent, ready, refresh, jump); }
                finally { if (!retained) ready.Dispose(); }
                return;
            }
        }
        NavigatePreparedLocation(location, focusName, record, intent, null, refresh, jump);
    }

    private sealed class NavigationReads : IDisposable
    {
        internal List<IDisposable> Leases { get; } = [];
        public void Dispose()
        {
            foreach (var lease in Leases)
                try { lease.Dispose(); }
                catch (Exception error) { Core.Diagnostics.AppLog.Warn("Navigation read cleanup failed: " + error.GetType().Name); }
            Leases.Clear();
        }
    }

    private NavigationReads? CaptureContainerReads(Location location)
    {
        var reads = new NavigationReads();
        try
        {
            if (location.Container is { IsFileSystem: false } member && ProviderReadLease.Capture(Services.Providers, [member]) is { } lease)
                reads.Leases.Add(lease);
            return reads.Leases.Count == 0 ? null : reads;
        }
        catch { reads.Dispose(); throw; }
    }

    private static async Task<PreparedLocation> KeepContainerReadsAsync(Task<PreparedLocation> pending, NavigationReads reads)
    {
        try
        {
            var prepared = await pending;
            reads.Leases.Add(prepared);
            return new(prepared.Location, reads);
        }
        catch { reads.Dispose(); throw; }
    }

    private long BeginNavigationIntent()
    {
        RetireMetadataAnalysis();
        long intent = ++NavigationGeneration;
        Listing.CancelStagedNavigation();
        _loadingHistory = null;
        var old = _pendingNavigation;
        string? previousBanner = _bannerBeforePendingNavigation;
        _pendingNavigation = null;
        _bannerBeforePendingNavigation = null;
        old?.Cancel();
        if (OwnsNavigation(intent))
        {
            if (old is not null) Banner = previousBanner;
            OnPropertyChanged(nameof(NavigationGeneration));
        }
        return intent;
    }

    private bool OwnsNavigation(long intent, CancellationTokenSource? pending = null) =>
        !_disposed && NavigationGeneration == intent &&
        (pending is null || ReferenceEquals(_pendingNavigation, pending) && !pending.IsCancellationRequested);

    private void BeginPendingNavigation(CancellationTokenSource pending, string message)
    {
        _bannerBeforePendingNavigation = Banner;
        _pendingNavigation = pending;
        Banner = message;
    }

    private void FinishPendingNavigation(CancellationTokenSource pending)
    {
        if (ReferenceEquals(_pendingNavigation, pending))
        {
            _pendingNavigation = null;
            _bannerBeforePendingNavigation = null;
            _loadingHistory = null;
        }
        pending.Dispose();
    }

    /// <summary>Escape abandons a slow destination without changing the current listing or its history.</summary>
    public bool CancelPendingNavigation()
    {
        if (_disposed || _pendingNavigation is null) return false;
        BeginNavigationIntent();
        return true;
    }

    private bool NavigatePreparedLocation(Location location, string? focusName, bool record, long intent,
        PreparedLocation? prepared, bool refresh, HistoryJump? jump)
    {
        if (jump is null) return CommitNavigation(location, focusName, record, intent, prepared, refresh);
        if (!OwnsNavigation(intent) || jump.Revision != HistoryRevision) return false;
        var pending = new CancellationTokenSource();
        if (_pendingNavigation is null)
            BeginPendingNavigation(pending, $"Opening {Formatters.SafeName(Services.Providers.Display(location))}…");
        else
            _pendingNavigation = pending; // hand off from provider preparation, keeping the original banner
        _loadingHistory = jump;
        _ = LoadHistoryAsync(location, jump, intent, pending, prepared);
        return true;
    }

    private bool CommitNavigation(Location location, string? focusName, bool record, long intent,
        PreparedLocation? prepared = null, bool refresh = false, HistoryJump? jump = null, string? fallback = null)
    {
        if (!OwnsNavigation(intent)) return false;
        if (IsLocked && Location is not null && !ReturnToRoot && !Services.Providers.For(location).IsSameLocation(location, Location))
        {
            if (jump is null) Panel.OpenTab(location, focusName, activate: true);
            return false;
        }
        // The provider's equivalence may be conservative for safety checks. A visit keeps the exact address opened.
        bool historyChanged = jump is not null || record && Location is not null && Location != location;
        if (jump is not null)
            MoveHistory(jump);
        else if (historyChanged)
        {
            _back.Add(CaptureVisit());
            int excess = _back.Count - HistoryLimit;
            if (excess > 0) _back.RemoveRange(0, excess);
            _forward.Clear();
        }
        EndQuickSearch();
        ComparisonLabel = null;
        Banner = fallback ?? LocationBanner(location);
        FilterValidationError = null;
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
        _navigatedFocus = (location, focusName);
        _historyDetachedTop = null;
        if (jump is not null)
        {
            var visit = location == jump.Visit.Location ? jump.Visit : null;
            Listing.LoadStagedNavigation(location, visit?.Focus, visit?.Top);
        }
        else if (refresh && Location == location && Listing.State != Core.Listing.ListingState.Failed) Listing.Refresh();
        else Listing.Load(location, focusName);
        var previousPreparation = _preparedLocation;
        _preparedLocation = prepared;
        previousPreparation?.Dispose();
        if (IsActiveTab) StartWatching();
        if (jump is null) Services.RecordFolder(location);
        UpdateTitle();
        AnnounceColumns();
        if (historyChanged) NotifyHistoryChanged();
        return true;
    }

    /// <summary>The name the last navigation was to put the cursor on, and where: a link shown by its target keeps it.</summary>
    private (Location At, string? Name)? _navigatedFocus;

    /// <summary>
    /// The location is a link that refuses to be listed itself while the folder it leads to lists (Windows' compatibility
    /// junctions, such as "Local Settings", deny everyone that): the panel shows that folder and says why, as Salamander
    /// does, where Explorer stops at "Access is denied". It takes the link's place, so Back returns to where it was entered from.
    /// </summary>
    private void ShowLinkTarget(LinkListingRefusedException refused)
    {
        var link = Location;
        long intent = NavigationGeneration;
        string? focus = _navigatedFocus is { } f && f.At == link ? f.Name : null;
        // Posted: the listing is still reporting the refusal.
        Services.Ui.Post(() =>
        {
            if (!OwnsNavigation(intent) || link is null || Location != link || Listing.LoadError != refused) return;
            EndQuickSearch();
            ComparisonLabel = null;
            _navigatedFocus = (refused.Target, focus);
            Listing.Load(refused.Target, focus);
            if (IsActiveTab) StartWatching();
            Services.RecordFolder(refused.Target);
            UpdateTitle();
            AnnounceColumns();
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(refused.Link));
            Banner = $"“{name}” is a link that refuses to be listed itself, as Windows' compatibility junctions do. This is the folder it leads to.";
        });
    }

    /// <summary>Connects in the background (the server's key and password prompts appear meanwhile) to learn the home folder.</summary>
    private async Task PrepareThenNavigateAsync(Location requested, Task<PreparedLocation> preparing, string? focusName, bool record,
        long intent, CancellationTokenSource pending, bool refresh, HistoryJump? jump)
    {
        string what = Formatters.SafeName(Services.Providers.Display(requested));
        try
        {
            var ready = await preparing;
            bool retained = false;
            try
            {
                if (OwnsNavigation(intent, pending))
                    retained = NavigatePreparedLocation(ready.Location, focusName, record, intent, ready, refresh, jump);
            }
            finally { if (!retained) ready.Dispose(); }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (OwnsNavigation(intent, pending))
                Banner = ex is OperationCanceledException ? _bannerBeforePendingNavigation : $"{what} could not be opened: {Formatters.SafeName(ex.Message)}";
        }
        finally { FinishPendingNavigation(pending); }
    }

    private async Task ConnectThenNavigateAsync(Location location, string? focusName, bool record,
        long intent, CancellationTokenSource pending, HistoryJump? jump)
    {
        try
        {
            await Task.Run(() =>
            {
                using var lease = Services.SftpProvider.Lease(location, pending.Token);
            }, pending.Token);
            if (!OwnsNavigation(intent, pending)) return;
            string resolved = Services.SftpProvider.Resolve(location);
            if (OwnsNavigation(intent, pending) && !resolved.StartsWith('~'))
                NavigatePreparedLocation(location.WithPath(resolved), focusName, record, intent, null, false, jump);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (OwnsNavigation(intent, pending))
                Services.Ui.Post(() =>
                {
                    if (OwnsNavigation(intent)) Banner = $"Not connected: {(ex is OperationCanceledException ? "canceled." : Formatters.SafeName(ex.Message))}";
                });
        }
        finally { FinishPendingNavigation(pending); }
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
        if (_disposed) return;
        if (Location is { Scheme: Schemes.Archive } archive && Services.Archives.HasPasswordPreparation(archive))
        {
            Navigate(archive, record: false, refresh: true);
            return;
        }
        ComparisonLabel = null;
        if (Location is { IsFileSystem: true }) _forceFreeSpaceOnListingComplete = true;
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
        ApplyExplicitSort(s.Field == field ? s with { Descending = !s.Descending } : s with { Field = field, Descending = field is SortField.Modified or SortField.Size });
    }

    /// <summary>Raised before a user-requested sort so the list can reveal its focused item when that revision arrives.</summary>
    internal event Action<int>? ExplicitSortRequested;

    private void ApplyExplicitSort(SortSpec sort)
    {
        if (Listing.Sort == sort) return;
        if (sort.Field != SortField.Metadata || Listing.Sort.MetadataId != sort.MetadataId) RetireMetadataAnalysis(reportPartial: false);
        ExplicitSortRequested?.Invoke(Listing.ViewSpecVersion + 1);
        Listing.Sort = sort;
        RefreshSortDescription();
    }

    /// <returns>Whether the draft was applied. A rejected draft leaves the current listing view intact.</returns>
    public bool SetFilter(string? text)
    {
        Mask? mask = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!Mask.TryParse(text, out var parsed, out var error))
            {
                FilterValidationError = error;
                return false;
            }
            mask = parsed;
        }
        if (mask is null || mask.IsMatchAll)
        {
            // "*" and "*.*" show everything: no filter.
            Listing.Filter = null;
            FilterText = null;
            FilterValidationError = null;
            UpdateStatus();
            return true;
        }
        Listing.Filter = mask;
        FilterText = mask.Text;
        FilterValidationError = null;
        UpdateStatus();
        return true;
    }

    public void OnUserMovedFocus()
    {
        if (QuickSearch is not null) EndQuickSearch();
    }

    // ---- Listing notifications ---------------------------------------------------------------------------

    private void OnListingChanged(object? sender, ListingChange change)
    {
        if (!Listing.IsRefreshing && Listing.State != ListingState.Loading && !ReferenceEquals(_ownershipDemandStore, Listing.Store))
            _ownershipDemandStore = Listing.Store; // A failed/canceled refresh kept its old store.
        CheckMetadataAnalysis();
        RefreshSortDescription();
        if ((change & ListingChange.State) != 0 || (change & ListingChange.Reset) != 0)
        {
            if (Location != _freeSpaceTrackedLocation)
            {
                _freeSpaceTrackedLocation = Location;
                _freeSpaceRequestId++;
                _freeSpacePendingLocation = null;
                _freeSpaceRefreshAfterPending = false;
                _forceFreeSpaceOnListingComplete = false;
                _freeBytes = -1;
                _freeBytesLocation = null;
                _freeBytesVolume = null;
                _freeSpaceUnavailable = false;
            }
            IsLoading = Listing.State == ListingState.Loading || Listing.IsRefreshing;
            if (Listing.Issues.Count > 0) Banner = Listing.Issues[^1];
            if (Listing.State == ListingState.Complete)
            {
                bool forceCapacity = _forceFreeSpaceOnListingComplete;
                _forceFreeSpaceOnListingComplete = false;
                RequestFreeSpace(forceCapacity);
                OnLoadCompleted();
                NoteSidecars();
                if (Listing.LastRefreshError is DirectoryNotFoundException && Location is { IsFileSystem: true } gone) LeaveVanishedFolder(gone);
            }
            else if (Listing.State == ListingState.Failed && Listing.LoadError is LinkListingRefusedException refused)
            {
                ShowLinkTarget(refused);
            }
        }
        // The focused item is described with items marked too (the marked ones are summed on the left).
        if ((change & (ListingChange.Rows | ListingChange.Marks | ListingChange.State | ListingChange.Reset | ListingChange.Focus)) != 0) UpdateStatus();
        if ((change & (ListingChange.Rows | ListingChange.State | ListingChange.Reset)) != 0) NotifySupplementaryInventory();
        else if (IsSupplementaryData && (change & ListingChange.Focus) != 0) OnPropertyChanged(nameof(CanReadSupplementaryReport));
    }

    public void UpdateTitle()
    {
        if (Location is null) return;
        var provider = Services.Providers.For(Location);
        DisplayPath = provider.GetDisplayPath(Location);
        Title = provider.GetDisplayName(Location);
    }

    private void RequestFreeSpace(bool force = false)
    {
        if (_disposed || Location is not { IsFileSystem: true } loc) return;
        var device = Services.Providers.For(loc).GetDeviceKey(loc);
        _freeBytesLocation = loc;
        if (_freeSpacePendingLocation == loc)
        {
            if (force) { _freeSpaceRefreshAfterPending = true; _freeBytes = -1; }
            UpdateStatus();
            return;
        }
        if (!force && _freeBytes >= 0 && DateTime.UtcNow - _freeBytesCheckedUtc < CapacityTtl)
        {
            UpdateStatus();
            return;
        }
        _freeBytes = -1;
        _freeBytesVolume = null;
        _freeSpaceUnavailable = false;
        _freeSpacePendingLocation = loc;
        _freeSpacePendingSince = DateTime.UtcNow;
        long request = ++_freeSpaceRequestId;
        UpdateStatus();
        _ = Services.Io.Run(device, Core.Threading.IoPriority.Background, _ =>
            CapacityQuery(loc.Path)).ContinueWith(t =>
        {
            Services.Ui.Post(() =>
            {
                if (_disposed || Location != loc || _freeSpaceRequestId != request) return;
                _freeSpacePendingLocation = null;
                if (_freeSpaceRefreshAfterPending)
                {
                    _freeSpaceRefreshAfterPending = false;
                    RequestFreeSpace(force: true);
                    return;
                }
                var capacity = t.IsCompletedSuccessfully ? t.Result : null;
                _freeBytes = capacity?.AvailableBytes ?? -1;
                _freeBytesVolume = capacity?.VolumeIdentity;
                _freeBytesCheckedUtc = DateTime.UtcNow;
                _freeSpaceUnavailable = _freeBytes < 0;
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
        else if (!l.ShowHidden && l.State == ListingState.Complete)
        {
            // The leading totals count the whole folder. Explain why fewer rows are visible
            // after hiding dotfiles, Finder-hidden files or Windows hidden/system items.
            int hidden = l.TotalCount - Math.Max(0, l.VisibleCount - (l.HasParentRow ? 1 : 0));
            if (hidden > 0) rest.Add($"{Formatters.Plural(hidden, "hidden or system item", "hidden or system items")} not shown");
        }
        // Checksums remain in the status; free space has a stable place beside the mask above.
        if (_verificationSummary is { } verified && fileSystem) rest.Add(verified);
        // Free space belongs to folders on disk, not to archives, servers, or lists (the last value would linger there).
        // A navigation can enter Loading before RequestFreeSpace runs. Match the last queried location without
        // consulting mapped-drive identity on every focus, mark or listing-status refresh.
        bool currentFreeSpace = fileSystem && Location == _freeBytesLocation;
        FreeSpaceText = !currentFreeSpace ? string.Empty
            : _freeSpacePendingLocation == Location ? DateTime.UtcNow - _freeSpacePendingSince > TimeSpan.FromSeconds(10)
                ? "Free space still being checked…" : "Checking free space…"
            : _freeBytes >= 0 && DateTime.UtcNow - _freeBytesCheckedUtc < CapacityTtl
                ? $"{Formatters.SizeWithUnit(_freeBytes)} free"
            : _freeSpaceUnavailable ? "Free space unavailable"
            : "Free space needs refresh";

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
            bool lowerBound = stats.LowerBoundDirectories > 0;
            string size = Formatters.SizeWithUnit(stats.Bytes);
            if (lowerBound) size = "at least " + size;
            if (unsized == 0)
            {
                if (stats.Bytes > 0 || stats.Files > 0 || lowerBound) marked += " · " + size;
            }
            else if (SizingFolders > 0)
                marked += " · " + (stats.Bytes > 0 || lowerBound ? $"{size} so far, " : "") + $"counting {Formatters.Plural(unsized, "folder", "folders")}…";
            else if (stats.Bytes > 0 || stats.Files > 0 || lowerBound)
                marked += $" · {size}, not counting {Formatters.Plural(unsized, "folder", "folders")}";
            else
                marked += unsized == 1 ? " · size not counted" : " · sizes not counted";
            if (lowerBound) marked += $" · lower bound ({Formatters.Plural(stats.LowerBoundDirectories, "folder", "folders")} partially counted)";
            if (stats.HiddenByFilter > 0) marked += $" · {stats.HiddenByFilter:N0} of them hidden by the filter";
            StatusMarked = marked;
            StatusLeft = rest.Count > 0 ? " · " + string.Join(" · ", rest) : string.Empty;
            CanCountMarked = (unsized > 0 || lowerBound) && SizingFolders == 0 && fileSystem;
        }
        if (IsSupplementaryData) StatusLeft = l.SupplementaryInventory?.Description ?? "Inventory pending / not inspected";
        StatusRight = l.TryGetFocused(out var f) && f.Kind != EntryKind.Parent ? DescribeFocused(f) + FocusedVerification(f, l.FocusedStoreIndex) : string.Empty;
    }

    internal static string DescribeFocused(in EntryData e)
    {
        // A drive: what it is and how full ("C: SYSTEM · NTFS · 189 GB free of 952 GB, 80% used").
        if (e.Tag is Core.FileSystem.DriveTag drive)
        {
            var parts = new List<string> { Formatters.SafeName(string.IsNullOrEmpty(drive.Label) ? e.Name : $"{e.Name} {drive.Label}") };
            if (!drive.Ready) parts.Add("not ready");
            if (!string.IsNullOrEmpty(drive.Format)) parts.Add(Formatters.SafeName(drive.Format));
            if (drive.Ready && drive.TotalBytes > 0)
                parts.Add($"{Formatters.SizeWithUnit(drive.FreeBytes)} free of {Formatters.SizeWithUnit(drive.TotalBytes)}, {100.0 * (drive.TotalBytes - drive.FreeBytes) / drive.TotalBytes:0}% used");
            if (!string.IsNullOrEmpty(drive.RemoteName)) parts.Add(Formatters.SafeName(drive.RemoteName));
            return string.Join(" · ", parts);
        }
        // Escaped as the file list shows names (§18.3): a direction override must not turn the name around here either.
        if (e.IsContainer) return Formatters.SafeName(e.Name);
        return $"{Formatters.SafeName(e.Name)} · {Formatters.ExactSize(e.Size)}";
    }

    // ---- Cell helpers for special providers -------------------------------------------------------------

    public string GetFolderText(in EntryData e) => e.Tag switch
    {
        Core.Search.ResultTag r => r.Folder ?? r.RelativeFolder,
        _ => string.Empty,
    };

    public string GetKindText(in EntryData e) => EntryPresentation.Kind(e, Location);

    // ---- Metadata columns (plan §10) --------------------------------------------------------------------

    /// <summary>Store indices drawn in the last frame; queued metadata work for other rows is skipped.</summary>
    public volatile HashSet<int> VisibleStoreIndices = [];

    private CancellationTokenSource? _analysis;

    [ObservableProperty] private string? _analysisStatus;

    private bool IsSlowLocation => Location is { IsFileSystem: true } l && (PathUtil.IsUncPath(l.Path) || Services.Providers.For(l).GetDeviceKey(l).StartsWith(@"\\", StringComparison.Ordinal));

    public string GetMetadataText(in EntryData e, int storeIndex, string fieldId, out bool pending)
    {
        var cell = MetadataPresentation(e, storeIndex, fieldId, null, demand: true);
        pending = cell.State == Core.Metadata.MetadataState.Pending;
        return cell.Text;
    }

    /// <param name="analyzing">Every value is about to be computed (Analyze folder): no note that the order is partial.</param>
    public void SortByMetadata(string fieldId, bool analyzing = false)
    {
        if (analyzing && !CanAnalyzeMetadata(fieldId)) return;
        var s = Listing.Sort;
        bool same = s.Field == SortField.Metadata && s.MetadataId == fieldId;
        ApplyExplicitSort(s with { Field = SortField.Metadata, MetadataId = fieldId, Descending = same && !s.Descending });
        if (fieldId == ColumnSpec.FolderSortKey || analyzing) return; // every item's folder is known at once
        var field = Services.Metadata.Field(fieldId);
        Banner = PartialMetadataOrder(field?.Title ?? fieldId);
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
            PictureView = PictureView,
            FocusName = focus,
            BackHistory = _back.TakeLast(10).Select(visit => visit.Location).Where(l => (l.Scheme != Schemes.ResultSet || Core.Search.ResultSetProvider.IsPersistent(l)) &&
                                                         !(l.Scheme == Schemes.Recovery && FileCat.Recovery.RecoveryProvider.IsDevice(l))).ToList(),
        };
    }

    public void ApplyState(TabState s)
    {
        IsLocked = s.Locked;
        ReturnToRoot = s.ReturnToRoot;
        LockedRoot = s.LockedRoot;
        // Through the setter: a profile removed since the state was saved (or a damaged number) becomes the last one there
        // is, and a list already showing this tab (a reopened or duplicated tab) takes its columns.
        ColumnProfile = s.ColumnProfile;
        PictureView = s.PictureView;
        Listing.Sort = Listing.Sort with { Field = s.SortField, Descending = s.SortDescending };
        if (!string.IsNullOrEmpty(s.Filter)) SetFilter(s.Filter);
        _back.Clear();
        _back.AddRange(s.BackHistory.TakeLast(HistoryLimit).Select(location => new HistoryVisit(location, null, null)));
        _forward.Clear();
        NotifyHistoryChanged();
    }

    /// <summary>Raised once, as the tab is closed (work started for it, such as folder counts, stops).</summary>
    internal event Action? Closed;

    public void Dispose()
    {
        EndQuickSearch();
        _disposed = true;
        _freeSpaceTimer.Stop();
        NavigationGeneration++;
        _pendingNavigation?.Cancel();
        _pendingNavigation = null;
        _bannerBeforePendingNavigation = null;
        Closed?.Invoke();
        RetireMetadataAnalysis();
        Closed = null;
        StopWatching();
        Listing.Changed -= OnListingChanged;
        Listing.PreparingStore -= PreparingMetadataStore;
        Services.Columns.Changed -= OnProfilesChanged;
        Services.Metadata.ValuesChanged -= OnMetadataValuesChanged;
        Listing.Dispose();
        _preparedLocation?.Dispose();
        _preparedLocation = null;
    }

    public override string ToString() => Title;

    internal static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}

/// <summary>How the columns a tab shows changed (<see cref="TabViewModel.ColumnsChanged"/>).</summary>
public sealed class ColumnsChangedEventArgs : EventArgs
{
    private ColumnsChangedEventArgs(bool widthsOnly) => WidthsOnly = widthsOnly;

    /// <summary>Other columns: another location or profile, or columns added, removed or moved.</summary>
    public static ColumnsChangedEventArgs Other { get; } = new(false);

    /// <summary>The same columns at other widths: a border dragged, a width set in Settings.</summary>
    public static ColumnsChangedEventArgs Widths { get; } = new(true);

    /// <summary>Only widths changed: a list stays where it is scrolled.</summary>
    public bool WidthsOnly { get; }
}
