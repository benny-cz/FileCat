namespace FileCat.Platform.Windows.Shell;

/// <summary>
/// Where Shell handlers may run on content at all (plan §8.2, AI-14): never for shortcut-like types, folder
/// customization files, or themes, whose parsing reaches out to paths named inside them; never for cloud placeholders,
/// which would be downloaded; and on network or removable drives only when the user opted in.
/// </summary>
public static class ShellPreviewPolicy
{
    // Types whose Shell parsing follows paths stored in the file (CVE-2025-24054 and theme-file NTLM leaks).
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", ".url", ".website", ".library-ms", ".searchconnector-ms", ".search-ms", ".scf", ".pif", ".appref-ms",
        ".theme", ".themepack", ".deskthemepack", ".msstyles", ".desklink", ".mapimail", ".zfsendtotarget",
    };

    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000, RecallOnDataAccess = (FileAttributes)0x400000;

    // Files an icon may be read from when a shortcut or desktop.ini names one.
    private static readonly HashSet<string> IconFiles = new(StringComparer.OrdinalIgnoreCase) { ".dll", ".exe", ".ico", ".icl", ".cpl", ".ocx", ".scr", ".mun" };

    /// <summary>
    /// Why an icon named inside a user's file may not be read, or null when it may: only resource files on this
    /// computer (a share named there would be contacted, revealing the user's credentials), never cloud placeholders.
    /// </summary>
    public static string? IconResourceRefusal(IconLocation location, bool allowNetworkAndRemovable)
    {
        if (!OperatingSystem.IsWindows()) return "Shell pictures exist only on Windows.";
        string file = location.File;
        if (!Path.IsPathFullyQualified(file) || file.Contains('\0') || file.Contains('|')) return "Not a full path.";
        if (!IconFiles.Contains(Path.GetExtension(file))) return "Icons are read only from programs, libraries, and icon files.";
        if (IsNetworkOrRemovable(file) && (!allowNetworkAndRemovable || file.StartsWith(@"\\", StringComparison.Ordinal))) return "The icon is not on this computer.";
        try
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess | FileAttributes.Directory)) != 0) return "Not a local file.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "The icon file cannot be read.";
        }
        return null;
    }

    /// <summary>Why no Shell handler may run for this file, or null when one may.</summary>
    public static string? Refusal(string path, FileAttributes attributes, bool allowNetworkAndRemovable)
    {
        if (!OperatingSystem.IsWindows()) return "Shell pictures exist only on Windows.";
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0')) return "Not a full path.";
        string name = Path.GetFileName(path);
        if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) return "Folder customization files are never handed to the Shell.";
        if (Excluded.Contains(Path.GetExtension(name))) return "Shortcut-like files are never handed to the Shell: their handlers follow paths stored inside them.";
        if ((attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0) return "Cloud placeholders are not downloaded for a picture.";
        if ((attributes & FileAttributes.Directory) != 0) return "Folders keep their type icon.";
        if (!allowNetworkAndRemovable && IsNetworkOrRemovable(path)) return "Shell pictures on network and removable drives are off (Settings → Privacy).";
        return null;
    }

    public static bool IsNetworkOrRemovable(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // \\?\C:\… is a local long path; \\server\share and \\?\UNC\… are network paths.
            if (!path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\.\", StringComparison.Ordinal)) return true;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return true;
            path = path[4..];
        }
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return true;
        try
        {
            return new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Ram);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}

/// <summary>
/// Shell pictures for FileCat's views: requests are served newest first (what is on screen now matters most),
/// identical requests share one answer, answers and misses are cached, and nothing reaches the helper that
/// <see cref="ShellPreviewPolicy"/> refuses.
/// </summary>
public sealed class ShellPreviews : IDisposable
{
    private const int CacheLimit = 1024;
    private readonly ShellHostClient _client;
    private readonly Func<bool> _allowNetworkAndRemovable;
    private readonly object _lock = new();
    private readonly List<Request> _pending = [];
    /// <summary>The request the helper works on now: the same picture asked for meanwhile waits for its answer.</summary>
    private Request? _running;
    private readonly Dictionary<string, ShellImage?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _cacheOrder = new();
    /// <summary>
    /// How often asking the helper for this picture failed outright (the helper died on it, most often because the
    /// Shell handler it ran did). A failure is not an answer: it is not remembered as "this file has no picture", so
    /// the next time the picture is wanted a fresh helper is asked. A handler that brings the helper down every time
    /// would otherwise be asked for ever, so after <see cref="MostAttempts"/> tries the miss is remembered after all.
    /// </summary>
    private readonly Dictionary<string, int> _failures = new(StringComparer.OrdinalIgnoreCase);
    private const int MostAttempts = 3;
    private readonly Thread _worker;
    private readonly SemaphoreSlim _signal = new(0);
    private volatile bool _disposed;

