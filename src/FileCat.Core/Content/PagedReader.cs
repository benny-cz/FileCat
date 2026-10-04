using FileCat.Core.Resources;

namespace FileCat.Core.Content;

/// <summary>
/// Bounded page cache over a random-access source (plan §13.1): 64-bit offsets, explicit short reads, and an
/// LRU budget, so opening a huge file never scans or loads it. Reads missing pages synchronously only when
/// asked to; views use <see cref="TryRead"/> and request pages in the background. The pages count against a
/// <see cref="PageCacheBudget"/> shared with every other reader, besides this reader's own limit.
/// </summary>
public sealed class PagedReader : IDisposable
{
    public const int PageSize = 64 * 1024;

    /// <summary>Pages a reader keeps whatever the shared budget says: more than one screen of text or hex.</summary>
    internal const int FloorPages = 4;

    private readonly IContentSource _source;
    private readonly int _maxPages;
    private readonly PageCacheBudget _budget;
    private readonly PageCacheBudget.Account _account;
    private bool _disposed;
    private int _sourceUses;
    private bool _sourceDisposed;
    private readonly object _lock = new();
    private readonly Dictionary<long, LinkedListNode<Page>> _pages = new();
    private readonly LinkedList<Page> _lru = new();
    private readonly HashSet<long> _loading = new();
    // Pages that could not be read, and when: views show the content as ending there (ReadError says why) instead of
    // asking for the page on every render, and try again after a pause, in case the cause was passing.
    private readonly Dictionary<long, DateTime> _failed = new();
    private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromSeconds(2);
    private long _length;
    // Bumped whenever cached content is replaced; a load that started earlier must not insert its stale page.
    private long _generation;

    private sealed class Page(long index, byte[] data, int length)
    {
        public long Index { get; } = index;
        public byte[] Data { get; } = data;
        public int Length { get; } = length;
        /// <summary>When it was last moved to the front, on the shared budget's clock.</summary>
        public long Used;
    }

    public PagedReader(IContentSource source, int maxPages = 256, PageCacheBudget? budget = null)
    {
        _source = source;
        _maxPages = Math.Max(FloorPages, maxPages);
        _length = Math.Max(0, source.Length);
        Revision = source.GetRevision();
        _budget = budget ?? PageCacheBudget.Shared;
        _account = _budget.Open(this);
    }

    /// <summary>Pages cached now.</summary>
    internal int CachedPages
    {
        get { lock (_lock) return _pages.Count; }
    }

    /// <summary>Queued or active asynchronous page loads, including their cache insertion and budget trimming.</summary>
    internal int PendingLoads
    {
        get { lock (_lock) return _loading.Count; }
    }

    /// <summary>Whether the page of that number is cached (tests: asking through a read would load it).</summary>
    internal bool HasPage(long index)
    {
        lock (_lock) return _pages.ContainsKey(index);
    }

    public IContentSource Source => _source;
    public long Length => Interlocked.Read(ref _length);
    public ContentRevision? Revision { get; private set; }

