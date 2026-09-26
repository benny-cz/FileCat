using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using FileCat.Core.State;

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
    private (int Folders, int Files, long Bytes, int Generation) _totals = (0, 0, 0, -1);
    private long _freeBytes = -1;
    private string? _freeBytesDevice;

    public TabViewModel(AppServices services, PanelViewModel panel)
    {
        Services = services;
        Panel = panel;
        Listing = new ListingModel(services.Providers, services.Io, services.Ui)
        {
            ShowHidden = services.Settings.ShowHidden,
            Sort = new SortSpec(SortField.Name, false, !services.Settings.DirectoriesFirst, !services.Settings.NaturalSort),
        };
        Listing.Changed += OnListingChanged;
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
            _columnProfile = Math.Clamp(value, 0, ColumnProfiles.Defaults.Count - 1);
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ColumnSpec[] Columns => ColumnProfiles.Get(_columnProfile, Location?.Scheme ?? Schemes.FileSystem);

    public string TabHeader => (IsLocked ? "🔒 " : string.Empty) + Title;

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(TabHeader));

    partial void OnIsLockedChanged(bool value) => OnPropertyChanged(nameof(TabHeader));

    partial void OnQuickSearchChanged(string? value) => OnPropertyChanged(nameof(IsQuickSearchActive));

    // ---- Navigation ----------------------------------------------------------------------------------------

    /// <summary>
    /// Navigates this tab. A locked tab keeps its location: navigating away opens a new tab instead
    /// (Total Commander's locked tabs, plan §4.1).
    /// </summary>
    public void Navigate(Location location, string? focusName = null, bool record = true)
    {
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
        Services.RecordFolder(location);
        UpdateTitle();
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
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
        for (int n = 0; n < count; n++)
        {
            int i = forward ? (start + n) % count : ((start - n) % count + count) % count;
            ref var e = ref Listing.GetVisible(i);
            if (e.Kind == EntryKind.Parent) continue;
            bool ok = wildcard ? Wildcard.IsMatch(e.Name, pattern)
                : anywhere ? e.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                : e.Name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase);
            if (ok) return i;
        }
        return -1;
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
            if (Listing.State == ListingState.Complete) RequestFreeSpace();
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
                    _freeBytes = t.Result;
                    UpdateStatus();
                });
        }, TaskScheduler.Default);
    }

    public void UpdateStatus()
    {
        var l = Listing;
        if (_totals.Generation != l.Generation || l.State == ListingState.Loading || (l.IsRefreshing is false && _totals.Folders + _totals.Files != l.TotalCount))
        {
            int folders = 0, files = 0;
            long bytes = 0;
            var store = l.Store;
            int n = Math.Min(store.Count, l.TotalCount + (l.HasParentRow ? 1 : 0));
            for (int i = 0; i < n; i++)
            {
                ref var e = ref store.GetRef(i);
                if (e.Kind == EntryKind.Parent) continue;
                if (e.IsContainer) folders++;
                else
                {
                    files++;
                    if (e.Size > 0) bytes += e.Size;
                }
            }
            _totals = (folders, files, bytes, l.Generation);
        }
        var left = $"{Formatters.Plural(_totals.Folders, "folder", "folders")}, {Formatters.Plural(_totals.Files, "file", "files")}";
        if (_totals.Bytes > 0) left += $" · {Formatters.SizeWithUnit(_totals.Bytes)}";
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
        Listing.Changed -= OnListingChanged;
        Listing.Dispose();
    }

    public override string ToString() => Title;

    internal static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Provider payloads that contribute Kind/Details columns (Registry values, archive members).</summary>
public interface IDisplayDetails
{
    string KindText { get; }
    string DetailsText { get; }
}
