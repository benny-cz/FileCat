using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows;

/// <summary>Bounded, lossless Registry export. A .reg file does not carry WOW64 view or ACLs.</summary>
public static class RegistryInterchange
{
    public static void Export(Location keyLocation, string? valueName, string destination, CancellationToken ct = default)
    {
        if (keyLocation.Scheme != Schemes.Registry || keyLocation.Path.Length == 0)
            throw new ArgumentException("Choose a concrete Registry key.", nameof(keyLocation));
        if (Path.GetExtension(destination).Equals(".reg", StringComparison.OrdinalIgnoreCase) is false)
            throw new ArgumentException("Registry export requires a .reg destination.", nameof(destination));

        RegistryTreeSnapshot? tree = null;
        RegistryValueData? selectedValue = null;
        if (valueName is null)
        {
            tree = RegistryTree.Scan(keyLocation, ct);
            if (tree.LinkCount > 0)
                throw new NotSupportedException("This subtree contains Registry links. Exporting them as ordinary .reg keys would change their meaning.");
        }
        else
        {
            using var key = WindowsRegistryProvider.Open(keyLocation, false);
            selectedValue = RegistryRaw.Read(key, valueName);
            if (selectedValue.Data.Length != selectedValue.Length)
                throw new IOException("The selected Registry value exceeds the 64 MiB export limit.");
        }

        string fullDestination = Path.GetFullPath(destination);
        string temp = Path.Combine(Path.GetDirectoryName(fullDestination)!, ".filecat-reg-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.SequentialScan | FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UnicodeEncoding(false, true), 64 * 1024, leaveOpen: true))
            {
                writer.WriteLine("Windows Registry Editor Version 5.00");
                writer.WriteLine();
                writer.WriteLine($"; FileCat source view: {WindowsRegistryProvider.ViewLabel(keyLocation.Session)}. Choose the same view when importing.");
                writer.WriteLine("; Security descriptors are not included.");
                writer.WriteLine();

                if (tree is null)
                {
                    WriteKeyHeader(writer, keyLocation.Path);
                    WriteValue(writer, valueName!, selectedValue!);
                }
                else
                {
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
            if (tree is not null && RegistryTree.Digest(RegistryTree.Scan(keyLocation, ct)) != RegistryTree.Digest(tree))
                throw new RegistryConflictException("The Registry subtree changed during export. No file was published.");
            if (selectedValue is not null)
            {
                using var key = WindowsRegistryProvider.Open(keyLocation, false);
                var now = RegistryRaw.Read(key, valueName!);
                if (now.Type != selectedValue.Type || !now.Data.AsSpan().SequenceEqual(selectedValue.Data))
                    throw new RegistryConflictException("The Registry value changed during export. No file was published.");
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
