using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Linux;

internal static unsafe partial class LinuxArchiveNative
{
    internal const int CloseOnExec = 0x80000, NoFollow = 0x20000, NonBlock = 0x800, DirectoryFlag = 0x10000;
    internal const ulong Cgroup2Magic = 0x63677270;
    internal static readonly UTF8Encoding ExactUtf8 = new(false, true);

    internal static uint RequireOrdinaryIdentity()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("This archive-worker route requires Linux x64 or ARM64.");
        string status = ReadPath("/proc/self/status", 64 << 10);
        if (!UnixWorkerIdentity.CapabilitiesClear(status)) throw Unavailable("The archive process has retained or unknown Linux capabilities.");
        uint uid = 0; bool user = false, group = false, supplementary = false;
        foreach (string line in status.Split('\n'))
        {
            if (line.StartsWith("Uid:", StringComparison.Ordinal) || line.StartsWith("Gid:", StringComparison.Ordinal))
            {
                bool isUser = line[0] == 'U';
                if (isUser ? user : group) throw Unavailable("The Linux process identity is ambiguous.");
                string[] fields = line[4..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 4 || fields.Any(p => !uint.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out uint value) || value == 0) ||
                    fields.Any(p => p != fields[0])) throw Unavailable("New archive workers require an ordinary, unchanged Linux identity.");
                if (isUser) { uid = uint.Parse(fields[0], CultureInfo.InvariantCulture); user = true; } else group = true;
            }
            if (line.StartsWith("Groups:", StringComparison.Ordinal))
            {
                if (supplementary) throw Unavailable("The Linux supplementary groups are ambiguous.");
                foreach (string field in line[7..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    if (!uint.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out uint value) || value == 0)
                        throw Unavailable("New archive workers cannot retain root-group authority.");
                supplementary = true;
            }
        }
        if (!user || !group || !supplementary || GetUid() != uid || GetEuid() != uid)
            throw Unavailable("The Linux archive process identity could not be verified.");
        return uid;
    }

    internal static SafeFileHandle OpenDirectory(string path)
    {
        int fd = Open(path, DirectoryFlag | NoFollow | CloseOnExec | NonBlock);
        if (fd < 0) throw Unavailable("The archive control directory could not be opened without following a link.");
        var handle = new SafeFileHandle((nint)fd, ownsHandle: true);
        try { if (UnixFiles.Stat(handle) is not { IsDirectory: true }) throw Unavailable("The archive control object is not a directory."); return handle; }
        catch { handle.Dispose(); throw; }
    }

    internal static SafeFileHandle OpenAt(SafeFileHandle parent, string leaf, bool write = false, bool directory = false)
    {
        if (string.IsNullOrEmpty(leaf) || leaf is "." or ".." || leaf.IndexOfAny(['/', '\0']) >= 0)
            throw new ArgumentException("A single control name is required.");
        bool retained = false;
        try
        {
            parent.DangerousAddRef(ref retained);
            int fd = OpenAtNative(checked((int)parent.DangerousGetHandle()), leaf,
                (write ? 1 : 0) | NoFollow | CloseOnExec | NonBlock | (directory ? DirectoryFlag : 0));
            if (fd < 0) throw Unavailable("The required archive control could not be opened.");
            return new((nint)fd, ownsHandle: true);
        }
        finally { if (retained) parent.DangerousRelease(); }
    }

    internal static void RequireCgroupFileSystem(SafeFileHandle handle)
    {
        if (FileSystemMagic(handle) != Cgroup2Magic)
            throw Unavailable("The archive memory controls are not on the native cgroup-v2 filesystem.");
    }

    internal static ulong FileSystemMagic(SafeFileHandle handle)
    {
        bool retained = false;
        try
        {
            handle.DangerousAddRef(ref retained); byte* facts = stackalloc byte[256];
            if (FStatFs(checked((int)handle.DangerousGetHandle()), facts) != 0)
                throw Unavailable("The archive control filesystem could not be identified.");
            return *(ulong*)facts;
        }
        finally { if (retained) handle.DangerousRelease(); }
    }

    internal static string ReadAt(SafeFileHandle directory, string leaf, int maximum = 4096)
    { using var file = OpenAt(directory, leaf); RequireCgroupFileSystem(file); return Read(file, maximum); }
    internal static void WriteAt(SafeFileHandle directory, string leaf, string value)
    {
        ReadOnlySpan<char> number = value.AsSpan().TrimEnd('\n');
        if (value.Length is < 1 or > 128 || number.IsEmpty || value.Length - number.Length > 1 ||
            number.ContainsAnyExceptInRange('0', '9'))
            throw new ArgumentException("A bounded numeric kernel control is required.");
        using var file = OpenAt(directory, leaf, write: true); RequireCgroupFileSystem(file);
        RandomAccess.Write(file, Encoding.ASCII.GetBytes(value), 0);
    }

    internal static string ReadPath(string path, int maximum)
    { using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Read(file, maximum); }
    private static string Read(SafeFileHandle file, int maximum)
    {
        if (maximum is < 1 or > 64 << 10) throw new ArgumentOutOfRangeException(nameof(maximum));
        byte[] buffer = new byte[maximum + 1]; int total = 0;
        while (total < buffer.Length)
        {
            int count = RandomAccess.Read(file, buffer.AsSpan(total), total);
            if (count == 0) break; total += count;
        }
        if (total > maximum) throw Unavailable("The archive kernel-control response exceeds its limit.");
        try { return ExactUtf8.GetString(buffer, 0, total); }
        catch (DecoderFallbackException) { throw Unavailable("The archive kernel-control response is not exact text."); }
    }

    internal static ArchiveWriteException Unavailable(string message) => new(ArchiveWriteReason.WorkerUnavailable, message);

    [LibraryImport("libc", EntryPoint = "getuid")]
    internal static partial uint GetUid();
    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEuid();
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);
    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAtNative(int parent, string path, int flags);
    [LibraryImport("libc", EntryPoint = "fstatfs", SetLastError = true)]
    private static partial int FStatFs(int fd, byte* buffer);
    [LibraryImport("libc", EntryPoint = "mkdirat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int MkdirAt(int parent, string path, uint mode);
    [LibraryImport("libc", EntryPoint = "unlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int UnlinkAt(int parent, string path, int flags);
    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
    internal static partial long Syscall(long number, nint a, nint b, nint c, nint d, nint e, nint f);
}