    /// <summary>
    /// Runs background work with the underlying source borrowed until the work returns. Close rejects new borrows
    /// and releases the cache immediately; the work must observe its cancellation at safe source-call boundaries.
    /// The callback must not dispose the source.
    /// </summary>
    public void WithSource(Action<IContentSource> work)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _sourceUses++;
        }
        try { work(_source); }
        finally { EndSourceUse(); }
    }

    /// <summary>Why content could not be read (for example an archive member found damaged part way), or null.</summary>
    public string? ReadError { get; private set; }

    /// <summary>Raised on a pool thread when a background page load finishes.</summary>
    public event Action? PageLoaded;

    /// <summary>Copies cached bytes at <paramref name="offset"/>; returns false when a page is missing (a load is started).</summary>
    public bool TryRead(long offset, Span<byte> destination, out int read)
    {
        read = 0;
        lock (_lock) if (_disposed) return true;
        long len = Length;
        if (offset >= len || destination.Length == 0) return true;
        bool complete = true;
        while (read < destination.Length && offset + read < len)
        {
            long pos = offset + read;
            long index = pos / PageSize;
            Page? page;
            lock (_lock)
            {
                if (_disposed) return true;
                if (_pages.TryGetValue(index, out var node))
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    node.Value.Used = _budget.Tick();
                    page = node.Value;
                }
                else page = null;
            }
            if (page is null)
            {
                if (RecentlyFailed(index)) break;
                RequestPage(index);
                complete = false;
                break;
            }
            int inPage = (int)(pos - index * PageSize);
            int n = Math.Min(destination.Length - read, page.Length - inPage);
            if (n <= 0) break;
            page.Data.AsSpan(inPage, n).CopyTo(destination[read..]);
            read += n;
        }
        return complete;
    }

    /// <summary>Blocking read for background work (search, checksums). Never call on the UI thread.</summary>
    public int Read(long offset, Span<byte> destination)
    {
        int total = 0;
        while (total < destination.Length)
        {
            long pos = offset + total;
            if (pos >= Length) break;
            long index = pos / PageSize;
            var page = LoadPage(index);
            if (page is null) break;
            int inPage = (int)(pos - index * PageSize);
            int n = Math.Min(destination.Length - total, page.Length - inPage);
            if (n <= 0) break;
            page.Data.AsSpan(inPage, n).CopyTo(destination[total..]);
            total += n;
        }
        return total;
    }

    private bool RecentlyFailed(long index)
    {
        lock (_lock) return _failed.TryGetValue(index, out var when) && DateTime.UtcNow - when < RetryFailedAfter;
    }

    private void RequestPage(long index)
    {
        lock (_lock)
        {
            if (_disposed || !_loading.Add(index)) return;
        }
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { LoadPage(index); }
            catch (Exception) { }
            finally
            {
                lock (_lock) _loading.Remove(index);
                PageLoaded?.Invoke();
            }
        });
    }

    private Page? LoadPage(long index)
    {
        long generation;
        lock (_lock)
        {
            if (_disposed) return null;
            if (_pages.TryGetValue(index, out var existing)) return existing.Value;
            generation = _generation;
            _sourceUses++;
        }
        byte[] buffer;
        int n;
        try
        {
            buffer = new byte[PageSize];
            n = _source.Read(index * PageSize, buffer);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ObjectDisposedException)
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    if (ex is not ObjectDisposedException) ReadError = ex.Message;
                    _failed[index] = DateTime.UtcNow;
                }
            }
            return null;
        }
        finally { EndSourceUse(); }
        var page = new Page(index, buffer, n);
        lock (_lock)
        {
            _failed.Remove(index);
            if (_pages.TryGetValue(index, out var raced)) return raced.Value;
            if (generation != _generation || _disposed) return page;
            page.Used = _budget.Tick();
            _pages[index] = _lru.AddFirst(page);
            if (_pages.Count > _maxPages)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _pages.Remove(last.Value.Index);
                // One in, one out: this reader's charge stays as it was.
                return page;
            }
            _budget.Adjust(_account, PageSize);
        }
        // Outside this reader's lock: trimming may take the oldest page of any reader, this one included.
        _budget.Trim();
        return page;
    }

    /// <summary>When this reader's oldest page was last used, if the shared budget may take it; otherwise the maximum.</summary>
    internal long OldestEvictable()
    {
        lock (_lock) return _pages.Count > FloorPages ? _lru.Last!.Value.Used : long.MaxValue;
    }

    /// <summary>Drops this reader's oldest page for the shared budget, unless it is down to its floor.</summary>
    internal void EvictOldest()
    {
        lock (_lock)
        {
            if (_pages.Count <= FloorPages) return;
            var last = _lru.Last!;
            _lru.RemoveLast();
            _pages.Remove(last.Value.Index);
            _budget.Adjust(_account, -PageSize);
        }
    }

    /// <summary>Re-reads length and revision; drops cached pages when the content changed (external truncation).</summary>
    public bool Refresh()
    {
        lock (_lock)
        {
            if (_disposed) return false;
            _sourceUses++;
        }
        ContentRevision? rev;
        long len;
        try
        {
            rev = _source.GetRevision();
            len = Math.Max(0, _source.Length);
        }
        finally { EndSourceUse(); }
        lock (_lock)
        {
            if (_disposed) return false;
            bool changed = rev != Revision || len != Length;
            if (changed)
            {
                _generation++;
                _budget.Adjust(_account, -(long)_pages.Count * PageSize);
                _pages.Clear();
                _lru.Clear();
                Interlocked.Exchange(ref _length, len);
                Revision = rev;
            }
            return changed;
        }
    }

    /// <summary>
    /// Applies bytes just written through the source to cached pages without I/O, so an editor updates in place
    /// instead of reloading (and briefly blanking) the view. Loads already in flight are discarded.
    /// </summary>
    public void Overwrite(long offset, ReadOnlySpan<byte> bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        lock (_lock)
        {
            _generation++;
            for (int done = 0; done < bytes.Length;)
            {
                long pos = offset + done;
                long index = pos / PageSize;
                int inPage = (int)(pos - index * PageSize);
                int span = Math.Min(bytes.Length - done, PageSize - inPage);
                if (_pages.TryGetValue(index, out var node))
                {
                    int copy = Math.Min(span, node.Value.Length - inPage);
                    if (copy > 0) bytes.Slice(done, copy).CopyTo(node.Value.Data.AsSpan(inPage));
                }
                done += span;
            }
        }
    }

    // A provider may still be inside a synchronous read/revision call when its view closes. Retire the cache
    // immediately, but release its source only after the last such call returns, without blocking the UI.
    private void EndSourceUse()
    {
        bool dispose;
        lock (_lock)
        {
            _sourceUses--;
            dispose = _disposed && _sourceUses == 0 && !_sourceDisposed;
            if (dispose) _sourceDisposed = true;
        }
        if (dispose) _source.Dispose();
    }

    /// <summary>Retires page demand and releases the cache budget; an active source call finishes before disposal.</summary>
    public void Dispose()
    {
        bool disposeSource;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            _pages.Clear();
            _lru.Clear();
            _failed.Clear();
            ReadError = null;
            disposeSource = _sourceUses == 0;
            if (disposeSource) _sourceDisposed = true;
        }
        _budget.Close(_account);
        if (disposeSource) _source.Dispose();
    }
}
