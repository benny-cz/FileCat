using System.Runtime.InteropServices;

namespace FileCat.Core.FileSystem;

/// <summary>Copies macOS extended attributes, including the resource fork, onto an owned new file.</summary>
internal static partial class MacExtendedAttributes
{
    private const uint CopyFileXattr = 1u << 2; // copyfile.h: COPYFILE_XATTR
    private const string ResourceFork = "com.apple.ResourceFork";

    public static void Copy(FileStream source, FileStream destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            int sourceFd = source.SafeFileHandle.DangerousGetHandle().ToInt32();
            int destinationFd = destination.SafeFileHandle.DangerousGetHandle().ToInt32();
            long sourceForkSize = ResourceForkSize(sourceFd);
            // fcopyfile binds both sides to the same handles used for the data-fork copy. Path-based copyfile
            // could follow a changed name or write to a replacement placed at the destination in the meantime.
            int result = FCopyFile(sourceFd, destinationFd, 0, CopyFileXattr);
            if (result != 0)
                throw new IOException($"macOS could not copy the file's extended attributes (errno {Marshal.GetLastPInvokeError()}).");
            if (ResourceForkSize(destinationFd) != sourceForkSize)
                throw new IOException("macOS did not retain the resource fork; the incomplete copy was discarded.");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new IOException("macOS extended-attribute copying is unavailable; the incomplete copy was discarded.", ex);
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

    [LibraryImport("libc", EntryPoint = "fcopyfile", SetLastError = true)]
    private static partial int FCopyFile(int sourceFd, int destinationFd, nint state, uint flags);

    [LibraryImport("libc", EntryPoint = "fgetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint FGetXattr(int fd, string name, byte[]? value, nint size, uint position, int options);
}