    public ShellPreviews(ShellHostClient client, Func<bool> allowNetworkAndRemovable)
    {
        _client = client;
        _allowNetworkAndRemovable = allowNetworkAndRemovable;
        _worker = new Thread(Work) { IsBackground = true, Name = "FileCat Shell pictures" };
        _worker.Start();
    }

    public ShellHostClient Client => _client;

    /// <summary>
    /// Set while FileCat recovers deleted files from the disk that holds the Shell's picture caches (release plan V09, I09):
    /// the helper asks for nothing, since the Shell writes what it draws into those caches. Pictures already known stay.
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>Why an icon a user's file names may not be read under this session's settings, or null when it may.</summary>
    public string? IconResourceRefusal(IconLocation location) => ShellPreviewPolicy.IconResourceRefusal(location, _allowNetworkAndRemovable());

    /// <summary>Tests: runs on the worker after it took a request and before it asks the helper.</summary>
    internal Action<string>? BeforeHelperRequest { get; init; }

    /// <summary>Tests: stands in for the helper, so that each kind of answer can be given on purpose.</summary>
    internal Func<ShellImageKind, string, int, TimeSpan, (ShellAnswer Answer, ShellImage? Image)>? AskForTests { get; init; }

    private int _helperRequests;

    /// <summary>How many requests reached the helper (tests).</summary>
    internal int HelperRequests => Volatile.Read(ref _helperRequests);

    /// <summary>Set when the helper failed repeatedly or could not start.</summary>
    public string? DisabledReason => _client.DisabledReason;

    private sealed record Request(string Key, ShellImageKind Kind, string Path, int Size, TimeSpan Timeout)
    {
        public List<(TaskCompletionSource<(ShellImage? Image, ShellAnswer Answer)> Tcs, CancellationToken Ct)> Waiters { get; } = [];
    }

    private static string KeyOf(ShellImageKind kind, string path, long modifiedTicks, int size) => $"{(int)kind}|{size}|{modifiedTicks}|{path}";

    /// <summary>A cached answer (true, possibly a cached miss), or false when none is known yet.</summary>
    public bool TryGetCached(ShellImageKind kind, string path, long modifiedTicks, int size, out ShellImage? image)
    {
        lock (_lock) return _cache.TryGetValue(KeyOf(kind, path, modifiedTicks, size), out image);
    }

    /// <summary>The picture, or null when policy refuses, the type has none, or the handler failed.</summary>
    public async Task<ShellImage?> GetAsync(ShellImageKind kind, string path, long modifiedTicks, FileAttributes attributes, int size, CancellationToken ct) =>
        (await GetWithAnswerAsync(kind, path, modifiedTicks, attributes, size, ct).ConfigureAwait(false)).Image;

    /// <summary>
    /// The picture for something on screen now, which is asked for once and then shown or not: when no helper
    /// answered (it was still starting, or it had just died), it is asked once more, which starts a fresh one. A
    /// refusal or a real "this file has none" is final, and nothing is asked while pictures are paused (I09).
    /// </summary>
    public async Task<ShellImage?> GetForDisplayAsync(ShellImageKind kind, string path, long modifiedTicks, FileAttributes attributes, int size, CancellationToken ct)
    {
        var (image, answer) = await GetWithAnswerAsync(kind, path, modifiedTicks, attributes, size, ct).ConfigureAwait(false);
        if (image is not null || answer != ShellAnswer.Failed || Paused || ct.IsCancellationRequested) return image;
        return (await GetWithAnswerAsync(kind, path, modifiedTicks, attributes, size, ct).ConfigureAwait(false)).Image;
    }

