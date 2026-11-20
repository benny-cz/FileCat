using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

/// <summary>Access to a live O_PATH pin through its process descriptor directory.</summary>
internal static unsafe partial class LinuxPinnedDescriptors
{
    internal static (SafeFileHandle? Handle, int Error) OpenDirectory(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int fd = Open("/proc/self/fd", 0x200000 | LinuxOpenFlags.CloseOnExec); // O_PATH | O_CLOEXEC
        if (fd < 0)
        {
            int failure = Marshal.GetLastPInvokeError();
            ct.ThrowIfCancellationRequested();
            return (null, failure);
        }

        var directory = new SafeFileHandle(fd, ownsHandle: true);
        int error;
        try
        {
            ct.ThrowIfCancellationRequested();
            byte* filesystem = stackalloc byte[256];
            error = FStatFs(directory, filesystem) != 0 ? Marshal.GetLastPInvokeError()
                : *(nint*)filesystem == 0x9fa0 /* PROC_SUPER_MAGIC */ ? 0 : 95 /* EOPNOTSUPP */;
            ct.ThrowIfCancellationRequested();
            if (error == 0) return (directory, 0);
        }
        catch { directory.Dispose(); throw; }
        directory.Dispose();
        return (null, error);
    }

    /// <summary>The caller keeps the verified, ordinary source inode pin live throughout this relative open.</summary>
    internal static (SafeFileHandle? Handle, int Error) ReopenReadOnly(SafeFileHandle directory, string entry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Procfs follows its numeric magic link to the pinned object, without resolving the original path again.
        int fd = OpenAt(directory, entry, LinuxOpenFlags.CloseOnExec | 0x800 | 0x100); // O_RDONLY | O_CLOEXEC | O_NONBLOCK | O_NOCTTY
        if (fd < 0)
        {
            int error = Marshal.GetLastPInvokeError();
            ct.ThrowIfCancellationRequested();
            return (null, error);
        }
        return (new SafeFileHandle(fd, ownsHandle: true), 0);
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAt(SafeFileHandle directory, string name, int flags);

    [LibraryImport("libc", EntryPoint = "fstatfs", SetLastError = true)]
    private static partial int FStatFs(SafeFileHandle directory, byte* buffer);
}
