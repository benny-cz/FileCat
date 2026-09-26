using FileCat.Core.Resources;

namespace FileCat.Core.Content;

/// <summary>
/// Bounded page cache over a random-access source (plan §13.1): 64-bit offsets, explicit short reads, and an
/// LRU budget, so opening a huge file never scans or loads it. Reads missing pages synchronously only when
/// asked to; views use <see cref="TryRead"/> and request pages in the background.
/// </summary>
public sealed class PagedReader : IDisposable
{
    public const int PageSize = 64 * 1024;
    private readonly IContentSource _source;
    private readonly int _maxPages;
    private readonly object _lock = new();
    private readonly Dictionary<long, LinkedListNode<Page>> _pages = new();
    private readonly LinkedList<Page> _lru = new();
    private readonly HashSet<long> _loading = new();
    private long _length;

    private sealed record Page(long Index, byte[] Data, int Length);

    public PagedReader(IContentSource source, int maxPages = 256)
    {
        _source = source;
        _maxPages = Math.Max(4, maxPages);
        _length = Math.Max(0, source.Length);
        Revision = source.GetRevision();
    }

    public IContentSource Source => _source;
    public long Length => Interlocked.Read(ref _length);
    public ContentRevision? Revision { get; private set; }

    /// <summary>Raised on a pool thread when a background page load finishes.</summary>
    public event Action? PageLoaded;

    /// <summary>Copies cached bytes at <paramref name="offset"/>; returns false when a page is missing (a load is started).</summary>
    public bool TryRead(long offset, Span<byte> destination, out int read)
    {
        read = 0;
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
                if (_pages.TryGetValue(index, out var node))
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    page = node.Value;
                }
                else page = null;
            }
            if (page is null)
            {
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

    private void RequestPage(long index)
    {
        lock (_lock)
        {
            if (!_loading.Add(index)) return;
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
        lock (_lock)
        {
            if (_pages.TryGetValue(index, out var existing)) return existing.Value;
        }
        var buffer = new byte[PageSize];
        int n;
        try
        {
            n = _source.Read(index * PageSize, buffer);
        }
        catch (IOException)
        {
            return null;
        }
        var page = new Page(index, buffer, n);
        lock (_lock)
        {
            if (_pages.TryGetValue(index, out var raced)) return raced.Value;
            _pages[index] = _lru.AddFirst(page);
            while (_pages.Count > _maxPages)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _pages.Remove(last.Value.Index);
            }
        }
        return page;
    }

    /// <summary>Re-reads length and revision; drops cached pages when the content changed (external truncation).</summary>
    public bool Refresh()
    {
        var rev = _source.GetRevision();
        long len = Math.Max(0, _source.Length);
        bool changed = rev != Revision || len != Length;
        if (changed)
        {
            lock (_lock)
            {
                _pages.Clear();
                _lru.Clear();
            }
            Interlocked.Exchange(ref _length, len);
            Revision = rev;
        }
        return changed;
    }

    public void Dispose() => _source.Dispose();
}
