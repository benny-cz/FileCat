using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

/// <summary>
/// Trusted-parent observations of the fixed native origin xattr on an already-held regular file or directory.
/// No path is opened and no value is interpreted, changed or deleted. Admission of the handle/filesystem and
/// later source/publication reconciliation remain the caller's responsibility. Matching observations do not
/// establish an atomic snapshot against a concurrent equivalent-privilege writer.
/// </summary>
internal static partial class ArchiveUnixOrigin
{
    internal static ArchiveOriginSnapshot Read(SafeFileHandle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        bool mac = OperatingSystem.IsMacOS();
        if (!mac && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("A native Unix origin attribute is required.");
        cancellationToken.ThrowIfCancellationRequested();
        var format = mac ? ArchiveOriginFormat.MacQuarantine : ArchiveOriginFormat.LinuxOriginUrl;
        string name = mac ? UnixFileOperations.QuarantineAttribute : UnixFileOperations.OriginAttribute;
        bool added = false;
        try
        {
            // Keep the descriptor alive across every native call; disposal cannot recycle it underneath a read.
            handle.DangerousAddRef(ref added);
            if (handle.IsInvalid) throw new ArgumentException("A live held Unix file is required.", nameof(handle));
            int fd = checked((int)handle.DangerousGetHandle());
            var before = UnixFiles.Stat(handle);
            if (before is not { IsRegular: true } && before is not { IsDirectory: true })
                return ArchiveOriginSnapshot.Unavailable(format, ArchiveOriginFailure.Unsupported);
            var first = Capture();
            if (first.State == ArchiveOriginState.Unavailable) return first;
            cancellationToken.ThrowIfCancellationRequested();
            var second = Capture();
            if (second.State == ArchiveOriginState.Unavailable) return second;
            cancellationToken.ThrowIfCancellationRequested();
            var after = UnixFiles.Stat(handle);
            return before != after || first.Digest != second.Digest
                ? ArchiveOriginSnapshot.Unavailable(format, ArchiveOriginFailure.Changed) : second;

            ArchiveOriginSnapshot Capture()
            {
                cancellationToken.ThrowIfCancellationRequested();
                nint size = Get(null, 0);
                if (size < 0) return Failure(Marshal.GetLastPInvokeError(), readingValue: false);
                if (size > ArchiveOriginSnapshot.MaxBytes)
                    return ArchiveOriginSnapshot.Unavailable(format, ArchiveOriginFailure.TooLarge);
                // A nonzero buffer also distinguishes an empty present value from a growth between calls.
                byte[] bytes = new byte[Math.Max(1, (int)size)];
                nint read = Get(bytes, bytes.Length);
                if (read < 0) return Failure(Marshal.GetLastPInvokeError(), readingValue: true);
                if (read != size) return ArchiveOriginSnapshot.Unavailable(format, ArchiveOriginFailure.Changed);
                return ArchiveOriginSnapshot.Present(format, bytes.AsSpan(0, (int)read));
            }

            nint Get(byte[]? bytes, nint size) => mac
                ? MacGet(fd, name, bytes, size, 0, 0) : LinuxGet(fd, name, bytes, size);

            ArchiveOriginSnapshot Failure(int error, bool readingValue)
            {
                // These are fixed com.apple.quarantine / user.xdg.origin.url names, never privileged Linux namespaces.
                // Only the native missing-attribute result denotes absence; unsupported/denied reads remain unknown.
                if (error == (mac ? 93 : 61))
                    return readingValue ? ArchiveOriginSnapshot.Unavailable(format, ArchiveOriginFailure.Changed)
                        : ArchiveOriginSnapshot.Absent(format);
                return ArchiveOriginSnapshot.Unavailable(format, error switch
                {
                    1 or 13 => ArchiveOriginFailure.AccessDenied,
                    16 => ArchiveOriginFailure.InUse,
                    7 => ArchiveOriginFailure.TooLarge,
                    34 => ArchiveOriginFailure.Changed,
                    _ when error == (mac ? 45 : 95) => ArchiveOriginFailure.Unsupported,
                    _ => ArchiveOriginFailure.IoFailure,
                });
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        { return ArchiveOriginSnapshot.Unavailable(format, ArchiveOriginFailure.Unsupported); }
        finally { if (added) handle.DangerousRelease(); }
    }

    [LibraryImport("libc", EntryPoint = "fgetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint LinuxGet(int fd, string name, byte[]? value, nint size);

    [LibraryImport("libc", EntryPoint = "fgetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint MacGet(int fd, string name, byte[]? value, nint size, uint position, int options);
}
