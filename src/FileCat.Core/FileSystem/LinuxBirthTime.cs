using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace FileCat.Core.FileSystem;

/// <summary>
/// When a Linux item was made: its birth time, which statx reports where the file system keeps one. .NET does not read
/// it there: its CreationTime on Linux is the earlier of the modification and status change times, a time the item may
/// never have been made at (a file whose modification time was set ahead shows when that was done), and its
/// SetCreationTime sets the modification time. Nothing on Linux sets a birth time.
/// </summary>
internal static unsafe partial class LinuxBirthTime
{
    // The link itself (as .NET's times are), no automount triggered, and no round trip for a network file system.
    private const int AtFdCwd = -100, Flags = 0x100 /* AT_SYMLINK_NOFOLLOW */ | 0x800 /* AT_NO_AUTOMOUNT */ | 0x4000 /* AT_STATX_DONT_SYNC */;
    private const uint Birth = 0x800; // STATX_BTIME
    private const int OpenPath = 0x200000; // O_PATH: the folder's items are asked by name; reading it is not needed

    /// <summary>The item's birth time in UTC ticks, or 0 where its file system keeps none or it cannot be read.</summary>
    public static long Ticks(string path) => Read(AtFdCwd, path);

    /// <summary>The birth time in UTC ticks of <paramref name="name"/> in a folder opened with <see cref="OpenFolder"/>.</summary>
    public static long Ticks(Folder folder, ReadOnlySpan<char> name) => Read(folder.Descriptor, name);

    /// <summary>Opens a folder for <see cref="Ticks(Folder, ReadOnlySpan{char})"/>; null when it cannot be opened.</summary>
    public static Folder? OpenFolder(string path)
    {
        int fd = OpenText(path, OpenPath | LinuxOpenFlags.Current.Directory | LinuxOpenFlags.CloseOnExec);
        return fd < 0 ? null : new Folder(fd);
    }

    /// <summary>A folder opened once, so that each item is asked by its name alone.</summary>
    public sealed class Folder(int descriptor) : IDisposable
    {
        private int _descriptor = descriptor;
        internal int Descriptor => _descriptor;
        public void Dispose()
        {
            int fd = Interlocked.Exchange(ref _descriptor, -1);
            if (fd >= 0) Close(fd);
        }
    }

    private static long Read(int directory, ReadOnlySpan<char> path)
    {
        int length = Encoding.UTF8.GetByteCount(path);
        byte[]? rented = length < 1024 ? null : ArrayPool<byte>.Shared.Rent(length + 1);
        Span<byte> text = rented is null ? stackalloc byte[1024] : rented;
        try
        {
            int n = Encoding.UTF8.GetBytes(path, text);
            text[n] = 0;
            byte* b = stackalloc byte[256];
            fixed (byte* p = text)
            {
                if (Statx(directory, p, Flags, Birth, b) != 0 || (*(uint*)b & Birth) == 0) return 0;
            }
            return ToTicks(*(long*)(b + 80), *(uint*)(b + 88));
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>UTC ticks of a statx timestamp, or 0 outside what DateTime holds.</summary>
    internal static long ToTicks(long seconds, uint nanoseconds)
    {
        const long MinSeconds = -62_135_596_800, MaxSeconds = 253_402_300_799; // 0001-01-01 and 9999-12-31 23:59:59
        if (seconds is < MinSeconds or > MaxSeconds || nanoseconds >= 1_000_000_000) return 0;
        return DateTime.UnixEpoch.Ticks + seconds * TimeSpan.TicksPerSecond + nanoseconds / 100;
    }

    private static int OpenText(string path, int flags)
    {
        byte[] text = new byte[Encoding.UTF8.GetByteCount(path) + 1];
        Encoding.UTF8.GetBytes(path, text);
        fixed (byte* p = text) return Open(p, flags);
    }

    [LibraryImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static partial int Statx(int directory, byte* path, int flags, uint mask, byte* buffer);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true)]
    private static partial int Open(byte* path, int flags);

    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int Close(int fd);
}
