using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace FileCat.Core.FileSystem;

/// <summary>
/// Linux and macOS (P9): where a file came from travels in extended attributes, as Mark-of-the-Web does on Windows. On
/// macOS that is <c>com.apple.quarantine</c>, which makes Gatekeeper check a downloaded program before it first runs; on
/// Linux it is freedesktop's <c>user.xdg.origin.url</c>, which file managers show. Inside FileCat a mark is
/// Zone.Identifier-style text, so the job engine carries it the same way on every platform.
/// </summary>
public class UnixFileOperations : PortableFileOperations
{
    public const string QuarantineAttribute = "com.apple.quarantine";
    public const string OriginAttribute = "user.xdg.origin.url";

    public override string? ReadOriginMark(string path)
    {
        if (OperatingSystem.IsMacOS())
            return Xattr.Get(path, QuarantineAttribute) is { Length: > 0 } q ? $"[ZoneTransfer]\r\nZoneId=3\r\nQuarantine={q}\r\n" : null;
        if (OperatingSystem.IsLinux())
            return Xattr.Get(path, OriginAttribute) is { Length: > 0 } url ? $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl={url}\r\n" : null;
        return null;
    }

    public override bool WriteOriginMark(string path, string mark)
    {
        var (zone, hostUrl, quarantine) = Parse(mark);
        // Only content from the internet (zone 3) or an untrusted zone (4) is marked, as on Windows.
        if (zone < 3 && quarantine is null) return true;
        if (OperatingSystem.IsMacOS())
        {
            // flags;hex time;agent;event id — 0081 as browsers write for downloads.
            string value = quarantine ?? $"0081;{DateTimeOffset.UtcNow.ToUnixTimeSeconds():x8};FileCat;";
            return Xattr.Set(path, QuarantineAttribute, value);
        }
        if (OperatingSystem.IsLinux())
            return hostUrl is null || Xattr.Set(path, OriginAttribute, hostUrl);
        return false;
    }

    internal static (int Zone, string? HostUrl, string? Quarantine) Parse(string mark)
    {
        int zone = 0;
        string? host = null, quarantine = null;
        foreach (var raw in mark.Split('\n'))
        {
            var line = raw.Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq], value = line[(eq + 1)..];
            if (key.Equals("ZoneId", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out zone);
            else if (key.Equals("HostUrl", StringComparison.OrdinalIgnoreCase)) host = value;
            else if (key.Equals("Quarantine", StringComparison.OrdinalIgnoreCase)) quarantine = value;
        }
        return (zone, host, quarantine);
    }
}

/// <summary>Extended attributes through libc, never following a link (a link carries no mark of its own).</summary>
internal static partial class Xattr
{
    private const int MacNoFollow = 0x0001;

    public static string? Get(string path, string name)
    {
        try
        {
            nint size = OperatingSystem.IsMacOS() ? MacGet(path, name, null, 0, 0, MacNoFollow) : LinuxGet(path, name, null, 0);
            if (size <= 0 || size > 64 * 1024) return null;
            var buffer = new byte[size];
            size = OperatingSystem.IsMacOS() ? MacGet(path, name, buffer, size, 0, MacNoFollow) : LinuxGet(path, name, buffer, size);
            return size <= 0 ? null : Encoding.UTF8.GetString(buffer, 0, (int)size).TrimEnd('\0');
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    public static bool Set(string path, string name, string value)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            int result = OperatingSystem.IsMacOS() ? MacSet(path, name, bytes, bytes.Length, 0, MacNoFollow) : LinuxSet(path, name, bytes, bytes.Length, 0);
            return result == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    [LibraryImport("libc", EntryPoint = "getxattr", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint MacGet(string path, string name, byte[]? value, nint size, uint position, int options);

    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MacSet(string path, string name, byte[] value, nint size, uint position, int options);

    [LibraryImport("libc", EntryPoint = "lgetxattr", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint LinuxGet(string path, string name, byte[]? value, nint size);

    [LibraryImport("libc", EntryPoint = "lsetxattr", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LinuxSet(string path, string name, byte[] value, nint size, int flags);
}
