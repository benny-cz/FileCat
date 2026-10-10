using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FileCat.Core.FileSystem;
using FileCat.Core.Platform;
using FileCat.Core.Resources;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Shell;

namespace FileCat.App.Services;

/// <summary>
/// Windows icons as Explorer shows them (plan §8.2): types by extension; drives by kind, the system drive with its
/// logo; known folders (Documents, Downloads, OneDrive, …) with their own icons wherever they were moved; programs and
/// icon files with their own icons; and shortcuts, Internet shortcuts, and customized folders with the icon they name.
/// FileCat reads those files itself, never through the Shell's handlers for them, and takes a named icon only from this
/// computer's drives, through the restricted helper: a server named there would learn the user's credentials (AI-14).
/// Everything loads off the UI thread at the display's pixel size and appears when ready; rows show a vector icon
/// meanwhile.
/// </summary>
public sealed class NativeIconSource : INativeIconSource
{
    // Types whose icon depends on the file itself: their type icon is a generic one.
    private static readonly HashSet<string> PerFileTypes = new(StringComparer.OrdinalIgnoreCase) { "exe", "ico", "lnk", "cur", "ani", "scr", "msc", "appref-ms", "library-ms", "searchconnector-ms" };
    // Of those, the ones whose own icon the helper may extract (the rest follow paths stored inside them).
    private static readonly HashSet<string> OwnIconTypes = new(StringComparer.OrdinalIgnoreCase) { "exe", "ico", "cur", "ani", "scr", "msc", "cpl" };
    // Files that name another item's icon, which FileCat reads itself.
    public static readonly HashSet<string> ShortcutTypes = new(StringComparer.OrdinalIgnoreCase) { "lnk", "url" };
    private const FileAttributes Placeholder = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000;

    /// <summary>What an item shows: its own picture, or a shared icon (a shortcut shows its target's type icon).</summary>
    /// <param name="AskAgain">
    /// The helper never answered (it died, or pictures were paused while FileCat recovered deleted files), so nothing
    /// is known about this item yet: the row shows its type icon for now, and the next time it is drawn it is asked
    /// about again instead of being remembered as having no icon of its own.
    /// </param>
    private sealed record Plan(IImage? Image, string? SharedKey, bool AskAgain = false);

    private readonly IShellServices _shell;
    private readonly Func<ShellPreviews?> _pictures;
    private readonly IconRequestCache _shared = new();
    private readonly AsyncIconRequestCache<Plan> _perItem = new(plan => (plan.Image as IDisposable)?.Dispose());
    private volatile HashSet<string>? _knownNames;
    private readonly Thread _thread;
    private readonly string _systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
    private volatile Dictionary<string, IconLocation>? _knownByPath;
    private volatile Dictionary<Guid, IconLocation>? _knownById;
    private int _pendingNotify;