    /// <summary>
    /// The picture, with what came of asking for it. <see cref="ShellAnswer.Failed"/> means nothing is known about this
    /// file yet — the helper did not answer, or pictures were paused, or the wait was given up — so a caller that
    /// remembers answers of its own must not remember this one.
    /// </summary>
    public Task<(ShellImage? Image, ShellAnswer Answer)> GetWithAnswerAsync(ShellImageKind kind, string path, long modifiedTicks, FileAttributes attributes, int size, CancellationToken ct)
    {
        if (_disposed || _client.DisabledReason is not null) return Task.FromResult<(ShellImage?, ShellAnswer)>((null, ShellAnswer.Refused));
        if (Paused)
        {
            // Paused is this moment's state, not this file's: what is already known still shows, and the rest is
            // asked again once the scan that paused it is over.
            lock (_lock)
                return Task.FromResult(_cache.TryGetValue(KeyOf(kind, path, modifiedTicks, size), out var known)
                    ? (known, ShellAnswer.Answered)
                    : ((ShellImage?)null, ShellAnswer.Failed));
        }
        if (kind == ShellImageKind.IconResource
                ? IconResourceRequest.Parse(path) is not { } location || ShellPreviewPolicy.IconResourceRefusal(location, _allowNetworkAndRemovable()) is not null
                : ShellPreviewPolicy.Refusal(path, attributes, _allowNetworkAndRemovable()) is not null)
            return Task.FromResult<(ShellImage?, ShellAnswer)>((null, ShellAnswer.Refused));
        string key = KeyOf(kind, path, modifiedTicks, size);
        var tcs = new TaskCompletionSource<(ShellImage? Image, ShellAnswer Answer)>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached)) return Task.FromResult((cached, ShellAnswer.Answered));
            // Release issue I29: a request the helper already works on is joined, not asked of the helper again.
            if (_running is { } running && running.Key == key)
            {
                running.Waiters.Add((tcs, ct));
                if (ct.CanBeCanceled) ct.Register(() => tcs.TrySetResult((null, ShellAnswer.Failed)));
                return tcs.Task;
            }
            var request = _pending.FirstOrDefault(r => r.Key == key);
            if (request is null)
            {
                request = new Request(key, kind, path, size, kind is ShellImageKind.Icon or ShellImageKind.OverlayIcon ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(8));
                _pending.Add(request);
            }
            else
            {
                // Asked again: it moves to the front.
                _pending.Remove(request);
                _pending.Add(request);
            }
            request.Waiters.Add((tcs, ct));
        }
        if (ct.CanBeCanceled) ct.Register(() => tcs.TrySetResult((null, ShellAnswer.Failed)));
        _signal.Release();
        return tcs.Task;
    }

    private void Work()
    {
        while (!_disposed)
        {
            _signal.Wait();
            while (!_disposed && Next() is { } request)
            {
                ShellImage? image = null;
                var answer = ShellAnswer.Failed;
                BeforeHelperRequest?.Invoke(request.Key);
                Interlocked.Increment(ref _helperRequests);
                try { image = Ask(request); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException) { }
                bool failed = answer == ShellAnswer.Failed;

                ShellImage? Ask(Request r)
                {
                    if (AskForTests is { } fake)
                    {
                        var (kind, picture) = fake(r.Kind, r.Path, r.Size, r.Timeout);
                        answer = kind;
                        return picture;
                    }
                    return _client.Get(r.Kind, r.Path, r.Size, r.Timeout, out answer);
                }
                List<(TaskCompletionSource<(ShellImage? Image, ShellAnswer Answer)> Tcs, CancellationToken Ct)> waiters;
                lock (_lock)
                {
                    // Counting tries costs memory of its own; past the cache's size, start the counts over.
                    if (_failures.Count > CacheLimit) _failures.Clear();
                    int attempts = failed ? _failures[request.Key] = _failures.GetValueOrDefault(request.Key) + 1 : 0;
                    if (!failed || attempts >= MostAttempts)
                    {
                        _failures.Remove(request.Key);
                        _cache[request.Key] = image;
                        _cacheOrder.Enqueue(request.Key);
                        while (_cacheOrder.Count > CacheLimit)
                        {
                            string oldest = _cacheOrder.Dequeue();
                            _cache.Remove(oldest);
                            _failures.Remove(oldest);
                        }
                    }
                    _running = null;
                    waiters = [.. request.Waiters];
                }
                foreach (var (tcs, _) in waiters) tcs.TrySetResult((image, answer));
            }
        }
    }

    /// <summary>The newest request someone still waits for; abandoned ones are dropped.</summary>
    private Request? Next()
    {
        lock (_lock)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var r = _pending[i];
                _pending.RemoveAt(i);
                // Paused meanwhile: what was asked for before is not asked of the helper either.
                if (!Paused && r.Waiters.Any(w => !w.Ct.IsCancellationRequested)) return _running = r;
                // Dropped because pictures were paused meanwhile, or nobody waits any more: no answer about the file.
                foreach (var (tcs, _) in r.Waiters) tcs.TrySetResult((null, ShellAnswer.Failed));
            }
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _signal.Release();
        _client.Dispose();
        lock (_lock)
        {
            foreach (var r in _pending)
                foreach (var (tcs, _) in r.Waiters) tcs.TrySetResult((null, ShellAnswer.Refused));
            _pending.Clear();
        }
    }
}
