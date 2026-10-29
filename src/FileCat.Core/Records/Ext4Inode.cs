using System.Buffers.Binary;
using System.Numerics;

namespace FileCat.Core.Records;

/// <summary>
/// One inode as an ext2, ext3, or ext4 volume keeps it in its inode table (D-56), read from the raw volume through
/// <c>read(offset, count)</c>: the superblock at byte 1,024 gives the block and inode sizes and the features; the inode's
/// block group descriptor (32 or 64 bytes, after the superblock or, with meta_bg, in its meta group) says where the
/// group's inode table starts; the inode is its entry there. With metadata_csum the inode carries a crc32c of itself,
/// computed here as the kernel's ext4_inode_csum computes it; i_version, the change counter, is l_i_version with
/// i_version_hi above it. Every value is checked before it is used, no read is longer than a block, and a malformed or
/// cut-short volume ends the read with a reason.
/// </summary>
public sealed class Ext4Inode
{
    /// <summary>A time as ext4 keeps it: seconds (34 bits with the extra field's epoch bits) and nanoseconds.</summary>
    public readonly record struct Timestamp(long Seconds, uint Nanoseconds) : IComparable<Timestamp>
    {
        public int CompareTo(Timestamp other) => (Seconds, Nanoseconds).CompareTo((other.Seconds, other.Nanoseconds));
    }

    private const int OldInodeSize = 128;

    public uint Number { get; private init; }
    public uint Group { get; private init; }

    /// <summary>The first block of the group's inode table, the inode's entry in it (from 0), and how many entries it has.</summary>
    public ulong InodeTable { get; private init; }
    public uint Index { get; private init; }
    public uint InodesPerGroup { get; private init; }

    /// <summary>Where the inode lies on the volume, in bytes.</summary>
    public long Offset { get; private init; }

    public int BlockSize { get; private init; }
    public int InodeSize { get; private init; }
    public int DescriptorSize { get; private init; }

    /// <summary>The inode's bytes as the table holds them.</summary>
    public byte[] Raw { get; private init; } = [];

    public ushort Mode { get; private init; }
    public ushort Links { get; private init; }
    public uint Uid { get; private init; }
    public uint Gid { get; private init; }
    public ulong Size { get; private init; }

    /// <summary>Its blocks in 512-byte units, as the kernel counts them (a huge file keeps file-system blocks).</summary>
    public ulong Blocks { get; private init; }

    public uint Flags { get; private init; }
    public uint Generation { get; private init; }

    /// <summary>i_dtime: when it was deleted; on an inode in use, the next inode on the orphan list.</summary>
    public uint DeletionTime { get; private init; }

    /// <summary>i_extra_isize as stored: how many bytes past the first 128 hold fields (nanoseconds, birth, i_version_hi).</summary>
    public ushort ExtraSize { get; private init; }

    public Timestamp Accessed { get; private init; }
    public Timestamp Changed { get; private init; }
    public Timestamp Modified { get; private init; }
    public Timestamp? Born { get; private init; }

    /// <summary>Whether the inode has room for its times' nanoseconds (the extra fields of ctime, mtime, and atime).</summary>
    public bool KeepsNanoseconds { get; private init; }

    /// <summary>i_version, the change counter; null on a volume the GNU Hurd made, which keeps something else there.</summary>
    public ulong? Version { get; private init; }

    /// <summary>Whether the inode has room for i_version_hi, the counter's upper 32 bits.</summary>
    public bool VersionHasHigh { get; private init; }

    /// <summary>l_i_checksum_lo, with i_checksum_hi above it when the inode has room for it.</summary>
    public uint StoredChecksum { get; private init; }

    /// <summary>The checksum the inode's bytes give (its low 16 bits when only those are stored), or null with <see cref="NoChecksum"/>.</summary>
    public uint? ComputedChecksum { get; private init; }
    public bool ChecksumHasHigh { get; private init; }
    public string? NoChecksum { get; private init; }
    public bool? ChecksumMatches => ComputedChecksum is { } computed ? computed == StoredChecksum : null;

    /// <summary>Whether the checksums start from the superblock's own seed (metadata_csum_seed) rather than the crc32c of its UUID.</summary>
    public bool SeedInSuperblock { get; private init; }
    public bool? SuperblockChecksumMatches { get; private init; }
    public bool? DescriptorChecksumMatches { get; private init; }

    /// <summary>What is odd but did not stop the read: an i_extra_isize the kernel refuses, an inode table not set up.</summary>
    public IReadOnlyList<string> Notes { get; private init; } = [];

