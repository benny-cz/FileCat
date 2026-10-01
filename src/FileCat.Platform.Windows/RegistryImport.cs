using System.Globalization;
using System.Text;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows;

public sealed record RegistryImportPlan(IReadOnlyList<RegistryChange> Changes, int AddedKeys, int AddedValues,
    int OverwrittenValues, int DeletedValues, int DeletedTrees, long DataBytes);

/// <summary>Strict .reg parser and read-only preflight scoped to a selected key and explicit view.</summary>
public static class RegistryImport
{
    public const long MaxFileBytes = 256L * 1024 * 1024;
    private const int MaxChanges = 250_000;

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
        var pendingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deletedKeys = new List<string>();
        Location? current = null;
        int addedKeys = 0, addedValues = 0, overwritten = 0, deletedValues = 0, deletedTrees = 0;
        long dataBytes = 0;
        int lineNumber = 1;
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
            if (line[0] == '[')
            {
                if (line[^1] != ']') throw new FormatException($"Invalid Registry key header at line {lineNumber}.");
                bool delete = line.StartsWith("[-", StringComparison.Ordinal);
                string path = line[(delete ? 2 : 1)..^1];
                current = ParseLocation(path, scope, lineNumber);
                if (deletedKeys.Any(p => current.Path.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                    current.Path.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)))
                    throw new FormatException($"This file changes a key after deleting its ancestor at line {lineNumber}.");
                if (delete)
                {
                    if (current.Path.Equals(scope.Path, StringComparison.OrdinalIgnoreCase))
                        throw new FormatException("The selected import scope itself cannot be deleted.");
                    if (pendingKeys.Any(p => p.Equals(current.Path, StringComparison.OrdinalIgnoreCase) ||
                        p.StartsWith(current.Path + "\\", StringComparison.OrdinalIgnoreCase)))
                        throw new FormatException("A newly created key is also deleted in this file.");
                    if (touched.Any(p => p.StartsWith(current.Path + "\\", StringComparison.OrdinalIgnoreCase) ||
                        p.StartsWith(current.Path + "\0", StringComparison.OrdinalIgnoreCase)))
                        throw new FormatException("A key is changed before being deleted in this file.");
                    if (Exists(current))
                    {
                        var tree = RegistryTree.Scan(current, ct);
                        changes.Add(new RegistryChange(RegistryAction.DeleteKey, Parent(current), Name(current),
                            TreeDigest: RegistryTree.Digest(tree)));
                        deletedTrees++;
                    }
                    deletedKeys.Add(current.Path);
                    current = null;
                }
                else
                {
                    var parts = current.Path[(scope.Path.Length)..].TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
                    var walk = scope;
                    foreach (var part in parts)
                    {
                        var child = walk.WithPath(walk.Path + "\\" + part);
                        if (!pendingKeys.Contains(child.Path) && !Exists(child))
                        {
                            changes.Add(new RegistryChange(RegistryAction.CreateKey, walk, part));
                            pendingKeys.Add(child.Path);
                            addedKeys++;
                        }
                        walk = child;
                    }
                }
            }
            else
            {
                if (current is null) throw new FormatException($"Value directive without a key at line {lineNumber}.");
                var (name, desired) = ParseValue(line, lineNumber);
                if (!touched.Add(current.Path + "\0" + name))
                    throw new FormatException($"Duplicate value directive at line {lineNumber}.");
                RegistryValueSnapshot? expected = null;
                if (!pendingKeys.Contains(current.Path))
                {
                    using var key = WindowsRegistryProvider.Open(current, false);
                    var old = RegistryRaw.ReadIfPresent(key, name);
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
        return new RegistryImportPlan(changes, addedKeys, addedValues, overwritten, deletedValues, deletedTrees, dataBytes);
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

    private static bool Exists(Location location)
    {
        try { using var _ = WindowsRegistryProvider.Open(location, false); return true; }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2) { return false; }
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
