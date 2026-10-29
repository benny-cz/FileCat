using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

/// <summary>
/// macOS volumes in a file's record (D-56): the item's volume as statfs gives it (mount point, file system, device,
/// options); on FAT and exFAT, the item's raw directory entry; on HFS+, the journal's summary. The last two are read as
/// root from the volume's raw device (/dev/rdisk…), in whole sectors.
/// </summary>
public sealed unsafe partial class UnixFileRecords
{
    private const uint MacJournaledFlag = 0x0080_0000; // MNT_JOURNALED

    /// <summary>
    /// The volume that holds a macOS item, as statfs gives it: where it is mounted, its type ("apfs", "hfs", "msdos",
    /// "exfat"), what it is mounted from ("/dev/disk4s1"), and its mount flags.
    /// </summary>
    private sealed record MacMount(string MountPoint, string Type, string Source, uint Flags)
    {
        /// <summary>
        /// The raw (character) device of a /dev/disk… source, or null for other sources. A mounted volume's block device
        /// does not open (EBUSY); the raw one does, reads past the system's cache (what the disk holds now), and reads
        /// whole sectors only.
        /// </summary>
        public string? RawDevice =>
            Source.StartsWith("/dev/disk", StringComparison.Ordinal) ? string.Concat("/dev/rdisk", Source.AsSpan("/dev/disk".Length))
            : Source.StartsWith("/dev/rdisk", StringComparison.Ordinal) ? Source : null;
    }

    /// <summary>statfs: the volume <paramref name="path"/> is on (links followed), or null when it cannot be asked.</summary>
    private static MacMount? MacMountOf(string path)
    {
        // The 64-bit-inode struct statfs (the only one on arm64; statfs$INODE64 on x64), 2,168 bytes: the flags at 64,
        // the type name at 72 (16 bytes), then the mount point and the source (1,024 bytes each).
        const int Size = 2168;
        byte* b = stackalloc byte[Size];
        new Span<byte>(b, Size).Clear();
        int result;
        try { result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? MacStatfs(path, b) : MacStatfsInode64(path, b); }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { return null; }
        if (result != 0) return null;
        static string Text(byte* at, int length)
        {
            var span = new ReadOnlySpan<byte>(at, length);
            int end = span.IndexOf((byte)0);
            return Encoding.UTF8.GetString(end < 0 ? span : span[..end]);
        }
        return new MacMount(Text(b + 88, 1024), Text(b + 72, 16), Text(b + 1112, 1024), *(uint*)(b + 64));
    }

    /// <summary>
    /// The item's path with the links above it resolved (the item itself is kept as it is, a link or not): statfs names
    /// mount points that way (/private/var/…, not /var/…).
    /// </summary>
    private static string MacResolvedPath(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        return parent is not null && UnixFiles.RealPath(parent) is { } real ? Path.Combine(real, Path.GetFileName(path)) : path;
    }

