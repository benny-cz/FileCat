using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Verification;

/// <summary>
/// Opens a local verification input (a checksum file, a signature, a key, a file to hash) without waiting for a Unix FIFO
/// writer, and refuses anything that is not a regular file: a pipe or a device planted under a file's name (an extracted
/// archive can hold one) would otherwise hold a check, or a background row, forever.
/// </summary>
internal static class VerificationFileReader
{
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenUnixReadOnly(string path, int flags);

    internal static FileStream OpenRead(string path, FileShare share = FileShare.Read) => Open(path, share, bufferSize: 4096, FileOptions.None);

    /// <summary>As <see cref="OpenRead"/>, for reading a file through once (hashing it): no buffer of its own, read ahead.</summary>
    internal static FileStream OpenSequential(string path) => Open(path, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.SequentialScan);

    /// <summary>
    /// Whether the path names something a reader would wait on or never finish (a pipe, a socket, a device), not a regular
    /// file. False when it cannot be opened at all: whoever reads it then says why.
    /// </summary>
    internal static bool IsSpecial(string path)
    {
        try
        {
            using var _ = OpenRead(path, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (NotAFileException) { return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>A file's bytes, read once, at most <paramref name="max"/> of them: null when it holds more (it may grow while it is read).</summary>
    internal static byte[]? ReadAll(string path, long max)
    {
        using var input = Open(path, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.SequentialScan);
        long length = input.Length;
        if (length > max) return null;
        using var bytes = new MemoryStream((int)length);
        byte[] buffer = new byte[64 * 1024];
        int count;
        while ((count = input.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, max + 1 - bytes.Length)))) > 0)
        {
            if (bytes.Length + count > max) return null;
            bytes.Write(buffer, 0, count);
        }
        return bytes.Length == bytes.Capacity ? bytes.GetBuffer() : bytes.ToArray();
    }

    private static FileStream Open(string path, FileShare share, int bufferSize, FileOptions options)
    {
        if (OperatingSystem.IsWindows())
        {
            // A device name (NUL, COM1) opens the device, which cannot seek.
            var file = new FileStream(path, FileMode.Open, FileAccess.Read, share, bufferSize, options);
            if (file.CanSeek) return file;
            file.Dispose();
            throw new NotAFileException("It is not a file (a device).");
        }

        // Ordinary links are followed. O_NONBLOCK makes a FIFO's open return at once; the descriptor's own type then decides.
        int nonblock = OperatingSystem.IsMacOS() ? 4 : 2048;
        int closeOnExec = OperatingSystem.IsMacOS() ? 16777216 : 524288;
        int fd = OpenUnixReadOnly(path, nonblock | closeOnExec);
        if (fd < 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            // ENOENT and ENOTDIR: gone (a vanished file is missing, not unreadable); EACCES and EPERM: not allowed.
            throw errno switch
            {
                2 or 20 => new FileNotFoundException("It is not there.", path),
                1 or 13 => new UnauthorizedAccessException("Access to it is denied."),
                _ => new IOException($"It could not be opened (errno {errno})."),
            };
        }
        var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
        try
        {
            if (UnixFiles.Stat(handle) is not { IsRegular: true })
                throw new NotAFileException("It is not a regular file (a pipe, a socket, or a device).");
            return new FileStream(handle, FileAccess.Read, bufferSize);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>The path names a pipe, a socket, or a device, not a file.</summary>
    private sealed class NotAFileException(string message) : IOException(message);
}