    private NativeIconSource(IShellServices shell, Func<ShellPreviews?> pictures)
    {
        _shell = shell;
        _pictures = pictures;
        _thread = new Thread(Worker) { IsBackground = true, Name = "FileCat icons" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        // A fixed set of asynchronous consumers bounds native helper/file loads as well as queued demand.
        // Awaiting an empty queue or helper reply uses no thread-pool thread.
        for (int i = 0; i < 4; i++) _ = Task.Run(PerItemWorker);
    }

    public event Action? IconsLoaded;

    /// <summary>Icons are made for this many device pixels (16 at 100% scaling, 24 at 150%).</summary>
    public int PixelSize { get; private set; } = 16;

    public static INativeIconSource? TryCreate(IShellServices shell, Func<ShellPreviews?>? pictures = null) =>
        OperatingSystem.IsWindows() ? new NativeIconSource(shell, pictures ?? (() => null)) : null;

    public void SetPixelSize(int size)
    {
        size = Math.Clamp(size, 16, 64);
        if (size == PixelSize) return;
        PixelSize = size;
        _perItem.Clear();
        _shared.Clear();
    }

    public IImage? GetIcon(in EntryData entry, Location? folder = null)
    {
        var parent = entry.Tag is Core.Search.ResultTag r ? r.Parent : folder;
        string? path = parent is { IsFileSystem: true } && entry.Kind is EntryKind.File or EntryKind.Directory ? Path.Join(parent.Path, entry.Name) : null;
        switch (entry.Kind)
        {
            case EntryKind.Drive:
                return Shared(DriveKey(entry));
            case EntryKind.Server:
                return Shared("stock:" + WindowsIcons.StockServer);
            case EntryKind.Share:
                return Shared("stock:" + WindowsIcons.StockServerShare);
            case EntryKind.Directory:
                // A phone's storage ("Internal shared storage", an SD card) looks like the drive it is.
                if (entry.Tag is FileCat.Platform.Windows.Mtp.MtpObjectTag { IsStorage: true }) return Shared("stock:" + WindowsIcons.StockFixedDrive);
                if (path is not null && IsKnownFolderName(entry.Name) && KnownFolder(path) is { } known) return Shared("res:" + known);
                if (path is not null && ((FileAttributes)entry.Attributes & (FileAttributes.System | FileAttributes.ReadOnly)) != 0 && FromPlan(CustomFolder(path, entry)) is { } custom)
                    return custom;
                return Shared("type:<dir>");
            case EntryKind.File:
                string ext = NameParts.GetExtension(entry.Name);
                if (path is not null && ShortcutTypes.Contains(ext) && FromPlan(Shortcut(path, ext, entry)) is { } linked) return linked;
                if (path is not null && OwnIconTypes.Contains(ext) && OwnIcon(path, entry.Modified, (FileAttributes)entry.Attributes) is { } own) return own;
                return Shared(TypeKey(ext));
            default:
                return null;
        }
    }

    public IImage? GetPlaceIcon(IconKind kind) => kind switch
    {
        IconKind.Computer => Shared("stock:" + WindowsIcons.StockDesktopPc),
        IconKind.Phone => Shared("stock:" + WindowsIcons.StockPhone),
        IconKind.Collection => Shared("stock:" + WindowsIcons.StockStack),
        IconKind.Server => Shared("stock:" + WindowsIcons.StockServer),
        IconKind.Network => Shared("stock:" + WindowsIcons.StockNetwork),
        IconKind.RecycleBin => Shared("stock:" + WindowsIcons.StockRecycler),
        IconKind.RecycleBinFull => Shared("stock:" + WindowsIcons.StockRecyclerFull),
        IconKind.Share => Shared("stock:" + WindowsIcons.StockServerShare),
        IconKind.RegistryKey => RegistryEditorIcon(),
        _ => null,
    };

    /// <summary>The Registry as Windows shows it: Registry Editor's own icon (through the Shell helper, when allowed).</summary>
    private IImage? RegistryEditorIcon()
    {
        var regedit = new FileInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "regedit.exe"));
        try
        {
            return regedit.Exists ? OwnIcon(regedit.FullName, regedit.LastWriteTimeUtc.Ticks, regedit.Attributes) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The installed Shell overlay composed with the ordinary icon, when Windows has assigned one.</summary>
    internal IImage? GetOverlayIcon(string path, long modified, FileAttributes attributes, GitStatusKind status)
    {
        if (_pictures() is not { } pictures || !WindowsIcons.IsLocal(path)) return null;
        int size = PixelSize;
        return FromPlan(PerItem("overlay|" + path + "|" + modified + "|" + status, async () =>
        {
            var image = await pictures.GetAsync(ShellImageKind.OverlayIcon, path, modified, attributes,
                size, CancellationToken.None).ConfigureAwait(false);
            return new Plan(image is null ? null : ShellBitmaps.ToBitmap(image), null);
        }));
    }

    /// <summary>Explorer's shortcut arrow at the current size; null until loaded.</summary>
    public IImage? LinkOverlay => Shared("stock:" + WindowsIcons.StockLink);

    /// <summary>
    /// A type's shared icon: its extension's, or for a name without one Explorer's "no associated program" page, and for
    /// a type whose icon is each file's own (a program before its icon is read) the generic one for such files.
    /// </summary>
    private static string TypeKey(string ext) =>
        ext.Length == 0 ? "type:<file>"
        : PerFileTypes.Contains(ext) ? (ext.Equals("exe", StringComparison.OrdinalIgnoreCase) || ext.Equals("scr", StringComparison.OrdinalIgnoreCase) ? "type:<app>" : "type:<generic>")
        : "type:." + ext;

    private string DriveKey(in EntryData entry)
    {
        if (entry.Tag is not DriveTag drive) return entry.Tag is { } tag && tag.GetType().Name == "MtpDeviceTag" ? "stock:" + WindowsIcons.StockPhone : "stock:" + WindowsIcons.StockFixedDrive;
        if (string.Equals(drive.RootPath, _systemRoot, StringComparison.OrdinalIgnoreCase)) return "sysdrive";
        return "stock:" + (drive.DriveType switch
        {
            "Fixed" => WindowsIcons.StockFixedDrive,
            "Removable" => WindowsIcons.StockRemovableDrive,
            "Network" => drive.Ready ? WindowsIcons.StockNetworkDrive : WindowsIcons.StockNetworkDriveOffline,
            "CDRom" => WindowsIcons.StockOpticalDrive,
            "Ram" => WindowsIcons.StockRamDrive,
            _ => WindowsIcons.StockUnknownDrive,
        });
    }

    private IImage? FromPlan(Plan? plan) => plan is null ? null : plan.Image ?? (plan.SharedKey is { } key ? Shared(key) : null);

    /// <summary>A shared icon at the current size: cached, or queued for the icon thread (null meanwhile).</summary>
    private IImage? Shared(string key) => _shared.Get((PixelSize, key));

    /// <summary>Whether a folder's name is one a known folder has here (checked before building its path).</summary>
    private bool IsKnownFolderName(string name)
    {
        if (_knownNames is { } names) return names.Contains(name);
        // The icon thread reads these once at startup; rows repaint when they are available.
        return false;
    }

    private IconLocation? KnownFolder(string path) =>
        _knownByPath is { } map && map.TryGetValue(path.TrimEnd('\\'), out var location) ? location : null;

    // ---- Items that name their own icon -----------------------------------------------------------------------

    private Plan? PerItem(string key, Func<Task<Plan>> load) => _perItem.Get((PixelSize, key), load);

    private async Task PerItemWorker()
    {
        await foreach (var request in _perItem.Requests().ConfigureAwait(false))
        {
            Plan result;
            try { result = await request.Load().ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
            {
                result = new Plan(null, null);
            }
            if (_perItem.Complete(request, result, result.AskAgain) && (result.Image is not null || result.SharedKey is not null))
                NotifyLoaded();
        }
    }

    /// <summary>A shortcut shows the icon it names, else its target's (own or type) icon, else the plain type icon.</summary>
    private Plan? Shortcut(string path, string ext, in EntryData entry)
    {
        if (((FileAttributes)entry.Attributes & Placeholder) != 0 || entry.Size > ShellFileIcons.MaxBytes) return null;
        int size = PixelSize;
        return PerItem("lnk|" + path + "|" + entry.Modified, async () =>
        {
            byte[] bytes;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan))
            {
                if (file.Length > ShellFileIcons.MaxBytes) return new Plan(null, null);
                bytes = new byte[file.Length];
                file.ReadExactly(bytes);
            }
            string folder = Path.GetDirectoryName(path) ?? path;
            var info = ext.Equals("url", StringComparison.OrdinalIgnoreCase)
                ? ShellFileIcons.ReadInternetShortcut(ShellFileIcons.DecodeText(bytes), folder)
                : ShellFileIcons.ReadShortcut(bytes, folder);
            if (info is null) return new Plan(null, null);
            if (info.IconFile is { } iconFile)
            {
                var (named, askAgain) = await Resource(new IconLocation(iconFile, info.IconIndex), size).ConfigureAwait(false);
                if (named is not null || askAgain) return new Plan(named, null, askAgain);
            }
            if (info.KnownFolder is { } id && _knownById is { } byId && byId.TryGetValue(id, out var knownIcon)) return new Plan(null, "res:" + knownIcon);
            if (info.TargetPath is { } target)
            {
                if (info.TargetIsDirectory)
                    return new Plan(null, KnownFolder(target) is { } folderIcon ? "res:" + folderIcon : "type:<dir>");
                string targetExt = NameParts.GetExtension(Path.GetFileName(target));
                if (OwnIconTypes.Contains(targetExt) && _pictures() is { } pictures && WindowsIcons.IsLocal(target))
                {
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(target); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { attributes = FileAttributes.Normal; }
                    var image = await pictures.GetAsync(ShellImageKind.Icon, target, File.GetLastWriteTimeUtc(target).Ticks, attributes, size, CancellationToken.None).ConfigureAwait(false);
                    if (image is not null) return new Plan(ShellBitmaps.ToBitmap(image), null);
                }
                if (targetExt.Length > 0) return new Plan(null, TypeKey(targetExt));
            }
            return new Plan(null, null);
        });
    }

