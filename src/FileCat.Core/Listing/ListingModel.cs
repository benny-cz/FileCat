using System.Diagnostics;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using FileCat.Core.Threading;

namespace FileCat.Core.Listing;

public enum ListingState
{
    Empty,
    Loading,
    Complete,
    Failed,
}

[Flags]
public enum ListingChange
{
    None = 0,
    /// <summary>A different store/location: rows, marks, and focus all changed.</summary>
    Reset = 1,
    Rows = 2,
    Marks = 4,
    Focus = 8,
    State = 16,
}

public readonly record struct MarkStats(int Count, int Files, int Directories, long Bytes, int HiddenByFilter, bool SizesIncomplete);

/// <summary>
/// One tab's listing: streaming enumeration into an <see cref="EntryStore"/>, background sorting and
/// filtering, and identity-based focus and marks (plan §4.3, §6.3, §10).
/// All public members are UI-thread affine. Every enumeration has a generation; results from an older
/// generation never touch the current one (AI-03).
/// </summary>
public sealed class ListingModel : IDisposable
{
    private static readonly TimeSpan RefreshSwapDelay = TimeSpan.FromMilliseconds(400);

    private readonly ProviderRegistry _providers;
    private readonly DeviceIoScheduler _io;
    private readonly IUiDispatcher _ui;
    private readonly string? _scratchDirectory;
    private readonly long _listingMemoryBudgetBytes;
    private readonly IndexMemoryBudget _indexBudget;

    private EntryStore _store = new();
    private int[] _visible = [];
    private DiskView? _diskView;
    private int[] _positions = [];
    private MarkSet _marks = new();
    private MarkStats? _statsCache;
    private int _appliedCount;
    private Pipeline? _pipeline;
    private Pipeline? _pendingRefresh;
    /// <summary>A refresh was asked for while one was under way: it runs when that one has finished.</summary>
    private bool _refreshAgain;
    /// <summary>The pipeline that began as a refresh and has not finished yet (it becomes the main one when it swaps in).</summary>
    private Pipeline? _refreshing;
    private int _generation;
    private int _specVersion;
    private SortSpec _sort = new();
    private Mask? _filter;
    private bool _showHidden = true;
    private int _focusStore = -1;
    private int _focusVisibleHint;
    /// <summary>The user (or a requested name) placed the cursor; until then it stays on the first row while entries stream in.</summary>
    private bool _focusAnchored;
    private string? _pendingFocusName;
    private EntryKind? _pendingFocusKind;
    private Dictionary<EntryKind, HashSet<string>>? _pendingMarks;
    /// <summary>
    /// Folder sizes computed here (Space), by name, with the folder's own time then. A refresh (a change the folder
    /// watcher saw, Ctrl+R) carries each over while that time is unchanged; a change in the folder makes it stale.
    /// </summary>
    private Dictionary<string, (long Size, long Modified)> _computedSizes = new(StringComparer.Ordinal);
    private Dictionary<string, (long Size, long Modified)>? _pendingSizes;
    /// <summary>A pending size measured while its folder was being listed again: it applies whatever the folder's time.</summary>
    private const long MeasuredDuringRefresh = long.MinValue;
    /// <summary>Entries are still arriving (a load, or a refresh after it replaced the rows) and names may not be found yet.</summary>
    private bool _awaitingEntries;
    private readonly List<string> _issues = [];
    private bool _disposed;

    public ListingModel(ProviderRegistry providers, DeviceIoScheduler io, IUiDispatcher ui,
        string? scratchDirectory = null, long listingMemoryBudgetBytes = 96L * 1024 * 1024,
        IndexMemoryBudget? indexBudget = null)
    {
        _providers = providers;
        _io = io;
        _ui = ui;
        _scratchDirectory = scratchDirectory;
        _listingMemoryBudgetBytes = listingMemoryBudgetBytes;
        _indexBudget = indexBudget ?? new IndexMemoryBudget();
    }

    public event EventHandler<ListingChange>? Changed;

