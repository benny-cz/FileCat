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
    private readonly Dictionary<string, ShellImage?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _cacheOrder = new();
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

    /// <summary>Set when the helper failed repeatedly or could not start.</summary>
    public string? DisabledReason => _client.DisabledReason;

    private sealed record Request(string Key, ShellImageKind Kind, string Path, int Size, TimeSpan Timeout)
    {
        public List<(TaskCompletionSource<ShellImage?> Tcs, CancellationToken Ct)> Waiters { get; } = [];
    }

    private static string KeyOf(ShellImageKind kind, string path, long modifiedTicks, int size) => $"{(int)kind}|{size}|{modifiedTicks}|{path}";

    /// <summary>A cached answer (true, possibly a cached miss), or false when none is known yet.</summary>
    public bool TryGetCached(ShellImageKind kind, string path, long modifiedTicks, int size, out ShellImage? image)
    {
        lock (_lock) return _cache.TryGetValue(KeyOf(kind, path, modifiedTicks, size), out image);
    }

    /// <summary>The picture, or null when policy refuses, the type has none, or the handler failed.</summary>
    public Task<ShellImage?> GetAsync(ShellImageKind kind, string path, long modifiedTicks, FileAttributes attributes, int size, CancellationToken ct)
    {
        if (_disposed || _client.DisabledReason is not null) return Task.FromResult<ShellImage?>(null);
        if (kind == ShellImageKind.IconResource
                ? IconResourceRequest.Parse(path) is not { } location || ShellPreviewPolicy.IconResourceRefusal(location, _allowNetworkAndRemovable()) is not null
                : ShellPreviewPolicy.Refusal(path, attributes, _allowNetworkAndRemovable()) is not null)
            return Task.FromResult<ShellImage?>(null);
        string key = KeyOf(kind, path, modifiedTicks, size);
        var tcs = new TaskCompletionSource<ShellImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached)) return Task.FromResult(cached);
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
        if (ct.CanBeCanceled) ct.Register(() => tcs.TrySetResult(null));
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
                try { image = _client.Get(request.Kind, request.Path, request.Size, request.Timeout); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException) { }
                lock (_lock)
                {
                    _cache[request.Key] = image;
                    _cacheOrder.Enqueue(request.Key);
                    while (_cacheOrder.Count > CacheLimit) _cache.Remove(_cacheOrder.Dequeue());
                }
                foreach (var (tcs, _) in request.Waiters) tcs.TrySetResult(image);
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
                if (r.Waiters.Any(w => !w.Ct.IsCancellationRequested)) return r;
                foreach (var (tcs, _) in r.Waiters) tcs.TrySetResult(null);
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
                foreach (var (tcs, _) in r.Waiters) tcs.TrySetResult(null);
            _pending.Clear();
        }
    }
}
