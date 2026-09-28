using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;

namespace FileCat.Core.FileSystem;

/// <summary>
/// POSIX permissions and ownership on Linux and macOS (P9): the rwx text <c>ls -l</c> prints, octal forms, owner and group
/// names, and the modes copies keep. An entry's own values are read (a link's, not its target's), as <c>ls -l</c> does.
/// </summary>
public static unsafe partial class UnixPermissions
{
    /// <summary>The nine read, write, and execute bits.</summary>
    public const UnixFileMode Access = (UnixFileMode)0x1FF;

    /// <summary>Set-user-ID, set-group-ID, and sticky.</summary>
    public const UnixFileMode Special = UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit;

    public const UnixFileMode AnyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    private const UnixFileMode OwnerAll = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>"rwxr-xr-x"; set-ID and sticky bits show as s/S and t/T in the execute places, as <c>ls</c> prints them.</summary>
    public static string Format(UnixFileMode mode)
    {
        Span<char> s = stackalloc char[9];
        s[0] = Letter(mode, UnixFileMode.UserRead, 'r');
        s[1] = Letter(mode, UnixFileMode.UserWrite, 'w');
        s[2] = Execute(mode, UnixFileMode.UserExecute, UnixFileMode.SetUser, 's');
        s[3] = Letter(mode, UnixFileMode.GroupRead, 'r');
        s[4] = Letter(mode, UnixFileMode.GroupWrite, 'w');
        s[5] = Execute(mode, UnixFileMode.GroupExecute, UnixFileMode.SetGroup, 's');
        s[6] = Letter(mode, UnixFileMode.OtherRead, 'r');
        s[7] = Letter(mode, UnixFileMode.OtherWrite, 'w');
        s[8] = Execute(mode, UnixFileMode.OtherExecute, UnixFileMode.StickyBit, 't');
        return new string(s);

        static char Letter(UnixFileMode mode, UnixFileMode bit, char letter) => (mode & bit) != 0 ? letter : '-';

        static char Execute(UnixFileMode mode, UnixFileMode execute, UnixFileMode special, char letter) =>
            (mode & special) == 0 ? Letter(mode, execute, 'x') : (mode & execute) != 0 ? letter : char.ToUpperInvariant(letter);
    }

    /// <summary>"644", or four digits ("2775") when a special bit is set.</summary>
    public static string Octal(UnixFileMode mode)
    {
        int value = (int)(mode & (Access | Special));
        return Convert.ToString(value, 8).PadLeft(value > (int)Access ? 4 : 3, '0');
    }

    /// <summary>Three or four octal digits, as chmod takes them ("644", "0755", "1777").</summary>
    public static bool TryParseOctal(string? text, out UnixFileMode mode)
    {
        mode = 0;
        var digits = text.AsSpan().Trim();
        if (digits.Length is < 3 or > 4) return false;
        int value = 0;
        foreach (char c in digits)
        {
            if (c is < '0' or > '7') return false;
            value = value * 8 + (c - '0');
        }
        mode = (UnixFileMode)value;
        return true;
    }

    /// <summary>
    /// A permission change as chmod applies it: <paramref name="clear"/> and <paramref name="set"/> bits, where set execute
    /// bits reach a file inside a changed folder only when it is already executable (chmod's X), so a recursive change
    /// never makes documents runnable. Folders always take them (execute lets you open a folder).
    /// </summary>
    public static UnixFileMode Apply(UnixFileMode current, UnixFileMode set, UnixFileMode clear, bool isDirectory, bool inside)
    {
        if (inside && !isDirectory && (current & AnyExecute) == 0) set &= ~AnyExecute;
        return (current & ~clear) | set;
    }

    /// <summary>
    /// The mode a copy gets: the source's, except set-user-ID and (for files) set-group-ID, which a copy owned by someone
    /// else must not carry (as cp does without -p).
    /// </summary>
    public static UnixFileMode ForCopy(UnixFileMode source, bool isDirectory) =>
        source & (Access | Special) & ~(isDirectory ? UnixFileMode.SetUser : UnixFileMode.SetUser | UnixFileMode.SetGroup);

    /// <summary>
    /// The mode a copy is created with while FileCat fills it: no wider than the source for group and others, and full
    /// access for the owner (the final mode follows once it is complete).
    /// </summary>
    public static UnixFileMode WhileCopying(UnixFileMode source, bool isDirectory) =>
        (source & Access & ~OwnerAll) | UnixFileMode.UserRead | UnixFileMode.UserWrite | (isDirectory ? UnixFileMode.UserExecute : 0);