    /// <summary>Inode <paramref name="number"/> of the volume that <paramref name="read"/> reads, or null with <paramref name="problem"/> saying why.</summary>
    public static Ext4Inode? Read(Func<long, int, byte[]> read, ulong number, out string? problem)
    {
        string? failure = null;
        byte[]? Get(long offset, int count, string what)
        {
            byte[] data;
            try { data = read(offset, count); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failure = $"The volume could not be read at byte {offset:N0} ({what}): {ex.Message}";
                return null;
            }
            if (data.Length >= count) return data.Length == count ? data : data[..count];
            failure = $"The volume ends before {what} (byte {offset:N0}).";
            return null;
        }

        // The superblock.
        problem = null;
        var sb = Get(1024, 1024, "its superblock");
        if (sb is null) return Fail(failure, out problem);
        if (U16(sb, 0x38) != 0xEF53) return Fail("It is not an ext2, ext3, or ext4 volume: its superblock has no 0xEF53 signature.", out problem);
        uint revision = U32(sb, 0x4C), incompat = U32(sb, 0x60), roCompat = U32(sb, 0x64), log = U32(sb, 0x18);
        if (log > 6) return Fail($"Its superblock's block size (2^(10 + {log:N0}) bytes) is not one of ext4's, 1 to 64 KiB.", out problem);
        int blockSize = 1024 << (int)log;
        int inodeSize = revision == 0 ? OldInodeSize : U16(sb, 0x58);
        if (inodeSize < OldInodeSize || inodeSize > blockSize || !BitOperations.IsPow2(inodeSize))
            return Fail($"Its superblock's inode size ({inodeSize:N0} bytes) is not a power of two from 128 to the block size ({blockSize:N0} bytes).", out problem);
        bool wide = (incompat & 0x80) != 0; // 64bit: 64-bit block numbers, descriptors of s_desc_size bytes
        int descriptorSize = wide ? U16(sb, 0xFE) : 32;
        if (wide && (descriptorSize < 64 || descriptorSize > 1024 || !BitOperations.IsPow2(descriptorSize)))
            return Fail($"Its superblock's group descriptor size ({descriptorSize:N0} bytes) is not a power of two from 64 to 1,024, as a 64-bit volume needs.", out problem);
        uint blocksPerGroup = U32(sb, 0x20), inodesPerGroup = U32(sb, 0x28), inodesCount = U32(sb, 0x00), firstData = U32(sb, 0x14);
        bool bigalloc = (roCompat & 0x200) != 0; // its groups count clusters, so they may hold more blocks than one bitmap block maps
        if (blocksPerGroup == 0 || (!bigalloc && blocksPerGroup > 8u * (uint)blockSize))
            return Fail($"Its superblock's blocks per group ({blocksPerGroup:N0}) is not from 1 to {8 * blockSize:N0}.", out problem);
        if (inodesPerGroup < blockSize / inodeSize || inodesPerGroup > 8u * (uint)blockSize)
            return Fail($"Its superblock's inodes per group ({inodesPerGroup:N0}) is not from {blockSize / inodeSize:N0} to {8 * blockSize:N0}.", out problem);
        ulong blocks = U32(sb, 0x04) | (wide ? (ulong)U32(sb, 0x150) << 32 : 0);
        if (blocks <= firstData || blocks > (ulong)(long.MaxValue / blockSize))
            return Fail($"Its superblock's block count ({blocks:N0}) is not a volume's: it must be past the first data block ({firstData:N0}) and fit a device.", out problem);
        ulong groups = (blocks - firstData + blocksPerGroup - 1) / blocksPerGroup;

        // The inode's block group and its descriptor.
        if (number == 0) return Fail("There is no inode 0: ext4 numbers inodes from 1.", out problem);
        if (number > inodesCount) return Fail($"Inode {number:N0} is not on this volume, which has {inodesCount:N0} inodes.", out problem);
        ulong group = (number - 1) / inodesPerGroup;
        uint index = (uint)((number - 1) % inodesPerGroup);
        if (group >= groups) return Fail($"Inode {number:N0} would be in block group {group:N0}, but the volume has {groups:N0}.", out problem);
        ulong superBlock = blockSize == 1024 ? 1UL : 0; // the block that holds byte 1,024
        ulong perBlock = (ulong)(blockSize / descriptorSize), tableBlock = group / perBlock, at;
        if ((incompat & 0x10) == 0 || tableBlock < U32(sb, 0x104)) // without meta_bg, or before s_first_meta_bg
            at = superBlock + tableBlock + 1;
        else
        {
            // meta_bg: a meta group's descriptors are in its first group, after the superblock backup if it has one.
            ulong first = tableBlock * perBlock;
            at = first * blocksPerGroup + firstData + (HasSuperblock(sb, first) ? 1UL : 0) + (blockSize == 1024 && tableBlock == 0 && firstData == 0 ? 1UL : 0);
        }
        if (at >= blocks) return Fail($"Block group {group:N0}'s descriptor would be at block {at:N0}, past the end of the volume ({blocks:N0} blocks).", out problem);
        var descriptor = Get((long)at * blockSize + (long)(group % perBlock) * descriptorSize, descriptorSize, $"block group {group:N0}'s descriptor");
        if (descriptor is null) return Fail(failure, out problem);
        ulong table = U32(descriptor, 0x08) | (wide ? (ulong)U32(descriptor, 0x28) << 32 : 0);
        ulong tableBlocks = ((ulong)inodesPerGroup * (ulong)inodeSize + (ulong)blockSize - 1) / (ulong)blockSize;
        if (table <= superBlock || table >= blocks || tableBlocks > blocks - table)
            return Fail($"Block group {group:N0}'s inode table (block {table:N0}, {tableBlocks:N0} blocks long) is not inside the volume's blocks {superBlock + 1:N0} to {blocks - 1:N0}.", out problem);
        var notes = new List<string>();
        if ((U16(descriptor, 0x12) & 1) != 0)
            notes.Add($"Block group {group:N0} is marked as not having its inode table set up (INODE_UNINIT), so none of its inodes should be in use.");

        // The inode.
        long offset = (long)table * blockSize + (long)index * inodeSize;
        var raw = Get(offset, inodeSize, $"inode {number:N0}");
        if (raw is null) return Fail(failure, out problem);
        // i_extra_isize says which fields past byte 128 the inode has; the kernel refuses one past the end or not 4-aligned.
        int extra = 0;
        ushort extraStored = inodeSize > OldInodeSize ? U16(raw, 0x80) : (ushort)0;
        if (OldInodeSize + extraStored > inodeSize || (extraStored & 3) != 0)
            notes.Add($"Its i_extra_isize ({extraStored:N0}) is {(OldInodeSize + extraStored > inodeSize ? $"past the end of its {inodeSize:N0} bytes" : "not a multiple of 4")}: the kernel refuses such an inode, and its fields after byte 128 were not read.");
        else extra = extraStored;

        // Checksums: the seed is the superblock's own or the crc32c of its UUID; the GNU Hurd keeps other data in the
        // inode's checksum field, so the kernel checks no Hurd inode.
        bool hurd = U32(sb, 0x48) == 1, seedStored = (incompat & 0x2000) != 0;
        byte algorithm = sb[0x175];
        string? noChecksum = (roCompat & 0x400) == 0 ? "none: this volume does not checksum its inodes (it was made without metadata_csum)"
            : algorithm != 1 ? $"not checked: the superblock names checksum algorithm {algorithm}, and ext4 knows only crc32c (1)"
            : hurd ? "none: the GNU Hurd made this volume, and its inodes keep other data where the checksum would be"
            : null;
        uint? seed = (roCompat & 0x400) != 0 && algorithm == 1 ? seedStored ? U32(sb, 0x270) : Crc32C(~0u, sb.AsSpan(0x68, 16)) : null;
        bool high = Fits(0x82, 2, extra);
        uint? computed = seed is { } s && !hurd ? InodeChecksum(s, (uint)number, raw, high) : null;
        uint flags = U32(raw, 0x20);
        ulong blocks512 = U32(raw, 0x1C);
        if (!hurd && (roCompat & 0x8) != 0) // huge_file: 48 bits, in file-system blocks when the inode says so
        {
            blocks512 |= (ulong)U16(raw, 0x74) << 32;
            if ((flags & 0x40000) != 0) blocks512 <<= (int)log + 1;
        }

        return new Ext4Inode
        {
            Number = (uint)number,
            Group = (uint)group,
            InodeTable = table,
            Index = index,
            InodesPerGroup = inodesPerGroup,
            Offset = offset,
            BlockSize = blockSize,
            InodeSize = inodeSize,
            DescriptorSize = descriptorSize,
            Raw = raw,
            Mode = U16(raw, 0x00),
            Uid = U16(raw, 0x02) | (uint)U16(raw, 0x78) << 16,
            Size = U32(raw, 0x04) | (ulong)U32(raw, 0x6C) << 32,
            Accessed = Time(raw, 0x08, 0x8C, extra),
            Changed = Time(raw, 0x0C, 0x84, extra),
            Modified = Time(raw, 0x10, 0x88, extra),
            DeletionTime = U32(raw, 0x14),
            Gid = U16(raw, 0x18) | (uint)U16(raw, 0x7A) << 16,
            Links = U16(raw, 0x1A),
            Blocks = blocks512,
            Flags = flags,
            Generation = U32(raw, 0x64),
            ExtraSize = extraStored,
            Born = Fits(0x90, 4, extra) ? Time(raw, 0x90, 0x94, extra) : null,
            KeepsNanoseconds = Fits(0x8C, 4, extra),
            Version = hurd ? null : U32(raw, 0x24) | (Fits(0x98, 4, extra) ? (ulong)U32(raw, 0x98) << 32 : 0),
            VersionHasHigh = !hurd && Fits(0x98, 4, extra),
            StoredChecksum = U16(raw, 0x7C) | (high ? (uint)U16(raw, 0x82) << 16 : 0),
            ComputedChecksum = computed is { } c ? high ? c : c & 0xFFFF : null,
            ChecksumHasHigh = high,
            NoChecksum = noChecksum,
            SeedInSuperblock = seedStored,
            SuperblockChecksumMatches = seed is null ? null : Crc32C(~0u, sb.AsSpan(0, 0x3FC)) == U32(sb, 0x3FC),
            DescriptorChecksumMatches = seed is { } g ? DescriptorChecksum(g, (uint)group, descriptor) == U16(descriptor, 0x1E) : null,
            Notes = notes,
        };
    }

