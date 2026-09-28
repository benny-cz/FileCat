using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FileCat.App.Controls;
using FileCat.App.Services;
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
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private bool _returnToRoot;
    [ObservableProperty] private string? _quickSearch;
    [ObservableProperty] private bool _quickSearchNoMatch;
    [ObservableProperty] private string? _comparisonLabel;
    [ObservableProperty] private string? _banner;
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

    public ColumnSpec[] Columns => Services.Columns.Get(_columnProfile, Location?.Scheme ?? Schemes.FileSystem);

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

    public string TabHeader => (IsLocked ? "🔒 " : string.Empty) + Title;

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(TabHeader));

    partial void OnIsLockedChanged(bool value) => OnPropertyChanged(nameof(TabHeader));

    partial void OnQuickSearchChanged(string? value) => OnPropertyChanged(nameof(IsQuickSearchActive));

    // ---- Change watching (plan §8.2): only the visible tab of each panel watches its folder ------------------

    private ChangeMonitor? _monitor;
    private RegistryChangeMonitor? _registryMonitor;
    private bool _registryDirty;
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
        var monitor = new ChangeMonitor(loc.Path, () => Services.Ui.Post(() =>
        {
            // The notification may arrive after the tab closed (the post outlives the watcher).
            if (!_disposed && Location == loc && Listing.State == ListingState.Complete && !Listing.IsRefreshing) Listing.Refresh();
        }));
        _monitor = monitor.IsActive ? monitor : null;
        if (!monitor.IsActive) monitor.Dispose();
    }

    private void StopWatching()
    {
        _monitor?.Dispose();
        _monitor = null;
        _registryMonitor?.Dispose();
        _registryMonitor = null;
        _registryDirty = false;
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
        if (Location is { IsFileSystem: true } loc)
        {
            var device = Services.Providers.For(loc).GetDeviceKey(loc);
            _ = Services.Io.Run(device, Core.Threading.IoPriority.Background, _ => Directory.GetLastWriteTimeUtc(loc.Path))
                .ContinueWith(t => { if (t.IsCompletedSuccessfully) _folderStampAtLoad = t.Result; }, TaskScheduler.Default);
        }
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
        Banner = null;
        Listing.Load(location, focusName);
        if (IsActiveTab) StartWatching();
        Services.RecordFolder(location);
        UpdateTitle();
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Connects in the background (the server's key and password prompts appear meanwhile) to learn the home folder.</summary>
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
        Listing.Load(target);
        if (IsActiveTab) StartWatching();
        UpdateTitle();
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
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
        if (string.IsNullOrWhiteSpace(text))
        {
            Listing.Filter = null;
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
            }
        }
        if ((change & (ListingChange.Rows | ListingChange.Marks | ListingChange.State | ListingChange.Reset)) != 0) UpdateStatus();
        else if ((change & ListingChange.Focus) != 0 && Listing.MarkedCount == 0) UpdateStatus();
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

    public void UpdateStatus()
    {
        var l = Listing;
        var totals = l.Store.Totals;
        var left = $"{Formatters.Plural(totals.Directories, "folder", "folders")}, {Formatters.Plural(totals.Files, "file", "files")}";
        if (totals.KnownFileBytes > 0) left += $" · {Formatters.SizeWithUnit(totals.KnownFileBytes)}";
        if (l.State == ListingState.Loading) left += " · loading…";
        else if (l.IsRefreshing) left += " · refreshing…";
        if (l.Filter is not null) left += $" · filter \"{l.Filter.Text}\" shows {Math.Max(0, l.VisibleCount - (l.HasParentRow ? 1 : 0))}";
        if (_freeBytes >= 0) left += $" · {Formatters.SizeWithUnit(_freeBytes)} free";
        StatusLeft = left;

        var stats = l.GetMarkStats();
        if (stats.Count == 0)
        {
            StatusRight = l.TryGetFocused(out var f) && f.Kind != EntryKind.Parent ? DescribeFocused(f) : string.Empty;
        }
        else
        {
            var s = $"Marked {Formatters.Plural(stats.Count, "item", "items")} · {Formatters.SizeWithUnit(stats.Bytes)}{(stats.SizesIncomplete ? "+" : "")}";
            if (stats.HiddenByFilter > 0) s += $" · {stats.HiddenByFilter} hidden by filter";
            if (l.State == ListingState.Loading) s += " · listing incomplete";
            StatusRight = s;
        }
    }

    private static string DescribeFocused(in EntryData e)
    {
        if (e.IsContainer) return e.Name;
        return $"{e.Name} · {Formatters.ExactSize(e.Size)}";
    }

    // ---- Cell helpers for special providers -------------------------------------------------------------

    public string GetFolderText(in EntryData e) => e.Tag switch
    {
        Core.Search.ResultTag r => r.RelativeFolder,
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

    public void SortByMetadata(string fieldId)
    {
        var s = Listing.Sort;
        bool same = s.Field == SortField.Metadata && s.MetadataId == fieldId;
        Listing.MetadataKeys ??= MetadataKey;
        Listing.Sort = s with { Field = SortField.Metadata, MetadataId = fieldId, Descending = same && !s.Descending };
        var field = Services.Metadata.Field(fieldId);
        Banner = $"Sorted by {field?.Title ?? fieldId} using the values computed so far; items without a value are listed last. Choose View > Analyze folder to compute every value.";
    }

    private IComparable? MetadataKey(EntryStore store, int storeIndex, string fieldId)
    {
        var loc = Location;
        if (loc is null || storeIndex >= store.Count) return null;
        var e = store[storeIndex];
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
            Location = Location is { Scheme: Schemes.ResultSet } ? null : Location,
            Locked = IsLocked,
            ReturnToRoot = ReturnToRoot,
            LockedRoot = LockedRoot,
            SortField = Listing.Sort.Field,
            SortDescending = Listing.Sort.Descending,
            Filter = Listing.Filter?.Text,
            ColumnProfile = ColumnProfile,
            FocusName = focus,
            BackHistory = _back.TakeLast(10).Where(l => l.Scheme != Schemes.ResultSet).ToList(),
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
        Listing.Dispose();
    }

    public override string ToString() => Title;

    internal static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