    /// <summary>A folder whose desktop.ini names an icon (Explorer reads it only for read-only or system folders).</summary>
    private Plan? CustomFolder(string path, in EntryData entry)
    {
        int size = PixelSize;
        return PerItem("dir|" + path + "|" + entry.Modified, async () =>
        {
            string ini = Path.Join(path, "desktop.ini");
            var fileInfo = new FileInfo(ini);
            if (!fileInfo.Exists || fileInfo.Length > ShellFileIcons.MaxBytes || (fileInfo.Attributes & Placeholder) != 0) return new Plan(null, null);
            var info = ShellFileIcons.ReadFolderIcon(ShellFileIcons.DecodeText(await File.ReadAllBytesAsync(ini).ConfigureAwait(false)), path);
            if (info?.IconFile is not { } iconFile) return new Plan(null, null);
            var (named, askAgain) = await Resource(new IconLocation(iconFile, info.IconIndex), size).ConfigureAwait(false);
            return new Plan(named, null, askAgain);
        });
    }

    /// <summary>An icon a user's file names, read by the restricted helper under its policy (local files only).</summary>
    private async Task<(IImage? Image, bool AskAgain)> Resource(IconLocation location, int size)
    {
        if (_pictures() is not { } pictures) return (null, false);
        if (ResourceTime(location, pictures.IconResourceRefusal) is not { } modified) return (null, false);
        var (image, answer) = await pictures.GetWithAnswerAsync(ShellImageKind.IconResource, IconResourceRequest.Format(location), modified, FileAttributes.Normal, size, CancellationToken.None).ConfigureAwait(false);
        return (image is null ? null : ShellBitmaps.ToBitmap(image), answer == ShellAnswer.Failed);
    }