    public Location? Location { get; private set; }
    public ResourceProvider? Provider { get; private set; }
    public ListingState State { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<string> Issues => _issues;

    /// <summary>Why the last reread failed (its rows were kept), or null; the folder may have gone, say.</summary>
    public Exception? LastRefreshError { get; private set; }
    public bool HasParentRow { get; private set; }
    public int Generation => _generation;
    public EntryStore Store => _store;
    public int VisibleCount => _diskView?.Count ?? _visible.Length;
    public bool HasExternalIndex => _diskView is not null;
    public long ExternalIndexBytes => (_diskView?.Bytes ?? 0) + (_pipeline?.ExternalSortBytes ?? 0) + (_pendingRefresh?.ExternalSortBytes ?? 0);
    public int TotalCount => Math.Max(0, _appliedCount - (HasParentRow ? 1 : 0));
    public DateTime? CompletedAtUtc { get; private set; }
    public TimeSpan LastLoadDuration { get; private set; }
    public bool IsRefreshing => _pendingRefresh is not null;

    /// <summary>Closed with its tab: nothing may read it any more.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>What the provider says a long load or refresh is doing, or null.</summary>
    public string? LoadingProgress => State == ListingState.Loading || IsRefreshing ? (_pendingRefresh ?? _pipeline)?.Progress : null;

    /// <summary>Metadata sort keys (set by the view layer); used only when sorting by a metadata field.</summary>
    public MetadataKeyProvider? MetadataKeys { get; set; }

    /// <summary>Re-sorts with current keys, e.g. after an explicit metadata analysis completed.</summary>
    public void Resort() => PushSpec();

    /// <summary>Selections with more items than this are captured as a <see cref="SelectionSnapshot"/>.</summary>
    public int SnapshotThreshold { get; set; } = 4096;

    /// <summary>Items of the last operation started from this listing (restore selection, Num /).</summary>
    private IReadOnlyList<ItemRef>? _lastOperation;

    public bool HasLastOperation => _lastOperation is not null;

    public void RememberOperation(IReadOnlyList<ItemRef> items) => _lastOperation = items.Count > 0 ? items : null;

    public SortSpec Sort
    {
        get => _sort;
        set
        {
            if (_sort == value) return;
            _sort = value;
            PushSpec();
        }
    }

    public Mask? Filter
    {
        get => _filter;
        set
        {
            if (ReferenceEquals(_filter, value)) return;
            _filter = value is { IsMatchAll: true } ? null : value;
            PushSpec();
        }
    }

    public bool ShowHidden
    {
        get => _showHidden;
        set
        {
            if (_showHidden == value) return;
            _showHidden = value;
            PushSpec();
        }
    }

    // ---- Row access ------------------------------------------------------------------------------------

    private int VisibleAt(int index) => _diskView?.GetStoreIndex(index) ?? _visible[index];
    private IEnumerable<int> EnumerateVisible() => _diskView?.Enumerate() ?? _visible;

    public int GetStoreIndex(int visibleIndex) => VisibleAt(visibleIndex);

    public EntryData GetVisible(int visibleIndex) => _store[VisibleAt(visibleIndex)];

    /// <summary>Visible position of a store index, or -1 when filtered out or not yet applied.</summary>
    public int GetVisibleIndex(int storeIndex) =>
        _diskView?.GetVisibleIndex(storeIndex) ??
        ((uint)storeIndex < (uint)_positions.Length ? _positions[storeIndex] : -1);

    public ItemRef GetItemRef(int storeIndex) => Provider!.GetItemRef(Location!, _store[storeIndex]);

    // Store entries never change identity, so a found index stays valid for the store's lifetime. Sizing progress
    // looks the same name up many times a second.
    private readonly Dictionary<string, int> _nameIndex = new(StringComparer.Ordinal);
    private EntryStore? _nameIndexStore;

    public int FindStoreIndex(string name)
    {
        if (!ReferenceEquals(_nameIndexStore, _store))
        {
            _nameIndex.Clear();
            _nameIndexStore = _store;
        }
        if (_nameIndex.TryGetValue(name, out int known) && known < _appliedCount) return known;
        using var scan = new EntryStore.Scan(_store, _appliedCount);
        for (int i = 0; i < _appliedCount; i++)
        {
            var e = scan[i];
            if (e.Kind == EntryKind.Parent || !e.Name.SequenceEqual(name)) continue;
            if (_nameIndex.Count >= 256) _nameIndex.Clear();
            _nameIndex[name] = i;
            return i;
        }
        return -1;
    }

    /// <summary>The one entry whose name differs from <paramref name="name"/> only in letter case; -1 when none or several do.</summary>
    public int FindStoreIndexIgnoringCase(string name)
    {
        int found = -1;
        using var scan = new EntryStore.Scan(_store, _appliedCount);
        for (int i = 0; i < _appliedCount; i++)
        {
            var e = scan[i];
            if (e.Kind == EntryKind.Parent || !e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (found >= 0) return -1;
            found = i;
        }
        return found;
    }

    /// <summary>Tests a name in place (spilled listings are searched without building strings).</summary>
    public delegate bool NameMatch(ReadOnlySpan<char> name);

    /// <summary>
    /// The first visible row from <paramref name="start"/> onward (wrapping, in either direction) whose name matches,
    /// or -1. The parent row never matches.
    /// </summary>
    public int FindVisible(int start, bool forward, NameMatch match)
    {
        int count = VisibleCount;
        if (count == 0) return -1;
        using var scan = new EntryStore.Scan(_store, _appliedCount);
        for (int n = 0; n < count; n++)
        {
            int row = forward ? (start + n) % count : ((start - n) % count + count) % count;
            if (row < 0) row += count;
            var e = scan[VisibleAt(row)];
            if (e.Kind != EntryKind.Parent && match(e.Name)) return row;
        }
        return -1;
    }

    // ---- Focus -----------------------------------------------------------------------------------------

    public int FocusedIndex => _focusStore >= 0 ? Math.Max(GetVisibleIndex(_focusStore), -1) : -1;

    public int FocusedStoreIndex => _focusStore;

    public bool TryGetFocused(out EntryData entry)
    {
        int vi = FocusedIndex;
        if (vi >= 0)
        {
            entry = GetVisible(vi);
            return true;
        }
        entry = default;
        return false;
    }

    public void SetFocus(int visibleIndex)
    {
        if (VisibleCount == 0) return;
        visibleIndex = Math.Clamp(visibleIndex, 0, VisibleCount - 1);
        int store = VisibleAt(visibleIndex);
        _pendingFocusName = null;
        _pendingFocusKind = null;
        _focusAnchored = true;
        if (store == _focusStore) return;
        _focusStore = store;
        _focusVisibleHint = visibleIndex;
        Raise(ListingChange.Focus);
    }

    public bool FocusName(string name)
    {
        int si = FindStoreIndex(name);
        if (si < 0 || GetVisibleIndex(si) < 0) return false;
        SetFocus(GetVisibleIndex(si));
        return true;
    }

    // ---- Loading -----------------------------------------------------------------------------------------

    /// <summary>Navigates to a location. Marks are cleared; <paramref name="focusName"/> is focused when it arrives.</summary>
    public void Load(Location location, string? focusName = null)
    {
        // First: a location nothing can list leaves the listing as it was, not its store released under it.
        var provider = _providers.For(location);
        CancelPipelines();
        Location = location;
        Provider = provider;
        _store = CreateStore(location);
        HasParentRow = provider.GetParent(location) is not null;
        if (HasParentRow) _store.Append(new EntryData("..", EntryKind.Parent));
        _visible = HasParentRow ? [0] : [];
        _positions = HasParentRow ? [0] : [];
        _appliedCount = _store.Count;
        _marks = new MarkSet();
        _statsCache = null;
        _issues.Clear();
        LastRefreshError = null;
        _focusStore = HasParentRow ? 0 : -1;
        _focusVisibleHint = 0;
        _focusAnchored = false;
        _pendingFocusName = focusName;
        _pendingFocusKind = null;
        _pendingMarks = null;
        _computedSizes = new(StringComparer.Ordinal);
        _pendingSizes = null;
        _awaitingEntries = true;
        _lastOperation = null;
        Error = null;
        State = ListingState.Loading;
        CompletedAtUtc = null;
        _pipeline = StartPipeline(location, provider, _store, isRefresh: false);
        Raise(ListingChange.Reset | ListingChange.State);
    }

    /// <summary>
    /// Re-enumerates the current location. The old rows stay visible until the new enumeration finishes
    /// (or briefly stalls); marks and focus then transfer by name. Vanished items are not substituted.
    /// </summary>
    public void Refresh()
    {
        if (Location is null || Provider is null || _disposed) return;
        if (_pendingRefresh is not null)
        {
            // The refresh under way may have read the location before the change this one is for: run again after it
            // (once, however many ask meanwhile), rather than dropping the request and keeping stale rows.
            _refreshAgain = true;
            return;
        }
        if (State == ListingState.Loading && _pendingRefresh is null)
        {
            // Loading again keeps the name still awaited (going up focuses the folder just left), or the row the user moved to.
            Load(Location, _pendingFocusName ?? (_focusAnchored && TryGetFocused(out var f) && f.Kind != EntryKind.Parent ? f.Name : null));
            return;
        }
        var store = CreateStore(Location);
        if (HasParentRow) store.Append(new EntryData("..", EntryKind.Parent));
        _pendingRefresh = _refreshing = StartPipeline(Location, Provider, store, isRefresh: true);
        Raise(ListingChange.State);
    }

    /// <summary>After a refresh has finished (or failed): the one asked for meanwhile, if any.</summary>
    private void RefreshFinished(Pipeline p)
    {
        if (!ReferenceEquals(p, _refreshing)) return;
        _refreshing = null;
        if (!_refreshAgain) return;
        _refreshAgain = false;
        // Only for the listing it was asked for: a navigation meanwhile loads its own location afresh.
        var current = _pipeline;
        _ui.Post(() =>
        {
            if (ReferenceEquals(current, _pipeline)) Refresh();
        });
    }

    public void CancelLoading()
    {
        if (State != ListingState.Loading && _pendingRefresh is null) return;
        _pipeline?.Cts.Cancel();
        _pendingRefresh?.Retire();
        _pendingRefresh = null;
        _refreshing = null;
        _refreshAgain = false;
        if (State == ListingState.Loading)
        {
            State = ListingState.Complete;
            _issues.Add("Listing was stopped before it finished; only part of this location is shown.");
        }
        Raise(ListingChange.State);
    }

    private EntryStore CreateStore(Location location) =>
        location.IsFileSystem && _scratchDirectory is not null
            ? new EntryStore(_scratchDirectory, _listingMemoryBudgetBytes)
            : new EntryStore();

    private Pipeline StartPipeline(Location location, ResourceProvider provider, EntryStore store, bool isRefresh)
    {
        var p = new Pipeline(++_generation, location, provider, store, isRefresh, CurrentSpec(), _ui, this);
        var device = provider.GetDeviceKey(location);
        p.LoadTask = _io.Run(device, IoPriority.Interactive, ct =>
        {
            provider.EnumerateAsync(location, p.Sink, ct).GetAwaiter().GetResult();
        }, p.Cts.Token);
        p.LoadTask.ContinueWith(t =>
        {
            p.LoadError = t.IsFaulted ? t.Exception!.GetBaseException() : null;
            p.LoadCanceled = t.IsCanceled;
            p.LoadDone = true;
            p.Signal();
        }, TaskScheduler.Default);
        p.Run();
        return p;
    }

    private void CancelPipelines()
    {
        if (_pipeline is null) _store.Dispose();
        _diskView?.Dispose();
        _diskView = null;
        _pipeline?.Retire();
        _pendingRefresh?.Retire();
        _pipeline = null;
        _pendingRefresh = null;
        // A new load reads the location afresh: nothing is owed to the requests made before it.
        _refreshing = null;
        _refreshAgain = false;
    }

    private ViewSpec CurrentSpec() => new(_sort, _filter, _showHidden, _specVersion);

    private void PushSpec()
    {
        _specVersion++;
        var spec = CurrentSpec();
        _pipeline?.UpdateSpec(spec);
        _pendingRefresh?.UpdateSpec(spec);
    }

    // ---- Pipeline results (UI thread) --------------------------------------------------------------------

    private void OnPipelineResult(Pipeline p, PipelineResult r)
    {
        if (_disposed) { r.External?.Dispose(); return; }
        if (r.ProgressOnly)
        {
            if (ReferenceEquals(p, _pipeline) || ReferenceEquals(p, _pendingRefresh)) Raise(ListingChange.State);
            return;
        }
        if (ReferenceEquals(p, _pendingRefresh))
        {
            if (!r.Done && Stopwatch.GetElapsedTime(p.StartedTimestamp) < RefreshSwapDelay)
            {
                r.External?.Dispose();
                return;
            }
            if (r.Done && r.Error is not null)
            {
                // Refresh failed: keep the old rows and report instead of replacing them with an error view.
                _pendingRefresh = null;
                r.External?.Dispose();
                p.Retire();
                _issues.Add($"Refresh failed: {r.Error.Message}");
                LastRefreshError = r.Error;
                Raise(ListingChange.State);
                RefreshFinished(p);
                return;
            }
            SwapToRefresh(p);
        }
        if (!ReferenceEquals(p, _pipeline)) { r.External?.Dispose(); return; }
        if (r.PreserveCurrent)
        {
            r.External?.Dispose();
            _issues.Add($"Could not update listing view: {DescribeError(r.Error!)}");
            Raise(ListingChange.State);
            if (r.Done) RefreshFinished(p);
            return;
        }
        ApplyResult(p, r);
        if (r.Done) RefreshFinished(p);
    }

    private void SwapToRefresh(Pipeline p)
    {
        // Carry marks and focus by exact name into the new generation.
        var marked = new Dictionary<EntryKind, HashSet<string>>();
        using (var scan = new EntryStore.Scan(_store, _appliedCount, _marks.Count))
        {
            foreach (int i in _marks.Enumerate())
            {
                if (i < _appliedCount)
                {
                    var entry = scan[i];
                    if (!marked.TryGetValue(entry.Kind, out var names)) marked[entry.Kind] = names = new HashSet<string>(StringComparer.Ordinal);
                    names.Add(entry.Name.ToString());
                }
            }
        }
        string? focusName = null;
        EntryKind? focusKind = null;
        if (_focusAnchored && _focusStore >= 0 && _focusStore < _appliedCount)
        {
            var focused = _store[_focusStore];
            focusName = focused.Name;
            focusKind = focused.Kind;
        }
        _pipeline?.Retire();
        _pipeline = p;
        _pendingRefresh = null;
        _store = p.Store;
        _diskView?.Dispose();
        _diskView = null;
        _visible = [];
        _positions = [];
        _marks = new MarkSet();
        _statsCache = null;
        _appliedCount = 0;
        _issues.Clear();
        LastRefreshError = null;
        _pendingMarks = marked.Count > 0 ? marked : null;
        _pendingSizes = _computedSizes.Count > 0 ? _computedSizes : null;
        _computedSizes = new(StringComparer.Ordinal);
        _awaitingEntries = true;
        _pendingFocusName = focusName;
        _pendingFocusKind = focusKind;
        _focusStore = -1;
    }

    private void ApplyResult(Pipeline p, PipelineResult r)
    {
        int oldFocusVisible = FocusedIndex >= 0 ? FocusedIndex : _focusVisibleHint;
        int previousCount = _appliedCount;
        _diskView?.Dispose();
        _diskView = r.External;
        _visible = r.Visible;
        _appliedCount = r.Count;
        _positions = r.External is null && _visible.Length > 0 ? BuildPositions(_visible, r.Count) : [];
        if (r.External is not null) p.ReleaseIndexReservation();
        var change = ListingChange.Rows;

        // Resolve names that were waiting for their entry to arrive.
        List<(int Index, long Size)>? sized = null;
        if (_pendingMarks is not null || _pendingFocusName is not null || _pendingSizes is not null)
        {
            using var scan = new EntryStore.Scan(_store, r.Count, r.Count - previousCount);
            for (int i = previousCount; i < r.Count; i++)
            {
                var e = scan[i];
                if (e.Kind == EntryKind.Parent) continue;
                // A folder whose own time is unchanged keeps the size computed for it before the refresh.
                if (_pendingSizes is not null && e.Kind == EntryKind.Directory && _pendingSizes.GetAlternateLookup<ReadOnlySpan<char>>().Remove(e.Name, out _, out var kept))
                {
                    if (kept.Modified == e.Modified || kept.Modified == MeasuredDuringRefresh) (sized ??= []).Add((i, kept.Size));
                }
                if (RemovePendingMark(e.Kind, e.Name))
                {
                    _marks.Set(i, true);
                    change |= ListingChange.Marks;
                }
                if (_pendingFocusName is not null && (_pendingFocusKind is null || _pendingFocusKind == e.Kind) && e.Name.SequenceEqual(_pendingFocusName))
                {
                    _focusStore = i;
                    _pendingFocusName = null;
                    _pendingFocusKind = null;
                    _focusAnchored = true;
                    change |= ListingChange.Focus;
                }
            }
        }
        if (sized is not null)
        {
            foreach (var (index, size) in sized)
            {
                var entry = _store[index];
                entry.Size = size;
                entry.Flags |= EntryFlags.SizeComputed;
                _store.Update(index, entry);
                _computedSizes[entry.Name] = (size, entry.Modified);
            }
            change |= ListingChange.Rows;
        }

        if (!_focusAnchored && _pendingFocusName is null)
        {
            // Entries stream in and re-sort: an untouched cursor stays on the first row, not on whichever entry
            // happened to be first in the first batch.
            if (VisibleCount > 0 && VisibleAt(0) != _focusStore)
            {
                _focusStore = VisibleAt(0);
                change |= ListingChange.Focus;
            }
        }
        else if (_focusStore < 0 || GetVisibleIndex(_focusStore) < 0)
        {
            // Fall back to the previous position; a pending focus name may still move focus when it arrives.
            if (VisibleCount > 0)
            {
                int vi = Math.Clamp(oldFocusVisible, 0, VisibleCount - 1);
                _focusStore = VisibleAt(vi);
                change |= ListingChange.Focus;
            }
        }
        _focusVisibleHint = Math.Max(0, FocusedIndex);
        _statsCache = null;

        if (r.Completion)
        {
            _pendingMarks = null;
            _pendingSizes = null;
            _awaitingEntries = false;
            _pendingFocusName = null;
            _pendingFocusKind = null;
            LastLoadDuration = Stopwatch.GetElapsedTime(p.StartedTimestamp);
            Diagnostics.FileCatEventSource.Log.ListingCompleted(TotalCount, LastLoadDuration.TotalMilliseconds);
            foreach (var issue in p.DrainIssues()) _issues.Add(issue);
            if (r.Error is not null)
            {
                Error = DescribeError(r.Error);
                State = VisibleCount > (HasParentRow ? 1 : 0) ? ListingState.Complete : ListingState.Failed;
                if (State == ListingState.Complete) _issues.Add($"Listing incomplete: {Error}");
            }
            else if (r.Canceled)
            {
                State = ListingState.Complete;
                _issues.Add("Listing was stopped before it finished; only part of this location is shown.");
            }
            else
            {
                State = ListingState.Complete;
            }
            CompletedAtUtc = DateTime.UtcNow;
            change |= ListingChange.State;
        }
        Raise(change);
    }

    private bool RemovePendingMark(EntryKind kind, ReadOnlySpan<char> name) =>
        _pendingMarks is not null && _pendingMarks.TryGetValue(kind, out var names) &&
        names.GetAlternateLookup<ReadOnlySpan<char>>().Remove(name);

    private static int[] BuildPositions(int[] visible, int count)
    {
        var pos = new int[count];
        Array.Fill(pos, -1);
        for (int i = 0; i < visible.Length; i++) pos[visible[i]] = i;
        return pos;
    }

    public static string DescribeError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Access is denied.",
        DirectoryNotFoundException => "The location does not exist or is no longer available.",
        FileNotFoundException => "The location does not exist.",
        PathTooLongException => "The path is too long.",
        IOException io => io.Message,
        _ => ex.Message,
    };

    // ---- Marks ---------------------------------------------------------------------------------------------

    public bool IsMarked(int storeIndex) => _marks.Get(storeIndex);

    public bool IsVisibleMarked(int visibleIndex) => _marks.Get(VisibleAt(visibleIndex));

    public int MarkedCount => _marks.Count;

    public void ToggleMark(int visibleIndex) => SetMark(visibleIndex, !IsVisibleMarked(visibleIndex));

    public void SetMark(int visibleIndex, bool value)
    {
        if ((uint)visibleIndex >= (uint)VisibleCount) return;
        int si = VisibleAt(visibleIndex);
        if (_store[si].Kind == EntryKind.Parent) return;
        if (_marks.Set(si, value)) MarksChanged();
    }

    public void SetMarkRange(int fromVisible, int toVisible, bool value)
    {
        if (VisibleCount == 0) return;
        int a = Math.Clamp(Math.Min(fromVisible, toVisible), 0, VisibleCount - 1);
        int b = Math.Clamp(Math.Max(fromVisible, toVisible), 0, VisibleCount - 1);
        bool changed = false;
        using var scan = new EntryStore.Scan(_store, _appliedCount, b - a + 1);
        for (int i = a; i <= b; i++)
        {
            int si = VisibleAt(i);
            if (scan[si].Kind != EntryKind.Parent) changed |= _marks.Set(si, value);
        }
        if (changed) MarksChanged();
    }

    /// <summary>Marks every visible item (the enumerated scope only while loading is incomplete).</summary>
    public void MarkAll(bool value, bool includeDirectories = true) =>
        ApplyToVisible(e => includeDirectories || !e.IsContainer, value);

    public void UnmarkEverything()
    {
        if (_marks.Count == 0) return;
        _marks.Clear();
        MarksChanged();
    }

    public void InvertMarks(bool includeDirectories)
    {
        bool changed = false;
        using var scan = new EntryStore.Scan(_store, _appliedCount);
        foreach (int si in EnumerateVisible())
        {
            var e = scan[si];
            if (e.Kind == EntryKind.Parent || !includeDirectories && e.IsContainer) continue;
            changed |= _marks.Set(si, !_marks.Get(si));
        }
        if (changed) MarksChanged();
    }

    public int MarkByMask(Mask mask, bool value, bool includeDirectories)
    {
        int affected = 0;
        bool changed = false;
        using var scan = new EntryStore.Scan(_store, _appliedCount);
        foreach (int si in EnumerateVisible())
        {
            var e = scan[si];
            if (e.Kind == EntryKind.Parent) continue;
            bool isDir = e.IsContainer;
            if (isDir && !includeDirectories && !MaskTargetsDirectories(mask)) continue;
            if (!mask.IsMatch(e.Name, isDir)) continue;
            affected++;
            changed |= _marks.Set(si, value);
        }
        if (changed) MarksChanged();
        return affected;
    }

    private static bool MaskTargetsDirectories(Mask mask) => mask.Text.TrimEnd().EndsWith('\\') || mask.Text.TrimEnd().EndsWith('/');

    public void MarkSameExtension(bool value)
    {
        if (!TryGetFocused(out var f) || f.IsContainer) return;
        var ext = NameParts.GetExtension(f.Name);
        ApplyToVisible(e => !e.IsContainer && NameParts.GetExtension(e.Name).Equals(ext, StringComparison.OrdinalIgnoreCase), value);
    }

    public void MarkSameName(bool value)
    {
        if (!TryGetFocused(out var f)) return;
        var stem = f.IsContainer ? f.Name : NameParts.GetStem(f.Name);
        ApplyToVisible(e => (e.IsContainer ? e.Name : NameParts.GetStem(e.Name)).Equals(stem, StringComparison.OrdinalIgnoreCase), value);
    }

    /// <summary>Marks or unmarks the entries with exactly these names (compare results).</summary>
    public void MarkNames(IEnumerable<string> names, bool value)
    {
        var set = names is HashSet<string> h && h.Comparer.Equals(StringComparer.Ordinal) ? h : new HashSet<string>(names, StringComparer.Ordinal);
        if (set.Count == 0) return;
        var lookup = set.GetAlternateLookup<ReadOnlySpan<char>>();
        bool changed = false;
        using var scan = new EntryStore.Scan(_store, _appliedCount);
        for (int i = 0; i < _appliedCount; i++)
        {
            var e = scan[i];
            if (e.Kind != EntryKind.Parent && lookup.Contains(e.Name)) changed |= _marks.Set(i, value);
        }
        if (changed) MarksChanged();
    }

    /// <summary>Restores the set used by the previous operation (Num /). False when it is no longer available.</summary>
    public bool RestoreSelection()
    {
        if (_lastOperation is not { } items) return false;
        try
        {
            MarkItems(items, Enumerable.Range(0, items.Count), true);
            return true;
        }
        catch (ObjectDisposedException)
        {
            // A captured selection of an earlier generation was released; its names are gone with it.
            _lastOperation = null;
            return false;
        }
    }

    /// <summary>
    /// Marks or unmarks the items at <paramref name="positions"/> of an operation's sources (for example the roots
    /// a job completed). A capture of the current generation maps by store index; otherwise items match by identity.
    /// </summary>
    public void MarkItems(IReadOnlyList<ItemRef> sources, IEnumerable<int> positions, bool value)
    {
        bool changed = false;
        if (sources is SelectionSnapshot s && ReferenceEquals(s.Store, _store))
        {
            foreach (int p in positions)
            {
                int si = s.GetStoreIndex(p);
                if (si < _appliedCount) changed |= _marks.Set(si, value);
            }
        }
        else if (Provider is { } provider && Location is { } location)
        {
            // Names first: an entry is compared by full identity only when its name is wanted, so the scan builds no
            // item per entry. A snapshot of one folder listing supplies its names through bulk reads of its store,
            // and in a file-system folder the name is the identity.
            var names = new HashSet<string>(StringComparer.Ordinal);
            HashSet<ItemRef>? wanted = null;
            if (sources is SelectionSnapshot { CommonParent: { } parent } snapshot && location.IsFileSystem)
            {
                if (!parent.Equals(location)) return;
                snapshot.CopyNames(positions, names);
            }
            else
            {
                wanted = [];
                foreach (int p in positions)
                {
                    var item = sources[p];
                    wanted.Add(item);
                    names.Add(item.Name);
                }
            }
            if (names.Count == 0) return;
            var lookup = names.GetAlternateLookup<ReadOnlySpan<char>>();
            // Unmarking only needs to look at marked entries.
            IEnumerable<int> candidates = value ? Enumerable.Range(0, _appliedCount) : _marks.Enumerate().Where(i => i < _appliedCount).ToList();
            using var scan = new EntryStore.Scan(_store, _appliedCount, value ? _appliedCount : _marks.Count);
            foreach (int si in candidates)
            {
                var e = scan[si];
                if (e.Kind == EntryKind.Parent || !lookup.Contains(e.Name)) continue;
                if (wanted is null || wanted.Contains(provider.GetItemRef(location, _store[si]))) changed |= _marks.Set(si, value);
            }
        }
        if (changed) MarksChanged();
    }

    public void UnmarkHidden()
    {
        bool changed = false;
        foreach (int si in _marks.Enumerate().ToList())
        {
            if (GetVisibleIndex(si) < 0) changed |= _marks.Set(si, false);
        }
        if (changed) MarksChanged();
    }

    private delegate bool EntryPredicate(EntryView e);

    private void ApplyToVisible(EntryPredicate predicate, bool value)
    {
        bool changed = false;
        using var scan = new EntryStore.Scan(_store, _appliedCount);
        foreach (int si in EnumerateVisible())
        {
            var e = scan[si];
            if (e.Kind == EntryKind.Parent || !predicate(e)) continue;
            changed |= _marks.Set(si, value);
        }
        if (changed) MarksChanged();
    }

    private void MarksChanged()
    {
        _statsCache = null;
        Raise(ListingChange.Marks);
    }

    public MarkStats GetMarkStats()
    {
        if (_statsCache is { } cached) return cached;
        int files = 0, dirs = 0, hidden = 0;
        long bytes = 0;
        bool incomplete = false;
        using var scan = new EntryStore.Scan(_store, _appliedCount, _marks.Count);
        foreach (int si in _marks.Enumerate())
        {
            if (si >= _appliedCount) continue;
            var e = scan[si];
            if (e.IsContainer)
            {
                dirs++;
                if (e.Has(EntryFlags.SizeComputed) && e.Size >= 0) bytes += e.Size;
                else incomplete = true;
            }
            else
            {
                files++;
                if (e.Size > 0) bytes += e.Size;
            }
            if (GetVisibleIndex(si) < 0) hidden++;
        }
        var stats = new MarkStats(files + dirs, files, dirs, bytes, hidden, incomplete);
        _statsCache = stats;
        return stats;
    }

    /// <summary>
    /// Marked items in display order (hidden marked items last), or the focused item when none are marked. Large
    /// selections come back as a <see cref="SelectionSnapshot"/> that the caller releases when done with it.
    /// </summary>
    public IReadOnlyList<ItemRef> GetSelection(bool includeHiddenMarks = true)
    {
        if (Provider is null || Location is null) return [];
        if (_marks.Count > 0)
        {
            var indices = new int[_marks.Count];
            var seen = new ulong[(Math.Max(_appliedCount, _store.Count) + 63) >> 6];
            int n = 0;
            foreach (int si in EnumerateVisible())
            {
                if (!_marks.Get(si) || n == indices.Length) continue;
                indices[n++] = si;
                seen[si >> 6] |= 1UL << (si & 63);
            }
            if (includeHiddenMarks)
            {
                foreach (int si in _marks.Enumerate())
                {
                    if (si < _appliedCount && n < indices.Length && (seen[si >> 6] & (1UL << (si & 63))) == 0) indices[n++] = si;
                }
            }
            if (n > SnapshotThreshold) return new SelectionSnapshot(Provider, Location, _store, n == indices.Length ? indices : indices[..n]);
            var list = new List<ItemRef>(n);
            for (int i = 0; i < n; i++) list.Add(GetItemRef(indices[i]));
            return list;
        }
        if (TryGetFocused(out var f) && f.Kind != EntryKind.Parent) return [GetItemRef(_focusStore)];
        return [];
    }

    public bool HasMarks => _marks.Count > 0;

    // ---- Entry updates -----------------------------------------------------------------------------------

    /// <summary>Records an explicitly computed directory size (Space) and re-sorts when sorting by size.</summary>
    public void SetComputedSize(string name, long bytes, bool complete)
    {
        int si = FindStoreIndex(name);
        if (si < 0)
        {
            // Measured while the folder is being listed again and before its row came back: the size waits for it.
            if (_awaitingEntries && complete && bytes >= 0) (_pendingSizes ??= new(StringComparer.Ordinal))[name] = (bytes, MeasuredDuringRefresh);
            return;
        }
        var e = _store[si];
        e.Size = bytes;
        e.Flags = complete ? e.Flags | EntryFlags.SizeComputed : e.Flags & ~EntryFlags.SizeComputed;
        _store.Update(si, e);
        if (complete && e.Kind == EntryKind.Directory && bytes >= 0) _computedSizes[e.Name] = (bytes, e.Modified);
        else _computedSizes.Remove(e.Name);
        _statsCache = null;
        if (_sort.Field == SortField.Size && complete) PushSpec();
        Raise(ListingChange.Rows | ListingChange.Marks);
    }

    private void Raise(ListingChange change)
    {
        if (change != ListingChange.None) Changed?.Invoke(this, change);
    }

    public void Dispose()
    {
        _disposed = true;
        CancelPipelines();
    }

    // ---- Background pipeline -------------------------------------------------------------------------------

    private sealed record ViewSpec(SortSpec Sort, Mask? Filter, bool ShowHidden, int Version);

    /// <param name="Completion">True exactly once per pipeline: the first result after enumeration ended.</param>
    private sealed record PipelineResult(int[] Visible, int Count, bool Done, Exception? Error, bool Canceled, bool Completion = false, DiskView? External = null, bool PreserveCurrent = false, bool ProgressOnly = false);

    private sealed class Pipeline
    {
        private readonly IUiDispatcher _ui;
        private readonly ListingModel _owner;
        private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
        private readonly object _issueLock = new();
        private readonly object _resultLock = new();
        private PipelineResult? _queuedResult;
        private bool _resultPosted;
        private readonly List<string> _issues = [];
        private volatile ViewSpec _spec;
        private long _externalSortBytes;

        public Pipeline(int generation, Location location, ResourceProvider provider, EntryStore store, bool isRefresh,
            ViewSpec spec, IUiDispatcher ui, ListingModel owner)
        {
            Generation = generation;
            Location = location;
            Provider = provider;
            Store = store;
            IsRefresh = isRefresh;
            _spec = spec;
            _ui = ui;
            _owner = owner;
            Sink = new StoreSink(this);
            StartedTimestamp = Stopwatch.GetTimestamp();
        }

        public int Generation { get; }
        public Location Location { get; }
        public ResourceProvider Provider { get; }
        public EntryStore Store { get; }
        public bool IsRefresh { get; }
        public CancellationTokenSource Cts { get; } = new();
        public IEnumerationSink Sink { get; }
        public long StartedTimestamp { get; }
        public Task? LoadTask { get; set; }
        public Task? WorkTask { get; private set; }
        public volatile bool LoadDone;
        public volatile bool LoadCanceled;
        public Exception? LoadError;
        public long ExternalSortBytes => Interlocked.Read(ref _externalSortBytes);

        /// <summary>The provider's latest word on a long load (<see cref="IEnumerationSink.ReportProgress"/>).</summary>
        public volatile string? Progress;
        private string? _publishedProgress;

        public void Signal()
        {
            try { _signal.Release(); }
            catch (ObjectDisposedException) { }
        }

        public void UpdateSpec(ViewSpec spec)
        {
            _spec = spec;
            Signal();
        }

        public void AddIssue(string issue)
        {
            lock (_issueLock)
            {
                if (_issues.Count < 200) _issues.Add(issue);
            }
        }

        public List<string> DrainIssues()
        {
            lock (_issueLock)
            {
                var copy = _issues.ToList();
                _issues.Clear();
                return copy;
            }
        }

        public void Run() => WorkTask = Task.Run(LoopAsync);

        public void ReleaseIndexReservation() => _owner._indexBudget.Release(this);

        public void Retire()
        {
            Cts.Cancel();
            _ = Task.WhenAll(LoadTask ?? Task.CompletedTask, WorkTask ?? Task.CompletedTask)
                .ContinueWith(_ =>
                {
                    lock (_resultLock)
                    {
                        _queuedResult?.External?.Dispose();
                        _queuedResult = null;
                    }
                    ReleaseIndexReservation();
                    Store.Dispose();
                    Cts.Dispose();
                    _signal.Dispose();
                }, TaskScheduler.Default);
        }

        private async Task LoopAsync()
        {
            var ct = Cts.Token;
            int sortedCount = Store.Count > 0 && Store[0].Kind == EntryKind.Parent ? 1 : 0;
            int firstIndex = sortedCount;
            int[] sorted = [];
            ViewSpec? applied = null;
            bool doneAnnounced = false;
            bool first = true;
            bool externalMode = false;
            DiskIntIndex? externalSorted = null;
            SortSpec? externalSort = null;
            int lastProgressCount = sortedCount;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    bool loading = !LoadDone;
                    if (loading) await _signal.WaitAsync(first ? 30 : 120, ct).ConfigureAwait(false);
                    else if (doneAnnounced) await _signal.WaitAsync(ct).ConfigureAwait(false);
                    first = false;
                    if (!LoadDone && Progress is { } progress && !ReferenceEquals(progress, _publishedProgress))
                    {
                        _publishedProgress = progress;
                        Publish(new PipelineResult([], Store.Count, false, null, false, ProgressOnly: true));
                    }

                    var spec = _spec;
                    bool done = LoadDone;
                    int n = Store.Count;
                    if (!externalMode && !_owner._indexBudget.TryReserve(this, 40L * n))
                    {
                        if (_owner._scratchDirectory is null)
                            throw new IOException("A private scratch directory is required for large listing indexes.");
                        externalMode = true;
                        sorted = [];
                    }
                    if (externalMode)
                    {
                        if (!done)
                        {
                            if (n - lastProgressCount >= Math.Max(4096, n / 8))
                            {
                                lastProgressCount = n;
                                Publish(new PipelineResult([], n, false, null, false, ProgressOnly: true));
                            }
                            continue;
                        }
                        if (doneAnnounced && applied?.Version == spec.Version) continue;
                        if (n > lastProgressCount)
                        {
                            lastProgressCount = n;
                            Publish(new PipelineResult([], n, false, null, false, ProgressOnly: true));
                        }
                        if (externalSorted is null || externalSort != spec.Sort ||
                            (spec.Sort.Field is SortField.Size or SortField.Metadata) && applied?.Version != spec.Version)
                        {
                            externalSorted?.Dispose();
                            Interlocked.Exchange(ref _externalSortBytes, 0);
                            externalSorted = ExternalViewBuilder.Sort(Store, n, spec.Sort,
                                _owner.MetadataKeys, _owner._scratchDirectory!, ct);
                            externalSort = spec.Sort;
                            Interlocked.Exchange(ref _externalSortBytes, externalSorted.Bytes);
                        }
                        var external = ExternalViewBuilder.BuildView(Store, n, externalSorted, spec.Filter,
                            spec.ShowHidden, _owner._scratchDirectory!, ct);
                        if (_spec.Version != spec.Version)
                        {
                            external.Dispose();
                            continue;
                        }
                        bool completion = !doneAnnounced;
                        sortedCount = n;
                        applied = spec;
                        doneAnnounced = true;
                        Publish(new PipelineResult([], n, true, LoadError, LoadCanceled, completion, external));
                        continue;
                    }
                    // Geometric batches keep streaming merges and position rebuilds near O(n log n).
                    if (!done && applied is not null && applied.Version == spec.Version &&
                        n - sortedCount < Math.Max(64, sortedCount / 2)) continue;
                    using var comparer = EntrySorter.CreateComparer(Store, spec.Sort, _owner.MetadataKeys, n);
                    var cmp = comparer.Comparison;
                    bool changed = false;
                    if (applied is null || applied.Sort != spec.Sort)
                    {
                        sorted = Range(firstIndex, n);
                        if (spec.Sort.Field != SortField.None) StableSort.Sort(sorted, cmp);
                        changed = true;
                    }
                    else if (n > sortedCount)
                    {
                        var added = Range(sortedCount, n);
                        if (spec.Sort.Field != SortField.None)
                        {
                            StableSort.Sort(added, cmp);
                            sorted = StableSort.Merge(sorted, added, cmp);
                        }
                        else
                        {
                            sorted = [.. sorted, .. added];
                        }
                        changed = true;
                    }
                    else if (applied.Version != spec.Version)
                    {
                        changed = true;
                        // A size re-sort request arrives as a version bump with the same sort.
                        if (spec.Sort.Field is SortField.Size or SortField.Metadata)
                        {
                            sorted = (int[])sorted.Clone(); // UI may still hold the previous array.
                            StableSort.Sort(sorted, cmp);
                        }
                    }
                    sortedCount = n;
                    bool announceDone = done && !doneAnnounced;
                    if (!changed && !announceDone) continue;

                    var visible = BuildVisible(sorted, spec, firstIndex == 1);
                    var result = new PipelineResult(visible, n, done, done ? LoadError : null, done && LoadCanceled, announceDone);
                    applied = spec;
                    if (announceDone) doneAnnounced = true;
                    Publish(result);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Publish(new PipelineResult([], sortedCount, true, ex, false, !doneAnnounced,
                    PreserveCurrent: doneAnnounced));
            }
            finally
            {
                externalSorted?.Dispose();
                Interlocked.Exchange(ref _externalSortBytes, 0);
            }
        }

        private void Publish(PipelineResult result)
        {
            lock (_resultLock)
            {
                // Rows waiting for the UI carry the progress along; a progress note never displaces them.
                if (result.ProgressOnly && _queuedResult is { ProgressOnly: false }) return;
                if (_queuedResult is { Completion: true } && !result.Completion)
                    result = result with { Completion = true };
                _queuedResult?.External?.Dispose();
                _queuedResult = result;
                if (_resultPosted) return;
                _resultPosted = true;
            }
            _ui.Post(() =>
            {
                PipelineResult? latest;
                lock (_resultLock)
                {
                    latest = _queuedResult;
                    _queuedResult = null;
                    _resultPosted = false;
                }
                if (latest is not null) _owner.OnPipelineResult(this, latest);
            });
        }

        private int[] BuildVisible(int[] sorted, ViewSpec spec, bool hasParent)
        {
            if (spec.Filter is null && spec.ShowHidden)
            {
                if (!hasParent) return sorted;
                var all = new int[sorted.Length + 1];
                all[0] = 0;
                Array.Copy(sorted, 0, all, 1, sorted.Length);
                return all;
            }
            var list = new List<int>(sorted.Length + 1);
            if (hasParent) list.Add(0);
            int limit = 0;
            foreach (int si in sorted) limit = Math.Max(limit, si + 1);
            using var reader = Store.OpenSpillReader(limit);
            foreach (int si in sorted)
            {
                bool keep = reader is not null
                    ? ExternalViewBuilder.Passes(reader.Get(si), spec.Filter, spec.ShowHidden)
                    : ExternalViewBuilder.Passes(new EntryView(Store[si]), spec.Filter, spec.ShowHidden);
                if (keep) list.Add(si);
            }
            return list.ToArray();
        }

        private static int[] Range(int from, int to)
        {
            var r = new int[Math.Max(0, to - from)];
            for (int i = 0; i < r.Length; i++) r[i] = from + i;
            return r;
        }

        private sealed class StoreSink(Pipeline owner) : IEnumerationSink
        {
            public void AddBatch(ReadOnlySpan<EntryData> entries)
            {
                owner.Cts.Token.ThrowIfCancellationRequested();
                int before = owner.Store.Count;
                owner.Store.Append(entries);
                if (before < 64 || (before >> 12) != (owner.Store.Count >> 12)) owner.Signal();
            }

            public void ReportIssue(string message) => owner.AddIssue(message);

            public void ReportProgress(string text)
            {
                owner.Progress = text;
                owner.Signal();
            }
        }
    }
}