    private static Ext4Inode? Fail(string? reason, out string? problem)
    {
        problem = reason;
        return null;
    }

    /// <summary>
    /// ext4_inode_csum: crc32c from the volume's seed over the inode number and its generation (32-bit little endian),
    /// then over the whole inode with l_i_checksum_lo and, when the inode has room for it, i_checksum_hi taken as zero.
    /// </summary>
    internal static uint InodeChecksum(uint seed, uint number, ReadOnlySpan<byte> raw, bool high)
    {
        Span<byte> le = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(le, number);
        uint crc = Crc32C(Crc32C(seed, le), raw.Slice(0x64, 4));
        crc = Crc32C(crc, raw[..0x7C]);
        crc = Crc32C(crc, [0, 0]);
        crc = Crc32C(crc, raw[0x7E..OldInodeSize]);
        if (raw.Length > OldInodeSize)
        {
            crc = Crc32C(crc, raw[OldInodeSize..0x82]);
            if (high) crc = Crc32C(crc, [0, 0]);
            crc = Crc32C(crc, raw[(high ? 0x84 : 0x82)..]);
        }
        return crc;
    }

    /// <summary>A group descriptor's checksum with metadata_csum: the low 16 bits of crc32c over the group number and the descriptor, bg_checksum taken as zero.</summary>
    private static ushort DescriptorChecksum(uint seed, uint group, ReadOnlySpan<byte> descriptor)
    {
        Span<byte> le = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(le, group);
        uint crc = Crc32C(Crc32C(seed, le), descriptor[..0x1E]);
        crc = Crc32C(crc, [0, 0]);
        return (ushort)Crc32C(crc, descriptor[0x20..]);
    }

