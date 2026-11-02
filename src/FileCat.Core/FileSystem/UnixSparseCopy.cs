using System.Runtime.InteropServices;

namespace FileCat.Core.FileSystem;

/// <summary>Copies reported data extents and leaves reported holes unwritten in a new Unix file.</summary>
internal static partial class UnixSparseCopy
{
    private const int NoSuchExtent = 6; // ENXIO: SEEK_DATA reached the end without more data.
    private const int InvalidArgument = 22, NotSupportedMac = 45, NotSupportedLinux = 95;
    private const long MinimumLength = 1024 * 1024;

    /// <returns>False only when the source filesystem does not report extents; the caller then copies every byte.</returns>
    internal static bool TryCopy(FileStream source, FileStream destination, long length, byte[] buffer,
        Action<long> advance, CancellationToken ct)
    {
        if ((!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) || length < MinimumLength) return false;
        if (source.Length != length) throw new IOException("The source file changed size before its copy began.");
        int dataWhence = OperatingSystem.IsMacOS() ? 4 : 3;
        int holeWhence = OperatingSystem.IsMacOS() ? 3 : 4;
        bool held = false;
        try
        {
            source.SafeFileHandle.DangerousAddRef(ref held);
            int fd = checked((int)source.SafeFileHandle.DangerousGetHandle());
            long data = LSeek(fd, 0, dataWhence);
            if (data < 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error is InvalidArgument or NotSupportedMac or NotSupportedLinux) return false;
                if (error == NoSuchExtent)
                {
                    ct.ThrowIfCancellationRequested();
                    destination.SetLength(length);
                    advance(length);
                    return true;
                }
                throw Failure(error);
            }
            long hole = LSeek(fd, data, holeWhence);
            if (hole < 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error is InvalidArgument or NotSupportedMac or NotSupportedLinux) return false;
                throw Failure(error);
            }
            long done = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (data < done || data >= length || hole <= data || hole > length)
                    throw new IOException("The source file's reported data extents changed during its copy.");
                if (data > done) { done = data; advance(done); }
                source.Position = data;
                destination.Position = data;
                while (done < hole)
                {
                    ct.ThrowIfCancellationRequested();
                    int count = (int)Math.Min(buffer.Length, hole - done);
                    int read = source.Read(buffer, 0, count);
                    if (read <= 0) throw new IOException("The source file ended during its copy.");
                    destination.Write(buffer, 0, read);
                    done += read;
                    advance(done);
                }
                if (done == length) break;
                data = LSeek(fd, done, dataWhence);
                if (data < 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error != NoSuchExtent) throw Failure(error);
                    done = length;
                    advance(done);
                    break;
                }
                hole = LSeek(fd, data, holeWhence);
                if (hole < 0) throw Failure(Marshal.GetLastPInvokeError());
            }
            if (source.Length != length) throw new IOException("The source file changed size during its copy.");
            // The final extent may be a hole. This also handles DisablePreallocation without
            // exposing a full-size, zero-filled partial file during an interrupted copy.
            destination.SetLength(length);
            return true;
        }
        finally { if (held) source.SafeFileHandle.DangerousRelease(); }
    }

    private static IOException Failure(int error) => new($"The source file's allocated ranges could not be read (errno {error}).");

    [LibraryImport("libc", EntryPoint = "lseek", SetLastError = true)]
    private static partial long LSeek(int fd, long offset, int whence);
}