    /// <summary>
    /// The icon file's time (part of the helper's cache key), or null when the helper's policy refuses that file. The
    /// policy decides first (release plan I16): a path a user's file names is not touched at all when it is not on this
    /// computer, as reading even its time would connect to the server it names.
    /// </summary>
    internal static long? ResourceTime(IconLocation location, Func<IconLocation, string?> refusal)
    {
        if (refusal(location) is not null) return null;
        try { return File.GetLastWriteTimeUtc(location.File).Ticks; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>A program's own icon from the helper once extracted; null meanwhile, when refused, or when it has none.</summary>
    private IImage? OwnIcon(string path, long modified, FileAttributes attributes)
    {
        if (_pictures() is not { } pictures) return null;
        int size = PixelSize;
        return FromPlan(PerItem("own|" + path + "|" + modified, async () =>
        {
            var (image, answer) = await pictures.GetWithAnswerAsync(ShellImageKind.Icon, path, modified, attributes, size, CancellationToken.None).ConfigureAwait(false);
            return new Plan(image is null ? null : ShellBitmaps.ToBitmap(image), null, answer == ShellAnswer.Failed);
        }));
    }

    private void NotifyLoaded()
    {
        if (Interlocked.Exchange(ref _pendingNotify, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _pendingNotify, 0);
            IconsLoaded?.Invoke();
        }, DispatcherPriority.Background);
    }

    // ---- The icon thread: shared icons from locations FileCat trusts -----------------------------------------

    private void Worker()
    {
        // Known-folder initialization is one bounded operation, independent of the bounded row request queue.
        try
        {
            var folders = WindowsIcons.KnownFolders();
            _knownById = folders.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First().Icon);
            var byPath = folders.Where(f => f.Path is not null).GroupBy(f => f.Path!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Icon, StringComparer.OrdinalIgnoreCase);
            _knownByPath = byPath;
            _knownNames = byPath.Keys.Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            NotifyLoaded();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or COMException)
        {
            _knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        foreach (var request in _shared.Requests()) LoadShared(request);
    }

    // The worker waits indefinitely for another request. End the previous image's local lifetime first.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LoadShared(IconRequestCache.Request request)
    {
        var sized = request.Key;
        IImage? image = null;
        try
        {
            image = Load(sized.Key, sized.Size);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or COMException)
        {
            image = null;
        }
        if (_shared.Complete(request, image) && image is not null) NotifyLoaded();
    }

    private IImage? Load(string key, int size)
    {
        IconLocation? location = key switch
        {
            "sysdrive" => new IconLocation(Path.Combine(Environment.SystemDirectory, "imageres.dll"), -36),
            _ when key.StartsWith("stock:", StringComparison.Ordinal) => WindowsIcons.StockLocation(int.Parse(key.AsSpan(6), System.Globalization.CultureInfo.InvariantCulture)),
            _ when key.StartsWith("res:", StringComparison.Ordinal) => IconLocation.Parse(key[4..]),
            "type:<dir>" => WindowsIcons.TypeLocation("folder", true),
            // Not another type's icon: ".bin" is VLC's or a disc tool's on many computers.
            "type:<file>" or "type:<generic>" => WindowsIcons.StockLocation(WindowsIcons.StockDocumentNoAssociation),
            "type:<app>" => WindowsIcons.StockLocation(WindowsIcons.StockApplication),
            _ => WindowsIcons.TypeLocation("file" + key[5..], false),
        };
        if (location is { } l && WindowsIcons.TryExtract(l, size, out int w, out int h, out var bgra)) return ToBitmap(w, h, bgra, size);
        if (key == "sysdrive") return Load("stock:" + WindowsIcons.StockFixedDrive, size);
        if (!key.StartsWith("type:", StringComparison.Ordinal)) return null;
        // Types drawn by an icon handler have no fixed location: the Shell's small icon (16 pixels) stands in.
        string name = key switch { "type:<dir>" => "folder", "type:<file>" or "type:<generic>" => "file", "type:<app>" => "file.exe", _ => "file" + key[5..] };
        return _shell.TryGetTypeIcon(name, key == "type:<dir>", size, out w, out h, out bgra) && w > 0 ? ToBitmap(w, h, bgra, size) : null;
    }

    private static Bitmap ToBitmap(int w, int h, byte[] bgra, int size)
    {
        // Drawn into 16 device-independent pixels: the resolution says how many pixels that holds.
        double dpi = 96.0 * w / 16;
        var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(dpi, dpi), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = bmp.Lock();
        for (int y = 0; y < h; y++)
            Marshal.Copy(bgra, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
        return bmp;
    }
}
