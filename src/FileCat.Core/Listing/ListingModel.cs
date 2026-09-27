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

    private EntryStore _store = new();
    private int[] _sorted = [];
    private int[] _visible = [];
    private int[] _positions = [];
    private MarkSet _marks = new();
    private MarkStats? _statsCache;
    private int _appliedCount;
    private Pipeline? _pipeline;
    private Pipeline? _pendingRefresh;
    private int _generation;
    private int _specVersion;
    private SortSpec _sort = new();
    private Mask? _filter;
    private bool _showHidden = true;
    private int _focusStore = -1;
    private int _focusVisibleHint;
    private string? _pendingFocusName;
    private HashSet<string>? _pendingMarkNames;
    private readonly List<string> _issues = [];
    private bool _disposed;

    public ListingModel(ProviderRegistry providers, DeviceIoScheduler io, IUiDispatcher ui,
        string? scratchDirectory = null, long listingMemoryBudgetBytes = 96L * 1024 * 1024)
    {
        _providers = providers;
        _io = io;
        _ui = ui;
        _scratchDirectory = scratchDirectory;
        _listingMemoryBudgetBytes = listingMemoryBudgetBytes;
    }

    public event EventHandler<ListingChange>? Changed;

    public Location? Location { get; private set; }
    public ResourceProvider? Provider { get; private set; }
    public ListingState State { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<string> Issues => _issues;
    public bool HasParentRow { get; private set; }
    public int Generation => _generation;
    public EntryStore Store => _store;
    public int VisibleCount => _visible.Length;
    public int TotalCount => Math.Max(0, _appliedCount - (HasParentRow ? 1 : 0));
    public DateTime? CompletedAtUtc { get; private set; }
    public TimeSpan LastLoadDuration { get; private set; }
    public bool IsRefreshing => _pendingRefresh is not null;

    /// <summary>Metadata sort keys (set by the view layer); used only when sorting by a metadata field.</summary>
    public MetadataKeyProvider? MetadataKeys { get; set; }

    /// <summary>Re-sorts with current keys, e.g. after an explicit metadata analysis completed.</summary>
    public void Resort() => PushSpec();

    /// <summary>Names used by the last operation started from this listing (restore selection).</summary>
    public IReadOnlyCollection<string> LastOperationNames { get; set; } = [];

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

    public int GetStoreIndex(int visibleIndex) => _visible[visibleIndex];

    public EntryData GetVisible(int visibleIndex) => _store[_visible[visibleIndex]];

    /// <summary>Visible position of a store index, or -1 when filtered out or not yet applied.</summary>
    public int GetVisibleIndex(int storeIndex) =>
        (uint)storeIndex < (uint)_positions.Length ? _positions[storeIndex] : -1;

    public ItemRef GetItemRef(int storeIndex) => Provider!.GetItemRef(Location!, _store[storeIndex]);

    public int FindStoreIndex(string name)
    {
        for (int i = 0; i < _appliedCount; i++)
        {
            var entry = _store[i];
            if (entry.Kind != EntryKind.Parent && string.Equals(entry.Name, name, StringComparison.Ordinal)) return i;
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
        if (_visible.Length == 0) return;
        visibleIndex = Math.Clamp(visibleIndex, 0, _visible.Length - 1);
        int store = _visible[visibleIndex];
        _pendingFocusName = null;
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
        CancelPipelines();
        var provider = _providers.For(location);
        Location = location;
        Provider = provider;
        _store = CreateStore(location);
        HasParentRow = provider.GetParent(location) is not null;
        if (HasParentRow) _store.Append(new EntryData("..", EntryKind.Parent));
        _sorted = [];
        _visible = HasParentRow ? [0] : [];
        _positions = HasParentRow ? [0] : [];
        _appliedCount = _store.Count;
        _marks = new MarkSet();
        _statsCache = null;
        _issues.Clear();
        _focusStore = HasParentRow ? 0 : -1;
        _focusVisibleHint = 0;
        _pendingFocusName = focusName;
        _pendingMarkNames = null;
        LastOperationNames = [];
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
        if (_pendingRefresh is not null) return;
        if (State == ListingState.Loading && _pendingRefresh is null)
        {
            Load(Location, TryGetFocused(out var f) ? f.Name : null);
            return;
        }
        var store = CreateStore(Location);
        if (HasParentRow) store.Append(new EntryData("..", EntryKind.Parent));
        _pendingRefresh = StartPipeline(Location, Provider, store, isRefresh: true);
        Raise(ListingChange.State);
    }

    public void CancelLoading()
    {
        if (State != ListingState.Loading && _pendingRefresh is null) return;
        _pipeline?.Cts.Cancel();
        _pendingRefresh?.Retire();
        _pendingRefresh = null;
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
        _pipeline?.Retire();
        _pendingRefresh?.Retire();
        _pipeline = null;
        _pendingRefresh = null;
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
        if (_disposed) return;
        if (ReferenceEquals(p, _pendingRefresh))
        {
            if (!r.Done && Stopwatch.GetElapsedTime(p.StartedTimestamp) < RefreshSwapDelay) return;
            if (r.Done && r.Error is not null)
            {
                // Refresh failed: keep the old rows and report instead of replacing them with an error view.
                _pendingRefresh = null;
                p.Retire();
                _issues.Add($"Refresh failed: {r.Error.Message}");
                Raise(ListingChange.State);
                return;
            }
            SwapToRefresh(p);
        }
        if (!ReferenceEquals(p, _pipeline)) return;
        ApplyResult(p, r);
    }

    private void SwapToRefresh(Pipeline p)
    {
        // Carry marks and focus by exact name into the new generation.
        var marked = new HashSet<string>(StringComparer.Ordinal);
        foreach (int i in _marks.Enumerate())
        {
            if (i < _appliedCount) marked.Add(_store[i].Name);
        }
        string? focusName = null;
        if (_focusStore >= 0 && _focusStore < _appliedCount) focusName = _store[_focusStore].Name;
        _pipeline?.Retire();
        _pipeline = p;
        _pendingRefresh = null;
        _store = p.Store;
        _sorted = [];
        _visible = [];
        _positions = [];
        _marks = new MarkSet();
        _statsCache = null;
        _appliedCount = 0;
        _issues.Clear();
        _pendingMarkNames = marked.Count > 0 ? marked : null;
        _pendingFocusName = focusName;
        _focusStore = -1;
    }

    private void ApplyResult(Pipeline p, PipelineResult r)
    {
        int oldFocusVisible = FocusedIndex >= 0 ? FocusedIndex : _focusVisibleHint;
        int previousCount = _appliedCount;
        _sorted = r.Sorted;
        _visible = r.Visible;
        _appliedCount = r.Count;
        _positions = BuildPositions(_visible, r.Count);
        var change = ListingChange.Rows;

        // Resolve names that were waiting for their entry to arrive.
        if (_pendingMarkNames is not null || _pendingFocusName is not null)
        {
            for (int i = previousCount; i < r.Count; i++)
            {
                var e = _store[i];
                if (e.Kind == EntryKind.Parent) continue;
                if (_pendingMarkNames is not null && _pendingMarkNames.Remove(e.Name))
                {
                    _marks.Set(i, true);
                    change |= ListingChange.Marks;
                }
                if (_pendingFocusName is not null && string.Equals(e.Name, _pendingFocusName, StringComparison.Ordinal))
                {
                    _focusStore = i;
                    _pendingFocusName = null;
                    change |= ListingChange.Focus;
                }
            }
        }

        if (_focusStore < 0 || GetVisibleIndex(_focusStore) < 0)
        {
            // Fall back to the previous position; a pending focus name may still move focus when it arrives.
            if (_visible.Length > 0)
            {
                int vi = Math.Clamp(oldFocusVisible, 0, _visible.Length - 1);
                _focusStore = _visible[vi];
                change |= ListingChange.Focus;
            }
        }
        _focusVisibleHint = Math.Max(0, FocusedIndex);
        _statsCache = null;

        if (r.Completion)
        {
            _pendingMarkNames = null;
            _pendingFocusName = null;
            LastLoadDuration = Stopwatch.GetElapsedTime(p.StartedTimestamp);
            foreach (var issue in p.DrainIssues()) _issues.Add(issue);
            if (r.Error is not null)
            {
                Error = DescribeError(r.Error);
                State = r.Count > (HasParentRow ? 1 : 0) ? ListingState.Complete : ListingState.Failed;
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

    public bool IsVisibleMarked(int visibleIndex) => _marks.Get(_visible[visibleIndex]);

    public int MarkedCount => _marks.Count;

    public void ToggleMark(int visibleIndex) => SetMark(visibleIndex, !IsVisibleMarked(visibleIndex));

    public void SetMark(int visibleIndex, bool value)
    {
        if ((uint)visibleIndex >= (uint)_visible.Length) return;
        int si = _visible[visibleIndex];
        if (_store[si].Kind == EntryKind.Parent) return;
        if (_marks.Set(si, value)) MarksChanged();
    }

    public void SetMarkRange(int fromVisible, int toVisible, bool value)
    {
        if (_visible.Length == 0) return;
        int a = Math.Clamp(Math.Min(fromVisible, toVisible), 0, _visible.Length - 1);
        int b = Math.Clamp(Math.Max(fromVisible, toVisible), 0, _visible.Length - 1);
        bool changed = false;
        for (int i = a; i <= b; i++)
        {
            int si = _visible[i];
            if (_store[si].Kind != EntryKind.Parent) changed |= _marks.Set(si, value);
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
        foreach (int si in _visible)
        {
            var e = _store[si];
            if (e.Kind == EntryKind.Parent || !includeDirectories && e.IsContainer) continue;
            changed |= _marks.Set(si, !_marks.Get(si));
        }
        if (changed) MarksChanged();
    }

    public int MarkByMask(Mask mask, bool value, bool includeDirectories)
    {
        int affected = 0;
        bool changed = false;
        foreach (int si in _visible)
        {
            var e = _store[si];
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

    public void MarkNames(IEnumerable<string> names, bool value)
    {
        var set = names as ISet<string> ?? new HashSet<string>(names, StringComparer.Ordinal);
        bool changed = false;
        for (int i = 0; i < _appliedCount; i++)
        {
            var e = _store[i];
            if (e.Kind != EntryKind.Parent && set.Contains(e.Name)) changed |= _marks.Set(i, value);
        }
        if (changed) MarksChanged();
    }

    /// <summary>Restores the set used by the previous operation (Num /).</summary>
    public void RestoreSelection()
    {
        if (LastOperationNames.Count == 0) return;
        MarkNames(LastOperationNames, true);
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

    private delegate bool EntryPredicate(EntryData e);

    private void ApplyToVisible(EntryPredicate predicate, bool value)
    {
        bool changed = false;
        foreach (int si in _visible)
        {
            var e = _store[si];
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
        foreach (int si in _marks.Enumerate())
        {
            if (si >= _appliedCount) continue;
            var e = _store[si];
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

    /// <summary>Marked items in display order (hidden marked items last), or the focused item when none are marked.</summary>
    public IReadOnlyList<ItemRef> GetSelection(bool includeHiddenMarks = true)
    {
        var result = new List<ItemRef>();
        if (Provider is null || Location is null) return result;
        if (_marks.Count > 0)
        {
            var seen = new HashSet<int>();
            foreach (int si in _visible)
            {
                if (_marks.Get(si))
                {
                    result.Add(GetItemRef(si));
                    seen.Add(si);
                }
            }
            if (includeHiddenMarks)
            {
                foreach (int si in _marks.Enumerate())
                {
                    if (si < _appliedCount && !seen.Contains(si)) result.Add(GetItemRef(si));
                }
            }
            return result;
        }
        if (TryGetFocused(out var f) && f.Kind != EntryKind.Parent) result.Add(GetItemRef(_focusStore));
        return result;
    }

    public bool HasMarks => _marks.Count > 0;

    // ---- Entry updates -----------------------------------------------------------------------------------

    /// <summary>Records an explicitly computed directory size (Space) and re-sorts when sorting by size.</summary>
    public void SetComputedSize(string name, long bytes, bool complete)
    {
        int si = FindStoreIndex(name);
        if (si < 0) return;
        var e = _store[si];
        e.Size = bytes;
        e.Flags = complete ? e.Flags | EntryFlags.SizeComputed : e.Flags & ~EntryFlags.SizeComputed;
        _store.Update(si, e);
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
    private sealed record PipelineResult(int[] Sorted, int[] Visible, int Count, bool Done, Exception? Error, bool Canceled, bool Completion = false);

    private sealed class Pipeline
    {
        private readonly IUiDispatcher _ui;
        private readonly ListingModel _owner;
        private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
        private readonly object _issueLock = new();
        private readonly List<string> _issues = [];
        private volatile ViewSpec _spec;

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

        public void Retire()
        {
            Cts.Cancel();
            _ = Task.WhenAll(LoadTask ?? Task.CompletedTask, WorkTask ?? Task.CompletedTask)
                .ContinueWith(_ =>
                {
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
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    bool loading = !LoadDone;
                    if (loading) await _signal.WaitAsync(first ? 30 : 120, ct).ConfigureAwait(false);
                    else if (doneAnnounced) await _signal.WaitAsync(ct).ConfigureAwait(false);
                    first = false;

                    var spec = _spec;
                    bool done = LoadDone;
                    int n = Store.Count;
                    var cmp = EntrySorter.CreateComparison(Store, spec.Sort, _owner.MetadataKeys);
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
                        if (spec.Sort.Field is SortField.Size or SortField.Metadata) StableSort.Sort(sorted, cmp);
                    }
                    sortedCount = n;
                    bool announceDone = done && !doneAnnounced;
                    if (!changed && !announceDone) continue;

                    var visible = BuildVisible(sorted, spec, firstIndex == 1);
                    var result = new PipelineResult(sorted, visible, n, done, done ? LoadError : null, done && LoadCanceled, announceDone);
                    applied = spec;
                    if (announceDone) doneAnnounced = true;
                    _ui.Post(() => _owner.OnPipelineResult(this, result));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _ui.Post(() => _owner.OnPipelineResult(this, new PipelineResult(sorted, [], sortedCount, true, ex, false, true)));
            }
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
            foreach (int si in sorted)
            {
                var e = Store[si];
                if (!spec.ShowHidden && (e.Flags & EntryFlags.Hidden) != 0) continue;
                if (spec.Filter is not null && !spec.Filter.IsMatch(e.Name, e.IsContainer)) continue;
                list.Add(si);
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
                owner.Store.Append(entries);
            }

            public void ReportIssue(string message) => owner.AddIssue(message);
        }
    }
}
