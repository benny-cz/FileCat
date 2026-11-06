using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Unix;

/// <summary>Observed development filesystem route for one held object, not release/package or power-loss qualification.</summary>
internal sealed unsafe partial record UnixArchiveVolume(string FileSystem, ulong Flags, ulong? MountId)
{
    internal static UnixArchiveVolume Observe(SafeFileHandle handle)
    {
        UnixArchiveNative.RequirePlatform(); bool retained = false;
        try
        {
            handle.DangerousAddRef(ref retained); int fd = UnixArchiveNative.Fd(handle);
            byte* bytes = stackalloc byte[4096]; new Span<byte>(bytes, 4096).Clear();
            if (UnixArchiveNative.Mac)
            {
                if ((RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? MacStatFs(fd, bytes) : MacStatFs64(fd, bytes)) != 0)
                    throw UnixArchiveNative.Unavailable();
                string name = Encoding.ASCII.GetString(new ReadOnlySpan<byte>(bytes + 72, 16)).TrimEnd('\0');
                uint flags = *(uint*)(bytes + 64);
                if (name != "apfs" || (flags & 0x1001) != 0x1000) throw UnixArchiveNative.Unavailable();
                return new(name, flags, null);
            }
            if (StatFs(fd, bytes) != 0 || *(ulong*)bytes != 0xef53 || (*(ulong*)(bytes + 80) & 1) != 0)
                throw UnixArchiveNative.Unavailable();
            ulong first = LinuxMountId(fd);
            // ext2/ext3 share the ext4 statfs magic. The descriptor's statx mount ID binds the exact kernel-reported
            // mountinfo filesystem type; neither a path suffix nor magic alone establishes an ext4 route.
            string mountInfo = ReadMountInfo(); string type = LinuxFileSystem(mountInfo, first);
            if (first != LinuxMountId(fd) || type != "ext4") throw UnixArchiveNative.Unavailable();
            return new(type, *(ulong*)(bytes + 80), first);
        }
        finally { if (retained) handle.DangerousRelease(); }
    }
    internal static string LinuxFileSystem(string mountInfo, ulong mountId)
    {
        if (mountId == 0 || mountInfo.Length > 1 << 20) throw UnixArchiveNative.Unavailable();
        string? result = null; int count = 0;
        foreach (string line in mountInfo.Split('\n'))
        {
            if (line.Length == 0) continue;
            if (++count > 16_384) throw UnixArchiveNative.Unavailable();
            int firstSpace = line.IndexOf(' ');
            if (firstSpace <= 0 || !ulong.TryParse(line.AsSpan(0, firstSpace), NumberStyles.None, CultureInfo.InvariantCulture, out ulong id))
                throw UnixArchiveNative.Unavailable();
            if (id != mountId) continue;
            string[] fields = line.Split(' ', StringSplitOptions.None); int separator = Array.IndexOf(fields, "-");
            if (result is not null || fields.Any(f => f.Length == 0) || separator < 6 || separator + 3 != fields.Length - 1 ||
                fields[5].Split(',').Contains("ro", StringComparer.Ordinal) || !fields[5].Split(',').Contains("rw", StringComparer.Ordinal))
                throw UnixArchiveNative.Unavailable();
            result = fields[separator + 1];
        }
        return result ?? throw UnixArchiveNative.Unavailable();
    }
    private static ulong LinuxMountId(int fd)
    {
        byte* stat = stackalloc byte[256]; new Span<byte>(stat, 256).Clear();
        if (Statx(fd, string.Empty, 0x1000, 0x1000 /* MNT_ID */, stat) != 0 || (*(uint*)stat & 0x1000) == 0 || *(ulong*)(stat + 144) == 0)
            throw UnixArchiveNative.Unavailable();
        return *(ulong*)(stat + 144);
    }
    private static string ReadMountInfo()
    {
        int fd = Open("/proc/self/mountinfo", UnixArchiveNative.SafeOpen); using var handle = UnixArchiveNative.Handle(fd);
        byte* stat = stackalloc byte[256];
        if (StatFs(fd, stat) != 0 || *(ulong*)stat != 0x9fa0 /* actual procfs */) throw UnixArchiveNative.Unavailable();
        byte[] bytes = new byte[(1 << 20) + 1]; int total = 0;
        while (total < bytes.Length)
        {
            int read = RandomAccess.Read(handle, bytes.AsSpan(total), total); if (read == 0) break; total += read;
        }
        if (total > 1 << 20) throw UnixArchiveNative.Unavailable();
        try { return UnixArchiveNative.Utf8.GetString(bytes, 0, total); }
        catch (DecoderFallbackException) { throw UnixArchiveNative.Unavailable(); }
    }
    [LibraryImport("libc", EntryPoint = "fstatfs", SetLastError = true)]
    private static partial int StatFs(int fd, byte* bytes);
    [LibraryImport("libc", EntryPoint = "fstatfs", SetLastError = true)]
    private static partial int MacStatFs(int fd, byte* bytes);
    [LibraryImport("libc", EntryPoint = "fstatfs$INODE64", SetLastError = true)]
    private static partial int MacStatFs64(int fd, byte* bytes);
    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Statx(int fd, string path, int flags, uint mask, byte* bytes);
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);
}
