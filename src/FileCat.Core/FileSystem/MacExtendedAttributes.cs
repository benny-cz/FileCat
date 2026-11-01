using System.Runtime.InteropServices;

namespace FileCat.Core.FileSystem;

/// <summary>Copies macOS extended attributes and ACLs onto an owned new file.</summary>
internal static partial class MacExtendedAttributes
{
    private const uint CopyFileAcl = 1u << 0; // copyfile.h: COPYFILE_ACL
    private const uint CopyFileXattr = 1u << 2; // copyfile.h: COPYFILE_XATTR
    private const int AclTypeExtended = 0x100; // sys/acl.h: ACL_TYPE_EXTENDED
    private const string ResourceFork = "com.apple.ResourceFork";

    public static void Copy(FileStream source, FileStream destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            int sourceFd = source.SafeFileHandle.DangerousGetHandle().ToInt32();
            int destinationFd = destination.SafeFileHandle.DangerousGetHandle().ToInt32();
            long sourceForkSize = ResourceForkSize(sourceFd);
            string sourceAcl = ReadAclText(sourceFd);
            // fcopyfile binds both sides to the same handles used for the data-fork copy. Path-based copyfile
            // could follow a changed name or write to a replacement placed at the destination in the meantime.
            int result = FCopyFile(sourceFd, destinationFd, 0, CopyFileXattr | CopyFileAcl);
            if (result != 0)
                throw new IOException($"macOS could not copy the file's extended attributes and ACL (errno {Marshal.GetLastPInvokeError()}).");
            if (ResourceForkSize(destinationFd) != sourceForkSize)
                throw new IOException("macOS did not retain the resource fork; the incomplete copy was discarded.");
            if (!string.Equals(ReadAclText(destinationFd), sourceAcl, StringComparison.Ordinal))
                throw new IOException("macOS did not retain the file's ACL; the incomplete copy was discarded.");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new IOException("macOS metadata copying is unavailable; the incomplete copy was discarded.", ex);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static long ResourceForkSize(int fd)
    {
        nint size = FGetXattr(fd, ResourceFork, null, 0, 0, 0);
        if (size >= 0) return size;
        int error = Marshal.GetLastPInvokeError();
        if (error == 93 /* ENOATTR */) return 0;
        throw new IOException($"macOS could not inspect the resource fork (errno {error}).");
    }

    internal static string ReadAclText(int fd)
    {
        nint acl = AclGetFdNp(fd, AclTypeExtended);
        if (acl == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error == 2 /* ENOENT: no ACL */) return string.Empty;
            throw new IOException($"macOS could not inspect the file's ACL (errno {error}).");
        }
        try
        {
            nint value = AclToText(acl, out nint length);
            if (value == 0) throw new IOException($"macOS could not read the file's ACL (errno {Marshal.GetLastPInvokeError()}).");
            try
            {
                if (length < 0 || length > 1_000_000) throw new IOException("The file's ACL text is outside the supported size.");
                return Marshal.PtrToStringUTF8(value, checked((int)length)) ?? string.Empty;
            }
            finally { AclFree(value); }
        }
        finally { AclFree(acl); }
    }

    [LibraryImport("libc", EntryPoint = "fcopyfile", SetLastError = true)]
    private static partial int FCopyFile(int sourceFd, int destinationFd, nint state, uint flags);

    [LibraryImport("libc", EntryPoint = "fgetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint FGetXattr(int fd, string name, byte[]? value, nint size, uint position, int options);

    [LibraryImport("libc", EntryPoint = "acl_get_fd_np", SetLastError = true)]
    private static partial nint AclGetFdNp(int fd, int type);

    [LibraryImport("libc", EntryPoint = "acl_to_text", SetLastError = true)]
    private static partial nint AclToText(nint acl, out nint length);

    [LibraryImport("libc", EntryPoint = "acl_free")]
    private static partial int AclFree(nint value);
}
