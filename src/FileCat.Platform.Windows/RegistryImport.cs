using System.Globalization;
using System.Text;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Microsoft.Win32;

namespace FileCat.Platform.Windows;

public sealed record RegistryImportPlan(IReadOnlyList<RegistryChange> Changes, int AddedKeys, int AddedValues,
    int OverwrittenValues, int DeletedValues, int DeletedTrees, long DataBytes);

/// <summary>Strict .reg parser and read-only preflight scoped to a selected key and explicit view.</summary>
public static class RegistryImport
{
    public const long MaxFileBytes = 256L * 1024 * 1024;
    private const int MaxChanges = 250_000;
    private const int MaxPreviewDirectives = 100_000;
    private const int MaxKeyPathChars = 8 * 1024 * 1024;

    public static RegistryImportPlan Preview(string file, Location scope, CancellationToken ct = default)
    {
        if (scope.Scheme != Schemes.Registry || scope.Path.Length == 0 ||
            scope.Path.Split('\\')[0] is "HKCR" or "HKCC")
            throw new ArgumentException("Choose a concrete, writable HKCU, HKLM, or HKU import scope.", nameof(scope));
        var info = new FileInfo(file);
        if (info.Length > MaxFileBytes) throw new IOException("This .reg file exceeds the 256 MiB import limit.");
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        string? header = reader.ReadLine();
        if (header != "Windows Registry Editor Version 5.00")
            throw new FormatException("Only Windows Registry Editor Version 5.00 .reg files are supported.");

        var changes = new List<RegistryChange>();
        // Keys below the scope, by the paths the headers name them with (KeyPath): those the file creates (true) or that
        // lead to one (false: they exist), those it changes values in or that lead to one (with each key's identity for
        // its values), and those it deletes. A key's ancestors are recorded with it once, up to the first one already there.
        var created = new Dictionary<KeyPath, bool>(KeyComparer.Instance);
        var changed = new Dictionary<KeyPath, int>(KeyComparer.Instance);
        var deletedKeys = new HashSet<KeyPath>(KeyComparer.Instance);
        var touched = new HashSet<(int Key, string Name)>(KeyComparer.Instance);
        var keys = new List<KeyPath>();
        Location? current = null;
        RegistryKey? currentKey = null;
        bool currentCreated = false;
        int currentId = -1;
        int addedKeys = 0, addedValues = 0, overwritten = 0, deletedValues = 0, deletedTrees = 0;
        long dataBytes = 0;
        int lineNumber = 1;
        int directives = 0, keyPathChars = 0, newKeyPathChars = 0;
        try
        {
            while (reader.ReadLine() is { } physical)
            {
                ct.ThrowIfCancellationRequested();
                lineNumber++;
                string line = physical.Trim();
                if (line.EndsWith('\\'))
                {
                    var joined = new StringBuilder(line.Length + 128);
                    while (line.EndsWith('\\'))
                    {
                        joined.Append(line.AsSpan(0, line.Length - 1));
                        line = (reader.ReadLine() ?? throw new FormatException($"Unfinished continuation at line {lineNumber}.")).TrimStart();
                        lineNumber++;
                        if (joined.Length + line.Length > MaxFileBytes)
                            throw new IOException("A Registry value line exceeds the import file limit.");
                    }
                    joined.Append(line);
                    line = joined.ToString();
                }
                if (line.StartsWith(ViewMarker, StringComparison.Ordinal)) RequireSameView(line[ViewMarker.Length..], scope);
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (++directives > MaxPreviewDirectives)
                    throw new IOException("Import preview exceeds 100,000 directives.");
                if (line[0] == '[')
                {
                    if (line[^1] != ']') throw new FormatException($"Invalid Registry key header at line {lineNumber}.");
                    bool delete = line.StartsWith("[-", StringComparison.Ordinal);
                    string path = line[(delete ? 2 : 1)..^1];
                    keyPathChars = checked(keyPathChars + path.Length);
                    if (keyPathChars > MaxKeyPathChars)
                        throw new IOException("Import preview exceeds 8 MiB of key paths.");
                    current = ParseLocation(path, scope, lineNumber);
                    currentKey?.Dispose();
                    currentKey = null;
                    currentId = -1;
                    KeysBelow(scope, current.Path, keys);
                    foreach (var key in keys)
                        if (deletedKeys.Contains(key))
                            throw new FormatException($"This file changes a key after deleting its ancestor at line {lineNumber}.");
                    if (delete)
                    {
                        if (current.Path.Equals(scope.Path, StringComparison.OrdinalIgnoreCase))
                            throw new FormatException("The selected import scope itself cannot be deleted.");
                        if (created.ContainsKey(keys[^1]))
                            throw new FormatException("A newly created key is also deleted in this file.");
                        if (changed.ContainsKey(keys[^1]))
                            throw new FormatException("A key is changed before being deleted in this file.");
                        if (Exists(scope, keys))
                        {
                            var tree = RegistryTree.Scan(current, ct);
                            changes.Add(new RegistryChange(RegistryAction.DeleteKey, Parent(current), Name(current),
                                TreeDigest: RegistryTree.Digest(tree)));
                            deletedTrees++;
                        }
                        deletedKeys.Add(keys[^1]);
                        current = null;
                    }
                    else
                    {
                        // Below a key this file creates, every key is new; otherwise the Registry says which exist, from
                        // the scope down, and the rest are new.
                        int known = keys.Count;
                        bool isNew = false;
                        while (known > 0 && !created.TryGetValue(keys[known - 1], out isNew)) known--;
                        int first = known;
                        if (!isNew && keys.Count > 0) currentKey = OpenPath(scope, keys, out first);
                        for (int i = first; i < keys.Count; i++)
                        {
                            var parent = scope;
                            if (i > 0)
                            {
                                // Each new key keeps its parent's path in the plan, so a deep header costs its depth times
                                // its length: these paths have a budget of their own, as the headers' do.
                                newKeyPathChars = checked(newKeyPathChars + keys[i - 1].Length);
                                if (newKeyPathChars > MaxKeyPathChars)
                                    throw new IOException("Import preview exceeds 8 MiB of new key paths.");
                                parent = scope.WithPath(string.Concat(scope.Path,
                                    current.Path.AsSpan(scope.Path.Length, keys[i - 1].Length - scope.Path.Length)));
                            }
                            changes.Add(new RegistryChange(RegistryAction.CreateKey, parent, Component(scope, keys, i)));
                            addedKeys++;
                            if (changes.Count > MaxChanges) throw new IOException("Import exceeds 250,000 changes.");
                        }
                        if (first < keys.Count)
                            for (int i = keys.Count - 1; i >= 0 && created.TryAdd(keys[i], i >= first); i--) { }
                        currentCreated = isNew || first < keys.Count;
                    }
                }
                else
                {
                    if (current is null) throw new FormatException($"Value directive without a key at line {lineNumber}.");
                    var (name, desired) = ParseValue(line, lineNumber);
                    if (currentId < 0) currentId = ValueKey(keys, changed);
                    if (!touched.Add((currentId, name)))
                        throw new FormatException($"Duplicate value directive at line {lineNumber}.");
                    RegistryValueSnapshot? expected = null;
                    if (!currentCreated)
                    {
                        currentKey ??= WindowsRegistryProvider.Open(current, false);
                        var old = RegistryRaw.ReadIfPresent(currentKey, name);
                        if (old is not null)
                        {
                            if (old.Data.Length != old.Length) throw new IOException("An existing Registry value exceeds the 64 MiB comparison limit.");
                            expected = new RegistryValueSnapshot(old.Type, old.Data);
                        }
                    }
                    if (desired is null)
                    {
                        if (expected is null) continue;
                        changes.Add(new RegistryChange(RegistryAction.DeleteValue, current, name, expected));
                        deletedValues++;
                    }
                    else
                    {
                        dataBytes = checked(dataBytes + desired.Data.Length);
                        if (dataBytes > RegistryTree.MaxDataBytes) throw new IOException("Import value data exceeds 256 MiB.");
                        if (expected?.Type == desired.Type && expected.Data.AsSpan().SequenceEqual(desired.Data)) continue;
                        changes.Add(new RegistryChange(RegistryAction.SetValue, current, name, expected, desired));
                        if (expected is null) addedValues++; else overwritten++;
                    }
                }
                if (changes.Count > MaxChanges) throw new IOException("Import exceeds 250,000 changes.");
            }
        }
        finally { currentKey?.Dispose(); }
        return new RegistryImportPlan(changes, addedKeys, addedValues, overwritten, deletedValues, deletedTrees, dataBytes);
    }

