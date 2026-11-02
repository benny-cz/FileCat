using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

/// <summary>Copies Linux POSIX ACLs through held file descriptors before a new copy is published.</summary>
internal static partial class LinuxPosixAcl
{
    private const string AccessAcl = "system.posix_acl_access";
    private const string DefaultAcl = "system.posix_acl_default";
    private const int NoData = 61, NotSupported = 95;
    private const int OpenDirectoryFlags = 0x10000 | 0x20000 | 0x80000; // O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC
    private const int MaxAclBytes = 1024 * 1024;

    internal readonly record struct DirectoryMetadata(FileIdentity Identity, uint Mode, byte[]? Access, byte[]? Default)
    {
        public bool HasMetadata => Access is not null || Default is not null;
    }

    internal static DirectoryMetadata ProbeDirectory(string path)
    {
        using var handle = OpenDirectory(path);
        var stat = UnixFiles.Stat(handle) is { IsDirectory: true } found ? found
            : throw new IOException("The source folder's identity could not be read.");
        int fd = Descriptor(handle);
        return new DirectoryMetadata(stat.Identity, stat.Mode, Read(fd, AccessAcl), Read(fd, DefaultAcl));
    }

    /// <returns>True when the final mode was set through the held destination; false only on a filesystem
    /// without ACLs or mode changes, where the caller may retain its existing best-effort mode fallback.</returns>
    internal static bool CopyFile(FileStream source, FileStream destination, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var before = UnixFiles.Stat(source.SafeFileHandle) is { IsRegular: true } stat ? stat
            : throw new IOException("The source file's identity could not be read.");
        var target = UnixFiles.Stat(destination.SafeFileHandle) is { IsRegular: true } dest ? dest
            : throw new IOException("The copied file's identity could not be read.");
        int from = Descriptor(source.SafeFileHandle), to = Descriptor(destination.SafeFileHandle);
        byte[]? acl = Read(from, AccessAcl);
        ct.ThrowIfCancellationRequested();
        if (UnixFiles.Stat(source.SafeFileHandle) is not { } sourceAfterRead ||
            sourceAfterRead.Identity != before.Identity || sourceAfterRead.Mode != before.Mode ||
            !Same(Read(from, AccessAcl), acl))
            throw new IOException("The source file's ACL changed while it was copied; the incomplete copy was discarded.");
        bool inherited = Read(to, AccessAcl) is not null;
        if (inherited) Remove(to, AccessAcl);
        if (acl is not null) Set(to, AccessAcl, acl);
        uint finalMode = (uint)UnixPermissions.ForCopy((UnixFileMode)before.Mode, isDirectory: false);
        if (FChmod(to, finalMode) != 0)
        {
            if (acl is not null || inherited) throw Failure("set the copied file's permissions");
            return false;
        }
        if (!Same(Read(to, AccessAcl), acl) || UnixFiles.Stat(destination.SafeFileHandle) is not { } targetAfter ||
            targetAfter.Identity != target.Identity || targetAfter.Mode != finalMode ||
            UnixFiles.Stat(source.SafeFileHandle) is not { } sourceAfterCopy ||
            sourceAfterCopy.Identity != before.Identity || sourceAfterCopy.Mode != before.Mode ||
            !Same(Read(from, AccessAcl), acl))
            throw new IOException("The copied file's POSIX ACL did not match the source; the incomplete copy was discarded.");
        ct.ThrowIfCancellationRequested();
        return true;
    }

