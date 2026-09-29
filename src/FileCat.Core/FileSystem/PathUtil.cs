using System.Text;

namespace FileCat.Core.FileSystem;

/// <summary>
/// File-system path helpers. Identity comparisons stay exact; the case-insensitive helpers here are
/// only used for conservative safety checks (overlap detection, destination-inside-source).
/// </summary>
public static class PathUtil
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Comparison for conservative containment checks: case-insensitive on Windows.</summary>
    public static StringComparison SafetyComparison => IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer SafetyComparer => IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static char Separator => Path.DirectorySeparatorChar;

    /// <summary>Normalizes for comparison only: full path, no trailing separator (roots keep theirs).</summary>
    public static string NormalizeForCompare(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception) when (path.Length > 0) { full = path; }
        var trimmed = Path.TrimEndingDirectorySeparator(full);
        return trimmed.Length == 0 ? full : trimmed;
    }

    /// <summary>True when <paramref name="path"/> equals <paramref name="root"/> or lies beneath it.</summary>
    public static bool IsSameOrUnder(string path, string root)
    {
        var p = NormalizeForCompare(path);
        var r = NormalizeForCompare(root);
        if (p.Equals(r, SafetyComparison)) return true;
        if (!p.StartsWith(r, SafetyComparison)) return false;
        // Roots such as "C:\" already end with a separator.
        return r.EndsWith(Path.DirectorySeparatorChar) || r.EndsWith(Path.AltDirectorySeparatorChar) ||
               p.Length > r.Length && (p[r.Length] == Path.DirectorySeparatorChar || p[r.Length] == Path.AltDirectorySeparatorChar);
    }

    public static bool SubtreesOverlap(string a, string b) => IsSameOrUnder(a, b) || IsSameOrUnder(b, a);

    public static bool IsUncPath(string path) =>
        path.Length > 2 && IsSep(path[0]) && IsSep(path[1]) && path[2] != '?' && path[2] != '.';

    /// <summary>
    /// A Windows path with its drive letter upper case ("c:\x" → "C:\x", also after "\\?\"), as Windows shows drives;
    /// the rest of the path, and any path elsewhere, stays as it is.
    /// </summary>
    public static string WithUpperDrive(string path)
    {
        if (!IsWindows) return path;
        int at = path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal) ? 4 : 0;
        if (path.Length < at + 2 || path[at + 1] != ':' || !char.IsAsciiLetterLower(path[at])) return path;
        return string.Concat(path.AsSpan(0, at), [char.ToUpperInvariant(path[at])], path.AsSpan(at + 1));
    }

    /// <summary>"\\server" for any UNC path, otherwise null.</summary>
    public static string? GetUncServer(string path)
    {
        if (!IsUncPath(path)) return null;
        int end = path.IndexOfAny(['\\', '/'], 2);
        return end < 0 ? path.TrimEnd('\\', '/') : path[..end];
    }

    /// <summary>True for "\\server" or "\\server\" (a root that lists shares, not a directory).</summary>
    public static bool IsUncServerRoot(string path)
    {
        if (!IsUncPath(path)) return false;
        var t = path.TrimEnd('\\', '/');
        return t.IndexOfAny(['\\', '/'], 2) < 0;
    }

    /// <summary>True for "\\server\share" (with or without trailing separator).</summary>
    public static bool IsUncShareRoot(string path)
    {
        if (!IsUncPath(path)) return false;
        var t = path.TrimEnd('\\', '/');
        int first = t.IndexOfAny(['\\', '/'], 2);
        return first > 2 && t.IndexOfAny(['\\', '/'], first + 1) < 0 && first < t.Length - 1;
    }

    /// <summary>Default device key: volume root ("C:") or UNC server ("\\server"). Platforms may refine it.</summary>
    public static string GetDeviceKey(string path)
    {
        if (IsUncPath(path)) return (GetUncServer(path) ?? path).ToUpperInvariant();
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return "local";
        return IsWindows ? root.TrimEnd('\\', '/').ToUpperInvariant() : root;
    }

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>Validates a single new item name for the current platform; returns an error or null.</summary>
    public static string? ValidateNewName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "The name cannot be empty.";
        if (name is "." or "..") return "\".\" and \"..\" are not valid names.";
        if (IsWindows)
        {
            foreach (var c in name)
            {
                if (c < 32) return "The name cannot contain control characters.";
                if ("<>:\"/\\|?*".Contains(c)) return $"The name cannot contain '{c}'.";
            }
            if (name.EndsWith(' ') || name.EndsWith('.')) return "On Windows a name cannot end with a space or a dot.";
            var stem = name.Split('.')[0].TrimEnd(' ');
            if (ReservedWindowsNames.Contains(stem)) return $"\"{stem}\" is a reserved device name on Windows.";
        }
        else
        {
            if (name.Contains('/') || name.Contains('\0')) return "The name cannot contain '/' or NUL.";
        }
        if (Encoding.UTF8.GetByteCount(name) > 1020 || name.Length > 255) return "The name is too long.";
        return null;
    }

    /// <summary>Generates "name (2).ext", "name (3).ext"... until <paramref name="exists"/> returns false.</summary>
    public static string MakeUniqueName(string name, Func<string, bool> exists, bool isDirectory = false)
    {
        if (!exists(name)) return name;
        string stem = isDirectory ? name : Resources.NameParts.GetStem(name);
        string ext = isDirectory ? string.Empty : Resources.NameParts.GetExtension(name);
        // Strip an existing " (n)" suffix so repeated copies do not become "a (2) (2)".
        int open = stem.LastIndexOf(" (", StringComparison.Ordinal);
        if (open > 0 && stem.EndsWith(')') && int.TryParse(stem.AsSpan(open + 2, stem.Length - open - 3), out _))
            stem = stem[..open];
        for (int i = 2; i < int.MaxValue; i++)
        {
            var candidate = ext.Length == 0 ? $"{stem} ({i})" : $"{stem} ({i}).{ext}";
            if (!exists(candidate)) return candidate;
        }
        throw new IOException("Could not generate a unique name.");
    }

    /// <summary>Expands environment variables and "~" in user-typed paths.</summary>
    public static string ExpandUserInput(string text)
    {
        var t = text.Trim().Trim('"');
        if (t == "~" || t.StartsWith("~/", StringComparison.Ordinal) || t.StartsWith("~\\", StringComparison.Ordinal))
            t = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), t.Length > 1 ? t[2..] : "");
        return Environment.ExpandEnvironmentVariables(t);
    }

    private static bool IsSep(char c) => c is '\\' or '/';
}
