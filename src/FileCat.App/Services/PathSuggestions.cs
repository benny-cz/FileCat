namespace FileCat.App.Services;

/// <summary>Folders that complete a path typed in a panel's location box, as Explorer's address bar suggests them.</summary>
internal static class PathSuggestions
{
    /// <summary>At most this many suggestions are shown.</summary>
    internal const int Max = 12;

    /// <summary>A folder with more entries than this is not read to the end for suggestions.</summary>
    private const int MaxScanned = 20_000;

    /// <summary>
    /// The folders in the typed path's parent whose names start with its last part ("C:\Us" → "C:\Users"; "C:\Users\"
    /// → its folders), sorted by name; none for anything but a full path to an existing folder. Reads the disk: call
    /// off the UI thread.
    /// </summary>
    internal static IReadOnlyList<string> For(string? typed, bool includeHidden, CancellationToken ct)
    {
        string text = (typed ?? string.Empty).Trim();
        if (text.Length == 0 || text.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return [];
        if (OperatingSystem.IsWindows() && text.Length == 2 && text[1] == ':') text += Path.DirectorySeparatorChar; // "C:" is the drive's root
        if (!Path.IsPathFullyQualified(text)) return [];
        bool atSeparator = text[^1] == Path.DirectorySeparatorChar || text[^1] == Path.AltDirectorySeparatorChar;
        string? parent = atSeparator ? text : Path.GetDirectoryName(text);
        string prefix = atSeparator ? string.Empty : Path.GetFileName(text);
        if (parent is null) return [];
        try
        {
            if (!Directory.Exists(parent)) return [];
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
            };
            var names = new List<string>();
            int scanned = 0;
            foreach (string dir in Directory.EnumerateDirectories(parent, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                if (++scanned > MaxScanned) break;
                string name = Path.GetFileName(dir);
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) names.Add(name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.Take(Max).Select(name => Path.Join(parent, name)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return [];
        }
    }
}
