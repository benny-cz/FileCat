using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FileCat.Core.FileSystem;
using FileCat.Core.Platform;
using FileCat.Core.Resources;

namespace FileCat.App.Services;

/// <summary>
/// macOS icons as Finder shows them: files by type, and folders, applications, and volumes by their own icon (special
/// folders, custom icons, app icons). Drawn on one background thread at the display's pixel size (Retina: 32 pixels).
/// </summary>
public sealed class MacIconSource : INativeIconSource
{
    private const int PerPathLimit = 4096;
    private readonly ConcurrentDictionary<(int Size, string Key), IImage?> _cache = new();
    private readonly BlockingCollection<(int Size, string Key)> _queue = new();
    private readonly Thread _thread;
    private int _pendingNotify;

    private MacIconSource()
    {
        _thread = new Thread(Worker) { IsBackground = true, Name = "FileCat icons" };
        _thread.Start();
    }

    public event Action? IconsLoaded;

    public int PixelSize { get; private set; } = 16;

    public static INativeIconSource? TryCreate() => OperatingSystem.IsMacOS() ? new MacIconSource() : null;

    public void SetPixelSize(int size) => PixelSize = Math.Clamp(size, 16, 64);

    public IImage? GetIcon(in EntryData entry, Location? folder = null)
    {
        var parent = entry.Tag is Core.Search.ResultTag r ? r.Parent : folder;
        switch (entry.Kind)
        {
            case EntryKind.Directory when parent is { IsFileSystem: true }:
                return Get("path:" + Path.Join(parent.Path, entry.Name));
            case EntryKind.Directory:
                return Get("type:folder");
            case EntryKind.Drive when entry.Tag is DriveTag drive:
                return Get("path:" + drive.RootPath);
            case EntryKind.File:
                return Get("type:" + NameParts.GetExtension(entry.Name).ToLowerInvariant());
            default:
                return null;
        }
    }

    private IImage? Get(string key)
    {
        var sized = (PixelSize, key);
        if (_cache.TryGetValue(sized, out var image)) return image;
        if (_cache.Count > PerPathLimit) _cache.Clear();
        if (_cache.TryAdd(sized, null)) _queue.Add(sized);
        return null;
    }

    private void Worker()
    {
        if (!OperatingSystem.IsMacOS()) return;
        foreach (var (size, key) in _queue.GetConsumingEnumerable())
        {
            IImage? image = null;
            try
            {
                bool drawn = key.StartsWith("path:", StringComparison.Ordinal)
                    ? MacIcons.TryPathIcon(key[5..], size, out int w, out int h, out var bgra)
                    : MacIcons.TryTypeIcon(key == "type:folder" ? "public.folder" : key[5..], size, out w, out h, out bgra);
                if (drawn) image = ToBitmap(w, h, bgra);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or IOException)
            {
                image = null;
            }
            if (image is null) continue;
            _cache[(size, key)] = image;
            if (Interlocked.Exchange(ref _pendingNotify, 1) != 0) continue;
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _pendingNotify, 0);
                IconsLoaded?.Invoke();
            }, DispatcherPriority.Background);
        }
    }

    private static Bitmap ToBitmap(int w, int h, byte[] bgra)
    {
        double dpi = 96.0 * w / 16;
        var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(dpi, dpi), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bmp.Lock();
        for (int y = 0; y < h; y++)
            Marshal.Copy(bgra, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
        return bmp;
    }
}
