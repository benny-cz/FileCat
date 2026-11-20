using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

/// <summary>
/// Trusted-parent observations of the fixed native origin xattr, plus create-only propagation to a new editor file.
/// No path is opened and origin bytes remain opaque. Admission of the handle/filesystem and
/// later source/publication reconciliation remain the caller's responsibility. Matching observations do not
/// establish an atomic snapshot against a concurrent equivalent-privilege writer.
/// </summary>
internal static partial class ArchiveUnixOrigin
{
    internal static void CopyToNewWorkingFile(SafeFileHandle handle, ArchiveOriginSnapshot origin, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var format = OperatingSystem.IsMacOS() ? ArchiveOriginFormat.MacQuarantine : ArchiveOriginFormat.LinuxOriginUrl;
        if (origin.Format != format || origin.State == ArchiveOriginState.Unavailable)
            throw new IOException("The archive origin cannot be preserved on the private edit copy.");
        if (origin.State == ArchiveOriginState.Absent) return;
        var existing = Read(handle, ct);
        if (existing.Digest == origin.Digest) return;
        if (existing.State != ArchiveOriginState.Absent) throw new IOException("The new working file already has different or unreadable origin data.");
        bool added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            if (UnixFiles.Stat(handle) is not { IsRegular: true }) throw new IOException("An owned regular edit file is required.");
            int fd = checked((int)handle.DangerousGetHandle()); byte[] bytes = origin.Bytes.ToArray();
            int result = OperatingSystem.IsMacOS()
                ? MacSet(fd, UnixFileOperations.QuarantineAttribute, bytes, bytes.Length, 0, 2 /* XATTR_CREATE */)
                : LinuxSet(fd, UnixFileOperations.OriginAttribute, bytes, bytes.Length, 1 /* XATTR_CREATE */);
            if (result != 0) throw new IOException("The private edit origin could not be created without replacement.", Marshal.GetLastPInvokeError());
            if (Read(handle, CancellationToken.None).Digest != origin.Digest)
                throw new IOException("The private edit origin did not survive exact readback.");
            ct.ThrowIfCancellationRequested();
        }
        finally { if (added) handle.DangerousRelease(); }
    }

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

    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxSet(int fd, string name, byte[] value, nint size, int flags);

    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacSet(int fd, string name, byte[] value, nint size, uint position, int options);
}
