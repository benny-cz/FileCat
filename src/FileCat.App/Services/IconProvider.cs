using System.Collections.Concurrent;
using Avalonia.Media;
using FileCat.Core.Resources;

namespace FileCat.App.Services;

/// <summary>Icon categories used by the vector fallback and by semantic text colors.</summary>
public enum IconKind
{
    Parent,
    Folder,
    File,
    Archive,
    Executable,
    Image,
    Code,
    Document,
    Audio,
    Video,
    Drive,
    NetworkDrive,
    RemovableDrive,
    Server,
    Share,
    RegistryKey,
    RegistryValue,
}

/// <summary>
/// Supplies row icons. v1 uses file type and extension only (plan §8.2): native per-extension icons on
/// Windows through <see cref="NativeIconSource"/>, and original vector icons elsewhere.
/// </summary>
public sealed class IconProvider
{
    private static readonly HashSet<string> ArchiveExt = new(StringComparer.OrdinalIgnoreCase) { "zip", "7z", "rar", "tar", "gz", "tgz", "bz2", "xz", "zst", "cab", "iso", "jar", "nupkg", "apk", "aab", "whl" };
    private static readonly HashSet<string> ExecExt = new(StringComparer.OrdinalIgnoreCase) { "exe", "com", "bat", "cmd", "ps1", "msi", "msix", "appx", "sh", "app", "scr", "lnk" };
    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { "png", "jpg", "jpeg", "gif", "bmp", "webp", "tif", "tiff", "ico", "svg", "heic", "avif", "raw", "psd" };
    private static readonly HashSet<string> CodeExt = new(StringComparer.OrdinalIgnoreCase) { "cs", "axaml", "xaml", "csproj", "props", "targets", "sln", "slnx", "c", "h", "cpp", "hpp", "rs", "go", "py", "js", "ts", "tsx", "jsx", "java", "kt", "swift", "json", "xml", "yml", "yaml", "toml", "ini", "sql", "css", "scss", "html", "htm", "md", "ps1", "psm1", "lua", "rb", "php", "gradle", "cmake", "mk", "dockerfile" };
    private static readonly HashSet<string> DocExt = new(StringComparer.OrdinalIgnoreCase) { "txt", "log", "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "rtf", "csv", "tsv", "epub" };
    private static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase) { "mp3", "wav", "flac", "ogg", "m4a", "aac", "wma", "opus" };
    private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase) { "mp4", "mkv", "avi", "mov", "wmv", "webm", "m4v", "mpg", "mpeg" };

    private readonly ConcurrentDictionary<(IconKind, string), IImage> _vector = new();
    private readonly ConcurrentDictionary<(GitStatusKind, string), IImage> _gitOverlays = new();

    /// <summary>Optional native provider (Windows extension icons); returns null to fall back to vectors.</summary>
    public INativeIconSource? Native { get; set; }

    public bool UseNativeIcons { get; set; } = true;

    public static IconKind Classify(in EntryData e)
    {
        switch (e.Kind)
        {
            case EntryKind.Parent: return IconKind.Parent;
            case EntryKind.Directory: return IconKind.Folder;
            case EntryKind.Drive:
                return e.Tag is Core.FileSystem.DriveTag t
                    ? t.DriveType switch { "Network" => IconKind.NetworkDrive, "Removable" or "CDRom" => IconKind.RemovableDrive, _ => IconKind.Drive }
                    : IconKind.Drive;
            case EntryKind.Server: return IconKind.Server;
            case EntryKind.Share: return IconKind.Share;
            case EntryKind.RegistryKey: return IconKind.RegistryKey;
            case EntryKind.RegistryValue: return IconKind.RegistryValue;
        }
        if (e.Has(EntryFlags.Container)) return IconKind.Archive;
        var ext = NameParts.GetExtension(e.Name);
        if (ext.Length == 0) return IconKind.File;
        if (ArchiveExt.Contains(ext)) return IconKind.Archive;
        if (ExecExt.Contains(ext)) return IconKind.Executable;
        if (ImageExt.Contains(ext)) return IconKind.Image;
        if (CodeExt.Contains(ext)) return IconKind.Code;
        if (DocExt.Contains(ext)) return IconKind.Document;
        if (AudioExt.Contains(ext)) return IconKind.Audio;
        if (VideoExt.Contains(ext)) return IconKind.Video;
        return IconKind.File;
    }

    /// <param name="folder">Where the entry is listed; lets programs show their own icons through the Shell helper.</param>
    public IImage GetIcon(in EntryData e, Location? folder = null)
    {
        if (UseNativeIcons && Native is not null && e.Kind is EntryKind.File or EntryKind.Directory or EntryKind.Drive)
        {
            var img = Native.GetIcon(e, folder);
            if (img is not null) return ThemeManager.Current.IconTint is { } tint ? Tinted(img, tint) : img;
        }
        var kind = Classify(e);
        return _vector.GetOrAdd((kind, ThemeManager.Current.Name), k => VectorIcons.Create(k.Item1));
    }

    public void ClearCache()
    {
        _vector.Clear();
        _gitOverlays.Clear();
        _vectorOverlay = null;
        _tinted = new();
    }

    private System.Runtime.CompilerServices.ConditionalWeakTable<IImage, IImage> _tinted = new();

    /// <summary>
    /// A theme's one-color version of a native icon (a phosphor screen): each pixel's brightness in the tint's color,
    /// its transparency kept. Made once per icon and theme.
    /// </summary>
    private IImage Tinted(IImage image, string tint)
    {
        if (image is not Avalonia.Media.Imaging.WriteableBitmap source) return image;
        return _tinted.GetValue(image, _ =>
        {
            var color = Color.Parse(tint);
            var size = source.PixelSize;
            var result = new Avalonia.Media.Imaging.WriteableBitmap(size, source.Dpi, Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            using var from = source.Lock();
            using var to = result.Lock();
            bool premultiplied = source.AlphaFormat == Avalonia.Platform.AlphaFormat.Premul;
            var row = new byte[size.Width * 4];
            for (int y = 0; y < size.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(from.Address + y * from.RowBytes, row, 0, row.Length);
                for (int x = 0; x < row.Length; x += 4)
                {
                    int a = row[x + 3];
                    if (a == 0) continue;
                    double b = row[x], g = row[x + 1], r = row[x + 2];
                    if (premultiplied) (b, g, r) = (b * 255 / a, g * 255 / a, r * 255 / a);
                    double light = 0.35 + 0.65 * (0.3 * r + 0.59 * g + 0.11 * b) / 255;
                    row[x] = (byte)(color.B * light * a / 255);
                    row[x + 1] = (byte)(color.G * light * a / 255);
                    row[x + 2] = (byte)(color.R * light * a / 255);
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, to.Address + y * to.RowBytes, row.Length);
            }
            return result;
        });
    }

    /// <summary>The display's scaling where rows are drawn: native icons are made at 16 times this many pixels.</summary>
    public void SetScale(double scaling) => Native?.SetPixelSize((int)Math.Round(16 * Math.Max(1, scaling)));

    /// <summary>Shortcut files, which Explorer marks with its arrow like links.</summary>
    public static bool IsShortcut(in EntryData e) =>
        e.Kind == EntryKind.File && NameParts.GetExtension(e.Name) is var ext &&
        (ext.Equals("lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals("url", StringComparison.OrdinalIgnoreCase));

    private IImage? _vectorOverlay;

    /// <summary>The arrow drawn over links and shortcuts: the platform's where it has one, else FileCat's own.</summary>
    public IImage LinkOverlay => (UseNativeIcons ? Native?.LinkOverlay : null) ?? (_vectorOverlay ??= VectorIcons.LinkOverlay());

    /// <summary>A small, high-contrast Git state mark at the lower right of the ordinary file icon.</summary>
    internal IImage? GitOverlay(GitStatusKind status) => status == GitStatusKind.None ? null
        : _gitOverlays.GetOrAdd((status, ThemeManager.Current.Name), key => VectorIcons.GitOverlay(key.Item1));

    /// <summary>Prefer the installed Windows overlay when available; Git badges remain a portable fallback.</summary>
    internal IImage? ShellOverlayIcon(in EntryData entry, Location? folder, GitStatusKind status)
    {
        if (!UseNativeIcons || Native is not NativeIconSource source ||
            folder is not { IsFileSystem: true } || entry.Kind is not (EntryKind.File or EntryKind.Directory)) return null;
        return source.GetOverlayIcon(Path.Join(folder.Path, entry.Name), entry.Modified, (FileAttributes)entry.Attributes, status);
    }
}

public interface INativeIconSource
{
    /// <summary>Returns a cached icon, or null while it loads or when unavailable. Must not block.</summary>
    IImage? GetIcon(in EntryData entry, Location? folder = null);

    /// <summary>The platform's shortcut arrow drawn over link icons; null to use FileCat's own.</summary>
    IImage? LinkOverlay => null;

    /// <summary>Icons are made for this many device pixels (16 times the display's scaling).</summary>
    void SetPixelSize(int size) { }

    event Action? IconsLoaded;
}

/// <summary>Original, simple 16×16 vector icons (plan §18.2: no third-party icon assets needed).</summary>
public static class VectorIcons
{
    private const string FolderPath = "M1,3.5 C1,2.7 1.6,2 2.4,2 L6,2 L7.6,3.6 L13.6,3.6 C14.4,3.6 15,4.3 15,5.1 L15,12.5 C15,13.3 14.4,14 13.6,14 L2.4,14 C1.6,14 1,13.3 1,12.5 Z";
    private const string FilePath = "M3.5,1 L9.5,1 L13,4.5 L13,14.2 C13,14.6 12.6,15 12.2,15 L3.8,15 C3.4,15 3,14.6 3,14.2 L3,1.5 C3,1.2 3.2,1 3.5,1 Z M9.5,1 L9.5,4.5 L13,4.5";
    private const string ParentPath = "M8,2 L13,7.5 L9.8,7.5 L9.8,14 L6.2,14 L6.2,7.5 L3,7.5 Z";
    private const string DrivePath = "M1.5,5 L14.5,5 L14.5,12 L1.5,12 Z M11.5,8.5 m-1,0 a1,1 0 1,0 2,0 a1,1 0 1,0 -2,0";
    private const string ServerPath = "M3,1.5 L13,1.5 L13,6.5 L3,6.5 Z M3,8.5 L13,8.5 L13,13.5 L3,13.5 Z";
    private const string KeyPath = "M1,4 L6,4 L7,5 L15,5 L15,13 L1,13 Z";

    public static IImage Create(IconKind kind)
    {
        var (path, colorKey, overlay) = kind switch
        {
            IconKind.Parent => (ParentPath, "FcTextMuted", (string?)null),
            IconKind.Folder => (FolderPath, "FcFolderIcon", null),
            IconKind.Archive => (FilePath, "FcArchiveIcon", "M6,5 L7,5 M7,6 L8,6 M6,7 L7,7 M7,8 L8,8 M6,9 L7,9 M6,10.5 L8,10.5 L8,12.5 L6,12.5 Z"),
            IconKind.Executable => (FilePath, "FcExecIcon", "M5.5,8 L7.5,10 L5.5,12 M8.5,12 L10.5,12"),
            IconKind.Image => (FilePath, "FcImageIcon", "M5,12.5 L7,9.5 L8.5,11.2 L9.5,10 L11,12.5 Z"),
            IconKind.Code => (FilePath, "FcCodeIcon", "M6.5,8 L5,10 L6.5,12 M9.5,8 L11,10 L9.5,12"),
            IconKind.Document => (FilePath, "FcFileIcon", "M5,7 L11,7 M5,9 L11,9 M5,11 L9,11"),
            IconKind.Audio => (FilePath, "FcImageIcon", "M7,12 L7,7.5 L10.5,6.8 L10.5,11.2"),
            IconKind.Video => (FilePath, "FcCodeIcon", "M6.5,8 L6.5,12 L10.5,10 Z"),
            IconKind.Drive or IconKind.RemovableDrive => (DrivePath, "FcDriveIcon", null),
            IconKind.NetworkDrive => (DrivePath, "FcDriveIcon", "M8,12 L8,14.5 M5,14.5 L11,14.5"),
            IconKind.Server or IconKind.Share => (ServerPath, "FcDriveIcon", null),
            IconKind.RegistryKey => (KeyPath, "FcFolderIcon", null),
            IconKind.RegistryValue => (FilePath, "FcCodeIcon", "M5.5,7 L10.5,7 M5.5,9.5 L10.5,9.5 M5.5,12 L8.5,12"),
            _ => (FilePath, "FcFileIcon", null),
        };
        var color = Resolve(colorKey);
        var group = new DrawingGroup();
        var fill = new SolidColorBrush(color, kind is IconKind.Folder or IconKind.Parent or IconKind.RegistryKey ? 1.0 : 0.16);
        var stroke = new Pen(new SolidColorBrush(color), 1.1, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        group.Children.Add(new GeometryDrawing
        {
            Geometry = Geometry.Parse(path),
            Brush = fill,
            Pen = kind is IconKind.Folder or IconKind.Parent or IconKind.RegistryKey ? null : stroke,
        });
        if (overlay is not null)
        {
            group.Children.Add(new GeometryDrawing
            {
                Geometry = Geometry.Parse(overlay),
                Pen = new Pen(new SolidColorBrush(color), 1.1, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
                Brush = kind == IconKind.Image || kind == IconKind.Video ? new SolidColorBrush(color) : null,
            });
        }
        // Fix the drawing bounds to the 16×16 box so all icons align.
        group.Children.Insert(0, new GeometryDrawing { Geometry = new RectangleGeometry(new Avalonia.Rect(0, 0, 16, 16)), Brush = Brushes.Transparent });
        return new DrawingImage(group);
    }

    public static IImage LinkOverlay()
    {
        var color = Resolve("FcTextLink");
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing { Geometry = new RectangleGeometry(new Avalonia.Rect(0, 0, 16, 16)), Brush = Brushes.Transparent });
        g.Children.Add(new GeometryDrawing
        {
            Geometry = Geometry.Parse("M0.5,9.5 L6.5,9.5 L6.5,15.5 L0.5,15.5 Z"),
            Brush = new SolidColorBrush(Resolve("FcPanel")),
            Pen = new Pen(new SolidColorBrush(color), 1),
        });
        g.Children.Add(new GeometryDrawing
        {
            Geometry = Geometry.Parse("M2,14 L5,11 M3,11 L5,11 L5,13"),
            Pen = new Pen(new SolidColorBrush(color), 1.1, lineCap: PenLineCap.Round),
        });
        return new DrawingImage(g);
    }

    internal static IImage GitOverlay(GitStatusKind status)
    {
        var palette = ThemeManager.Current;
        var fill = Color.Parse(status switch
        {
            GitStatusKind.Clean => palette.Success,
            GitStatusKind.Modified => palette.Warning,
            GitStatusKind.Added => palette.Progress,
            GitStatusKind.Untracked => palette.TextLink,
            GitStatusKind.Conflict => palette.Error,
            _ => palette.TextMuted,
        });
        string mark = status switch
        {
            GitStatusKind.Clean => "M10.4,12.3 L11.8,13.8 L14.5,10.8",
            GitStatusKind.Modified => "M12.5,10.5 L12.5,12.9 M12.5,14.1 L12.5,14.2",
            GitStatusKind.Added => "M12.5,10.5 L12.5,14.5 M10.5,12.5 L14.5,12.5",
            GitStatusKind.Untracked => "M11,11.5 C11,10 14,10 14,11.5 C14,12.2 12.5,12.4 12.5,13.2 M12.5,14.2 L12.5,14.3",
            _ => "M10.7,10.7 L14.3,14.3 M14.3,10.7 L10.7,14.3",
        };
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing { Geometry = new RectangleGeometry(new Avalonia.Rect(0, 0, 16, 16)), Brush = Brushes.Transparent });
        g.Children.Add(new GeometryDrawing { Geometry = new EllipseGeometry(new Avalonia.Rect(8.5, 8.5, 8, 8)), Brush = new SolidColorBrush(Color.Parse(palette.Window)) });
        g.Children.Add(new GeometryDrawing { Geometry = new EllipseGeometry(new Avalonia.Rect(9.2, 9.2, 6.6, 6.6)), Brush = new SolidColorBrush(fill) });
        g.Children.Add(new GeometryDrawing
        {
            Geometry = Geometry.Parse(mark),
            Pen = new Pen(Brushes.White, 1.05, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
        });
        return new DrawingImage(g);
    }

    private static Color Resolve(string key)
    {
        if (Avalonia.Application.Current?.TryGetResource(key + "Color", null, out var v) == true && v is Color c) return c;
        return Colors.Gray;
    }
}
