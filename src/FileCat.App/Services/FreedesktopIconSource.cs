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
/// Linux icons from the desktop's icon theme, as its file manager shows them: types by the shared MIME database,
/// the user's home and special folders (Documents, Downloads, …), and drives by kind. Lookups and drawing
/// run on one background thread at the display's pixel size; rows show a vector icon meanwhile.
/// </summary>
public sealed class FreedesktopIconSource : INativeIconSource
{
    private readonly FreedesktopIcons _icons;
    private readonly ConcurrentDictionary<(int Size, string Key), IImage?> _cache = new();
    private readonly BlockingCollection<(int Size, string Key)> _queue = new();
    private readonly Thread _thread;
    private int _pendingNotify;

    private FreedesktopIconSource(FreedesktopIcons icons)
    {
        _icons = icons;
        _thread = new Thread(Worker) { IsBackground = true, Name = "FileCat icons" };
        _thread.Start();
        // Symbolic icons are drawn in the theme's text color: a new theme draws them again (this source lives as long as
        // FileCat, so the static event keeps nothing alive that would otherwise go).
        ThemeManager.ThemeChanged += () =>
        {
            _cache.Clear();
            IconsLoaded?.Invoke();
        };
    }

    public event Action? IconsLoaded;

    public int PixelSize { get; private set; } = 16;

    public static INativeIconSource? TryCreate() => FreedesktopIcons.TryCreate() is { } icons ? new FreedesktopIconSource(icons) : null;

    public void SetPixelSize(int size) => PixelSize = Math.Clamp(size, 16, 64);

    public IImage? LinkOverlay => Get("emblem:emblem-symbolic-link");

    public IImage? GetIcon(in EntryData entry, Location? folder = null)
    {
        switch (entry.Kind)
        {
            case EntryKind.Directory:
                var parent = entry.Tag is Core.Search.ResultTag r ? r.Parent : folder;
                if (parent is { IsFileSystem: true } && _icons.SpecialFolder(Path.Join(parent.Path, entry.Name)) is { } special) return Get("names:" + special + ",folder");
                return Get("names:folder");
            case EntryKind.Drive:
                return Get(entry.Tag is DriveTag drive ? drive.DriveType switch
                {
                    "Removable" => "names:drive-removable-media,drive-harddisk",
                    "CDRom" => "names:drive-optical,drive-harddisk",
                    "Network" => "names:folder-remote,network-server,drive-harddisk",
                    _ => "names:drive-harddisk",
                } : "names:drive-harddisk");
            case EntryKind.Server:
            case EntryKind.Share:
                return Get("names:network-server,folder-remote");
            case EntryKind.File:
                // By name, as the shared MIME database defines types (one cache entry per extension).
                return Get("file:" + NameParts.GetExtension(entry.Name).ToLowerInvariant());
            default:
                return null;
        }
    }

    public IImage? GetPlaceIcon(IconKind kind) => kind switch
    {
        IconKind.Computer => Get("names:computer,user-desktop"),
        IconKind.Phone => Get("names:phone,multimedia-player,drive-removable-media"),
        IconKind.Collection => Get("names:folder-saved-search,edit-find"),
        IconKind.Server or IconKind.Share => Get("names:network-server,folder-remote"),
        IconKind.Network => Get("names:network-workgroup,network-server,folder-remote"),
        IconKind.RecycleBin => Get("names:user-trash"),
        IconKind.RecycleBinFull => Get("names:user-trash-full,user-trash"),
        _ => null,
    };

    private IImage? Get(string key)
    {
        var sized = (PixelSize, key);
        if (_cache.TryGetValue(sized, out var image)) return image;
        if (_cache.TryAdd(sized, null)) _queue.Add(sized);
        return null;
    }

    private void Worker()
    {
        foreach (var (size, key) in _queue.GetConsumingEnumerable())
        {
            IImage? image = null;
            try
            {
                IReadOnlyList<string> names = key switch
                {
                    _ when key.StartsWith("names:", StringComparison.Ordinal) => key[6..].Split(','),
                    _ when key.StartsWith("emblem:", StringComparison.Ordinal) => [key[7..]],
                    _ => _icons.NamesForFile("file." + key[5..], executable: false),
                };
                if (_icons.Find(names, size) is { } file && FreedesktopIcons.TryRender(file, size, out int w, out int h, out var bgra))
                {
                    if (FreedesktopIcons.IsSymbolic(file)) Tint(bgra, ThemeManager.Current.Text);
                    image = ToBitmap(w, h, bgra);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
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

    /// <summary>A symbolic icon in <paramref name="color"/>: its own color is a placeholder, as GTK treats it (I27).</summary>
    internal static void Tint(byte[] bgra, string color)
    {
        var c = Color.Parse(color);
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            bgra[i] = c.B;
            bgra[i + 1] = c.G;
            bgra[i + 2] = c.R;
        }
    }

    private static Bitmap ToBitmap(int w, int h, byte[] bgra)
    {
        double dpi = 96.0 * w / 16;
        var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(dpi, dpi), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = bmp.Lock();
        for (int y = 0; y < h; y++)
            Marshal.Copy(bgra, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
        return bmp;
    }
}
