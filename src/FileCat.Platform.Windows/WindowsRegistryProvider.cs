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

    /// <summary>"Registry › HKCU\Software", naming the view when it is not the default ("Registry (32-bit view) › …").</summary>
    public override string GetDisplayPath(Location location)
    {
        string registry = location.Session is null or "default" ? "Registry" : $"Registry ({ViewLabel(location.Session)} view)";
        return location.Path.Length == 0 ? registry : $"{registry} › {location.Path}";
    }

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
        location.Path.Length == 0 ? "Choose a Registry root such as HKCU or HKLM first."
        : RegistryAliases.IsAliasPath(location.Path) ? RegistryAliases.ReadOnlyReason
        : "Registry data is typed; use its Registry commands rather than file operations.";

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

    /// <summary>The long root names regedit shows, for FileCat's short ones.</summary>
    private static readonly (string Long, string Short)[] LongRoots =
    [
        ("HKEY_CURRENT_USER", "HKCU"), ("HKEY_LOCAL_MACHINE", "HKLM"), ("HKEY_CLASSES_ROOT", "HKCR"), ("HKEY_USERS", "HKU"),
        ("HKEY_CURRENT_CONFIG", "HKCC"),
    ];

    /// <summary>
    /// A path as regedit writes it, in FileCat's short form: "HKEY_CURRENT_USER\Software" is "HKCU\Software", and the
    /// "Computer\" its address bar starts with (in the system's language) is dropped, so a path copied there opens here.
    /// </summary>
    internal static string ShortRoots(string path)
    {
        int hkey = path.IndexOf("\\HKEY_", StringComparison.OrdinalIgnoreCase);
        if (hkey > 0 && path.IndexOf('\\') == hkey) path = path[(hkey + 1)..];
        foreach (var (longName, shortName) in LongRoots)
        {
            if (path.Equals(longName, StringComparison.OrdinalIgnoreCase)) return shortName;
            if (path.StartsWith(longName + "\\", StringComparison.OrdinalIgnoreCase)) return shortName + path[longName.Length..];
        }
        return path;
    }

    public override bool TryParse(string text, Location? current, out Location? location)
    {
        location = null;
        var path = text.Trim();
        bool prefixed = path.StartsWith("reg:", StringComparison.OrdinalIgnoreCase);
        path = ShortRoots(prefixed ? path[4..].TrimStart('\\') : path);
        if (!prefixed && !Roots.Any(r => path.Equals(r.Name, StringComparison.OrdinalIgnoreCase) || path.StartsWith(r.Name + "\\", StringComparison.OrdinalIgnoreCase)))
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

    /// <summary>A hive's predefined key handle (HKEY_LOCAL_MACHINE and the others), sign-extended as Windows defines them.</summary>
    private static nint PredefinedHandle(RegistryHive hive) => hive switch
    {
        RegistryHive.ClassesRoot => unchecked((int)0x80000000),
        RegistryHive.CurrentUser => unchecked((int)0x80000001),
        RegistryHive.LocalMachine => unchecked((int)0x80000002),
        RegistryHive.Users => unchecked((int)0x80000003),
        RegistryHive.CurrentConfig => unchecked((int)0x80000005),
        _ => throw new ArgumentOutOfRangeException(nameof(hive)),
    };

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
        // The root's predefined handle, not RegistryKey.OpenBaseKey: with an explicit view, .NET reopens a base key's
        // handle for writing before any subkey open, which a user without administrator rights is denied under HKLM,
        // HKU, and HKCC; the view is passed with each subkey open instead (release issue I21).
        var key = RegistryKey.FromHandle(new SafeRegistryHandle(PredefinedHandle(hive), ownsHandle: false), view);
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
    private const int KeyQueryValue = 0x0001;
    private const int DeleteAccess = 0x00010000;
    private const int NoMoreItems = 259;
    private const int FileNotFound = 2, PathNotFound = 3;

    public static IEnumerable<string> SubKeyNames(RegistryKey key) => EnumerateNames(key, values: false);

    /// <summary>
    /// Keys to watch for changes to <paramref name="location"/>. A key below a root is itself. A root is opened by its
    /// kernel path, never watched through its predefined handle (HKEY_CURRENT_USER and the like): reading a root
    /// through that shared handle signals the notifications on it, so a view that rereads on each change would reread
    /// for ever. HKCR is the machine's and the user's classes together.
    /// </summary>
    public static List<RegistryKey> OpenForWatching(Location location)
    {
        if (location.Path.Contains('\\')) return [WindowsRegistryProvider.Open(location, writable: false)];
        string user = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value
                      ?? throw new UnauthorizedAccessException("The current user's Registry cannot be found.");
        string[] paths = location.Path switch
        {
            "HKLM" => [@"\Registry\Machine"],
            "HKU" => [@"\Registry\User"],
            "HKCU" => [$@"\Registry\User\{user}"],
            "HKCC" => [@"\Registry\Machine\System\CurrentControlSet\Hardware Profiles\Current"],
            "HKCR" => [@"\Registry\Machine\Software\Classes", $@"\Registry\User\{user}_Classes"],
            _ => throw new ArgumentException("A Registry root is required.", nameof(location)),
        };
        var view = location.Session switch { "32" => RegistryView.Registry32, "64" => RegistryView.Registry64, _ => RegistryView.Default };
        var keys = new List<RegistryKey>();
        Exception? failure = null;
        foreach (string path in paths)
        {
            // The user's classes may not be loaded; the machine's alone still tell most changes.
            try { keys.Add(RegistryKey.FromHandle(OpenKernelPath(path), view)); }
            catch (Win32Exception ex) { failure ??= ex; }
        }
        return keys.Count > 0 ? keys : throw failure!;
    }

    private static SafeRegistryHandle OpenKernelPath(string path)
    {
        nint buffer = Marshal.StringToHGlobalUni(path);
        try
        {
            var name = new UnicodeString { Length = (ushort)(path.Length * 2), MaximumLength = (ushort)(path.Length * 2 + 2), Buffer = buffer };
            nint namePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            try
            {
                Marshal.StructureToPtr(name, namePointer, false);
                var attributes = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(), ObjectName = namePointer, Attributes = 0x40 /* OBJ_CASE_INSENSITIVE */ };
                int status = NtOpenKey(out var handle, KeyRead, ref attributes);
                if (status < 0)
                {
                    handle.Dispose();
                    throw new Win32Exception(RtlNtStatusToDosError(status));
                }
                return handle;
            }
            finally { Marshal.FreeHGlobal(namePointer); }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length;
        public nint RootDirectory;
        public nint ObjectName;
        public uint Attributes;
        public nint SecurityDescriptor;
        public nint SecurityQualityOfService;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtOpenKey(out SafeRegistryHandle handle, int access, ref ObjectAttributes attributes);
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

    /// <summary>
    /// KEY_WOW64_64KEY/KEY_WOW64_32KEY: every open below a root must carry the view explicitly, otherwise a 64-bit
    /// process silently reads the 64-bit view of redirected keys such as HKLM\SOFTWARE.
    /// </summary>
    internal static int ViewAccess(RegistryView view) => view switch
    {
        RegistryView.Registry64 => 0x0100,
        RegistryView.Registry32 => 0x0200,
        _ => 0,
    };

    public static RegistryKey OpenNoLink(RegistryKey parent, string name, RegistryView view, bool writable)
    {
        int code = RegOpenKeyEx(parent.Handle, name, OpenLink, (writable ? KeyWriteAndRead : KeyRead) | ViewAccess(view), out var handle);
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

    /// <summary>
    /// Opens a subkey for reading without following it: a Registry link comes back as <paramref name="linkTarget"/>
    /// (and a null key), anything else as the open key. One open serves both the check and the reading.
    /// </summary>
    public static RegistryKey? OpenChild(RegistryKey parent, string name, out string? linkTarget)
    {
        int code = RegOpenKeyEx(parent.Handle, name, OpenLink, KeyRead | ViewAccess(parent.View), out var handle);
        if (code != 0) throw new Win32Exception(code);
        var key = RegistryKey.FromHandle(handle, parent.View);
        try
        {
            linkTarget = ReadIfPresent(key, "SymbolicLinkValue", PreviewLimit) is { Type: 6 } link ? Preview(link) : null;
            if (linkTarget is null) return key;
            key.Dispose();
            return null;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    public static string? LinkTarget(RegistryKey parent, string name)
    {
        int code = RegOpenKeyEx(parent.Handle, name, OpenLink, KeyRead | ViewAccess(parent.View), out var handle);
        if (code != 0) throw new Win32Exception(code);
        using var key = RegistryKey.FromHandle(handle, parent.View);
        return ReadIfPresent(key, "SymbolicLinkValue", PreviewLimit) is { Type: 6 } link ? Preview(link) : null;
    }

    /// <summary>Whether a subkey or a Registry link with this name exists; a link is not followed.</summary>
    public static bool SubKeyExists(RegistryKey parent, string name)
    {
        int code = RegOpenKeyEx(parent.Handle, name, OpenLink, KeyQueryValue | ViewAccess(parent.View), out var handle);
        using (handle)
        {
            if (code == 0) return true;
            if (code is FileNotFound or PathNotFound) return false;
            throw new Win32Exception(code);
        }
    }

    /// <summary>
    /// Opens the key object itself for deletion. A Registry link opens as the link, never its target, so the caller
    /// checks exactly what it deletes and <see cref="DeleteOpenKey"/> removes that object.
    /// </summary>
    public static RegistryKey OpenForDelete(RegistryKey parent, string name)
    {
        int code = RegOpenKeyEx(parent.Handle, name, OpenLink, DeleteAccess | KeyRead | ViewAccess(parent.View), out var handle);
        if (code != 0) throw new Win32Exception(code);
        return RegistryKey.FromHandle(handle, parent.View);
    }

    public static bool IsLink(RegistryKey key) => ReadIfPresent(key, "SymbolicLinkValue", PreviewLimit) is { Type: 6 };

    /// <summary>
    /// Deletes the key behind this handle. RegDeleteKeyEx reopens by name and would follow a link to its target;
    /// NtDeleteKey on a handle opened with REG_OPTION_OPEN_LINK removes the link itself.
    /// </summary>
    public static void DeleteOpenKey(RegistryKey key)
    {
        int status = NtDeleteKey(key.Handle);
        if (status != 0) throw new Win32Exception(RtlNtStatusToDosError(status));
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

    /// <summary>Create a single key only when it was absent at the native create call.</summary>
    public static RegistryKey CreateNewKey(RegistryKey parent, string name, string? view)
    {
        int access = KeyWriteAndRead | (view switch { "32" => 0x0200, "64" => 0x0100, _ => 0 });
        int code = RegCreateKeyEx(parent.Handle, name, 0, null, 0, access, 0, out var handle, out uint disposition);
        if (code != 0) throw new Win32Exception(code);
        if (disposition != 1)
        {
            handle.Dispose();
            throw new RegistryConflictException("The destination key already exists.");
        }
        return RegistryKey.FromHandle(handle, view switch
        {
            "32" => RegistryView.Registry32,
            "64" => RegistryView.Registry64,
            _ => RegistryView.Default,
        });
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

    [LibraryImport("advapi32.dll", EntryPoint = "RegCreateKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegCreateKeyEx(SafeRegistryHandle parent, string subKey, uint reserved, string? className,
        uint options, int access, nint securityAttributes, out SafeRegistryHandle result, out uint disposition);

    [LibraryImport("ntdll.dll")]
    private static partial int NtDeleteKey(SafeRegistryHandle key);

    [LibraryImport("ntdll.dll")]
    private static partial int RtlNtStatusToDosError(int status);

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
