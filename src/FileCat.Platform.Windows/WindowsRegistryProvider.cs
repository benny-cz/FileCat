using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows;

/// <summary>Typed, local Registry navigation. A location's session is its explicit WOW64 view.</summary>
public sealed class WindowsRegistryProvider : ResourceProvider
{
    public const int MaxListingEntries = 250_000;
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
        int listed = 0;
        void Add(EntryData entry)
        {
            if (listed++ >= MaxListingEntries)
                throw new RegistryListingLimitException($"This key has more than {MaxListingEntries:N0} entries. The listing is incomplete; use Registry search to narrow it.");
            batch.Add(entry);
            if (batch.Count < 128) return;
            sink.AddBatch(CollectionsMarshal.AsSpan(batch));
            batch.Clear();
        }
        foreach (var name in RegistryRaw.SubKeyNames(key))
        {
            ct.ThrowIfCancellationRequested();
            if (listed >= MaxListingEntries) { sink.ReportIssue($"Listing stopped at {MaxListingEntries:N0} entries; use Registry search to narrow it."); break; }
            try
            {
                var link = RegistryRaw.LinkTarget(key, name);
                Add(new EntryData(name, EntryKind.RegistryKey)
                {
                    Flags = link is null ? EntryFlags.None : EntryFlags.Link,
                    Tag = new RegistryRowInfo(link is null ? "Key" : "Link", link ?? "", link),
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                Add(new EntryData(name, EntryKind.RegistryKey)
                {
                    Flags = EntryFlags.Unavailable,
                    Tag = new RegistryRowInfo("Key", "Cannot inspect link or access rights"),
                });
                sink.ReportIssue($"Cannot inspect Registry key '{name}': {ex.Message}");
            }
        }
        foreach (var name in RegistryRaw.ValueNames(key))
        {
            ct.ThrowIfCancellationRequested();
            if (listed >= MaxListingEntries) { sink.ReportIssue($"Listing stopped at {MaxListingEntries:N0} entries; use Registry search to narrow it."); break; }
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

    public override Location? GetChildLocation(Location parent, in EntryData entry) => entry.Kind == EntryKind.RegistryKey && !entry.Has(EntryFlags.Link | EntryFlags.Unavailable)
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
        var key = RegistryKey.OpenBaseKey(hive, view);
        if (split < 0) return key;
        try
        {
            foreach (var part in location.Path[(split + 1)..].Split('\\'))
            {
                if (part.Length == 0) throw new ArgumentException("Empty Registry path component.", nameof(location));
                var child = RegistryRaw.OpenNoLink(key, part, view, writable);
                key.Dispose();
                key = child;
            }
            return key;
        }
        catch { key.Dispose(); throw; }
    }

    public static string ViewLabel(string? view) => view switch { "32" => "32-bit", "64" => "64-bit", _ => "default view" };
}

public sealed record RegistryRowInfo(string KindText, string DetailsText, string? LinkTarget = null) : IDisplayDetails;

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
    private const uint OpenLink = 0x00000008;
    private const int KeyRead = 0x20019;
    private const int KeyWriteAndRead = 0x2001F;
    private const int NoMoreItems = 259;

    public static IEnumerable<string> SubKeyNames(RegistryKey key) => EnumerateNames(key, values: false);
    public static IEnumerable<string> ValueNames(RegistryKey key) => EnumerateNames(key, values: true);

    private static IEnumerable<string> EnumerateNames(RegistryKey key, bool values)
    {
        for (uint index = 0; ; index++)
        {
            int capacity = 256;
            while (true)
            {
                var buffer = new StringBuilder(capacity);
                uint length = (uint)capacity;
                int code = values ? RegEnumValue(key.Handle, index, buffer, ref length, 0, 0, 0, 0)
                    : RegEnumKeyEx(key.Handle, index, buffer, ref length, 0, 0, 0, 0);
                if (code == NoMoreItems) yield break;
                if (code == MoreData && capacity < 32768) { capacity *= 2; continue; }
                if (code != 0) throw new Win32Exception(code);
                yield return buffer.ToString(0, checked((int)length));
                break;
            }
        }
    }

    public static RegistryKey OpenNoLink(RegistryKey parent, string name, RegistryView view, bool writable)
    {
        int code = RegOpenKeyEx(parent.Handle, name, OpenLink, writable ? KeyWriteAndRead : KeyRead, out var handle);
        if (code != 0) throw new Win32Exception(code);
        var key = RegistryKey.FromHandle(handle, view);
        try
        {
            if (ReadIfPresent(key, "SymbolicLinkValue", PreviewLimit) is { Type: 6 } link)
                throw new RegistryLinkException(Preview(link));
            return key;
        }
        catch { key.Dispose(); throw; }
    }

    public static string? LinkTarget(RegistryKey parent, string name)
    {
        int code = RegOpenKeyEx(parent.Handle, name, OpenLink, KeyRead, out var handle);
        if (code != 0) throw new Win32Exception(code);
        using var key = RegistryKey.FromHandle(handle);
        return ReadIfPresent(key, "SymbolicLinkValue", PreviewLimit) is { Type: 6 } link ? Preview(link) : null;
    }

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

    public static void DeleteKey(RegistryKey parent, string name, string? view)
    {
        uint flags = view switch { "32" => 0x0200u, "64" => 0x0100u, _ => 0u };
        int code = RegDeleteKeyEx(parent.Handle, name, flags, 0);
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

    [LibraryImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegOpenKeyEx(SafeRegistryHandle parent, string subKey, uint options, int access, out SafeRegistryHandle result);

    [LibraryImport("advapi32.dll", EntryPoint = "RegDeleteKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegDeleteKeyEx(SafeRegistryHandle parent, string subKey, uint viewFlags, uint reserved);

    [DllImport("advapi32.dll", EntryPoint = "RegEnumKeyExW", CharSet = CharSet.Unicode)]
    private static extern int RegEnumKeyEx(SafeRegistryHandle key, uint index, StringBuilder name, ref uint nameLength,
        nint reserved, nint className, nint classLength, nint lastWrite);

    [DllImport("advapi32.dll", EntryPoint = "RegEnumValueW", CharSet = CharSet.Unicode)]
    private static extern int RegEnumValue(SafeRegistryHandle key, uint index, StringBuilder name, ref uint nameLength,
        nint reserved, nint type, nint data, nint dataLength);
}

public sealed class RegistryLinkException(string target) : IOException($"Registry link; open its target explicitly: {target}")
{
    public string Target { get; } = target;
}

public sealed class RegistryListingLimitException(string message) : IOException(message);
