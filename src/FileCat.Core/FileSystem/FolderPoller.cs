namespace FileCat.Core.FileSystem;

/// <summary>
/// Reads a folder's time stamp every few seconds and reports a change as a notification would: for folders on network
/// file systems, whose changes made on the server the system does not report (inotify never sees them, and some SMB
/// servers send nothing), and for folders it cannot watch at all. A read that hangs on a dead mount holds up only the
/// next reads, never the UI.
/// </summary>
public sealed class FolderPoller : IDisposable
{
    private readonly string _path;
    private readonly Action _changed;
    private readonly Timer _timer;
    private DateTime? _last;
    private int _busy;
    private volatile bool _disposed;

    public FolderPoller(string path, Action changed, TimeSpan interval)
    {
        _path = path;
        _changed = changed;
        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, interval);
    }

    private void Poll()
    {
        if (_disposed || Interlocked.Exchange(ref _busy, 1) != 0) return;
        try
        {
            DateTime stamp = Directory.GetLastWriteTimeUtc(_path);
            if (_last is { } last && stamp != last && !_disposed) _changed();
            _last = stamp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        finally { Volatile.Write(ref _busy, 0); }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
