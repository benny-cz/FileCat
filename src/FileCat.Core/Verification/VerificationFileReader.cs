using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Verification;

/// <summary>Open a local verification input without waiting for a Unix FIFO writer.</summary>
internal static class VerificationFileReader
{
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenUnixReadOnly(string path, int flags);

    internal static FileStream OpenRead(string path, FileShare share = FileShare.Read)
    {
        if (OperatingSystem.IsWindows())
            return new FileStream(path, FileMode.Open, FileAccess.Read, share);

        // Keep ordinary linked sidecars readable. O_NONBLOCK makes a FIFO open return immediately, after which
        // CanSeek refuses that descriptor; the callers still enforce their own opened-byte limits.
        int nonblock = OperatingSystem.IsMacOS() ? 4 : 2048;
        int closeOnExec = OperatingSystem.IsMacOS() ? 16777216 : 524288;
        int fd = OpenUnixReadOnly(path, nonblock | closeOnExec);
        if (fd < 0) throw new IOException($"The verification input could not be opened (errno {Marshal.GetLastPInvokeError()}).");
        var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
        try
        {
            var stream = new FileStream(handle, FileAccess.Read);
            if (stream.CanSeek) return stream;
            stream.Dispose();
            throw new IOException("The verification input is not a seekable file.");
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }
}
