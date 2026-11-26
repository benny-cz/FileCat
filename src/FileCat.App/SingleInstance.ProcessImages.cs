using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.App;

/// <summary>
/// How the recovery census reads another process's executable image on Linux and macOS (I646). The census runs as root
/// on Linux (--filecat-recovery-process-census), so it never opens a name that another account can replace.
/// </summary>
public static partial class SingleInstance
{
    // Linux open(2) flags that x64 and ARM64 share; O_DIRECTORY and O_NOFOLLOW differ (LinuxOpenFlags).
    private const int LinuxNonBlock = 0x800, LinuxNoControllingTerminal = 0x100;

    // Darwin: O_NONBLOCK | O_NOCTTY | O_CLOEXEC | O_NOFOLLOW_ANY, which refuses a link anywhere on the name, its last part
    // included. Never with O_NOFOLLOW: XNU's vn_open_auth refuses the two together (EINVAL), so every open failed (I646).
    private const int MacImageOpenFlags = 0x4 | 0x20000 | 0x1000000 | 0x20000000;

    /// <summary>
    /// Linux: the process's /proc directory as a descriptor. Everything opened through it belongs to that process: once
    /// it has exited, such opens fail (ENOENT or ESRCH) instead of reaching a process that has since taken over its ID.
    /// </summary>
    internal static SafeFileHandle OpenLinuxProcess(int pid)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux process directories.");
        if (pid <= 0) throw new IOException("Not a Linux process ID.");
        int fd = OpenNative("/proc/" + pid.ToString(CultureInfo.InvariantCulture),
            LinuxOpenFlags.CloseOnExec | LinuxOpenFlags.Current.Directory);
        if (fd < 0) throw LinuxProcessError("directory", Marshal.GetLastPInvokeError());
        return new SafeFileHandle(fd, ownsHandle: true);
    }

    /// <summary>Linux: the process's stat line, read through its directory (bounded as <see cref="KernelThreadFromStat"/> expects).</summary>
    internal static string ReadLinuxProcessStat(SafeFileHandle process)
    {
        using var stat = OpenLinuxProcessEntry(process, "stat", 0);
        using var input = new FileStream(stat, FileAccess.Read, 1);
        var bytes = new byte[4097];
        int length = 0, count;
        while (length < bytes.Length && (count = input.Read(bytes, length, bytes.Length - length)) > 0) length += count;
        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    /// <summary>Linux: the process's status text, read through its directory and bounded (I681's thread count).</summary>
    internal static string ReadLinuxProcessStatus(SafeFileHandle process)
    {
        using var status = OpenLinuxProcessEntry(process, "status", 0);
        using var input = new FileStream(status, FileAccess.Read, 1);
        var bytes = new byte[16384];
        int length = 0, count;
        while (length < bytes.Length && (count = input.Read(bytes, length, bytes.Length - length)) > 0) length += count;
        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    /// <summary>
    /// Linux: whether the image the process runs is a FileCat apphost; null when that cannot be told. The exe entry is the
    /// kernel's own link to the running image and reaches it even after it was deleted. The name Process.MainModule gives
    /// is only that link's text, "&lt;path&gt; (deleted)" once deleted, which any account that can write the folder can
    /// create again as a FIFO or a link to a device.
    /// </summary>
    internal static bool? LinuxImageIsFileCat(SafeFileHandle process, out string? unknownReason)
    {
        SafeFileHandle image;
        try { image = OpenLinuxProcessEntry(process, "exe", LinuxNonBlock | LinuxNoControllingTerminal); }
        catch (IOException ex)
        {
            // The process has exited, is a kernel thread, a thread-group leader that exited before its other threads, or
            // not ours to inspect. (Zombies whose threads have all exited are skipped before this, I681.)
            unknownReason = ex.Message;
            return null;
        }
        using (image) return RegularImageIsFileCat(image, out unknownReason);
    }

    /// <summary>
    /// macOS: whether the executable named for a process is a FileCat apphost; null when that cannot be told. Darwin gives
    /// no descriptor for another process's image, only its name (proc_pidpath, which Process.MainModule reports), and its
    /// kernel checks only that something exists under that name, not that it is the image. Darwin builds the name from the
    /// image's own folders, so no link is on it; a link found there was put in place since, and is refused, never followed.
    /// Only a regular file at the name is opened, with no link followed anywhere on it and without waiting on a FIFO, and
    /// only a regular file is read. Linux, which does not use this for the census, follows links on the name's folders.
    /// </summary>
    internal static bool? ImageAtPathIsFileCat(string? path, out string? unknownReason)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Unix executable names.");
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || path.Contains('\0') || PathUtil.HasAmbiguousUnixName(path))
        {
            unknownReason = "no usable image name";
            return null;
        }
        // lstat opens nothing, so a FIFO, device or link already at the name is never opened. What is put there after this
        // look is a link, which the open refuses, or another file, whose own descriptor must then be a regular file.
        if (UnixFiles.Stat(path) is not { } named)
        {
            _ = UnixPermissions.Stat(path, out int error);
            unknownReason = "image name error " + error.ToString(CultureInfo.InvariantCulture);
            return null;
        }
        if (!named.IsRegular)
        {
            unknownReason = "the named image is not a regular file";
            return null;
        }
        int fd = OpenNative(path, OperatingSystem.IsMacOS() ? MacImageOpenFlags
            : LinuxNonBlock | LinuxNoControllingTerminal | LinuxOpenFlags.CloseOnExec | LinuxOpenFlags.Current.NoFollow);
        if (fd < 0)
        {
            unknownReason = "image error " + Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture);
            return null;
        }
        using var image = new SafeFileHandle(fd, ownsHandle: true);
        return RegularImageIsFileCat(image, out unknownReason);
    }

    private static bool? RegularImageIsFileCat(SafeFileHandle image, out string? unknownReason)
    {
        // The type is the opened descriptor's: a second lookup by name could meet another file.
        if (UnixFiles.Stat(image) is not { IsRegular: true })
        {
            unknownReason = "the image is not a regular file";
            return null;
        }
        using var input = new FileStream(image, FileAccess.Read, 1);
        bool? result = IsFileCatAppHost(input);
        unknownReason = result is null ? "incomplete image read" : null;
        return result;
    }

    private static SafeFileHandle OpenLinuxProcessEntry(SafeFileHandle process, string leaf, int flags)
    {
        bool added = false;
        try
        {
            process.DangerousAddRef(ref added);
            int fd = OpenAtNative((int)process.DangerousGetHandle(), leaf, LinuxOpenFlags.CloseOnExec | flags, 0);
            if (fd < 0) throw LinuxProcessError(leaf, Marshal.GetLastPInvokeError());
            return new SafeFileHandle(fd, ownsHandle: true);
        }
        finally
        {
            if (added) process.DangerousRelease();
        }
    }

    private static IOException LinuxProcessError(string entry, int error) =>
        new($"Cannot open the process {entry}: {new Win32Exception(error).Message} (errno {error.ToString(CultureInfo.InvariantCulture)})",
            unchecked((int)0x80070000) | error);

    // Without O_CREAT the creation mode is never read, so the two-argument form suits both Linux and Apple ABIs.
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenNative(string path, int flags);

    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAtNative(int directory, string path, int flags, uint mode);
}