    /// <summary>
    /// The item's raw directory entry on a FAT or exFAT volume (macOS, as root): the same section Linux and Windows give,
    /// read from the volume's raw device.
    /// </summary>
    private static InspectionSection? MacFatSection(MacMount mount, string path)
    {
        const string Title = "Directory entry";
        string relative = Path.GetRelativePath(mount.MountPoint, MacResolvedPath(path));
        if (relative == ".") return null; // the root folder has no entry of its own
        if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return new InspectionSection(Title, []) { Lines = RecordText.Wrap($"Where it lies on the volume mounted at {mount.MountPoint} could not be worked out.") };
        if (mount.RawDevice is not { } device)
            return new InspectionSection(Title, []) { Lines = RecordText.Wrap($"Its volume is mounted from {mount.Source}, not from a disk device FileCat can read.") };
        if (GetEuid() != 0)
        {
            string what = mount.Type == "exfat"
                ? "the entry set and its checksum, every time with its UTC offset, its first cluster"
                : "the 8.3 name and case bits, every time as FAT keeps it, its first cluster";
            return new InspectionSection(Title, []) { Lines = RecordText.Wrap($"Its raw directory entry ({what}) is read from {device}, which needs root.") };
        }
        try
        {
            using var disk = SectorDevice.Open(device);
            if (FatEntries.Describe(disk.Read, relative, out string? problem) is { } entry) return entry;
            // The raw device gives what the disk holds now: an entry made moments ago may still be only in memory.
            if (problem is null || problem.Contains("was not found", StringComparison.Ordinal))
                problem = (problem ?? "It was not found.") + " The disk is read as it is now, past the system's cache, so an item made moments ago may not be on it yet.";
            return new InspectionSection(Title, []) { Lines = RecordText.Wrap(problem) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new InspectionSection(Title, []) { Lines = RecordText.Wrap($"{device} could not be read: {ex.Message}") };
        }
    }

    /// <summary>The item's volume (macOS): where it is mounted, its file system, its device, its mount options; for HFS+, its journal.</summary>
    private static InspectionSection MacVolumeSection(MacMount mount)
    {
        var fields = new List<(string, string)>
        {
            ("Mounted at", mount.MountPoint),
            ("File system", mount.Type),
            ("Source", mount.Source),
            ("Options", Words(mount.Flags, MacMountFlagNames)),
        };
        var children = new List<InspectionSection>();
        if (mount.Type == "hfs") children.Add(MacHfsJournal(mount));
        return new InspectionSection("Volume", fields) { Children = children };
    }

    /// <summary>HFS+'s journal, read from the volume's raw device (as root); without root, what the system says of it.</summary>
    private static InspectionSection MacHfsJournal(MacMount mount)
    {
        const string Title = "Journal (HFS+)";
        bool journaled = (mount.Flags & MacJournaledFlag) != 0;
        string? device = mount.RawDevice;
        if (GetEuid() != 0 || device is null)
        {
            List<string> lines = !journaled ? []
                : device is null ? RecordText.Wrap($"Its volume is mounted from {mount.Source}, not from a disk device FileCat can read.")
                : RecordText.Wrap($"Where the journal lies, whether transactions wait in it to be replayed, its sequence number and checksum are read from {device}, which needs root.");
            return new InspectionSection(Title, [("Journaled", journaled ? "yes (the system says so)" : "no (the system says so)")]) { Lines = lines };
        }
        try
        {
            using var disk = SectorDevice.Open(device);
            return HfsJournal.Describe(disk.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new InspectionSection(Title, []) { Lines = RecordText.Wrap($"{device} could not be read: {ex.Message}") };
        }
    }

    /// <summary>Mount flags (MNT_*) as macOS's mount(8) names them.</summary>
    private static readonly (ulong Flag, string Text)[] MacMountFlagNames =
    [
        (0x1, "read-only"), (0x2, "synchronous"), (0x4, "noexec"), (0x8, "nosuid"), (0x10, "nodev"), (0x20, "union"),
        (0x40, "asynchronous"), (0x80, "protect"), (0x100, "NFS exported"), (0x200, "removable"), (0x400, "quarantine"),
        (0x1000, "local"), (0x2000, "with quotas"), (0x4000, "root file system"), (0x8000, "volfs"), (0x10_0000, "nobrowse"),
        (0x20_0000, "noowners"), (0x40_0000, "automounted"), (0x80_0000, "journaled"), (0x100_0000, "nouserxattr"),
        (0x200_0000, "defwrite"), (0x400_0000, "multilabel"), (0x800_0000, "nofollow"), (0x1000_0000, "noatime"),
        (0x4000_0000, "snapshot"), (0x8000_0000, "strictatime"),
    ];

    /// <summary>
    /// Reads whole sectors at <paramref name="offset"/> (a sector boundary) into <paramref name="buffer"/> (whole
    /// sectors): the bytes read, 0 past the end. A device that fails throws IOException.
    /// </summary>
    internal delegate int SectorRead(long offset, Span<byte> buffer);

    /// <summary>
    /// <paramref name="count"/> bytes at <paramref name="offset"/> from a source that reads only whole sectors of
    /// <paramref name="unit"/> bytes at sector boundaries (macOS's raw devices refuse anything else with EINVAL): the
    /// sectors around the range are read, at most 1 MiB at a time, and the range is cut from them. A source that ends
    /// early gives a shorter answer, as a file does.
    /// </summary>
    internal static byte[] ReadCovering(SectorRead read, int unit, long offset, int count)
    {
        if (offset < 0 || count <= 0 || unit <= 0 || offset > long.MaxValue - count - unit) return [];
        const int Piece = 1 << 20;
        int most = Math.Max(unit, Piece / unit * unit);
        long end = offset + count;
        using var result = new MemoryStream();
        byte[] chunk = [];
        for (long at = offset; at < end;)
        {
            long start = at / unit * unit;
            int want = (int)Math.Min(most, (end - start + unit - 1) / unit * unit);
            if (chunk.Length < want) chunk = new byte[want];
            int got = 0;
            while (got < want)
            {
                int n = read(start + got, chunk.AsSpan(got, want - got));
                if (n <= 0) break;
                got += n;
                if (n % unit != 0) break; // a short read: the device ends here
            }
            int usable = (int)Math.Min(got - (at - start), end - at);
            if (usable <= 0) break;
            result.Write(chunk, (int)(at - start), usable);
            at += usable;
            if (got < want) break;
        }
        return result.ToArray();
    }

    /// <summary>
    /// A disk device (or an image file) opened read-only and read at any offset and length: each read covers the whole
    /// sectors around the range (4 KiB, or the device's own sector size when that is larger), as macOS's raw devices
    /// (/dev/rdisk…) require.
    /// </summary>
    internal sealed class SectorDevice : IDisposable
    {
        private int _fd;

        private SectorDevice(int fd, int blockSize)
        {
            _fd = fd;
            BlockSize = blockSize;
            Unit = blockSize is > 4096 and <= 65536 && (blockSize & (blockSize - 1)) == 0 ? blockSize : 4096;
        }

        /// <summary>The device's own block size (DKIOCGETBLOCKSIZE on macOS), or 0 where it does not say (a file).</summary>
        public int BlockSize { get; }

        /// <summary>The whole-sector unit every read is aligned to.</summary>
        public int Unit { get; }

        public static SectorDevice Open(string path)
        {
            int fd = UnixFileRecords.Open(path, OperatingSystem.IsMacOS() ? 0x0100_0000 : 0x80000 /* O_RDONLY | O_CLOEXEC */);
            if (fd < 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                string message = $"{path}: {Marshal.GetPInvokeErrorMessage(errno)}";
                throw errno is 1 or 13 ? new UnauthorizedAccessException(message) : new IOException(message, errno);
            }
            uint blockSize = 0;
            if (OperatingSystem.IsMacOS() && MacIoctl(fd, 0x4004_6418 /* DKIOCGETBLOCKSIZE */, &blockSize) != 0) blockSize = 0;
            return new SectorDevice(fd, blockSize <= 65536 ? (int)blockSize : 0);
        }

        /// <summary>Up to <paramref name="count"/> bytes at <paramref name="offset"/>: fewer where the device ends.</summary>
        public byte[] Read(long offset, int count) => ReadCovering(ReadSectors, Unit, offset, count);

        private int ReadSectors(long offset, Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_fd < 0, this);
            nint got;
            fixed (byte* b = buffer) got = PRead(_fd, b, (nuint)buffer.Length, offset);
            if (got < 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                throw new IOException($"Reading {buffer.Length:N0} bytes at byte {offset:N0} failed: {Marshal.GetPInvokeErrorMessage(errno)}", errno);
            }
            return (int)got;
        }

        public void Dispose()
        {
            if (_fd >= 0) _ = Close(_fd);
            _fd = -1;
        }
    }

    /// <summary>
    /// ioctl (variadic) with its one argument where the calling convention reads it from: Apple's arm64 convention
    /// passes variadic arguments on the stack, not in registers.
    /// </summary>
    private static int MacIoctl(int fd, ulong request, void* argument) =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? IoctlStacked(fd, request, 0, 0, 0, 0, 0, 0, argument) // eight registers taken, the ninth goes on the stack
            : Ioctl(fd, request, argument);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int IoctlStacked(int fd, ulong request, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, void* argument);

    [LibraryImport("libc", EntryPoint = "statfs", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacStatfs(string path, byte* buffer);

    [LibraryImport("libc", EntryPoint = "statfs$INODE64", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacStatfsInode64(string path, byte* buffer);
}
