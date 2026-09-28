using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows;

/// <summary>Bounded, lossless Registry export. A .reg file does not carry WOW64 view or ACLs.</summary>
public static class RegistryInterchange
{
    public static void Export(Location keyLocation, string? valueName, string destination, CancellationToken ct = default) =>
        ExportMany([(keyLocation, valueName)], destination, ct);

    /// <summary>
    /// Exports several keys (with their subtrees) and single values of one view into one .reg file. Everything is read
    /// twice, and nothing is published if any part changed meanwhile.
    /// </summary>
    public static void ExportMany(IReadOnlyList<(Location Key, string? Value)> items, string destination, CancellationToken ct = default)
    {
        if (items.Count == 0) throw new ArgumentException("Choose something to export.", nameof(items));
        foreach (var (key, _) in items)
            if (key.Scheme != Schemes.Registry || key.Path.Length == 0)
                throw new ArgumentException("Choose a concrete Registry key.", nameof(items));
        if (items.Select(i => i.Key.Session ?? "default").Distinct().Count() > 1)
            throw new ArgumentException("A .reg file holds one Registry view; export each view separately.", nameof(items));
        if (Path.GetExtension(destination).Equals(".reg", StringComparison.OrdinalIgnoreCase) is false)
            throw new ArgumentException("Registry export requires a .reg destination.", nameof(destination));

        var parts = new List<(Location Key, string? Value, RegistryTreeSnapshot? Tree, RegistryValueData? Data)>();
        foreach (var (keyLocation, valueName) in items)
        {
            ct.ThrowIfCancellationRequested();
            if (valueName is null)
            {
                var tree = RegistryTree.Scan(keyLocation, ct);
                if (tree.LinkCount > 0)
                    throw new NotSupportedException($"{keyLocation.Path} contains Registry links. Exporting them as ordinary .reg keys would change their meaning.");
                parts.Add((keyLocation, null, tree, null));
            }
            else
            {
                using var key = WindowsRegistryProvider.Open(keyLocation, false);
                var value = RegistryRaw.Read(key, valueName);
                if (value.Data.Length != value.Length) throw new IOException("A selected Registry value exceeds the 64 MiB export limit.");
                parts.Add((keyLocation, valueName, null, value));
            }
        }

        string fullDestination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        string temp = Path.Combine(Path.GetDirectoryName(fullDestination)!, ".filecat-reg-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.SequentialScan | FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UnicodeEncoding(false, true), 64 * 1024, leaveOpen: true))
            {
                writer.WriteLine("Windows Registry Editor Version 5.00");
                writer.WriteLine();
                writer.WriteLine($"; FileCat source view: {WindowsRegistryProvider.ViewLabel(items[0].Key.Session)}. Choose the same view when importing.");
                writer.WriteLine("; Security descriptors are not included.");
                writer.WriteLine();

                foreach (var (keyLocation, valueName, tree, data) in parts)
                {
                    if (tree is null)
                    {
                        WriteKeyHeader(writer, keyLocation.Path);
                        WriteValue(writer, valueName!, data!);
                        writer.WriteLine();
                        continue;
                    }
                    foreach (var entry in tree.Keys)
                    {
                        ct.ThrowIfCancellationRequested();
                        var location = entry.RelativePath.Length == 0 ? keyLocation :
                            keyLocation.WithPath(keyLocation.Path + "\\" + entry.RelativePath);
                        using var key = WindowsRegistryProvider.Open(location, false);
                        WriteKeyHeader(writer, location.Path);
                        foreach (var name in RegistryRaw.ValueNames(key))
                        {
                            ct.ThrowIfCancellationRequested();
                            var value = RegistryRaw.Read(key, name);
                            if (value.Data.Length != value.Length)
                                throw new IOException("A Registry value exceeds the 64 MiB export limit.");
                            WriteValue(writer, name, value);
                        }
                        writer.WriteLine();
                    }
                }
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            ct.ThrowIfCancellationRequested();
            foreach (var (keyLocation, valueName, tree, data) in parts)
            {
                if (tree is not null && RegistryTree.Digest(RegistryTree.Scan(keyLocation, ct)) != RegistryTree.Digest(tree))
                    throw new RegistryConflictException($"{keyLocation.Path} changed during export. No file was published.");
                if (data is not null)
                {
                    using var key = WindowsRegistryProvider.Open(keyLocation, false);
                    var now = RegistryRaw.Read(key, valueName!);
                    if (now.Type != data.Type || !now.Data.AsSpan().SequenceEqual(data.Data))
                        throw new RegistryConflictException("A Registry value changed during export. No file was published.");
                }
            }
            File.Move(temp, fullDestination, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static void WriteKeyHeader(TextWriter writer, string path)
    {
        var slash = path.IndexOf('\\');
        var root = slash < 0 ? path : path[..slash];
        var fullRoot = root switch
        {
            "HKCU" => "HKEY_CURRENT_USER", "HKLM" => "HKEY_LOCAL_MACHINE",
            "HKCR" => "HKEY_CLASSES_ROOT", "HKU" => "HKEY_USERS",
            "HKCC" => "HKEY_CURRENT_CONFIG", _ => throw new ArgumentException("Unknown Registry root."),
        };
        writer.WriteLine($"[{fullRoot}{(slash < 0 ? "" : path[slash..])}]");
    }

    private static void WriteValue(TextWriter writer, string name, RegistryValueData value)
    {
        writer.Write(name.Length == 0 ? "@=" : "\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"=");
        writer.Write(value.Type switch
        {
            3 => "hex:",
            4 when value.Data.Length == 4 => "dword:",
            _ => $"hex({value.Type:x}):",
        });
        if (value.Type == 4 && value.Data.Length == 4)
        {
            writer.WriteLine(BitConverter.ToUInt32(value.Data).ToString("x8"));
            return;
        }
        for (int i = 0; i < value.Data.Length; i++)
        {
            if (i > 0) writer.Write(',');
            if (i > 0 && i % 24 == 0) writer.Write("\\\r\n  ");
            writer.Write(value.Data[i].ToString("x2"));
        }
        writer.WriteLine();
    }
}
