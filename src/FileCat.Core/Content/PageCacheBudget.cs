namespace FileCat.Core.Content;

/// <summary>
/// The page caches' shared budget (plan §21.2: one content cache budget, 64 MiB unless configured otherwise, so that
/// per-view caches cannot multiply). Every <see cref="PagedReader"/> charges its cached pages here; when the total passes
/// the limit, the least recently used pages go first, whichever reader holds them. Each reader keeps its few most recent
/// pages (more than a screen shows), so a scan through one file cannot empty the views of others; only that floor, four
/// pages per open reader, can take the total past the limit. A reader's charge ends when it is disposed, or when it is
/// collected without having been disposed.
/// </summary>
public sealed class PageCacheBudget
{
    public const long DefaultLimitBytes = 64L * 1024 * 1024;

    /// <summary>The application's budget, used by every reader not given another.</summary>
    public static PageCacheBudget Shared { get; } = new();

    private readonly object _gate = new();
    private readonly List<Account> _accounts = [];
    private long _limit;
    private long _used;
    private long _clock;

    public PageCacheBudget(long limitBytes = DefaultLimitBytes) => LimitBytes = limitBytes;

    /// <summary>The most the page caches together keep, beyond their floors; lowering it trims at once.</summary>
    public long LimitBytes
    {
        get => Interlocked.Read(ref _limit);
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, PagedReader.PageSize);
            Interlocked.Exchange(ref _limit, value);
            Trim();
        }
    }

    /// <summary>Bytes of pages cached by the readers now charged here.</summary>
    public long UsedBytes => Interlocked.Read(ref _used);

    /// <summary>The readers charged here now.</summary>
    public int Readers
    {
        get
        {
            lock (_gate)
            {
                _accounts.RemoveAll(Collected);
                return _accounts.Count;
            }
        }
    }

    /// <summary>A reader's charge. It outlives the reader, so the pages of a reader collected undisposed are written off.</summary>
    internal sealed class Account(PagedReader reader)
    {
        public readonly WeakReference<PagedReader> Reader = new(reader);
        public long Bytes;
    }

    internal Account Open(PagedReader reader)
    {
        var account = new Account(reader);
        lock (_gate) _accounts.Add(account);
        return account;
    }

    /// <summary>Ends a reader's charge; the reader has emptied its cache and caches nothing more.</summary>
    internal void Close(Account account)
    {
        lock (_gate) _accounts.Remove(account);
        Interlocked.Add(ref _used, -Interlocked.Exchange(ref account.Bytes, 0));
    }

    /// <summary>A moment on the budget's clock, stamped on a page whenever it is used.</summary>
    internal long Tick() => Interlocked.Increment(ref _clock);

    /// <summary>
    /// Adds bytes to a reader's charge, or removes them (negative). The reader calls it under its own lock, as it changes
    /// its cache: disposal empties the cache under that lock too, so it always finds every charge made before it. It
    /// trims nothing; the reader calls <see cref="Trim"/> once it let go of its lock.
    /// </summary>
    internal void Adjust(Account account, long bytes)
    {
        Interlocked.Add(ref account.Bytes, bytes);
        Interlocked.Add(ref _used, bytes);
    }

    /// <summary>Evicts the oldest pages of all until the total is within the limit or every reader is at its floor.</summary>
    internal void Trim()
    {
        if (UsedBytes <= LimitBytes) return;
        // Lock order: this budget, then one reader at a time; a reader never waits for the budget while holding its own.
        lock (_gate)
        {
            _accounts.RemoveAll(Collected);
            while (UsedBytes > LimitBytes)
            {
                PagedReader? victim = null;
                long oldest = long.MaxValue;
                foreach (var account in _accounts)
                {
                    if (!account.Reader.TryGetTarget(out var reader)) continue;
                    long used = reader.OldestEvictable();
                    if (used < oldest)
                    {
                        oldest = used;
                        victim = reader;
                    }
                }
                if (victim is null) break;
                victim.EvictOldest();
            }
        }
    }

    private bool Collected(Account account)
    {
        if (account.Reader.TryGetTarget(out _)) return false;
        Interlocked.Add(ref _used, -Interlocked.Exchange(ref account.Bytes, 0));
        return true;
    }
}
