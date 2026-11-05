using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Unix;

internal static unsafe partial class UnixArchiveNative
{
    internal static bool Mac => OperatingSystem.IsMacOS();
    internal static int SafeOpen => Mac ? 0x1000000 | 0x100 | 4 : 0x80000 | 0x20000 | 0x800;
    internal static int DirectoryFlag => Mac ? 0x100000 : 0x10000;
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static void RequirePlatform()
    {
        if ((!Mac && !OperatingSystem.IsLinux()) || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Native Unix archive storage requires x64 or ARM64.");
    }
    internal static uint RequireOrdinaryIdentity()
    {
        RequirePlatform();
        if (!Mac) return Linux.LinuxArchiveNative.RequireOrdinaryIdentity();
        uint user = GetUid(), group = GetGid();
        if (user == 0 || group == 0 || GetEuid() != user || GetEgid() != group || IsSetugid() != 0)
            throw new UnauthorizedAccessException("New archive storage requires an ordinary unchanged Unix identity.");
        int count = GetGroups(0, null); if (count is < 0 or > 65_536) throw Unavailable();
        uint[] groups = new uint[Math.Max(1, count)];
        fixed (uint* values = groups)
            if (GetGroups(count, values) != count || groups.AsSpan(0, count).Contains(0u)) throw Unavailable();
        return user;
    }
    internal static void Leaf(string leaf)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        if (leaf.Length == 0 || leaf is "." or ".." || leaf.Any(c => c < 32 || c == 127 || c is '/' or '\\' or '\uFFFD'))
            throw new ArgumentException("A single unambiguous Unix archive name is required.");
        try { if (Utf8.GetByteCount(leaf) > 255) throw new ArgumentException("The Unix archive filename is too long."); }
        catch (EncoderFallbackException) { throw new ArgumentException("The Unix archive filename has invalid Unicode."); }
    }
    internal static SafeFileHandle PinDirectory(string path)
    {
        RequirePlatform();
        if (!Path.IsPathFullyQualified(path) || path != Path.GetFullPath(path) || (path.Length > 1 && path.EndsWith('/')))
            throw new ArgumentException("An absolute canonical Unix archive directory is required.");
        var held = Handle(Open("/", SafeOpen | DirectoryFlag, 0));
        try
        {
            foreach (string leaf in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                Leaf(leaf); var child = Handle(OpenAt(Fd(held), leaf, SafeOpen | DirectoryFlag, 0)); held.Dispose(); held = child;
            }
            if (UnixFiles.Stat(held) is not { IsDirectory: true }) throw Unavailable();
            return held;
        }
        catch { held.Dispose(); throw; }
    }
    internal static SafeFileHandle Child(SafeFileHandle parent, string leaf, bool write = false)
    { Leaf(leaf); return Handle(OpenAt(Fd(parent), leaf, SafeOpen | (write ? 2 : 0), 0)); }
    internal static SafeFileHandle Create(SafeFileHandle parent, string fullPath, string leaf)
    {
        Leaf(leaf);
        if (!Mac) return Handle(OpenAt(Fd(parent), leaf, SafeOpen | 2 | 0x40 | 0x80 /* CREAT EXCL */, 0x180));
        // Public Darwin openx_np supplies an explicit empty no-inherit ACL at creation, avoiding a window where an
        // inherited allow ACE overrides mode 0600. Its public interface takes a path, so the caller separately pins
        // and rechecks both the parent namespace and its relative child before returning any writable stream.
        nint security = FileSecInit(), acl = 0;
        if (security == 0) throw Unavailable();
        try
        {
            acl = AclInit(0); nint flags = 0;
            if (acl == 0 || AclFlagSet(acl, &flags) != 0 || AclAddFlag(flags, 1 << 17 /* NO_INHERIT */) != 0 || AclSetFlagSet(acl, flags) != 0) throw Unavailable();
            ushort mode = 0x180;
            if (FileSecSet(security, 4 /* MODE */, &mode) != 0 || FileSecSet(security, 5 /* ACL */, &acl) != 0) throw Unavailable();
            // openx_np on the observed system rejects O_NOFOLLOW_ANY with EINVAL. O_NOFOLLOW still protects the
            // final component; full ancestor pinning plus post-create relative identity review stays mandatory.
            return Handle(OpenX(fullPath, SafeOpen | 2 | 0x200 | 0x800 /* CREAT EXCL */, security));
        }
        finally { if (acl != 0) AclFree(acl); FileSecFree(security); }
    }
    internal static void RequirePrivate(SafeFileHandle held, uint user)
    {
        var facts = UnixFiles.Stat(held);
        if (facts is not { IsRegular: true, Owner: var owner, Mode: 0x180 } || owner != user) throw Unavailable();
        if (ArchiveUnixOuterMetadata.Capture(held).Native.LinkCount != 1) throw Unavailable();
        if (Mac)
        {
            nint acl = GetAcl(Fd(held), 0x100);
            if (acl == 0) { if (Marshal.GetLastPInvokeError() == 2) return; throw Unavailable(); }
            try
            {
                nint entry = 0;
                // Darwin acl_get_entry returns 0 for an entry, -1/EINVAL when there are no entries.
                int result = AclEntry(acl, 0, &entry);
                if (result == 0 || result != -1 || Marshal.GetLastPInvokeError() != 22) throw Unavailable();
            }
            finally { AclFree(acl); }
        }
        // Linux POSIX access ACL named/group permissions are limited by the mode's zero group mask. Owner is the
        // only effective accessor under 0600, including an inherited default ACL. Admission of other security state
        // and equivalent-UID races are separate publication gates.
    }
    internal static void SyncFile(SafeFileHandle file)
    {
        if (Fsync(Fd(file)) != 0 || Mac && Fcntl(Fd(file), 51 /* FULLFSYNC */, 0) != 0) throw Unavailable();
    }
    internal static void SyncDirectory(SafeFileHandle directory)
    { if (Fsync(Fd(directory)) != 0) throw Unavailable(); } // Successful fsync, not universal power-loss qualification.
    internal static int Fd(SafeFileHandle file) => checked((int)file.DangerousGetHandle()); // Caller retains/serializes ownership.
    internal static SafeFileHandle Handle(int fd) => fd < 0 ? throw new IOException("The native Unix archive open failed (errno " + Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture) + ").") : new((nint)fd, ownsHandle: true);
    internal static ArchiveWriteException Unavailable() => new(ArchiveWriteReason.PublicationUnavailable, "The native Unix archive storage/permission control is unavailable; retain existing evidence.");
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags, uint mode);
    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAt(int parent, string name, int flags, uint mode);
    [LibraryImport("libc", EntryPoint = "openx_np", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenX(string path, int flags, nint security);
    [LibraryImport("libc", EntryPoint = "filesec_init")]
    private static partial nint FileSecInit();
    [LibraryImport("libc", EntryPoint = "filesec_free")]
    private static partial void FileSecFree(nint security);
    [LibraryImport("libc", EntryPoint = "filesec_set_property", SetLastError = true)]
    private static partial int FileSecSet(nint security, int property, void* value);
    [LibraryImport("libc", EntryPoint = "acl_init", SetLastError = true)]
    private static partial nint AclInit(int count);
    [LibraryImport("libc", EntryPoint = "acl_get_flagset_np", SetLastError = true)]
    private static partial int AclFlagSet(nint acl, nint* flags);
    [LibraryImport("libc", EntryPoint = "acl_add_flag_np", SetLastError = true)]
    private static partial int AclAddFlag(nint flags, int flag);
    [LibraryImport("libc", EntryPoint = "acl_set_flagset_np", SetLastError = true)]
    private static partial int AclSetFlagSet(nint acl, nint flags);
    [LibraryImport("libc", EntryPoint = "acl_get_fd_np", SetLastError = true)]
    private static partial nint GetAcl(int fd, int type);
    [LibraryImport("libc", EntryPoint = "acl_get_entry", SetLastError = true)]
    private static partial int AclEntry(nint acl, int kind, nint* entry);
    [LibraryImport("libc", EntryPoint = "acl_free")]
    private static partial int AclFree(nint acl);
    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static partial int Fsync(int fd);
    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int fd, int command, nint value);
    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUid();
    [LibraryImport("libc", EntryPoint = "geteuid")]
    internal static partial uint GetEuid();
    [LibraryImport("libc", EntryPoint = "getgid")]
    private static partial uint GetGid();
    [LibraryImport("libc", EntryPoint = "getegid")]
    private static partial uint GetEgid();
    [LibraryImport("libc", EntryPoint = "issetugid")]
    private static partial int IsSetugid();
    [LibraryImport("libc", EntryPoint = "getgroups", SetLastError = true)]
    private static partial int GetGroups(int count, uint* groups);
}
