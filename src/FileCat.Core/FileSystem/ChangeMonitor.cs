namespace FileCat.Core.FileSystem;

/// <summary>
/// Watches one open folder (not recursively, not every persisted tab; plan §8.2). Notifications are only
/// invalidation hints (AI-08): they are coalesced and trigger a revalidating refresh; an overflow or error also
/// refreshes. Refreshes of large folders are throttled so churn cannot cause an endless rescan.
/// </summary>
public sealed class ChangeMonitor : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _debounce;
    private readonly Action _onChange;
    private readonly object _lock = new();
    private DateTime _firstPending = DateTime.MaxValue;
    private bool _disposed;
    private Timer? _rearm;
    private int _rearmAttempts;

    public ChangeMonitor(string path, Action onChange)
    {
        _onChange = onChange;
        _debounce = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size |
                               NotifyFilters.LastWrite | NotifyFilters.Attributes,
            };
            _watcher.Created += (_, _) => Pending();
            _watcher.Deleted += (_, _) => Pending();
            _watcher.Renamed += (_, _) => Pending();
            _watcher.Changed += (_, _) => Pending();
            _watcher.Error += (_, e) => OnError(e);
            _watcher.EnableRaisingEvents = true;
            IsActive = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _watcher = null;
        }
    }

    /// <summary>False when the platform or location cannot be watched; callers fall back to refresh-on-activate.</summary>
    public bool IsActive { get; }

    /// <summary>Minimum time between refreshes; set from the last listing duration.</summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromMilliseconds(300);

    private void Pending()
    {
        lock (_lock)
        {
            if (_disposed) return;
            var now = DateTime.UtcNow;
            if (_firstPending == DateTime.MaxValue) _firstPending = now;
            // Coalesce bursts, but never delay a refresh by more than two seconds plus the throttle.
            var wait = now - _firstPending > TimeSpan.FromSeconds(2) ? TimeSpan.Zero : TimeSpan.FromMilliseconds(250);
            if (wait < MinInterval) wait = MinInterval;
            _debounce.Change(wait, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnError(ErrorEventArgs e)
    {
        Pending(); // an overflow or a lost watch: reconcile by re-enumerating
        if (e.GetException() is InternalBufferOverflowException) return;
        // A watch lost to a network drop or a vanished folder raises nothing more: re-arm it, retrying for a minute.
        lock (_lock)
        {
            if (_disposed) return;
            _rearmAttempts = 0;
            _rearm ??= new Timer(_ => Rearm(), null, Timeout.Infinite, Timeout.Infinite);
            _rearm.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        }
    }

    private void Rearm()
    {
        lock (_lock)
        {
            if (_disposed || _watcher is null) return;
        }
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.EnableRaisingEvents = true;
            Pending(); // catch up with what changed while the watch was down
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            lock (_lock)
            {
                if (!_disposed && ++_rearmAttempts < 12) _rearm?.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void Fire()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _firstPending = DateTime.MaxValue;
        }
        _onChange();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }
        _watcher?.Dispose();
        _debounce.Dispose();
        _rearm?.Dispose();
    }
}