    /// <summary>
    /// CRC-32C (Castagnoli) continued from <paramref name="crc"/> with no inversion at either end, as Linux's crc32c and
    /// the processors' CRC32 instructions compute it: ext4 starts from ~0 or a seed and keeps the result as it is.
    /// </summary>
    internal static uint Crc32C(uint crc, ReadOnlySpan<byte> data)
    {
        for (; data.Length >= 8; data = data[8..]) crc = BitOperations.Crc32C(crc, BinaryPrimitives.ReadUInt64LittleEndian(data));
        foreach (byte b in data) crc = BitOperations.Crc32C(crc, b);
        return crc;
    }

    /// <summary>Whether a field past byte 128 lies within the inode's i_extra_isize (EXT4_FITS_IN_INODE).</summary>
    private static bool Fits(int field, int size, int extra) => field + size <= OldInodeSize + extra;

    /// <summary>
    /// A time with its extra field when the inode has one: two epoch bits above the signed 32-bit seconds, then the
    /// nanoseconds. Without it, the seconds alone.
    /// </summary>
    private static Timestamp Time(byte[] raw, int field, int extraField, int extra)
    {
        uint seconds = U32(raw, field);
        if (!Fits(extraField, 4, extra)) return new Timestamp((int)seconds, 0);
        uint bits = U32(raw, extraField);
        return new Timestamp((int)seconds + ((long)(bits & 3) << 32), bits >> 2);
    }

    /// <summary>ext4_bg_has_super: whether a block group keeps a copy of the superblock.</summary>
    private static bool HasSuperblock(byte[] sb, ulong group)
    {
        if (group == 0) return true;
        if ((U32(sb, 0x5C) & 0x200) != 0) return group == U32(sb, 0x24C) || group == U32(sb, 0x250); // sparse_super2: s_backup_bgs
        if (group == 1 || (U32(sb, 0x64) & 0x1) == 0) return true; // without sparse_super, every group
        return group % 2 == 1 && (IsPowerOf(group, 3) || IsPowerOf(group, 5) || IsPowerOf(group, 7));
    }

    private static bool IsPowerOf(ulong value, ulong root)
    {
        while (value > 1 && value % root == 0) value /= root;
        return value == 1;
    }

    private static ushort U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));

    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
}
