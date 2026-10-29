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

    /// <summary>
    /// A rename, never a copy: .NET's File.Move copies and deletes when a rename crosses file systems (btrfs subvolumes,
    /// bind mounts, overlays), unguarded, which the job engine then does itself, verified and journaled. Without
    /// replacing, the new name is claimed atomically (renameat2 with RENAME_NOREPLACE on Linux, renamex_np with
    /// RENAME_EXCL on macOS), so an item that appears there meanwhile is never overwritten. File systems that take no
    /// rename flags (NFS, 9p, FUSE without rename2) claim it with a hard link instead (UnixRename.NoReplaceWithoutFlags).
    /// </summary>
    public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
    {
        if (replaceExisting && Directory.Exists(source) && new DirectoryInfo(source).LinkTarget is null)
            throw new IOException("Directories are merged item by item, never replaced wholesale.");
        if (!replaceExisting)
        {
            int errno = ClaimName(source, destination);
            if (errno == 17 && IsOtherSpelling(source, destination)) errno = NewSpelling(source, destination);
            if (errno != 0) throw UnixRename.Failure(errno, source, destination);
            return;
        }
        int error = UnixRename.Replace(source, destination);
        if (error != 0) throw UnixRename.Failure(error, source, destination);
    }

    private static int ClaimName(string source, string destination)
    {
        int errno = UnixRename.NoReplace(source, destination);
        return UnixRename.Unsupported(errno) ? UnixRename.NoReplaceWithoutFlags(source, destination) : errno;
    }

    // The destination is the source itself spelled another way (letter case, or Unicode normalization) on a file system
    // that ignores the difference: FAT, exFAT, macOS's default APFS.
    private static bool IsOtherSpelling(string source, string destination) =>
        string.Equals(Path.GetDirectoryName(source), Path.GetDirectoryName(destination), StringComparison.Ordinal)
        && string.Equals(Path.GetFileName(source).Normalize(), Path.GetFileName(destination).Normalize(), StringComparison.OrdinalIgnoreCase)
        && UnixFiles.Stat(source) is { } from && UnixFiles.Stat(destination) is { } to && from.Device == to.Device && from.Inode == to.Inode;

    // A plain rename would not respell the item: Linux takes two names of one item as nothing to do. So it goes through a
    // temporary name, each step claiming its name without replacing, and back if its new name was taken meanwhile.
    private static int NewSpelling(string source, string destination)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(destination) ?? ".", "." + Guid.NewGuid().ToString("N") + ".filecat-rename");
        int errno = ClaimName(source, temporary);
        if (errno != 0) return errno;
        errno = ClaimName(temporary, destination);
        if (errno != 0 && ClaimName(temporary, source) != 0)
            throw new IOException($"\"{Path.GetFileName(destination)}\" was taken while \"{Path.GetFileName(source)}\" was being renamed to it; "
                + $"the item is now named \"{Path.GetFileName(temporary)}\".", errno);
        return errno;
    }

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
            string value = string.IsNullOrWhiteSpace(quarantine)
                ? $"0081;{DateTimeOffset.UtcNow.ToUnixTimeSeconds():x8};FileCat;" : quarantine;
            return Xattr.WriteReadable(path, QuarantineAttribute, value);
        }
        if (OperatingSystem.IsLinux())
            // Linux only has an origin-URL xattr here. A zone-only Internet mark has no value to
            // write, so reporting success would hand out an unmarked copy as if it were safe.
            return !string.IsNullOrWhiteSpace(hostUrl) && Xattr.WriteReadable(path, OriginAttribute, hostUrl);
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
    internal const int MaxValueBytes = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static IOException ReadFailure(string name, int error) =>
        new($"The origin attribute '{name}' could not be read (errno {error}).");

    private static bool MissingAttribute(int error) =>
        OperatingSystem.IsMacOS() ? error == 93 /* ENOATTR */ : error == 61 /* ENODATA */;

    public static string? Get(string path, string name)
    {
        try
        {
            nint size = OperatingSystem.IsMacOS() ? MacGet(path, name, null, 0, 0, MacNoFollow) : LinuxGet(path, name, null, 0);
            if (size < 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (MissingAttribute(error)) return null;
                throw ReadFailure(name, error);
            }
            if (size == 0 || size > MaxValueBytes)
                throw new IOException($"The origin attribute '{name}' is empty or exceeds {MaxValueBytes} bytes.");
            var buffer = new byte[size];
            nint read = OperatingSystem.IsMacOS() ? MacGet(path, name, buffer, size, 0, MacNoFollow) : LinuxGet(path, name, buffer, size);
            if (read < 0) throw ReadFailure(name, Marshal.GetLastPInvokeError());
            if (read != size) throw new IOException($"The origin attribute '{name}' changed while it was read.");
            string value;
            try { value = StrictUtf8.GetString(buffer).TrimEnd('\0'); }
            catch (DecoderFallbackException ex) { throw new IOException($"The origin attribute '{name}' is not valid UTF-8.", ex); }
            if (value.Length == 0) throw new IOException($"The origin attribute '{name}' is empty.");
            return value;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        { throw new IOException($"The origin attribute '{name}' cannot be read on this system.", ex); }
    }

    public static bool WriteReadable(string path, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || StrictUtf8.GetByteCount(value) > MaxValueBytes) return false;
        return Set(path, name, value) && string.Equals(Get(path, name), value, StringComparison.Ordinal);
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

    [LibraryImport("libc", EntryPoint = "getxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint MacGet(string path, string name, byte[]? value, nint size, uint position, int options);

    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MacSet(string path, string name, byte[] value, nint size, uint position, int options);

    [LibraryImport("libc", EntryPoint = "lgetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint LinuxGet(string path, string name, byte[]? value, nint size);

    [LibraryImport("libc", EntryPoint = "lsetxattr", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LinuxSet(string path, string name, byte[] value, nint size, int flags);
}