    internal static bool CopyDirectory(string source, string destination, DirectoryMetadata expected,
        FileIdentity destinationIdentity, CancellationToken ct)
    {
        using var from = OpenDirectory(source);
        using var to = OpenDirectory(destination);
        int sourceFd = Descriptor(from), targetFd = Descriptor(to);
        if (UnixFiles.Stat(from) is not { IsDirectory: true } sourceStat || sourceStat.Identity != expected.Identity ||
            sourceStat.Mode != expected.Mode || UnixFiles.Stat(to)?.Identity != destinationIdentity ||
            !Same(Read(sourceFd, AccessAcl), expected.Access) || !Same(Read(sourceFd, DefaultAcl), expected.Default))
            throw new IOException("The folder or its ACL changed before its metadata could be copied.");
        ct.ThrowIfCancellationRequested();
        bool inherited = Read(targetFd, AccessAcl) is not null || Read(targetFd, DefaultAcl) is not null;
        Remove(targetFd, AccessAcl);
        Remove(targetFd, DefaultAcl);
        if (expected.Access is not null) Set(targetFd, AccessAcl, expected.Access);
        if (expected.Default is not null) Set(targetFd, DefaultAcl, expected.Default);
        uint finalMode = (uint)UnixPermissions.ForCopy((UnixFileMode)expected.Mode, isDirectory: true);
        if (FChmod(targetFd, finalMode) != 0)
        {
            if (expected.HasMetadata || inherited) throw Failure("set the copied folder's permissions");
            return false;
        }
        if (!Same(Read(targetFd, AccessAcl), expected.Access) || !Same(Read(targetFd, DefaultAcl), expected.Default) ||
            UnixFiles.Stat(to) is not { } targetAfter || targetAfter.Identity != destinationIdentity ||
            targetAfter.Mode != finalMode || UnixFiles.Stat(from) is not { } sourceAfter ||
            sourceAfter.Identity != expected.Identity || sourceAfter.Mode != expected.Mode ||
            !Same(Read(sourceFd, AccessAcl), expected.Access) || !Same(Read(sourceFd, DefaultAcl), expected.Default))
            throw new IOException("The copied folder's POSIX ACL did not match the source.");
        ct.ThrowIfCancellationRequested();
        return true;
    }

    private static bool Same(byte[]? a, byte[]? b) =>
        a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);

    private static SafeFileHandle OpenDirectory(string path)
    {
        int fd = Open(path, OpenDirectoryFlags);
        if (fd < 0) throw Failure("open the folder without following a link");
        var handle = new SafeFileHandle((nint)fd, ownsHandle: true);
        if (UnixFiles.Stat(handle) is { IsDirectory: true }) return handle;
        handle.Dispose();
        throw new IOException("The path no longer names a folder.");
    }

    private static int Descriptor(SafeFileHandle handle) => checked((int)handle.DangerousGetHandle());

    private static byte[]? Read(int fd, string name)
    {
        nint size = FGetXattr(fd, name, null, 0);
        if (size < 0)
        {
            if (Marshal.GetLastPInvokeError() is NoData or NotSupported) return null;
            throw Failure("read the POSIX ACL");
        }
        if (size is <= 0 or > MaxAclBytes) throw new IOException("The POSIX ACL is outside the supported size.");
        var bytes = new byte[(int)size];
        if (FGetXattr(fd, name, bytes, size) != size) throw Failure("read the complete POSIX ACL");
        return bytes;
    }

    private static void Set(int fd, string name, byte[] bytes)
    {
        if (FSetXattr(fd, name, bytes, bytes.Length, 0) != 0) throw Failure("copy the POSIX ACL");
    }

    private static void Remove(int fd, string name)
    {
        if (FRemoveXattr(fd, name) != 0 && Marshal.GetLastPInvokeError() is not (NoData or NotSupported))
            throw Failure("clear an inherited POSIX ACL");
    }

    private static IOException Failure(string action) =>
        new($"Linux could not {action} (errno {Marshal.GetLastPInvokeError()}).");

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "fgetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint FGetXattr(int fd, string name, byte[]? value, nint size);

    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int FSetXattr(int fd, string name, byte[] value, nint size, int flags);

    [LibraryImport("libc", EntryPoint = "fremovexattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int FRemoveXattr(int fd, string name);

    [LibraryImport("libc", EntryPoint = "fchmod", SetLastError = true)]
    private static partial int FChmod(int fd, uint mode);
}
