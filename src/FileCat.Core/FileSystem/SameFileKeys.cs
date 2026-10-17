namespace FileCat.Core.FileSystem;

/// <summary>
/// One key per file however its path is spelled in its folder: by the folder's own rule for letter case (Windows'
/// per-folder flag; exact names elsewhere), read once per folder. Paths in different folders, or spelled differently
/// where the folder tells names apart, get different keys: two files are never taken for one.
/// </summary>
public sealed class SameFileKeys
{
    private readonly Dictionary<string, StringComparer> _rules = new(StringComparer.Ordinal);

    /// <summary>The key of the file at <paramref name="path"/>, or null for a path with no folder.</summary>
    public string? Of(string path)
    {
        if (Path.GetDirectoryName(path) is not { } folder) return null;
        if (!_rules.TryGetValue(folder, out var rule)) _rules[folder] = rule = WindowsCaseSensitivity.NamesIn(folder);
        string name = Path.GetFileName(path);
        return folder + "\0" + (ReferenceEquals(rule, StringComparer.OrdinalIgnoreCase) ? name.ToUpperInvariant() : name);
    }
}
