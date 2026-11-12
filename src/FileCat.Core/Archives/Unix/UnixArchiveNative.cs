using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Unix;

internal static unsafe partial class UnixArchiveNative
{
    internal static bool Mac => OperatingSystem.IsMacOS();
    internal static int SafeOpen => Mac ? 0x1000000 | 0x100 | 4 : LinuxOpenFlags.CloseOnExec | LinuxOpenFlags.Current.NoFollow | 0x800;
    internal static int DirectoryFlag => Mac ? 0x100000 : LinuxOpenFlags.Current.Directory;
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
    internal static SafeFileHandle ChildDirectory(SafeFileHandle parent, string leaf)
    { Leaf(leaf); return Handle(OpenAt(Fd(parent), leaf, SafeOpen | DirectoryFlag, 0)); }
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
        if (Fsync(Fd(file)) != 0 || Mac && Fcntl(Fd(file), 51 /* FULLFSYNC */, 0) != 0) throw SyncFailed(Marshal.GetLastPInvokeError());
    }
    /// <summary>
    /// A failed fsync keeps the system's error as its cause (AT08): a full drive or quota that the file system reports only
    /// when the data is written out (delayed allocation) is then the full-drive refusal, and an I/O error the drive-error
    /// one; the backend words both by phase. The message is the wording for a failure once saving may have begun.
    /// </summary>
    internal static ArchiveWriteException SyncFailed(int errno) => new(ArchiveWriteReason.PublicationUnavailable, ArchiveWriteMessages.DriveErrorDuringSave,
        new IOException("fsync failed (errno " + errno.ToString(CultureInfo.InvariantCulture) + ").") { HResult = errno });
    internal static void RequireCreationDirectory(SafeFileHandle directory, uint user)
    {
        var facts = UnixFiles.Stat(directory);
        if (facts is not { IsDirectory: true } || facts.Value.Owner != user || (facts.Value.Mode & 0x2 /* other write */) != 0 ||
            (facts.Value.Mode & 0x10 /* group write */) != 0 && !UserPrivateGroupFolder(directory))
            throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable,
                "Archive writing needs a folder you own that no other user or shared group can change.");
        if (!Mac) return; // POSIX named/group ACL permissions are limited by the mode's group mask.
        nint acl = GetAcl(Fd(directory), 0x100);
        if (acl == 0) { if (Marshal.GetLastPInvokeError() == 2) return; throw Unavailable(); }
        try
        {
            // macOS puts "group:everyone deny delete" on the home folder and on Desktop, Documents and Downloads: it protects the
            // folder itself, grants no one anything, and blocks nothing FileCat does inside. Exactly such entries are accepted:
            // ones that deny only deletion of the folder and pass nothing on. An allow entry could let another user change the
            // folder's names, an inherited one would reach the private stage folder, and a deny of anything else (adding or
            // removing names) would stop the exchange or its cleanup part way; all are refused, as is a list not read in full.
            nint entry = 0; int count = 0;
            for (int id = 0 /* ACL_FIRST_ENTRY */; AclEntry(acl, id, &entry) == 0; id = -1 /* ACL_NEXT_ENTRY */)
            {
                int tag = 0; nint flags = 0, rights = 0;
                if (++count > 4096 || AclTag(entry, &tag) != 0 || tag != 2 /* ACL_EXTENDED_DENY */ || AclFlagSet(entry, &flags) != 0 ||
                    AclGetFlag(flags, 1 << 5 /* ACL_ENTRY_FILE_INHERIT */) != 0 || AclGetFlag(flags, 1 << 6 /* ACL_ENTRY_DIRECTORY_INHERIT */) != 0 ||
                    AclGetFlag(flags, 1 << 8 /* ACL_ENTRY_ONLY_INHERIT */) != 0 || AclPermSet(entry, &rights) != 0 || !DeniesOnlyDeletion(rights))
                    throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable,
                        "Archive writing needs a folder whose access list (ACL) gives no one else access and limits nothing but deleting the folder itself.");
            }
            // Darwin acl_get_entry ends the list with -1/EINVAL; any other end is an access list not read in full.
            if (Marshal.GetLastPInvokeError() != 22) throw Unavailable();
        }
        finally { AclFree(acl); }
    }
    /// <summary>
    /// Debian, Ubuntu and Fedora give each user a private primary group and a 002 umask, so folders a user creates are
    /// group-writable by that group alone. Such a folder is as private as a 0755 one when its group is the process's own
    /// primary group, is named after the user, lists no other member, and no extended ACL widens access. Anything else,
    /// and every macOS folder (shared groups such as staff), keeps group write refused.
    /// </summary>
    /// <summary>Path form of <see cref="UserPrivateGroupFolder(SafeFileHandle)"/> for a folder pinned without following links.</summary>
    internal static bool IsUserPrivateGroupFolder(string folder)
    {
        try { using var held = PinDirectory(folder); return UserPrivateGroupFolder(held); }
        catch (Exception ex) when (ex is ArchiveWriteException or IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
    private static bool UserPrivateGroupFolder(SafeFileHandle directory)
    {
        if (Mac) return false;
        ArchiveUnixOuterMetadata metadata;
        try { metadata = ArchiveUnixOuterMetadata.CaptureDirectory(directory); }
        catch (Exception ex) when (ex is ArchiveWriteException or IOException or UnauthorizedAccessException or ArgumentException) { return false; }
        if (metadata.AclPresent || metadata.Native.Group != GetEgid()) return false;
        string user = Environment.UserName;
        int length = 16 * 1024;
        while (length <= 1 << 20)
        {
            byte* buffer = (byte*)NativeMemory.Alloc((nuint)length);
            try
            {
                GroupEntry entry; GroupEntry* found = null;
                int error = GetGroup(metadata.Native.Group, &entry, buffer, (nuint)length, &found);
                if (error == 34 /* ERANGE */) { length *= 4; continue; }
                if (error != 0 || found == null || Marshal.PtrToStringUTF8((nint)entry.Name) != user) return false;
                for (byte** member = entry.Members; member != null && *member != null; member++)
                    if (Marshal.PtrToStringUTF8((nint)(*member)) != user) return false;
                return true;
            }
            finally { NativeMemory.Free(buffer); }
        }
        return false;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct GroupEntry { internal byte* Name; internal byte* Password; internal uint Gid; internal byte** Members; }
    [LibraryImport("libc", EntryPoint = "getgrgid_r")]
    private static partial int GetGroup(uint gid, GroupEntry* group, byte* buffer, nuint length, GroupEntry** result);
    /// <summary>A second close-on-exec descriptor of the same open file description (fcntl F_DUPFD_CLOEXEC).</summary>
    internal static SafeFileHandle Duplicate(SafeFileHandle file) => Handle(Mac && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        // fcntl is variadic: Darwin ARM64 passes the variadic argument on the stack, after eight register slots.
        ? FcntlMacArm64(Fd(file), 67, 0, 0, 0, 0, 0, 0, 0) : Fcntl(Fd(file), Mac ? 67 : 1030, 0));
    internal static void SyncDirectory(SafeFileHandle directory)
    { if (Fsync(Fd(directory)) != 0) throw SyncFailed(Marshal.GetLastPInvokeError()); } // Successful fsync, not universal power-loss qualification.
    internal static int Fd(SafeFileHandle file) => checked((int)file.DangerousGetHandle()); // Caller retains/serializes ownership.
    internal static SafeFileHandle Handle(int fd) => fd < 0 ? throw OpenFailed(Marshal.GetLastPInvokeError()) : new((nint)fd, ownsHandle: true);
    // The raw errno is kept as HResult, as .NET keeps it, so a device or space error is recognized for the user's text.
    private static IOException OpenFailed(int errno) =>
        new("The native Unix archive open failed (errno " + errno.ToString(CultureInfo.InvariantCulture) + ").", errno);
    internal static ArchiveWriteException Unavailable() => new(ArchiveWriteReason.PublicationUnavailable, UnavailableText);
    /// <summary>The same refusal for a failed system call, with its errno kept as the HResult of the inner exception.</summary>
    internal static ArchiveWriteException Unavailable(int errno) => new(ArchiveWriteReason.PublicationUnavailable, UnavailableText,
        new IOException("The native call failed (errno " + errno.ToString(CultureInfo.InvariantCulture) + ").", errno));
    private const string UnavailableText = "The native Unix archive storage/permission control is unavailable; retain existing evidence.";
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
    [LibraryImport("libc", EntryPoint = "acl_get_tag_type", SetLastError = true)]
    private static partial int AclTag(nint entry, int* tag);
    [LibraryImport("libc", EntryPoint = "acl_get_flag_np", SetLastError = true)]
    private static partial int AclGetFlag(nint flags, int flag);
    [LibraryImport("libc", EntryPoint = "acl_get_permset", SetLastError = true)]
    private static partial int AclPermSet(nint entry, nint* rights);
    [LibraryImport("libc", EntryPoint = "acl_get_perm_np", SetLastError = true)]
    private static partial int AclGetPerm(nint rights, int right);

    // Every right acl_get_perm_np takes (acl_perm_t in sys/acl.h), but ACL_DELETE (1 << 4). Darwin's generic rights (1 << 21
    // to 1 << 24) are not among them: asked for one, acl_get_perm_np fails, and every entry would be refused (the Mac run of
    // 04dbc8fa refused macOS's own "deny delete" that way).
    private static readonly int[] RightsOtherThanDelete =
        [1 << 1, 1 << 2, 1 << 3, 1 << 5, 1 << 6, 1 << 7, 1 << 8, 1 << 9, 1 << 10, 1 << 11, 1 << 12, 1 << 13, 1 << 20];

    /// <summary>
    /// Whether this process may give a file it owns <paramref name="group"/>: its effective group, or one of its
    /// supplementary groups (a membership the system resolves otherwise, such as a nested macOS group, is not assumed).
    /// </summary>
    internal static bool MayGiveGroup(uint group)
    {
        if (group == GetEgid()) return true;
        int count = GetGroups(0, null);
        if (count is <= 0 or > 65_536) return false;
        uint[] groups = new uint[count];
        fixed (uint* values = groups) count = GetGroups(count, values);
        return count > 0 && groups.AsSpan(0, count).Contains(group);
    }
    private static bool DeniesOnlyDeletion(nint rights)
    {
        if (AclGetPerm(rights, 1 << 4 /* ACL_DELETE */) != 1) return false;
        foreach (int right in RightsOtherThanDelete) if (AclGetPerm(rights, right) != 0) return false;
        return true;
    }
    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static partial int Fsync(int fd);
    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int fd, int command, nint value);
    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int FcntlMacArm64(int fd, int command, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, nint value);
    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUid();
    [LibraryImport("libc", EntryPoint = "geteuid")]
    internal static partial uint GetEuid();
    [LibraryImport("libc", EntryPoint = "getgid")]
    private static partial uint GetGid();
    [LibraryImport("libc", EntryPoint = "getegid")]
    internal static partial uint GetEgid();
    [LibraryImport("libc", EntryPoint = "issetugid")]
    private static partial int IsSetugid();
    [LibraryImport("libc", EntryPoint = "getgroups", SetLastError = true)]
    private static partial int GetGroups(int count, uint* groups);
}
