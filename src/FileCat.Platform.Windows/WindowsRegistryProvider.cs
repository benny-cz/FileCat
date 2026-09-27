using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows;

/// <summary>Typed, local Registry navigation. A location's session is its explicit WOW64 view.</summary>
public sealed class WindowsRegistryProvider : ResourceProvider
{
    private static readonly (string Name, RegistryHive Hive)[] Roots =
    [
        ("HKCU", RegistryHive.CurrentUser), ("HKLM", RegistryHive.LocalMachine),
        ("HKCR", RegistryHive.ClassesRoot), ("HKU", RegistryHive.Users),
        ("HKCC", RegistryHive.CurrentConfig),
    ];

    public override string Scheme => Schemes.Registry;

    public static Location Home(string view = "default") => new(Schemes.Registry, "", session: view);

    public override string GetDisplayPath(Location location) =>
        location.Path.Length == 0 ? $"Registry [{ViewLabel(location.Session)}]" :
        $"Registry [{ViewLabel(location.Session)}] \\{location.Path}";

    public override Location? GetParent(Location location)
    {
        if (location.Path.Length == 0) return null;
        int slash = location.Path.LastIndexOf('\\');
        return location.WithPath(slash < 0 ? "" : location.Path[..slash]);
    }

    public override string? GetNameInParent(Location location) =>
        location.Path.Length == 0 ? null : location.Path[(location.Path.LastIndexOf('\\') + 1)..];

