using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

public sealed unsafe partial class UnixFileRecords
{
    /// <summary>What the kernel shows of an item now: statx, and for files and folders FS_IOC_GETVERSION and FS_IOC_GETFLAGS.</summary>
    internal sealed record Ext4Live(int Mode, uint Links, uint Uid, uint Gid, ulong Size, ulong Blocks, Ext4Inode.Timestamp Modified,
        Ext4Inode.Timestamp Changed, Ext4Inode.Timestamp Accessed, Ext4Inode.Timestamp? Born, uint? Generation, uint? Flags);

    /// <summary>The inode flags ext4 shows programs (EXT4_FL_USER_VISIBLE); it keeps the others for itself.</summary>
    private const uint Ext4VisibleFlags = 0x725B_DFFF;

    private static readonly (ulong Flag, string Text)[] Ext4HiddenFlagNames =
    [
        (0x2000, "AFS directory (IMAGIC)"), (0x4_0000, "huge file: its blocks count in file-system blocks"),
        (0x20_0000, "holds a large extended attribute value (EA inode)"), (0x8000_0000, "reserved for e2fsprogs"),
    ];

    /// <summary>
    /// The item's inode as an ext2, ext3, or ext4 volume keeps it in its inode table (D-56), read as root from the device
    /// the volume is mounted from, through the kernel's page cache: its checksum checked as the kernel checks it,
    /// i_version, and each field beside what the kernel shows now. Without root, one line says what root would add.
    /// </summary>
    private static InspectionSection Ext4Section(string type, string source, string path, ulong inode, uint major, uint minor, List<string> warnings)
    {
        string title = $"Inode on disk ({type})";
        InspectionSection Note(string text) => new(title, []) { Lines = RecordText.Wrap(text) };
        if (GetEuid() != 0)
            return Note($"Its inode as the inode table keeps it (its crc32c checksum where the volume keeps one, the i_version change counter, and each field beside what the kernel shows) is read from {source}, which needs root.");
        var (fd, device, reason) = OpenVolumeDevice(source, major, minor);
        if (fd < 0) return Note(reason ?? $"{source} could not be opened.");
        try
        {
            byte[] ReadVolume(long offset, int count)
            {
                var buffer = new byte[count];
                int got = 0;
                fixed (byte* b = buffer)
                    while (got < count)
                    {
                        long n = PRead(fd, b + got, (nuint)(count - got), offset + got);
                        if (n > 0) got += (int)n;
                        else if (n == 0) break;
                        else if (Marshal.GetLastPInvokeError() != 4 /* EINTR */) throw new IOException(Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError()));
                    }
                return got == count ? buffer : buffer[..got];
            }
            var first = Ext4Inode.Read(ReadVolume, inode, out string? problem);
            if (first is null) return Note(problem ?? "Its inode could not be read.");
            // A read made while the kernel rewrites the same block can catch half of the change: a checksum that does not
            // match is read once more before it is reported.
            var raw = first;
            if (first.ChecksumMatches == false || first.DescriptorChecksumMatches == false || first.SuperblockChecksumMatches == false)
                raw = Ext4Inode.Read(ReadVolume, inode, out _) ?? first;
            return Ext4Describe(title, device, raw, Ext4LiveView(path, inode), !raw.Raw.AsSpan().SequenceEqual(first.Raw), warnings);
        }
        finally { Close(fd); }
    }

    /// <summary>
    /// The block device a volume is mounted from (<paramref name="major"/>:<paramref name="minor"/>), opened only to read:
    /// mountinfo's source when it is that device, else /dev/ and the kernel's own name for it (mountinfo may say
    /// "/dev/root"). The descriptor is -1, with the reason, when neither is.
    /// </summary>
    private static (int Fd, string Device, string? Reason) OpenVolumeDevice(string source, uint major, uint minor)
    {
        int fd = OpenBlockDevice(source, major, minor, out string? reason);
        if (fd >= 0) return (fd, source, null);
        try
        {
            if (new FileInfo($"/sys/dev/block/{major}:{minor}").LinkTarget is { } link && "/dev/" + Path.GetFileName(link.TrimEnd('/')) is var named && named != source)
            {
                fd = OpenBlockDevice(named, major, minor, out _);
                if (fd >= 0) return (fd, named, null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return (-1, source, reason);
    }

    /// <summary><paramref name="device"/> opened only to read, when it is block device major:minor before and after it is opened; -1 with the reason when not.</summary>
    private static int OpenBlockDevice(string device, uint major, uint minor, out string? reason)
    {
        byte* st = stackalloc byte[256];
        bool Is(int fd)
        {
            new Span<byte>(st, 256).Clear();
            int result = fd < 0 ? Statx(-100 /* AT_FDCWD */, device, 0, 0x1 /* STATX_TYPE */, st) : Statx(fd, "", 0x1000 /* AT_EMPTY_PATH */, 0x1, st);
            return result == 0 && (*(ushort*)(st + 28) & TypeMask) == 0x6000 && *(uint*)(st + 128) == major && *(uint*)(st + 132) == minor;
        }
        reason = $"{device} is not the device the volume is mounted from ({major}:{minor}), so its inode table was not read.";
        if (!Is(-1)) return -1;
        int fd = Open(device, 0x100 | 0x80000 /* O_RDONLY | O_NOCTTY | O_CLOEXEC */);
        if (fd < 0)
        {
            reason = $"{device} could not be opened: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}";
            return -1;
        }
        if (Is(fd))
        {
            reason = null;
            return fd;
        }
        Close(fd);
        return -1;
    }

    /// <summary>What the kernel shows of the item now, or null when its path no longer leads to <paramref name="inode"/>.</summary>
    private static Ext4Live? Ext4LiveView(string path, ulong inode)
    {
        byte* b = stackalloc byte[256];
        new Span<byte>(b, 256).Clear();
        if (Statx(-100 /* AT_FDCWD */, path, 0x100 /* AT_SYMLINK_NOFOLLOW */, 0xFFF /* basic stats and birth */, b) != 0 || *(ulong*)(b + 32) != inode) return null;
        static Ext4Inode.Timestamp At(byte* p) => new(*(long*)p, *(uint*)(p + 8));
        int mode = *(ushort*)(b + 28);
        var live = new Ext4Live(mode, *(uint*)(b + 16), *(uint*)(b + 20), *(uint*)(b + 24), *(ulong*)(b + 40), *(ulong*)(b + 48),
            At(b + 112), At(b + 96), At(b + 64), (*(uint*)b & 0x800) != 0 ? At(b + 80) : null, null, null);
        if ((mode & TypeMask) is not (0x8000 or 0x4000)) return live;
        // Its generation and flags, through a descriptor opened only to read (as InodeFlags opens it).
        int noFollow = RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm or Architecture.Ppc64le ? 0x8000 : 0x20000;
        int fd = Open(path, 0x800 | 0x100 | 0x80000 | noFollow);
        if (fd < 0) return live;
        try
        {
            if (Statx(fd, "", 0x1000 /* AT_EMPTY_PATH */, 0x100 /* STATX_INO */, b) != 0 || *(ulong*)(b + 32) != inode) return live;
            uint generation = 0, flags = 0;
            return live with
            {
                Generation = Ioctl(fd, 0x8008_7601 /* FS_IOC_GETVERSION */, &generation) == 0 ? generation : null,
                Flags = Ioctl(fd, 0x8008_6601 /* FS_IOC_GETFLAGS */, &flags) == 0 ? flags : null,
            };
        }
        finally { Close(fd); }
    }

    /// <summary>
    /// The section: where the inode lies, its checksums and i_version, and each field beside what the kernel shows, with
    /// what a difference means. A table behind the kernel is said to be not yet written, not damaged.
    /// </summary>
    internal static InspectionSection Ext4Describe(string title, string device, Ext4Inode raw, Ext4Live? live, bool changedWhileRead, List<string> warnings)
    {
        static string Matches(bool ok) => ok ? "matches" : "does not match";
        static string N(ulong value) => value.ToString("N0", CultureInfo.CurrentCulture);
        static string T(Ext4Inode.Timestamp t) => new UnixTime(t.Seconds, t.Nanoseconds).ToString();
        static string Join(List<string> names) => names.Count == 1 ? names[0] : string.Join(", ", names[..^1]) + " and " + names[^1];
        var fields = new List<(string, string)>
        {
            ("Read from", $"{device}: block group {raw.Group:N0}, inode table at block {raw.InodeTable:N0}, entry {raw.Index + 1:N0} of {raw.InodesPerGroup:N0} (byte {raw.Offset:N0})"),
            ("Layout", $"{RecordText.Short(raw.BlockSize)} blocks, {raw.InodeSize:N0}-byte inodes, {raw.DescriptorSize:N0}-byte group descriptors"),
        };
        string Hex(uint value) => raw.ChecksumHasHigh ? $"0x{value:X8}" : $"0x{value:X4}";
        if (raw.ComputedChecksum is { } computed)
            fields.Add(("Inode checksum", raw.ChecksumMatches == true
                ? $"{Hex(raw.StoredChecksum)} matches (crc32c of the volume's seed {(raw.SeedInSuperblock ? "kept in its superblock" : "from its UUID")}, the inode number, its generation, and its {raw.InodeSize:N0} bytes{(raw.ChecksumHasHigh ? "" : "; the inode keeps only the low 16 bits")})"
                : $"{Hex(raw.StoredChecksum)} does not match: its bytes give {Hex(computed)}"));
        else fields.Add(("Inode checksum", raw.NoChecksum ?? "none"));
        if (raw.SuperblockChecksumMatches is { } superblock && raw.DescriptorChecksumMatches is { } descriptor)
            fields.Add(("Other checksums", $"the superblock's {Matches(superblock)}; block group {raw.Group:N0}'s descriptor's {Matches(descriptor)}"));
        if (raw.Version is { } version)
            fields.Add(("i_version", raw.VersionHasHigh ? $"{N(version)} (0x{version >> 32:X8}:{version & 0xFFFF_FFFF:X8})" : $"{N(version)} (0x{version:X8}; the inode keeps only the low 32 bits)"));
        if (raw.InodeSize > 128) fields.Add(("Extra fields", $"{raw.ExtraSize:N0} bytes after the first 128 (i_extra_isize)"));
        if (raw.DeletionTime != 0) fields.Add(("i_dtime", $"{raw.DeletionTime:N0}: on an inode in use, the next inode on the orphan list (items being deleted or cut short)"));

        // Each field beside the kernel's: the size, blocks, and times can be behind it; the rest are copied at once.
        var rows = new List<string[]>();
        List<string> dataBehind = [], timesBehind = [], different = [];
        void Row(string name, string noun, string onDisk, string? now, bool same, bool behind, List<string> lagging)
        {
            rows.Add([name, onDisk, now is null ? "—" : same ? "the same" : now]);
            if (now is not null && !same) (behind ? lagging : different).Add(noun);
        }
        bool regular = (raw.Mode & TypeMask) == 0x8000;
        static string Octal(int mode) => Convert.ToString(mode, 8).PadLeft(6, '0');
        Row("Mode", "mode", Octal(raw.Mode), live is null ? null : Octal(live.Mode), live?.Mode == raw.Mode, false, different);
        Row("Hard links", "link count", N(raw.Links), live is null ? null : N(live.Links), live?.Links == raw.Links, false, different);
        Row("Owner (UID)", "owner", N(raw.Uid), live is null ? null : N(live.Uid), live?.Uid == raw.Uid, false, different);
        Row("Group (GID)", "group", N(raw.Gid), live is null ? null : N(live.Gid), live?.Gid == raw.Gid, false, different);
        Row("Size", "size", N(raw.Size), live is null ? null : N(live.Size), live?.Size == raw.Size, regular && raw.Size < live?.Size, dataBehind);
        // The kernel adds the blocks it reserved for data not yet written (delayed allocation), and counts inline data.
        ulong inline = (raw.Flags & 0x1000_0000) != 0 && live is not null ? (live.Size + 511) / 512 : 0;
        Row("Blocks (512 bytes)", "blocks", N(raw.Blocks), live is null ? null : N(live.Blocks), live?.Blocks == raw.Blocks + inline, regular && raw.Blocks + inline < live?.Blocks, dataBehind);
        // An inode without room for nanoseconds keeps whole seconds, while the kernel holds the nanoseconds in memory.
        void Time(string name, string noun, Ext4Inode.Timestamp onDisk, Ext4Inode.Timestamp? now)
        {
            var kept = now is { } n && !raw.KeepsNanoseconds ? n with { Nanoseconds = 0 } : now;
            Row(name, noun, T(onDisk), now is { } shown ? T(shown) : null, kept == onDisk, kept is { } later && onDisk.CompareTo(later) < 0, timesBehind);
        }
        Time("Modified", "modification time", raw.Modified, live?.Modified);
        Time("Status changed", "status change time", raw.Changed, live?.Changed);
        Time("Accessed", "access time", raw.Accessed, live?.Accessed);
        if (raw.Born is { } born) Row("Birth", "birth time", T(born), live?.Born is { } b ? T(b) : null, live?.Born == born, false, different);
        Row("Generation", "generation", N(raw.Generation), live?.Generation is { } g ? N(g) : null, live?.Generation == raw.Generation, false, different);
        uint shown = raw.Flags & Ext4VisibleFlags & (regular ? ~0x2000_0000u : ~0u); // project inherit is not shown for files
        Row("Flags", "flags", $"0x{raw.Flags:X8}", live?.Flags is { } f ? $"0x{f:X8}" : null, live?.Flags == shown, false, different);

        var lines = new List<string>();
        void Say(string text, bool strong = false) => lines.AddRange(RecordText.Wrap(text, 110, strong ? "⚠ " : "• "));
        if (raw.ChecksumMatches == false)
        {
            if (changedWhileRead) Say("It changed while it was read, and its checksum did not match its bytes either time: read it again.");
            else
            {
                Say("Its checksum does not match its bytes, read twice. The kernel sets the checksum whenever it copies the inode into the table, so something other than ext4 changed the table, or it is damaged: the kernel refuses the inode (“Bad message”) when it next loads it, and e2fsck can repair it.", strong: true);
                warnings.Add("Inode on disk: its checksum does not match its bytes.");
            }
        }
        List<string> structures = [];
        if (raw.SuperblockChecksumMatches == false) structures.Add("superblock's");
        if (raw.DescriptorChecksumMatches == false) structures.Add($"block group {raw.Group:N0}'s descriptor's");
        if (structures.Count > 0)
            Say($"The {Join(structures)} {(structures.Count == 1 ? "checksum does" : "checksums do")} not match, read twice. The kernel rewrites {(structures.Count == 1 ? "it" : "them")} with every change it makes there, so a mismatch that stays means the volume was changed outside ext4 or is damaged.", strong: true);
        foreach (string note in raw.Notes) Say(note);
        if (dataBehind.Count > 0)
            Say($"The inode table's {Join(dataBehind)} {(dataBehind.Count == 1 ? "is" : "are")} behind the kernel's: ext4 records new data's size and blocks when it allocates them, as the data is written out (delayed allocation, usually within half a minute, or at a sync). Not yet written, not damage.");
        if (timesBehind.Count > 0)
            Say($"The inode table's {Join(timesBehind)} {(timesBehind.Count == 1 ? "is" : "are")} older than the kernel's: the newer {(timesBehind.Count == 1 ? "one is" : "ones are")} still only in memory (lazytime keeps them there until the inode is written for another reason, a sync, or 12 hours). Not yet written, not damage.");
        if (different.Count > 0)
            Say($"The inode table's {Join(different)} differ{(different.Count == 1 ? "s" : "")} from the kernel's. ext4 copies {(different.Count == 1 ? "it" : "these")} into the table as soon as {(different.Count == 1 ? "it changes" : "they change")}, so the item changed while it was read (read it again), or the table holds what ext4 did not write there.", strong: true);
        if (live is null) Say("What the kernel shows now was not compared: the item was removed or replaced while it was read.");
        else if (!regular && (raw.Mode & TypeMask) != 0x4000) Say("The kernel's generation and flags are asked of files and folders only.");
        if (!raw.KeepsNanoseconds) Say("This inode has no room for nanoseconds, so its times are compared with the kernel's to the second.");
        uint hidden = raw.Flags & ~Ext4VisibleFlags;
        if (hidden != 0) Say($"Its flags also have 0x{hidden:X8} ({Words(hidden, Ext4HiddenFlagNames)}), which ext4 keeps for itself and does not show programs.");
        lines.AddRange(RecordText.Wrap($"Read as root from {device} through the kernel's page cache (not O_DIRECT): this is the inode table as the kernel holds it, with any changes that have not reached the disk yet."));
        if (raw.Version is not null)
            lines.AddRange(RecordText.Wrap("i_version is the inode's change counter (NFS's change attribute): it goes up when the item changes. Linux does not show programs the kernel's current value, so only the inode table's is shown."));
        lines.Add($"The inode as the table holds it ({raw.InodeSize:N0} bytes):");
        lines.AddRange(RecordText.HexLines(raw.Raw, 256));
        return new InspectionSection(title, fields) { Table = new InspectionTable(["Field", "In the inode table", "What the kernel shows"], rows), Lines = lines };
    }
}
