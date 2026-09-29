namespace FileCat.App.Services;

/// <summary>
/// Tab completion in the command line, as shells complete: the word before the caret becomes the first name that starts
/// with it in its folder (relative to the panel's folder, or a full path), and repeated Tab steps through the others
/// (Shift+Tab back). A folder keeps its separator so the next Tab goes on inside it; a name with spaces is quoted.
/// </summary>
internal static class CommandLineCompletion
{
    /// <summary>A folder with more entries than this is not read to the end.</summary>
    private const int MaxScanned = 20_000;

    /// <summary>What repeated Tab steps through: the text around the word, and the names found for it.</summary>
    internal sealed record Cycle(string Before, string After, IReadOnlyList<string> Candidates, int Index);

    /// <summary>
    /// The command line after completing (or stepping to the next completion of) the word before
    /// <paramref name="caret"/>, and the cycle for another Tab; null when nothing completes it.
    /// </summary>
    internal static (string Text, int Caret, Cycle Cycle)? Complete(string text, int caret, string folder, bool backwards, Cycle? cycle)
    {
        if (cycle is { Candidates.Count: > 0 })
        {
            int count = cycle.Candidates.Count;
            int next = ((backwards ? cycle.Index - 1 : cycle.Index + 1) % count + count) % count;
            return Build(cycle with { Index = next });
        }
        caret = Math.Clamp(caret, 0, text.Length);
        // The word: after the last opening quote when inside quotes, otherwise after the last space.
        bool quoted = text[..caret].Count(c => c == '"') % 2 == 1;
        int start = quoted ? text.LastIndexOf('"', Math.Max(0, caret - 1)) + 1 : text.LastIndexOf(' ', Math.Max(0, caret - 1)) + 1;
        if (caret == 0 || start > caret) start = caret;
        string word = text[start..caret];
        if (word.Length == 0) return null;
        int cut = word.LastIndexOfAny(['\\', '/']) + 1;
        string dirPart = word[..cut], prefix = word[cut..];
        string dir;
        try
        {
            dir = Path.IsPathFullyQualified(dirPart) ? dirPart : Path.GetFullPath(Path.Join(folder, dirPart));
            if (!Directory.Exists(dir)) return null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
        var names = new List<string>();
        try
        {
            int scanned = 0;
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options))
            {
                if (++scanned > MaxScanned) break;
                if (!entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                names.Add(dirPart + entry.Name + (entry is DirectoryInfo ? Path.DirectorySeparatorChar.ToString() : string.Empty));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
        if (names.Count == 0) return null;
        names.Sort(StringComparer.OrdinalIgnoreCase);
        // The opening quote (typed, or added for a name with spaces) belongs to the word's replacement.
        string before = quoted ? text[..(start - 1)] : text[..start];
        string after = text[caret..];
        if (quoted && after.StartsWith('"')) after = after[1..];
        return Build(new Cycle(before, after, names, backwards ? names.Count - 1 : 0));
    }

    private static (string, int, Cycle) Build(Cycle cycle)
    {
        string name = cycle.Candidates[cycle.Index];
        bool folder = name.EndsWith(Path.DirectorySeparatorChar);
        if (!name.Contains(' ')) return (cycle.Before + name + cycle.After, cycle.Before.Length + name.Length, cycle);
        // Quoted: a folder leaves the caret inside the quotes, to go on typing or completing in it.
        string insert = "\"" + name + "\"";
        int caret = cycle.Before.Length + insert.Length - (folder ? 1 : 0);
        return (cycle.Before + insert + cycle.After, caret, cycle);
    }
}