    /// <summary>
    /// Gives a folder FileCat created as a copy the mode it should have: <paramref name="final"/> once its contents are in,
    /// otherwise the restricted mode it is filled with. False where the drive has no permissions (FAT, some network shares).
    /// </summary>
    public static bool TryCopyFolderMode(string source, string target, bool final)
    {
        if (OperatingSystem.IsWindows()) return false;
        try
        {
            var mode = File.GetUnixFileMode(source);
            File.SetUnixFileMode(target, final ? ForCopy(mode, isDirectory: true) : WhileCopying(mode, isDirectory: true));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The entry's own mode, owner, and group (a link's, not its target's); null when they cannot be read.</summary>
    public static (UnixFileMode Mode, uint Uid, uint Gid)? Stat(string path)
    {
        try
        {
            if (OperatingSystem.IsLinux()) return LinuxStat(path);
            if (OperatingSystem.IsMacOS()) return MacStat(path);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // A C library without statx (glibc before 2.28): no owner columns.
        }
        return null;
    }

    private static (UnixFileMode, uint, uint)? LinuxStat(string path)
    {
        // struct statx has one layout on every architecture: uid at 20, gid at 24, the 16-bit mode at 28 (256 bytes).
        const int AtFdCwd = -100, AtSymlinkNoFollow = 0x100;
        const uint Wanted = 0x1 | 0x2 | 0x8 | 0x10; // STATX_TYPE | STATX_MODE | STATX_UID | STATX_GID
        byte* buffer = stackalloc byte[256];
        if (Statx(AtFdCwd, path, AtSymlinkNoFollow, Wanted, buffer) != 0) return null;
        return ((UnixFileMode)(*(ushort*)(buffer + 28) & 0xFFF), *(uint*)(buffer + 20), *(uint*)(buffer + 24));
    }

    private static (UnixFileMode, uint, uint)? MacStat(string path)
    {
        // struct stat with 64-bit inodes: dev_t st_dev; mode_t st_mode (16-bit, at 4); nlink_t; ino_t; uid at 16; gid at 20.
        byte* buffer = stackalloc byte[256];
        int result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? MacLstat(path, buffer) : MacLstatInode64(path, buffer);
        if (result != 0) return null;
        return ((UnixFileMode)(*(ushort*)(buffer + 4) & 0xFFF), *(uint*)(buffer + 16), *(uint*)(buffer + 20));
    }

    /// <summary>This process's user ID (0 on Windows, which has none).</summary>
    public static uint CurrentUserId => OperatingSystem.IsWindows() ? 0 : _uid ??= GetUid();

    private static uint? _uid;

    /// <summary>Whether this user may add entries to a directory (honors ACLs and read-only mounts, unlike mode bits).</summary>
    public static bool CanWrite(string directory)
    {
        if (OperatingSystem.IsWindows()) return false;
        try
        {
            return AccessCheck(directory, 2 | 1) == 0; // W_OK | X_OK
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    private static readonly ConcurrentDictionary<uint, string> Users = new(), Groups = new();

    /// <summary>The account name for a user ID, or the number when the system has no name for it.</summary>
    public static string UserName(uint uid) => Users.GetOrAdd(uid, id => LookUp(id, user: true));

    /// <summary>The group name for a group ID, or the number when the system has no name for it.</summary>
    public static string GroupName(uint gid) => Groups.GetOrAdd(gid, id => LookUp(id, user: false));

    private static string LookUp(uint id, bool user)
    {
        const int ERANGE = 34; // the same on Linux and macOS
        // struct passwd and struct group both begin with the name pointer on Linux and macOS.
        byte* record = stackalloc byte[256];
        try
        {
            for (int size = 4096; size <= 1 << 20; size *= 4)
            {
                var strings = new byte[size];
                int error;
                fixed (byte* buffer = strings)
                {
                    nint result = 0;
                    error = user ? GetPwUidR(id, record, buffer, size, &result) : GetGrGidR(id, record, buffer, size, &result);
                    if (error == 0 && result != 0 && Marshal.PtrToStringUTF8(*(nint*)result) is { Length: > 0 } name) return name;
                }
                if (error != ERANGE) break;
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
        return id.ToString(CultureInfo.InvariantCulture);
    }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Statx(int directory, string path, int flags, uint mask, byte* buffer);

    [LibraryImport("libc", EntryPoint = "lstat", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MacLstat(string path, byte* buffer);

    [LibraryImport("libc", EntryPoint = "lstat$INODE64", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MacLstatInode64(string path, byte* buffer);

    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUid();

    [LibraryImport("libc", EntryPoint = "access", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int AccessCheck(string path, int mode);

    [LibraryImport("libc", EntryPoint = "getpwuid_r")]
    private static partial int GetPwUidR(uint uid, byte* record, byte* buffer, nint size, nint* result);

    [LibraryImport("libc", EntryPoint = "getgrgid_r")]
    private static partial int GetGrGidR(uint gid, byte* record, byte* buffer, nint size, nint* result);
}