    /// <summary>
    /// A key below the import scope: the start of a header's path up to the key (the header's string, never copied) and
    /// the hash of its components below the scope, built one component at a time, so all of a header's ancestors are
    /// looked up for the cost of the header's own length. Keys compare as Windows names them, ignoring case.
    /// </summary>
    private readonly record struct KeyPath(string Path, int Length, int Hash);

    private sealed class KeyComparer : IEqualityComparer<KeyPath>, IEqualityComparer<(int Key, string Name)>
    {
        public static readonly KeyComparer Instance = new();

        public bool Equals(KeyPath x, KeyPath y) => x.Hash == y.Hash && x.Length == y.Length &&
            (ReferenceEquals(x.Path, y.Path) || x.Path.AsSpan(0, x.Length).Equals(y.Path.AsSpan(0, y.Length), StringComparison.OrdinalIgnoreCase));

        public int GetHashCode(KeyPath key) => key.Hash;

        public bool Equals((int Key, string Name) x, (int Key, string Name) y) =>
            x.Key == y.Key && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((int Key, string Name) value) => HashCode.Combine(value.Key, StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name));
    }

    /// <summary>The keys of <paramref name="path"/> below the scope, from the top; none for the scope itself.</summary>
    private static void KeysBelow(Location scope, string path, List<KeyPath> keys)
    {
        keys.Clear();
        int hash = 0;
        for (int start = scope.Path.Length + 1; start <= path.Length;)
        {
            int end = path.IndexOf('\\', start);
            if (end < 0) end = path.Length;
            hash = HashCode.Combine(hash, string.GetHashCode(path.AsSpan(start, end - start), StringComparison.OrdinalIgnoreCase));
            keys.Add(new KeyPath(path, end, hash));
            start = end + 1;
        }
    }

    private static string Component(Location scope, List<KeyPath> keys, int index) =>
        keys[index].Path[(index == 0 ? scope.Path.Length + 1 : keys[index - 1].Length + 1)..keys[index].Length];

    /// <summary>
    /// The identity of a header's key for its values (0 for the scope itself). Its first value records the key and its
    /// ancestors, as keys a deletion may not reach.
    /// </summary>
    private static int ValueKey(List<KeyPath> keys, Dictionary<KeyPath, int> changed)
    {
        if (keys.Count == 0) return 0;
        if (changed.TryGetValue(keys[^1], out int id)) return id;
        id = changed.Count + 1;
        for (int i = keys.Count - 1; i >= 0 && changed.TryAdd(keys[i], changed.Count + 1); i--) { }
        return id;
    }

    /// <summary>The comment FileCat's export writes first (<see cref="RegistryInterchange"/>): the view the keys were read in.</summary>
    internal const string ViewMarker = "; FileCat source view: ";

    /// <summary>
    /// A file FileCat exported from the 32-bit view names its keys as that view shows them, so imported into another
    /// view it would write other keys (the 64-bit ones under the same path). The default view is the 64-bit one here.
    /// </summary>
    private static void RequireSameView(string rest, Location scope)
    {
        string label = rest.Split('.')[0].Trim();
        bool file32 = label == WindowsRegistryProvider.ViewLabel("32");
        bool scope32 = scope.Session == "32";
        if (file32 == scope32) return;
        string View(bool is32) => is32 ? "32-bit view" : "64-bit (default) view";
        throw new FormatException($"FileCat exported this file from the Registry's {View(file32)}, but the import would go to the " +
                                  $"{View(scope32)}, where the same paths are other keys. Open the key in the {View(file32)} and import it there.");
    }

    /// <summary>
    /// A header's keys opened one below another from the scope, by the same opens as reading the last one: that key, or
    /// null where one is absent; <paramref name="present"/> counts the keys found from the top.
    /// </summary>
    private static RegistryKey? OpenPath(Location scope, List<KeyPath> keys, out int present)
    {
        present = 0;
        var key = OpenIfPresent(scope);
        while (key is not null && present < keys.Count)
        {
            RegistryKey? child;
            try { child = OpenIfPresent(key, Component(scope, keys, present)); }
            finally { key.Dispose(); }
            key = child;
            if (key is not null) present++;
        }
        return key;
    }

    private static bool Exists(Location scope, List<KeyPath> keys)
    {
        using var key = OpenPath(scope, keys, out _);
        return key is not null;
    }

    private static RegistryKey? OpenIfPresent(Location location)
    {
        try { return WindowsRegistryProvider.Open(location, false); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2) { return null; }
    }

    private static RegistryKey? OpenIfPresent(RegistryKey parent, string name)
    {
        try { return RegistryRaw.OpenNoLink(parent, name, parent.View, false); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2) { return null; }
    }

    private static Location ParseLocation(string raw, Location scope, int line)
    {
        var slash = raw.IndexOf('\\');
        string root = slash < 0 ? raw : raw[..slash];
        root = root.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" => "HKCU", "HKEY_LOCAL_MACHINE" => "HKLM",
            "HKEY_USERS" => "HKU", "HKEY_CLASSES_ROOT" => "HKCR",
            "HKEY_CURRENT_CONFIG" => "HKCC", _ => root,
        };
        string path = root + (slash < 0 ? "" : raw[slash..]);
        var provider = new WindowsRegistryProvider();
        if (!provider.TryParse(path, scope, out var location) || location is null ||
            !(location.Path.Equals(scope.Path, StringComparison.OrdinalIgnoreCase) ||
              location.Path.StartsWith(scope.Path + "\\", StringComparison.OrdinalIgnoreCase)))
            throw new FormatException($"Key at line {line} is outside {scope.Path} or has an invalid path.");
        return location;
    }

    private static Location Parent(Location key) => key.WithPath(key.Path[..key.Path.LastIndexOf('\\')]);
    private static string Name(Location key) => key.Path[(key.Path.LastIndexOf('\\') + 1)..];

    private static (string Name, RegistryValueSnapshot? Value) ParseValue(string line, int lineNumber)
    {
        string name;
        int pos;
        if (line[0] == '@') { name = ""; pos = 1; }
        else if (line[0] == '"') { (name, pos) = ReadQuoted(line, 0, lineNumber); }
        else throw new FormatException($"Invalid value name at line {lineNumber}.");
        if (name.Contains('\0') || name.Length > 16383 || pos >= line.Length || line[pos] != '=')
            throw new FormatException($"Invalid value assignment at line {lineNumber}.");
        string data = line[(pos + 1)..].Trim();
        if (data == "-") return (name, null);
        if (data.StartsWith('"'))
        {
            var (s, end) = ReadQuoted(data, 0, lineNumber);
            if (end != data.Length || s.Contains('\0')) throw new FormatException($"Invalid string value at line {lineNumber}.");
            return (name, new RegistryValueSnapshot(1, Encoding.Unicode.GetBytes(s + "\0")));
        }
        if (data.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
        {
            if (!uint.TryParse(data[6..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number))
                throw new FormatException($"Invalid DWORD at line {lineNumber}.");
            return (name, new RegistryValueSnapshot(4, BitConverter.GetBytes(number)));
        }
        uint type;
        string hex;
        if (data.StartsWith("hex:", StringComparison.OrdinalIgnoreCase)) { type = 3; hex = data[4..]; }
        else if (data.StartsWith("hex(", StringComparison.OrdinalIgnoreCase))
        {
            int close = data.IndexOf("):", StringComparison.Ordinal);
            if (close < 0 || !uint.TryParse(data[4..close], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out type))
                throw new FormatException($"Invalid Registry type at line {lineNumber}.");
            hex = data[(close + 2)..];
        }
        else throw new FormatException($"Unsupported value encoding at line {lineNumber}.");
        int count = hex.Length == 0 ? 0 : 1;
        foreach (char c in hex) if (c == ',') count++;
        if (count > RegistryRaw.EditLimit) throw new IOException("A Registry value exceeds the 64 MiB import limit.");
        var bytes = new byte[count];
        int start = 0;
        for (int i = 0; i < count; i++)
        {
            int end = i + 1 == count ? hex.Length : hex.IndexOf(',', start);
            if (end < start || !byte.TryParse(hex.AsSpan(start, end - start).Trim(), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out bytes[i]) || hex.AsSpan(start, end - start).Trim().Length != 2)
                throw new FormatException($"Invalid hex byte at line {lineNumber}.");
            start = end + 1;
        }
        return (name, new RegistryValueSnapshot(type, bytes));
    }

    private static (string Text, int End) ReadQuoted(string text, int start, int line)
    {
        var result = new StringBuilder();
        for (int i = start + 1; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') return (result.ToString(), i + 1);
            if (c == '\\')
            {
                if (++i >= text.Length || text[i] is not ('\\' or '"'))
                    throw new FormatException($"Invalid quoted escape at line {line}.");
                c = text[i];
            }
            result.Append(c);
        }
        throw new FormatException($"Unclosed quoted value at line {line}.");
    }
}
