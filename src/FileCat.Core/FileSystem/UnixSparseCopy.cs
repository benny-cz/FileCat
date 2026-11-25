using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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
        bool held = false, destinationHeld = false;
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
            MacHoles? holes = null;
            if (OperatingSystem.IsMacOS())
            {
                destination.SafeFileHandle.DangerousAddRef(ref destinationHeld);
                holes = MacHoles.For(destination.SafeFileHandle);
            }
            long done = 0, written = 0; // written: the end of the last data extent copied
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
                    // The gap before this extent exists in the destination once the extent's first bytes do.
                    if (written < data) { holes?.Punch(written, data); written = data; }
                    advance(done);
                }
                written = done;
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
            holes?.Punch(written, length);
            return true;
        }
        finally
        {
            if (held) source.SafeFileHandle.DangerousRelease();
            if (destinationHeld) destination.SafeFileHandle.DangerousRelease();
        }
    }

    private static IOException Failure(int error) => new($"The source file's allocated ranges could not be read (errno {error}).");

    /// <summary>
    /// APFS gives a file written with gaps real zero blocks for its smaller gaps once the file's data is flushed, at close
    /// or fsync: a 32 MiB copy of two 4 KiB extents was fully allocated, although every byte matched (I652; on macOS 27.2.1
    /// gaps of 8 to 16 MiB were filled, gaps of 32 MiB and more stayed holes, and punched holes survived close). Punching
    /// after everything is written, the order of copyfile(3)'s sparse copy, let the first punch's flush allocate the
    /// remaining gaps for a moment. Each gap is therefore punched as soon as the extent after it has its first bytes, and
    /// the trailing hole after the final length is set. Only whole blocks between copied extents are punched, never a
    /// block holding copied bytes, so the copy's bytes are the same either way. Punching is best effort: a destination
    /// that reports no holes, or refuses one, keeps those zero bytes allocated, as every copy did before.
    /// </summary>
    private sealed class MacHoles(int fd, long block)
    {
        private const int PunchHoleCommand = 99; // fcntl.h: F_PUNCHHOLE
        private const int MinimumHoleSizeName = 27; // unistd.h: _PC_MIN_HOLE_SIZE
        private bool _refused;

        internal static MacHoles? For(SafeFileHandle destination)
        {
            int fd = checked((int)destination.DangerousGetHandle());
            long minimum = FPathConf(fd, MinimumHoleSizeName);
            // Zero or an error: the destination's filesystem keeps no holes, so there is nothing to punch.
            return minimum <= 0 ? null : new MacHoles(fd, minimum > 1 ? minimum : 4096);
        }

        internal void Punch(long start, long end)
        {
            if (_refused) return;
            long from = (start + block - 1) / block * block, to = end / block * block;
            if (to <= from) return;
            if (!PunchHole(fd, from, to - from)) _refused = true;
        }

        private static unsafe bool PunchHole(int fd, long offset, long length)
        {
            // struct fpunchhole: u32 fp_flags, u32 reserved, off_t fp_offset, off_t fp_length.
            long* arguments = stackalloc long[3];
            arguments[0] = 0;
            arguments[1] = offset;
            arguments[2] = length;
            // fcntl is variadic: Darwin ARM64 passes the variadic argument on the stack, after eight register slots.
            int result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? FcntlArm64(fd, PunchHoleCommand, 0, 0, 0, 0, 0, 0, (nint)arguments)
                : Fcntl(fd, PunchHoleCommand, (nint)arguments);
            return result == 0;
        }
    }

    [LibraryImport("libc", EntryPoint = "lseek", SetLastError = true)]
    private static partial long LSeek(int fd, long offset, int whence);

    [LibraryImport("libc", EntryPoint = "fpathconf", SetLastError = true)]
    private static partial long FPathConf(int fd, int name);

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int fd, int command, nint argument);

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int FcntlArm64(int fd, int command, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, nint argument);
}
