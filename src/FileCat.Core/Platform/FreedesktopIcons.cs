using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileCat.Core.Platform;

/// <summary>
/// Icons from the desktop's icon theme on Linux and other freedesktop systems (Icon Theme and Shared MIME-info
/// specifications): a file's type from its name, the type's icon names with their generic fallbacks, the user's special
/// folders, and the file of the best size in the current theme, the themes it inherits, and hicolor. Pictures are drawn
/// by gdk-pixbuf, which reads PNG and SVG on practically every Linux desktop.
/// </summary>
public sealed class FreedesktopIcons
{
    private sealed record ThemeDirectory(string Path, int Size, int Scale, string Type, int MinSize, int MaxSize, int Threshold);

    private sealed class Theme
    {
        public required string Name { get; init; }
        public required List<string> Inherits { get; init; }
        // Icon name → the directories that hold it, with the file's extension.
        public required Dictionary<string, List<(ThemeDirectory Directory, string File)>> Icons { get; init; }
    }

    private readonly Dictionary<string, Theme?> _themes = new(StringComparer.Ordinal);
    private readonly List<string> _baseDirectories;
    private readonly Dictionary<string, (int Weight, string Mime)> _byExtension = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Weight, string Mime)> _byExtensionCaseSensitive = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _mimeIcons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _genericIcons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _specialFolders = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public string ThemeName { get; }

    private FreedesktopIcons(string theme, List<string> baseDirectories)
    {
        ThemeName = theme;
        _baseDirectories = baseDirectories;
    }

    /// <summary>The desktop's icons; null where there is no freedesktop icon theme (Windows, macOS).</summary>
    public static FreedesktopIcons? TryCreate(string? theme = null)
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) return null;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } dh ? dh : Path.Combine(home, ".local", "share");
        var dataDirs = (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dd ? dd : "/usr/local/share:/usr/share").Split(':', StringSplitOptions.RemoveEmptyEntries);
        var bases = new List<string> { Path.Combine(dataHome, "icons"), Path.Combine(home, ".icons") };
        bases.AddRange(dataDirs.Select(d => Path.Combine(d, "icons")));
        bases.Add("/usr/share/pixmaps");
        bases = bases.Where(Directory.Exists).Distinct().ToList();
        if (bases.Count == 0) return null;
        var icons = new FreedesktopIcons(theme ?? DetectTheme(bases), bases);
        foreach (var dir in dataDirs.Reverse().Append(dataHome)) icons.ReadMimeDatabase(Path.Combine(dir, "mime"));
        icons.ReadUserDirectories(home);
        return icons;
    }

    // ---- The current theme ---------------------------------------------------------------------------------

    /// <summary>The icon theme the desktop uses (GNOME, KDE, or GTK settings), else Adwaita, else hicolor.</summary>
    private static string DetectTheme(List<string> bases)
    {
        bool Exists(string? name) => name is { Length: > 0 } && bases.Any(b => File.Exists(Path.Combine(b, name, "index.theme")));
        if (Environment.GetEnvironmentVariable("FILECAT_ICON_THEME") is { } forced && Exists(forced)) return forced;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } ch ? ch : Path.Combine(home, ".config");
        bool kde = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("KDE", StringComparison.OrdinalIgnoreCase);
        var candidates = new List<Func<string?>>
        {
            () => IniValue(Path.Combine(config, "kdeglobals"), "Icons", "Theme"),
            () => Gsettings("org.gnome.desktop.interface", "icon-theme"),
            () => IniValue(Path.Combine(config, "gtk-4.0", "settings.ini"), "Settings", "gtk-icon-theme-name"),
            () => IniValue(Path.Combine(config, "gtk-3.0", "settings.ini"), "Settings", "gtk-icon-theme-name"),
        };
        if (!kde) (candidates[0], candidates[1]) = (candidates[1], candidates[0]);
        foreach (var candidate in candidates)
        {
            var name = candidate();
            if (Exists(name)) return name!;
        }
        return Exists("Adwaita") ? "Adwaita" : "hicolor";
    }

    private static string? Gsettings(string schema, string key)
    {
        string? tool = new[] { "/usr/bin/gsettings", "/bin/gsettings" }.FirstOrDefault(File.Exists);
        if (tool is null) return null;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(tool, ["get", schema, key]) { RedirectStandardOutput = true, RedirectStandardError = true });
            if (p is null) return null;
            string text = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(2000)) return null;
            return p.ExitCode == 0 ? text.Trim().Trim('\'', '"') : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? IniValue(string file, string section, string key)
    {
        try
        {
            if (!File.Exists(file)) return null;
            bool inSection = false;
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith('[')) inSection = line.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase);
                else if (inSection && line.StartsWith(key, StringComparison.Ordinal) && line.IndexOf('=') is var eq and > 0 && line[..eq].Trim() == key)
                    return line[(eq + 1)..].Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    // ---- Types ----------------------------------------------------------------------------------------------

    private void ReadMimeDatabase(string mimeDir)
    {
        try
        {
            string globs = Path.Combine(mimeDir, "globs2");
            if (File.Exists(globs))
            {
                foreach (var line in File.ReadLines(globs))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var parts = line.Split(':');
                    if (parts.Length < 3 || !int.TryParse(parts[0], out int weight)) continue;
                    string mime = parts[1], glob = parts[2];
                    bool caseSensitive = parts.Length > 3 && parts[3].Split(',').Contains("cs");
                    if (glob.StartsWith("*.", StringComparison.Ordinal) && glob.IndexOfAny(['*', '?', '['], 2) < 0)
                    {
                        string ext = glob[2..];
                        var map = caseSensitive ? _byExtensionCaseSensitive : _byExtension;
                        if (!caseSensitive) ext = ext.ToLowerInvariant();
                        if (!map.TryGetValue(ext, out var existing) || existing.Weight < weight) map[ext] = (weight, mime);
                    }
                    else if (glob.IndexOfAny(['*', '?', '[']) < 0)
                    {
                        _byName.TryAdd(glob, mime);
                    }
                }
            }
            ReadPairs(Path.Combine(mimeDir, "icons"), _mimeIcons);
            ReadPairs(Path.Combine(mimeDir, "generic-icons"), _genericIcons);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void ReadPairs(string file, Dictionary<string, string> map)
    {
        if (!File.Exists(file)) return;
        foreach (var line in File.ReadLines(file))
        {
            int colon = line.IndexOf(':');
            if (colon > 0) map[line[..colon]] = line[(colon + 1)..].Trim();
        }
    }

    /// <summary>The type of a file by its name (longest extension first: "archive.tar.gz" is a compressed tar).</summary>
    public string? MimeType(string fileName)
    {
        if (_byName.TryGetValue(fileName, out var named)) return named;
        for (int dot = fileName.IndexOf('.', 1); dot > 0 && dot < fileName.Length - 1; dot = fileName.IndexOf('.', dot + 1))
        {
            string ext = fileName[(dot + 1)..];
            if (_byExtensionCaseSensitive.TryGetValue(ext, out var cs)) return cs.Mime;
            if (_byExtension.TryGetValue(ext.ToLowerInvariant(), out var ci)) return ci.Mime;
        }
        return null;
    }

    /// <summary>Icon names for a file, most specific first: its type's icon, the type's generic icon, then a plain file.</summary>
    public IReadOnlyList<string> NamesForFile(string fileName, bool executable)
    {
        var names = new List<string>(4);
        if (MimeType(fileName) is { } mime)
        {
            names.Add(_mimeIcons.TryGetValue(mime, out var icon) ? icon : mime.Replace('/', '-'));
            int slash = mime.IndexOf('/');
            names.Add(_genericIcons.TryGetValue(mime, out var generic) ? generic : mime[..slash] + "-x-generic");
        }
        if (executable) names.Add("application-x-executable");
        names.Add("text-x-generic");
        names.Add("unknown");
        return names;
    }

    // ---- Special folders -----------------------------------------------------------------------------------

    private void ReadUserDirectories(string home)
    {
        _specialFolders[home.TrimEnd('/')] = "user-home";
        string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } ch ? ch : Path.Combine(home, ".config");
        string file = Path.Combine(config, "user-dirs.dirs");
        var icons = new Dictionary<string, string>
        {
            ["XDG_DESKTOP_DIR"] = "user-desktop", ["XDG_DOCUMENTS_DIR"] = "folder-documents", ["XDG_DOWNLOAD_DIR"] = "folder-download",
            ["XDG_MUSIC_DIR"] = "folder-music", ["XDG_PICTURES_DIR"] = "folder-pictures", ["XDG_VIDEOS_DIR"] = "folder-videos",
            ["XDG_PUBLICSHARE_DIR"] = "folder-publicshare", ["XDG_TEMPLATES_DIR"] = "folder-templates",
        };
        try
        {
            if (!File.Exists(file)) return;
            foreach (var raw in File.ReadLines(file))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0 || !icons.TryGetValue(raw[..eq].Trim(), out var icon)) continue;
                string path = raw[(eq + 1)..].Trim().Trim('"').Replace("$HOME", home, StringComparison.Ordinal).TrimEnd('/');
                if (path.Length > 0 && path != home.TrimEnd('/')) _specialFolders[path] = icon;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The icon name of the user's home or a special folder (Documents, Downloads, …) at this path, if it is one.</summary>
    public string? SpecialFolder(string path) => _specialFolders.GetValueOrDefault(path.TrimEnd('/'));

    // ---- Finding the file ----------------------------------------------------------------------------------

    /// <summary>The best file for the first of <paramref name="names"/> the theme chain has, at <paramref name="size"/> pixels.</summary>
    public string? Find(IReadOnlyList<string> names, int size)
    {
        lock (_lock)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var theme in Chain(ThemeName, visited).Append("hicolor"))
            {
                if (Load(theme) is not { } t) continue;
                foreach (var name in names)
                    if (Best(t, name, size) is { } file) return file;
            }
            foreach (var name in names)
            {
                foreach (var ext in new[] { ".png", ".svg" })
                {
                    string pixmap = Path.Combine("/usr/share/pixmaps", name + ext);
                    if (File.Exists(pixmap)) return pixmap;
                }
            }
            // Last, as GTK does, the symbolic variants: adwaita-icon-theme 41 and later ship only those for many types
            // (release issue I27). They are drawn in one color, which the caller sets to the text's (IsSymbolic).
            foreach (var theme in Chain(ThemeName, new HashSet<string>(StringComparer.Ordinal)).Append("hicolor"))
            {
                if (Load(theme) is not { } t) continue;
                foreach (var name in names)
                    if (Best(t, name + "-symbolic", size) is { } file) return file;
            }
            return null;
        }
    }

    /// <summary>Whether a found icon is a symbolic one, meant to be drawn in the text's color.</summary>
    public static bool IsSymbolic(string file) => Path.GetFileNameWithoutExtension(file).EndsWith("-symbolic", StringComparison.Ordinal);

    /// <summary>Tests: icons from <paramref name="baseDirectories"/> with <paramref name="theme"/> as the desktop's theme.</summary>
    internal static FreedesktopIcons ForTests(string theme, params string[] baseDirectories) => new(theme, [.. baseDirectories]);

    private IEnumerable<string> Chain(string theme, HashSet<string> visited)
    {
        if (!visited.Add(theme)) yield break;
        yield return theme;
        if (Load(theme) is not { } t) yield break;
        foreach (var parent in t.Inherits)
            foreach (var inherited in Chain(parent, visited))
                yield return inherited;
    }

    private static string? Best(Theme theme, string name, int size)
    {
        if (!theme.Icons.TryGetValue(name, out var candidates)) return null;
        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (var (dir, file) in candidates)
        {
            if (dir.Scale != 1) continue;
            int distance = Distance(dir, size);
            // A bitmap of the exact size beats a scalable picture, which beats scaling a bitmap.
            if (distance == 0 && dir.Type != "Scalable") return file;
            int rank = distance * 2 + (dir.Type == "Scalable" ? 0 : 1);
            if (rank < bestDistance)
            {
                bestDistance = rank;
                best = file;
            }
        }
        return best;
    }

    private static int Distance(ThemeDirectory dir, int size) => dir.Type switch
    {
        "Fixed" => Math.Abs(dir.Size - size),
        "Scalable" => size < dir.MinSize ? dir.MinSize - size : size > dir.MaxSize ? size - dir.MaxSize : 0,
        _ => size < dir.Size - dir.Threshold ? dir.MinSize - size : size > dir.Size + dir.Threshold ? size - dir.MaxSize : 0,
    };

    private Theme? Load(string name)
    {
        if (_themes.TryGetValue(name, out var cached)) return cached;
        Theme? theme = null;
        var roots = _baseDirectories.Select(b => Path.Combine(b, name)).Where(Directory.Exists).ToList();
        string? index = roots.Select(r => Path.Combine(r, "index.theme")).FirstOrDefault(File.Exists);
        if (index is not null)
        {
            var sections = ParseIni(index);
            var main = sections.GetValueOrDefault("Icon Theme") ?? [];
            var directories = (main.GetValueOrDefault("Directories") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Concat((main.GetValueOrDefault("ScaledDirectories") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var icons = new Dictionary<string, List<(ThemeDirectory, string)>>(StringComparer.Ordinal);
            foreach (var d in directories.Distinct())
            {
                if (!sections.TryGetValue(d, out var s) || !int.TryParse(s.GetValueOrDefault("Size"), out int size)) continue;
                int Int(string key, int fallback) => int.TryParse(s.GetValueOrDefault(key), out int v) ? v : fallback;
                var dir = new ThemeDirectory(d, size, Int("Scale", 1), s.GetValueOrDefault("Type") ?? "Threshold", Int("MinSize", size), Int("MaxSize", size), Int("Threshold", 2));
                foreach (var root in roots)
                {
                    string full = Path.Combine(root, d);
                    if (!Directory.Exists(full)) continue;
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(full))
                        {
                            string ext = Path.GetExtension(file);
                            if (ext is not (".png" or ".svg")) continue;
                            string iconName = Path.GetFileNameWithoutExtension(file);
                            if (!icons.TryGetValue(iconName, out var list)) icons[iconName] = list = [];
                            list.Add((dir, file));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            var inherits = (main.GetValueOrDefault("Inherits") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            theme = new Theme { Name = name, Inherits = inherits, Icons = icons };
        }
        _themes[name] = theme;
        return theme;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseIni(string file)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        Dictionary<string, string>? current = null;
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                string name = line[1..^1];
                if (!sections.TryGetValue(name, out current)) sections[name] = current = new Dictionary<string, string>(StringComparer.Ordinal);
                continue;
            }
            int eq = line.IndexOf('=');
            if (current is not null && eq > 0) current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return sections;
    }

    // ---- Drawing -------------------------------------------------------------------------------------------

    private static readonly Lazy<bool> PixbufAvailable = new(() =>
    {
        try { return NativeLibrary.TryLoad("libgdk_pixbuf-2.0.so.0", out _); }
        catch (Exception) { return false; }
    });

    /// <summary>
    /// Draws an icon file (PNG or SVG) at <paramref name="size"/> pixels as straight BGRA, through gdk-pixbuf.
    /// False when it cannot (no gdk-pixbuf, an unreadable file).
    /// </summary>
    public static unsafe bool TryRender(string file, int size, out int width, out int height, out byte[] bgra)
    {
        width = height = 0;
        bgra = [];
        if (!PixbufAvailable.Value) return false;
        nint error = 0;
        nint pixbuf = gdk_pixbuf_new_from_file_at_size(file, size, size, &error);
        if (pixbuf == 0)
        {
            if (error != 0) g_error_free(error);
            return false;
        }
        try
        {
            int w = gdk_pixbuf_get_width(pixbuf), h = gdk_pixbuf_get_height(pixbuf), stride = gdk_pixbuf_get_rowstride(pixbuf), channels = gdk_pixbuf_get_n_channels(pixbuf);
            if (w <= 0 || h <= 0 || w > 1024 || h > 1024 || channels is not (3 or 4) || gdk_pixbuf_get_bits_per_sample(pixbuf) != 8) return false;
            byte* pixels = gdk_pixbuf_get_pixels(pixbuf);
            var result = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                byte* row = pixels + (long)y * stride;
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    result[o] = row[x * channels + 2];
                    result[o + 1] = row[x * channels + 1];
                    result[o + 2] = row[x * channels];
                    result[o + 3] = channels == 4 ? row[x * channels + 3] : (byte)255;
                }
            }
            (width, height, bgra) = (w, h, result);
            return true;
        }
        finally
        {
            g_object_unref(pixbuf);
        }
    }

    [DllImport("libgdk_pixbuf-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe nint gdk_pixbuf_new_from_file_at_size([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, int width, int height, nint* error);

    [DllImport("libgdk_pixbuf-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int gdk_pixbuf_get_width(nint pixbuf);

    [DllImport("libgdk_pixbuf-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int gdk_pixbuf_get_height(nint pixbuf);

    [DllImport("libgdk_pixbuf-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int gdk_pixbuf_get_rowstride(nint pixbuf);

    [DllImport("libgdk_pixbuf-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int gdk_pixbuf_get_n_channels(nint pixbuf);

    [DllImport("libgdk_pixbuf-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int gdk_pixbuf_get_bits_per_sample(nint pixbuf);

    [DllImport("libgdk_pixbuf-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe byte* gdk_pixbuf_get_pixels(nint pixbuf);

    [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_object_unref(nint instance);

    [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_error_free(nint error);
}
