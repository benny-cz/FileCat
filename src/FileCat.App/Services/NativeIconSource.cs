using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FileCat.Core.Platform;
using FileCat.Core.Resources;

namespace FileCat.App.Services;

/// <summary>
/// Extension-based native icons (Windows): the Shell is asked by extension only, never by file content,
/// so no third-party handler runs on untrusted files (plan §8.2, AI-14). Icons load on one STA thread and
/// appear when ready; rows use the vector icon meanwhile.
/// </summary>
public sealed class NativeIconSource : INativeIconSource
{
    private const int Size = 16;
    // Types whose icon depends on the file itself: show a generic per-type icon only.
    private static readonly HashSet<string> PerFileTypes = new(StringComparer.OrdinalIgnoreCase) { "exe", "ico", "lnk", "url", "cur", "ani", "scr", "msc", "appref-ms", "library-ms", "searchconnector-ms" };

    private readonly IShellServices _shell;
    private readonly ConcurrentDictionary<string, IImage?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<string> _queue = new();
    private readonly Thread _thread;
    private int _pendingNotify;

    private NativeIconSource(IShellServices shell)
    {
        _shell = shell;
        _thread = new Thread(Worker) { IsBackground = true, Name = "FileCat icons" };
        if (OperatingSystem.IsWindows()) _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public event Action? IconsLoaded;

    public static INativeIconSource? TryCreate(IShellServices shell) =>
        OperatingSystem.IsWindows() ? new NativeIconSource(shell) : null;

    public IImage? GetIcon(in EntryData entry)
    {
        string key = entry.Kind switch
        {
            EntryKind.Directory => entry.Has(EntryFlags.Link) ? "<dirlink>" : "<dir>",
            EntryKind.Drive => "<drive>",
            _ => NameParts.GetExtension(entry.Name) is { Length: > 0 } ext ? "." + ext : "<file>",
        };
        if (_cache.TryGetValue(key, out var img)) return img;
        if (_cache.TryAdd(key, null)) _queue.Add(key);
        return null;
    }

    private void Worker()
    {
        foreach (var key in _queue.GetConsumingEnumerable())
        {
            IImage? image = null;
            try
            {
                bool isDir = key is "<dir>" or "<dirlink>" or "<drive>";
                var name = key.StartsWith('.') ? "file" + key : isDir ? "folder" : "file";
                if (key.StartsWith('.') && PerFileTypes.Contains(key[1..])) name = "file.bin";
                if (_shell.TryGetTypeIcon(name, isDir, Size, out int w, out int h, out var bgra) && w > 0 && h > 0)
                    image = ToBitmap(w, h, bgra);
            }
            catch (Exception)
            {
                image = null;
            }
            if (image is not null)
            {
                _cache[key] = image;
                if (Interlocked.Exchange(ref _pendingNotify, 1) == 0)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        Interlocked.Exchange(ref _pendingNotify, 0);
                        IconsLoaded?.Invoke();
                    }, DispatcherPriority.Background);
                }
            }
        }
    }

    private static Bitmap ToBitmap(int w, int h, byte[] bgra)
    {
        var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = bmp.Lock();
        for (int y = 0; y < h; y++)
            Marshal.Copy(bgra, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
        return bmp;
    }
}
