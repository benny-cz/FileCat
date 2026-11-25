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
            var due = DueAt(now, _firstPending, _lastFire, Interval);
            _debounce.Change(Wait(due, now), Timeout.InfiniteTimeSpan);
            if (_churning) _calm?.Change(CalmAfter, Timeout.InfiniteTimeSpan);
            Noticed?.Invoke(now, due);
        }
    }

    /// <summary>The least time between rereads now: <see cref="ChurnInterval"/> while the folder keeps changing.</summary>
    private TimeSpan Interval => _churning && ChurnInterval > MinInterval ? ChurnInterval : MinInterval;

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
        DateTime now;
        lock (_lock)
        {
            if (_disposed || !_churning) return;
            now = DateTime.UtcNow;
            var quiet = now - _lastNotice;
            if (quiet < CalmAfter)
            {
                _calm?.Change(CalmAfter - quiet, Timeout.InfiniteTimeSpan);
                return;
            }
            _churning = false;
            _recentFires.Clear();
        }
        Turned?.Invoke(now, false);
        ChurnChanged?.Invoke();
    }

    internal static TimeSpan RefreshDelay(DateTime now, DateTime firstPending, DateTime lastFire, TimeSpan minInterval) =>
        Wait(DueAt(now, firstPending, lastFire, minInterval), now);

    /// <summary>
    /// When a reread falls due after the latest change: a quarter second after it, so that a burst is read once, but at
    /// most two seconds after the first change still unread (I87), and not sooner than <paramref name="minInterval"/> after
    /// the last reread (a long preceding scan, or a folder that keeps changing).
    /// </summary>
    private static DateTime DueAt(DateTime latest, DateTime firstPending, DateTime lastFire, TimeSpan minInterval)
    {
        var due = latest + TimeSpan.FromMilliseconds(250);
        if (due > firstPending + TimeSpan.FromSeconds(2)) due = firstPending + TimeSpan.FromSeconds(2);
        if (lastFire != DateTime.MinValue && due < lastFire + minInterval) due = lastFire + minInterval;
        return due;
    }

    private static TimeSpan Wait(DateTime due, DateTime now) => due > now ? due - now : TimeSpan.Zero;

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

    /// <summary>Runs as a reread falls due, before it takes the monitor's lock (tests: a timer callback held up).</summary>
    internal Action? BeforeFire { get; set; }

    /// <summary>
    /// Runs with the time a reread was taken at, the time its interval counts from, before it reads (tests: the time between
    /// rereads on the monitor's own clock, which the time its callback ran at only approximates on a loaded machine).
    /// </summary>
    internal Action<DateTime>? Fired { get; set; }

    /// <summary>
    /// Runs under the monitor's lock with the time each change was noticed at and the time the reread it asks for falls
    /// due (tests: on the monitor's own clock, how long the folder was quiet before it counted as calm, and how soon a
    /// change is read again).
    /// </summary>
    internal Action<DateTime, DateTime>? Noticed { get; set; }

    /// <summary>
    /// Runs with the time the folder started (true) or stopped (false) counting as busy, on the monitor's own clock (tests:
    /// which rereads were taken while the folder counted as busy).
    /// </summary>
    internal Action<DateTime, bool>? Turned { get; set; }

    /// <summary>
    /// How early a reread may still be taken before its interval ends: a timer can fall due up to a system timer tick
    /// (15.6 ms on Windows) early. A reread earlier than that waits out the rest.
    /// </summary>
    private static readonly TimeSpan TimerSlack = TimeSpan.FromMilliseconds(15);

    private void Fire()
    {
        BeforeFire?.Invoke();
        bool started;
        DateTime fired;
        lock (_lock)
        {
            // Nothing noticed since the last reread: a notice that came as that reread fell due, before it took this lock,
            // armed the timer again from the reread before it, and that reread has already read its change. Reading again
            // here read a busy folder 0.4 s after the last time (seen on macOS in CI).
            if (_disposed || _firstPending == DateTime.MaxValue) return;
            var now = DateTime.UtcNow;
            // Armed before the last reread, and already queued when a change noticed since armed the timer again: a timer
            // does not withdraw a queued callback when it is armed again, so this one still runs, and reading now would read
            // the folder again before its interval ends (a busy folder sooner than every ChurnInterval). That notice armed
            // the timer for when the reread falls due; this only makes sure of it.
            if (_lastFire != DateTime.MinValue && now < _lastFire + Interval - TimerSlack)
            {
                _debounce.Change(Wait(DueAt(_lastNotice, _firstPending, _lastFire, Interval), now), Timeout.InfiniteTimeSpan);
                return;
            }
            _firstPending = DateTime.MaxValue;
            _lastFire = fired = now;
            started = NoteFire(fired);
        }
        Fired?.Invoke(fired);
        if (started)
        {
            Turned?.Invoke(fired, true);
            ChurnChanged?.Invoke();
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
        _rootCheck?.Dispose();
        _calm?.Dispose();
    }
}
