namespace FileCat.Core.FileSystem;

/// <summary>What changed among the drives: roots that came and went, or the medium in a drive that stayed (a card, a disc).</summary>
public sealed record DriveChange(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, bool MediaChanged = false);

/// <summary>
/// Notices drives and mount points coming and going (plan §8.2). The roots This PC lists are compared every two seconds
/// while anyone listens: one system call, and no I/O on any drive, so a hung network drive cannot delay it. A platform
/// that is told sooner (Windows' device notifications) calls <see cref="Check"/> itself, which also reports a medium
/// inserted into or taken out of a drive that stays.
/// </summary>
public sealed class DriveWatcher : IDisposable
{
    private readonly Func<IReadOnlyList<string>> _roots;
    private readonly TimeSpan _interval;
    private readonly object _lock = new();
    private readonly List<Action<DriveChange>> _listeners = [];
    private readonly Timer _timer;
    private HashSet<string>? _known;
    private bool _disposed;

    public DriveWatcher(Func<IReadOnlyList<string>> roots, TimeSpan? interval = null)
    {
        _roots = roots;
        _interval = interval ?? TimeSpan.FromSeconds(2);
        _timer = new Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Calls <paramref name="changed"/> (on a background thread) after every change; disposing the result stops that.</summary>
    public IDisposable Listen(Action<DriveChange> changed)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _listeners.Add(changed);
            if (_listeners.Count == 1)
            {
                _known = Read();
                _timer.Change(_interval, _interval);
            }
        }
        return new Listener(this, changed);
    }

    /// <summary>
    /// Compares the drives now. <paramref name="mediaChanged"/> reports a change even when the same drives are there
    /// (a card or disc went in or out).
    /// </summary>
    public void Check(bool mediaChanged = false)
    {
        Action<DriveChange>[] listeners;
        DriveChange change;
        lock (_lock)
        {
            if (_disposed || _listeners.Count == 0) return;
            var now = Read();
            var before = _known ?? now;
            var added = now.Where(r => !before.Contains(r)).OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
            var removed = before.Where(r => !now.Contains(r)).OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
            _known = now;
            if (added.Count == 0 && removed.Count == 0 && !mediaChanged) return;
            change = new DriveChange(added, removed, mediaChanged);
            listeners = [.. _listeners];
        }
        foreach (var listener in listeners) listener(change);
    }

    private HashSet<string> Read()
    {
        try
        {
            return new HashSet<string>(_roots(), PathUtil.SafetyComparer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable this time: nothing is reported, and the next check compares again.
            return _known ?? new HashSet<string>(PathUtil.SafetyComparer);
        }
    }

    private void Stop(Action<DriveChange> changed)
    {
        lock (_lock)
        {
            if (!_listeners.Remove(changed) || _listeners.Count > 0 || _disposed) return;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _known = null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _listeners.Clear();
        }
        _timer.Dispose();
    }

    private sealed class Listener(DriveWatcher owner, Action<DriveChange> changed) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Stop(changed);
        }
    }
}