    public override LocationCapabilities GetCapabilities(Location location) => location.Path.Length == 0 ||
        location.Path is "HKCR" or "HKCC" || location.Path.StartsWith("HKCR\\", StringComparison.Ordinal) ||
        location.Path.StartsWith("HKCC\\", StringComparison.Ordinal)
        ? LocationCapabilities.Enumerate
        : LocationCapabilities.Enumerate | LocationCapabilities.CreateDirectory | LocationCapabilities.Delete |
          LocationCapabilities.Rename | LocationCapabilities.TransferTarget;

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) =>
        "Registry data is typed; use its Registry commands rather than file operations.";

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) =>
        Task.Run(() => Enumerate(location, sink, ct), ct);

    private static void Enumerate(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        if (location.Path.Length == 0)
        {
            sink.AddBatch(Roots.Select(r => new EntryData(r.Name, EntryKind.RegistryKey)
            {
                Tag = new RegistryRowInfo("Root", r.Name is "HKCR" or "HKCC" ? "Alias or merged view; write through an explicit target." : "")
            }).ToArray());
            return;
        }
        using var key = Open(location, writable: false);
        var batch = new List<EntryData>(128);
        void Add(EntryData entry)
        {
            batch.Add(entry);
            if (batch.Count < 128) return;
            sink.AddBatch(CollectionsMarshal.AsSpan(batch));
            batch.Clear();
        }
        foreach (var name in key.GetSubKeyNames())
        {
            ct.ThrowIfCancellationRequested();
            Add(new EntryData(name, EntryKind.RegistryKey) { Tag = new RegistryRowInfo("Key", "") });
        }
        foreach (var name in key.GetValueNames())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var value = RegistryRaw.Read(key, name, RegistryRaw.PreviewLimit);
                Add(new EntryData(name, EntryKind.RegistryValue, value.Length)
                {
                    Tag = new RegistryRowInfo(value.TypeName, RegistryRaw.Preview(value))
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                sink.ReportIssue($"Cannot read Registry value '{(name.Length == 0 ? "(Default)" : name)}': {ex.Message}");
            }
        }
        if (batch.Count > 0) sink.AddBatch(CollectionsMarshal.AsSpan(batch));
    }

    public override Location? GetChildLocation(Location parent, in EntryData entry) => entry.Kind == EntryKind.RegistryKey
        ? parent.WithPath(parent.Path.Length == 0 ? entry.Name : parent.Path + "\\" + entry.Name) : null;

    public override bool TryParse(string text, Location? current, out Location? location)
    {
        location = null;
        var path = text.Trim();
        if (path.StartsWith("reg:", StringComparison.OrdinalIgnoreCase)) path = path[4..].TrimStart('\\');
        else if (!Roots.Any(r => path.Equals(r.Name, StringComparison.OrdinalIgnoreCase) || path.StartsWith(r.Name + "\\", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (path.Length == 0)
        {
            location = Home(current?.Scheme == Scheme ? current.Session ?? "default" : "default");
            return true;
        }
        var split = path.IndexOf('\\');
        var root = split < 0 ? path : path[..split];
        var canonical = Roots.FirstOrDefault(r => r.Name.Equals(root, StringComparison.OrdinalIgnoreCase)).Name;
        if (canonical is null || path.Split('\\').Any(p => p is "" or "." or "..")) return false;
        location = new Location(Scheme, canonical + (split < 0 ? "" : path[split..]),
            session: current?.Scheme == Scheme ? current.Session : "default");
        return true;
    }

    public static RegistryKey Open(Location location, bool writable)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var split = location.Path.IndexOf('\\');
        var root = split < 0 ? location.Path : location.Path[..split];
        var hive = Roots.FirstOrDefault(r => r.Name == root).Name is null
            ? throw new ArgumentException("A Registry root is required.", nameof(location))
            : Roots.First(r => r.Name == root).Hive;
        if (writable && root is "HKCR" or "HKCC")
            throw new InvalidOperationException("This merged or alias root is read-only here. Choose an explicit HKCU or HKLM target.");
        var view = location.Session switch
        {
            "32" => RegistryView.Registry32,
            "64" => RegistryView.Registry64,
            null or "default" => RegistryView.Default,
            _ => throw new ArgumentException("Unsupported Registry view.", nameof(location)),
        };
        var baseKey = RegistryKey.OpenBaseKey(hive, view);
        if (split < 0) return baseKey;
        try { return baseKey.OpenSubKey(location.Path[(split + 1)..], writable) ?? throw new IOException("Registry key no longer exists."); }
        finally { baseKey.Dispose(); }
    }

    public static string ViewLabel(string? view) => view switch { "32" => "32-bit", "64" => "64-bit", _ => "default view" };
}

public sealed record RegistryRowInfo(string KindText, string DetailsText) : IDisplayDetails;

/// <summary>Raw Registry value. Type and bytes are retained even for unknown or malformed data.</summary>
public sealed record RegistryValueData(uint Type, byte[] Data, int Length)
{
    public string TypeName => Type switch
    {
        0 => "REG_NONE", 1 => "REG_SZ", 2 => "REG_EXPAND_SZ", 3 => "REG_BINARY",
        4 => "REG_DWORD", 7 => "REG_MULTI_SZ", 11 => "REG_QWORD", 5 => "REG_DWORD_BIG_ENDIAN",
        6 => "REG_LINK", _ => $"REG_TYPE_{Type}"
    };
}

public static partial class RegistryRaw
{
    public const int PreviewLimit = 4096;
    public const int EditLimit = 64 * 1024 * 1024;
    private const int MoreData = 234;

    public static RegistryValueData Read(RegistryKey key, string name, int limit = EditLimit)
    {
        uint size = 0;
        int code = RegQueryValueEx(key.Handle, name, 0, out uint type, null, ref size);
        if (code != 0) throw new Win32Exception(code);
        if (size > int.MaxValue) throw new IOException("This Registry value exceeds the supported size.");
        if (size > limit) return new RegistryValueData(type, [], checked((int)size));
        var bytes = new byte[size];
        for (int i = 0; i < 3; i++)
        {
            uint actual = (uint)bytes.Length;
            code = RegQueryValueEx(key.Handle, name, 0, out type, bytes, ref actual);
            if (code == 0) return new RegistryValueData(type, bytes[..checked((int)actual)], checked((int)actual));
            if (code != MoreData || actual > limit) break;
            bytes = new byte[actual];
        }
        throw new Win32Exception(code == MoreData ? MoreData : code);
    }

    public static RegistryValueData? ReadIfPresent(RegistryKey key, string name, int limit = EditLimit)
    {
        uint size = 0;
        int code = RegQueryValueEx(key.Handle, name, 0, out _, null, ref size);
        if (code == 2) return null;
        if (code != 0) throw new Win32Exception(code);
        return Read(key, name, limit);
    }

    public static void Set(RegistryKey key, string name, uint type, byte[] data)
    {
        int code = RegSetValueEx(key.Handle, name, 0, type, data, checked((uint)data.Length));
        if (code != 0) throw new Win32Exception(code);
    }

    public static void Delete(RegistryKey key, string name)
    {
        int code = RegDeleteValue(key.Handle, name);
        if (code != 0) throw new Win32Exception(code);
    }

    public static string Preview(RegistryValueData value)
    {
        if (value.Data.Length < value.Length) return $"{value.Length:N0} bytes (open to inspect)";
        var data = value.Data;
        if (value.Type is 1 or 2 or 7 or 6)
        {
            if ((data.Length & 1) != 0) return "Malformed UTF-16 data";
            var s = System.Text.Encoding.Unicode.GetString(data).TrimEnd('\0').Replace("\0", " · ");
            return s.Length > 180 ? s[..180] + "…" : s;
        }
        if (value.Type == 4 && data.Length == 4) return $"0x{BitConverter.ToUInt32(data):X8} ({BitConverter.ToUInt32(data)})";
        if (value.Type == 11 && data.Length == 8) return $"0x{BitConverter.ToUInt64(data):X16} ({BitConverter.ToUInt64(data)})";
        return data.Length == 0 ? "(empty)" : Convert.ToHexString(data.AsSpan(0, Math.Min(32, data.Length))) + (data.Length > 32 ? "…" : "");
    }

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegQueryValueEx(SafeRegistryHandle key, string valueName, nint reserved, out uint type, byte[]? data, ref uint dataSize);

    [LibraryImport("advapi32.dll", EntryPoint = "RegSetValueExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegSetValueEx(SafeRegistryHandle key, string valueName, nint reserved, uint type, byte[] data, uint dataSize);

    [LibraryImport("advapi32.dll", EntryPoint = "RegDeleteValueW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegDeleteValue(SafeRegistryHandle key, string valueName);
}
