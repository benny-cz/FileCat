namespace FileCat.Core.FileSystem;

/// <summary>
/// Watches one open folder (not recursively, not every persisted tab; plan §8.2). Notifications are only
/// invalidation hints (AI-08): they are coalesced and trigger a revalidating refresh; an overflow or error also
/// refreshes. Refreshes of large folders are throttled so churn cannot cause an endless rescan, and a folder that keeps
/// changing (a log written every second, as in C:\Windows) is read again at most every <see cref="ChurnInterval"/>.
/// </summary>
public sealed class ChangeMonitor : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _debounce;
    private readonly Action _onChange;
    private readonly object _lock = new();
    private DateTime _firstPending = DateTime.MaxValue;
    private DateTime _lastFire = DateTime.MinValue;
    private DateTime _lastNotice = DateTime.MinValue;
    private readonly Queue<DateTime> _recentFires = new();
    private bool _churning;
    private Timer? _calm;
    private bool _disposed;
    private Timer? _rearm;
    private int _rearmAttempts;
    private readonly Timer? _rootCheck;
    private int _checking;
    private readonly string _path;
    private int _overflows;

    public ChangeMonitor(string path, Action onChange)
    {
        _path = path;
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
            _watcher.Created += (_, _) => Notified();
            _watcher.Deleted += (_, _) => Notified();
            _watcher.Renamed += (_, _) => Notified();
            _watcher.Changed += (_, _) => Notified();
            _watcher.Error += (_, e) => OnError(e);
            _watcher.EnableRaisingEvents = true;
            IsActive = true;
            // On Linux and macOS the watch says nothing when its own folder is deleted or moved away (inotify and
            // FSEvents do, the watcher does not pass it on): a look every two seconds notices, and reports it.
            if (!OperatingSystem.IsWindows())
                _rootCheck = new Timer(_ => CheckRoot(path), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _watcher = null;
        }
    }

    /// <summary>False when the platform or location cannot be watched; callers fall back to refresh-on-activate.</summary>
    public bool IsActive { get; }

    /// <summary>Minimum time between rereads; set from the last listing duration.</summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// While the folder keeps changing, the time between rereads (at least): rereading a busy folder every second shows
    /// nothing a reader can follow and costs a full read each time. <see cref="ChurnRereads"/> rereads within
    /// <see cref="ChurnWindow"/> start it; <see cref="CalmAfter"/> without a change ends it.
    /// </summary>
    public TimeSpan ChurnInterval { get; internal set; } = TimeSpan.FromSeconds(2);
    internal const int ChurnRereads = 3;
    internal TimeSpan ChurnWindow { get; set; } = TimeSpan.FromSeconds(10);
    internal TimeSpan CalmAfter { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Whether the folder keeps changing, so that it is read again only every <see cref="ChurnInterval"/>.</summary>
    public bool IsChurning { get { lock (_lock) return _churning; } }

    /// <summary>Raised on a worker thread when the folder starts or stops changing often (<see cref="IsChurning"/>).</summary>
    public event Action? ChurnChanged;

    /// <summary>Reports a change once more, as a notification would (one that arrived while the folder was being read).</summary>
    public void Again() => Pending();

    /// <summary>Runs before each notification is handled (tests: a handler held up, as on a busy machine).</summary>
    internal Action? BeforeNotification { get; set; }

    private void Notified()
    {
        BeforeNotification?.Invoke();
        Pending();
    }

    /// <summary>
    /// How often more changed at once than the system's notification buffer holds. Each time, which items changed is
    /// lost, and the folder is read again in full.
    /// </summary>
    public int Overflows => Volatile.Read(ref _overflows);

    private void Pending()
    {
        lock (_lock)
        {
            if (_disposed) return;
            var now = DateTime.UtcNow;
            _lastNotice = now;
            if (_firstPending == DateTime.MaxValue) _firstPending = now;
            var interval = _churning && ChurnInterval > MinInterval ? ChurnInterval : MinInterval;
            _debounce.Change(RefreshDelay(now, _firstPending, _lastFire, interval), Timeout.InfiniteTimeSpan);
            if (_churning) _calm?.Change(CalmAfter, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>A reread happened: three within the window mean the folder keeps changing.</summary>
    private bool NoteFire(DateTime now)
    {
        _recentFires.Enqueue(now);
        while (_recentFires.Count > 0 && now - _recentFires.Peek() > ChurnWindow) _recentFires.Dequeue();
        if (_churning || _recentFires.Count < ChurnRereads) return false;
        _churning = true;
        (_calm ??= new Timer(_ => Calm(), null, Timeout.Infinite, Timeout.Infinite)).Change(CalmAfter, Timeout.InfiniteTimeSpan);
        return true;
    }

    /// <summary>No change for <see cref="CalmAfter"/>: the next change is read again as soon as it settles.</summary>
    private void Calm()
    {
        lock (_lock)
        {
            if (_disposed || !_churning) return;
            var quiet = DateTime.UtcNow - _lastNotice;
            if (quiet < CalmAfter)
            {
                _calm?.Change(CalmAfter - quiet, Timeout.InfiniteTimeSpan);
                return;
            }
            _churning = false;
            _recentFires.Clear();
        }
        ChurnChanged?.Invoke();
    }

    internal static TimeSpan RefreshDelay(DateTime now, DateTime firstPending, DateTime lastFire, TimeSpan minInterval)
    {
        // Coalesce bursts within a quarter second of the latest change and cap the burst at two seconds (I87).
        // A long preceding scan can require a later refresh through the minimum interval.
        var due = now + TimeSpan.FromMilliseconds(250);
        if (due > firstPending + TimeSpan.FromSeconds(2)) due = firstPending + TimeSpan.FromSeconds(2);
        if (lastFire != DateTime.MinValue && due < lastFire + minInterval) due = lastFire + minInterval;
        var wait = due - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    private void OnError(ErrorEventArgs e)
    {
        Pending(); // an overflow or a lost watch: reconcile by re-enumerating
        if (e.GetException() is InternalBufferOverflowException)
        {
            if (Interlocked.Increment(ref _overflows) == 1)
                Diagnostics.AppLog.Info($"Change notifications for {Diagnostics.AppLog.P(_path)} overflowed; the folder is read again in full.");
            return;
        }
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

    private void CheckRoot(string path)
    {
        // A look that hangs on a dead mount holds up only the next looks.
        if (Interlocked.Exchange(ref _checking, 1) != 0) return;
        try
        {
            if (Directory.Exists(path)) return;
            _rootCheck?.Change(Timeout.Infinite, Timeout.Infinite);
            Pending();
        }
        finally { Volatile.Write(ref _checking, 0); }
    }

    private void Fire()
    {
        bool started;
        lock (_lock)
        {
            if (_disposed) return;
            _firstPending = DateTime.MaxValue;
            _lastFire = DateTime.UtcNow;
            started = NoteFire(_lastFire);
        }
        if (started) ChurnChanged?.Invoke();
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
        _rootCheck?.Dispose();
        _calm?.Dispose();
    }
}
