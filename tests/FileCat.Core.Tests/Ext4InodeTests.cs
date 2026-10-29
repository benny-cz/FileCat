using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using FileCat.Tests;

namespace FileCat.Core.Tests;

/// <summary>
/// D-56: an ext2, ext3, or ext4 inode read from the raw volume. Volumes made here test the checksum, i_version, and every
/// refusal on each system; on Linux with e2fsprogs, volumes made by mke2fs are read and compared with what debugfs says
/// of the same inodes.
/// </summary>
public sealed class Ext4InodeTests
{
    // ---- CRC-32C ------------------------------------------------------------------------------------------------------

    /// <summary>Bit by bit, reflected 0x82F63B78, no inversion: the reference the tests check FileCat's against.</summary>
    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0x82F6_3B78 : crc >> 1;
        }
        return crc;
    }

    [Fact]
    public void Crc32c_is_Castagnolis_with_no_inversion_inside()
    {
        // The standard check value inverts at both ends; ext4 seeds with ~0 and keeps the result as it is.
        Assert.Equal(0xE306_9283u, ~Ext4Inode.Crc32C(~0u, "123456789"u8));
        var data = new byte[300];
        new Random(5).NextBytes(data);
        for (int split = 0; split <= data.Length; split += 7)
            Assert.Equal(Crc(0x1234_5678, data), Ext4Inode.Crc32C(Ext4Inode.Crc32C(0x1234_5678, data.AsSpan(0, split)), data.AsSpan(split)));
    }

    // ---- Volumes made here --------------------------------------------------------------------------------------------

    private const int InodeTableBlock = 5;

    /// <summary>
    /// A 64 KiB volume of 1 KiB blocks: one block group of 16 inodes, the descriptor table at block 2 and the inode table
    /// at block 5, and inode 12 a file. <paramref name="change"/> alters it before the checksums are set with the bitwise
    /// reference.
    /// </summary>
    private static byte[] MakeVolume(int inodeSize = 256, bool checksums = true, uint? storedSeed = null, Action<byte[]>? change = null)
    {
        var v = new byte[64 * 1024];
        void Sb16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(v.AsSpan(1024 + at), value);
        void Sb32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(1024 + at), value);
        Sb32(0x00, 16); // inodes
        Sb32(0x04, 64); // blocks
        Sb32(0x14, 1); // first data block
        Sb32(0x18, 0); // 1 KiB blocks
        Sb32(0x20, 8192);
        Sb32(0x28, 16);
        Sb16(0x38, 0xEF53);
        Sb32(0x4C, 1); // dynamic revision
        Sb32(0x54, 11);
        Sb16(0x58, (ushort)inodeSize);
        Sb32(0x60, 0xC2u | (storedSeed is null ? 0 : 0x2000u)); // 64bit, extents, filetype, csum_seed
        Sb32(0x64, checksums ? 0x400u : 0); // metadata_csum
        for (int i = 0; i < 16; i++) v[1024 + 0x68 + i] = (byte)(0x10 + i);
        Sb16(0xFE, 64);
        v[1024 + 0x175] = 1; // crc32c
        Sb32(0x270, storedSeed ?? 0);

        BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(2048 + 0x08), InodeTableBlock);
        BinaryPrimitives.WriteUInt16LittleEndian(v.AsSpan(2048 + 0x12), 0x4); // inode table zeroed

        int inode = InodeOffset(12, inodeSize);
        void I16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(v.AsSpan(inode + at), value);
        void I32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(inode + at), value);
        I16(0x00, 0x81A4); // file, 644
        I16(0x02, 1000);
        I32(0x04, 5000);
        I32(0x08, 1_700_000_000);
        I32(0x0C, 1_700_000_100);
        I32(0x10, 1_700_000_200);
        I16(0x18, 1000);
        I16(0x1A, 1);
        I32(0x1C, 10);
        I32(0x20, 0x80000); // extents
        I32(0x24, 7); // l_i_version
        I32(0x64, 0x1234_5678); // generation
        I16(0x78, 1); // uid high: 66,536
        if (inodeSize > 128)
        {
            I16(0x80, 32);
            I32(0x84, 123_456_789u << 2);
            I32(0x88, (987_654_321u << 2) | 1); // an epoch bit: 2^32 seconds later
            I32(0x8C, 5u << 2);
            I32(0x90, 1_690_000_000);
            I32(0x94, 42u << 2);
            I32(0x98, 1); // i_version_hi
        }
        change?.Invoke(v);
        Seal(v, inodeSize, storedSeed);
        return v;
    }

    private static int InodeOffset(uint number, int inodeSize) => InodeTableBlock * 1024 + (int)(number - 1) * inodeSize;

    /// <summary>The checksums as e2fsprogs writes them, each over its bytes with its own checksum field taken as zero.</summary>
    private static void Seal(byte[] v, int inodeSize, uint? storedSeed)
    {
        uint seed = storedSeed ?? Crc(~0u, v.AsSpan(1024 + 0x68, 16));
        Span<byte> le = stackalloc byte[4];
        var inode = v.AsSpan(InodeOffset(12, inodeSize), inodeSize);
        bool high = inodeSize > 128 && BinaryPrimitives.ReadUInt16LittleEndian(inode[0x80..]) >= 4;
        var copy = inode.ToArray();
        copy[0x7C] = copy[0x7D] = 0;
        if (high) copy[0x82] = copy[0x83] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(le, 12);
        uint crc = Crc(Crc(Crc(seed, le), copy.AsSpan(0x64, 4)), copy);
        BinaryPrimitives.WriteUInt16LittleEndian(inode[0x7C..], (ushort)crc);
        if (high) BinaryPrimitives.WriteUInt16LittleEndian(inode[0x82..], (ushort)(crc >> 16));

        var gd = v.AsSpan(2048, 64);
        var descriptor = gd.ToArray();
        descriptor[0x1E] = descriptor[0x1F] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(le, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(gd[0x1E..], (ushort)Crc(Crc(seed, le), descriptor));
        BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(1024 + 0x3FC), Crc(~0u, v.AsSpan(1024, 0x3FC)));
    }

    /// <summary>Reads inode <paramref name="number"/> of a volume made here; every read must be bounded and inside it.</summary>
    private static Ext4Inode? Read(byte[] volume, ulong number, out string? problem) => Ext4Inode.Read((offset, count) =>
    {
        Assert.InRange(offset, 0, long.MaxValue);
        Assert.InRange(count, 1, 64 * 1024);
        return offset >= volume.Length ? [] : volume.AsSpan((int)offset, (int)Math.Min(count, volume.Length - offset)).ToArray();
    }, number, out problem);

    [Fact]
    public void A_volume_made_here_reads_with_its_checksum_and_i_version()
    {
        var inode = Read(MakeVolume(), 12, out string? problem);
        Assert.Null(problem);
        Assert.NotNull(inode);
        Assert.Equal(InodeOffset(12, 256), inode.Offset);
        Assert.Equal((0u, 11u, (ulong)InodeTableBlock), (inode.Group, inode.Index, inode.InodeTable));
        Assert.True(inode.ChecksumMatches);
        Assert.True(inode.ChecksumHasHigh);
        Assert.True(inode.SuperblockChecksumMatches);
        Assert.True(inode.DescriptorChecksumMatches);
        Assert.False(inode.SeedInSuperblock);
        Assert.Equal(0x1_0000_0007UL, inode.Version);
        Assert.True(inode.VersionHasHigh);
        Assert.Equal(0x1234_5678u, inode.Generation);
        Assert.Equal(((ushort)0x81A4, 66_536u, 1000u, 5000UL, (ushort)1, 10UL, 0x80000u), (inode.Mode, inode.Uid, inode.Gid, inode.Size, inode.Links, inode.Blocks, inode.Flags));
        Assert.Equal(new Ext4Inode.Timestamp(1_700_000_100, 123_456_789), inode.Changed);
        Assert.Equal(new Ext4Inode.Timestamp(1_700_000_200 + (1L << 32), 987_654_321), inode.Modified);
        Assert.Equal(new Ext4Inode.Timestamp(1_700_000_000, 5), inode.Accessed);
        Assert.Equal(new Ext4Inode.Timestamp(1_690_000_000, 42), inode.Born);
        Assert.True(inode.KeepsNanoseconds);
        Assert.Empty(inode.Notes);

        // One byte changed outside ext4 (i_obso_faddr, which nothing reads): the checksum no longer matches.
        var changed = MakeVolume();
        changed[InodeOffset(12, 256) + 0x71] ^= 0xFF;
        var damaged = Read(changed, 12, out _);
        Assert.False(damaged!.ChecksumMatches);
        Assert.True(damaged.DescriptorChecksumMatches);
    }

    [Fact]
    public void A_128_byte_inode_keeps_16_checksum_bits_and_32_version_bits()
    {
        var inode = Read(MakeVolume(inodeSize: 128), 12, out string? problem);
        Assert.Null(problem);
        Assert.True(inode!.ChecksumMatches);
        Assert.False(inode.ChecksumHasHigh);
        Assert.InRange(inode.StoredChecksum, 0u, 0xFFFFu);
        Assert.Equal(7UL, inode.Version);
        Assert.False(inode.VersionHasHigh);
        Assert.Null(inode.Born);
        Assert.False(inode.KeepsNanoseconds);
        Assert.Equal(new Ext4Inode.Timestamp(1_700_000_200, 0), inode.Modified);
    }

    [Fact]
    public void A_stored_seed_is_used_instead_of_the_UUIDs_and_no_metadata_csum_means_no_checksum()
    {
        var seeded = Read(MakeVolume(storedSeed: 0xCAFE_BABE), 12, out _);
        Assert.True(seeded!.SeedInSuperblock);
        Assert.True(seeded.ChecksumMatches);
        Assert.True(seeded.DescriptorChecksumMatches);

        var plain = Read(MakeVolume(checksums: false), 12, out string? problem);
        Assert.Null(problem);
        Assert.Null(plain!.ComputedChecksum);
        Assert.Null(plain.ChecksumMatches);
        Assert.Null(plain.SuperblockChecksumMatches);
        Assert.Contains("metadata_csum", plain.NoChecksum, StringComparison.Ordinal);
        Assert.Equal(0x1_0000_0007UL, plain.Version);
    }

    [Fact]
    public void An_extra_size_the_kernel_refuses_is_said_and_the_first_128_bytes_still_read()
    {
        var inode = Read(MakeVolume(change: v => BinaryPrimitives.WriteUInt16LittleEndian(v.AsSpan(InodeOffset(12, 256) + 0x80), 6)), 12, out string? problem);
        Assert.Null(problem);
        Assert.Contains(inode!.Notes, n => n.Contains("i_extra_isize (6)", StringComparison.Ordinal) && n.Contains("multiple of 4", StringComparison.Ordinal));
        Assert.Null(inode.Born);
        Assert.Equal(7UL, inode.Version);
        Assert.Equal(1000u, inode.Gid);
        var past = Read(MakeVolume(change: v => BinaryPrimitives.WriteUInt16LittleEndian(v.AsSpan(InodeOffset(12, 256) + 0x80), 200)), 12, out _);
        Assert.Contains(past!.Notes, n => n.Contains("past the end", StringComparison.Ordinal));
    }

    public static TheoryData<string, uint, string> Malformed => new()
    {
        { "no signature", 12, "0xEF53" },
        { "inode 0", 0, "There is no inode 0" },
        { "inode past the count", 17, "not on this volume, which has 16" },
        { "group past the count", 40, "would be in block group 2" },
        { "inode size 0", 12, "inode size (0 bytes)" },
        { "inode size 4096 on 1 KiB blocks", 12, "inode size (4,096 bytes)" },
        { "inode size 200", 12, "inode size (200 bytes)" },
        { "block size 2^17", 12, "block size" },
        { "block size field all ones", 12, "block size" },
        { "no blocks per group", 12, "blocks per group (0)" },
        { "no inodes per group", 12, "inodes per group (0)" },
        { "too many inodes per group", 12, "inodes per group (100,000)" },
        { "descriptor size 48", 12, "group descriptor size (48 bytes)" },
        { "block count at the first data block", 12, "block count (1)" },
        { "block count past any device", 12, "block count" },
        { "inode table past the end", 12, "inode table (block 1,000" },
        { "inode table on the superblock", 12, "inode table (block 1," },
        { "cut inside the superblock", 12, "ends before its superblock" },
        { "cut before the descriptor", 12, "ends before block group 0's descriptor" },
        { "cut inside the inode table", 12, "ends before inode 12" },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void A_malformed_or_cut_short_volume_is_refused_with_a_reason(string what, uint number, string reason)
    {
        void Sb(byte[] v, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(1024 + at), value);
        var volume = MakeVolume(change: v =>
        {
            switch (what)
            {
                case "no signature": v[1024 + 0x38] = 0; break;
                case "group past the count": Sb(v, 0x00, 64); break;
                case "inode size 0": BinaryPrimitives.WriteUInt16LittleEndian(v.AsSpan(1024 + 0x58), 0); break;
                case "inode size 4096 on 1 KiB blocks": BinaryPrimitives.WriteUInt16LittleEndian(v.AsSpan(1024 + 0x58), 4096); break;
                case "inode size 200": BinaryPrimitives.WriteUInt16LittleEndian(v.AsSpan(1024 + 0x58), 200); break;
                case "block size 2^17": Sb(v, 0x18, 7); break;
                case "block size field all ones": Sb(v, 0x18, uint.MaxValue); break;
                case "no blocks per group": Sb(v, 0x20, 0); break;
                case "no inodes per group": Sb(v, 0x28, 0); break;
                case "too many inodes per group": Sb(v, 0x28, 100_000); break;
                case "descriptor size 48": BinaryPrimitives.WriteUInt16LittleEndian(v.AsSpan(1024 + 0xFE), 48); break;
                case "block count at the first data block": Sb(v, 0x04, 1); break;
                case "block count past any device": Sb(v, 0x150, uint.MaxValue); break;
                case "inode table past the end": BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(2048 + 0x08), 1000); break;
                case "inode table on the superblock": BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(2048 + 0x08), 1); break;
            }
        });
        // The superblock is bytes 1,024 to 2,047, the descriptor 2,048 to 2,111, and inode 12 starts at byte 7,936.
        int cut = what switch { "cut inside the superblock" => 1500, "cut before the descriptor" => 2080, "cut inside the inode table" => 7000, _ => volume.Length };
        Assert.Null(Read(volume[..cut], number, out string? problem));
        Assert.Contains(Grouped(reason), problem, StringComparison.Ordinal);
    }

    /// <summary>Numbers as the current culture groups their digits ("4,096" or "4 096").</summary>
    private static string Grouped(string text) => Regex.Replace(text, @"(?<=\d),(?=\d)", CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator);

    [Fact]
    public void A_volume_that_cannot_be_read_is_said_not_thrown()
    {
        Assert.Null(Ext4Inode.Read((_, _) => throw new IOException("Input/output error"), 12, out string? problem));
        Assert.Equal(Grouped("The volume could not be read at byte 1,024 (its superblock): Input/output error"), problem);
    }

    // ---- The record's section: the inode table beside the kernel ------------------------------------------------------

    private static UnixFileRecords.Ext4Live LiveOf(Ext4Inode inode) => new(inode.Mode, inode.Links, inode.Uid, inode.Gid, inode.Size, inode.Blocks,
        inode.Modified, inode.Changed, inode.Accessed, inode.Born, inode.Generation, inode.Flags);

    private static string[] Row(InspectionSection section, string name) => section.Table!.Rows.Single(r => r[0] == name);

    [Fact]
    public void A_section_beside_a_kernel_that_agrees_says_the_same_and_where_it_was_read()
    {
        var inode = Read(MakeVolume(), 12, out _)!;
        var warnings = new List<string>();
        var section = UnixFileRecords.Ext4Describe("Inode on disk (ext4)", "/dev/loop7", inode, LiveOf(inode), false, warnings);
        Assert.Equal(Grouped("/dev/loop7: block group 0, inode table at block 5, entry 12 of 16 (byte 7,936)"), section.Fields.Single(f => f.Name == "Read from").Value);
        Assert.Matches(@"^0x[0-9A-F]{8} matches \(crc32c of the volume's seed from its UUID", section.Fields.Single(f => f.Name == "Inode checksum").Value);
        Assert.StartsWith("the superblock's matches; block group 0's descriptor's matches", section.Fields.Single(f => f.Name == "Other checksums").Value, StringComparison.Ordinal);
        Assert.EndsWith("(0x00000001:00000007)", section.Fields.Single(f => f.Name == "i_version").Value, StringComparison.Ordinal);
        Assert.All(section.Table!.Rows, r => Assert.Equal("the same", r[2]));
        Assert.Contains(section.Lines, l => l.Contains("page cache (not O_DIRECT)", StringComparison.Ordinal));
        Assert.DoesNotContain(section.Lines, l => l.StartsWith("⚠", StringComparison.Ordinal));
        Assert.Empty(warnings);
    }

    [Fact]
    public void A_table_behind_the_kernel_is_said_to_be_not_yet_written_and_other_differences_are_flagged()
    {
        var inode = Read(MakeVolume(), 12, out _)!;
        var warnings = new List<string>();
        // Just written: a new size and blocks wait for delayed allocation; a lazytime mtime waits in memory.
        var written = LiveOf(inode) with { Size = 9000, Blocks = 18, Modified = inode.Modified with { Nanoseconds = 987_654_322 } };
        var section = UnixFileRecords.Ext4Describe("Inode on disk (ext4)", "/dev/sda2", inode, written, false, warnings);
        Assert.Equal(new[] { "Size", 5000.ToString("N0", CultureInfo.CurrentCulture), 9000.ToString("N0", CultureInfo.CurrentCulture) }, Row(section, "Size"));
        string text = string.Join(" ", section.Lines);
        Assert.Contains("size and blocks are behind the kernel's", text, StringComparison.Ordinal);
        Assert.Contains("modification time is older than the kernel's", text, StringComparison.Ordinal);
        Assert.Contains("Not yet written, not damage.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(section.Lines, l => l.StartsWith("⚠", StringComparison.Ordinal));

        // What ext4 copies at once (the mode) differs: flagged, not excused.
        var chmod = Read(MakeVolume(), 12, out _)!;
        section = UnixFileRecords.Ext4Describe("Inode on disk (ext4)", "/dev/sda2", chmod, LiveOf(chmod) with { Mode = 0x81ED }, false, warnings);
        Assert.Equal("100755", Row(section, "Mode")[2]);
        Assert.Contains(section.Lines, l => l.StartsWith("⚠ The inode table's mode differs from the kernel's.", StringComparison.Ordinal));
        Assert.Empty(warnings);

        // A checksum that does not match is a warning of the report.
        var changed = MakeVolume();
        changed[InodeOffset(12, 256) + 0x71] ^= 0xFF;
        var damaged = Read(changed, 12, out _)!;
        section = UnixFileRecords.Ext4Describe("Inode on disk (ext4)", "/dev/sda2", damaged, LiveOf(damaged), false, warnings);
        Assert.Contains("does not match: its bytes give 0x", section.Fields.Single(f => f.Name == "Inode checksum").Value, StringComparison.Ordinal);
        Assert.Contains(section.Lines, l => l.StartsWith("⚠ Its checksum does not match its bytes, read twice.", StringComparison.Ordinal));
        Assert.Equal(new[] { "Inode on disk: its checksum does not match its bytes." }, warnings);
    }

    [Fact]
    public void A_superblock_and_descriptor_changed_outside_ext4_are_flagged()
    {
        var volume = MakeVolume();
        volume[2048 + 0x3C] ^= 1; // bg_reserved
        volume[1024 + 0x200] ^= 1; // s_mount_opts
        var inode = Read(volume, 12, out _)!;
        Assert.Equal((false, false, true), (inode.SuperblockChecksumMatches, inode.DescriptorChecksumMatches, inode.ChecksumMatches));
        var section = UnixFileRecords.Ext4Describe("Inode on disk (ext4)", "/dev/sda2", inode, LiveOf(inode), false, []);
        Assert.Equal("the superblock's does not match; block group 0's descriptor's does not match", section.Fields.Single(f => f.Name == "Other checksums").Value);
        Assert.Contains(section.Lines, l => l.StartsWith("⚠ The superblock's and block group 0's descriptor's checksums do not match, read twice.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_128_byte_inode_is_compared_to_the_second()
    {
        var inode = Read(MakeVolume(inodeSize: 128), 12, out _)!;
        // The kernel holds nanoseconds such an inode has no room for.
        var live = LiveOf(inode) with { Modified = inode.Modified with { Nanoseconds = 500 }, Born = null };
        var section = UnixFileRecords.Ext4Describe("Inode on disk (ext4)", "/dev/sda2", inode, live, false, []);
        Assert.Equal("the same", Row(section, "Modified")[2]);
        Assert.Contains(section.Lines, l => l.Contains("compared with the kernel's to the second", StringComparison.Ordinal));
        Assert.Matches(@"^0x[0-9A-F]{4} matches .*only the low 16 bits", section.Fields.Single(f => f.Name == "Inode checksum").Value);
    }

    // ---- Volumes made by mke2fs, checked against debugfs (Linux) -------------------------------------------------------

    /// <summary>A program from PATH or the system folders (e2fsprogs lives in /usr/sbin, which a user's PATH may lack).</summary>
    internal static string? Tool(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Concat(["/usr/sbin", "/sbin", "/usr/bin", "/bin"])
            .Where(d => d.Length > 0).Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);

    internal static (int Exit, string Output) Run(string tool, params string[] arguments)
    {
        var start = new ProcessStartInfo(tool, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment["LC_ALL"] = "C";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{tool} {string.Join(' ', arguments)} did not finish in two minutes.");
        }
        return (process.ExitCode, output.Result + error.Result);
    }

    /// <summary>What debugfs's stat says of an inode: the oracle the parsed inode is compared with.</summary>
    internal sealed record DebugfsInode(int Permissions, uint Flags, uint Generation, ulong Version, bool VersionHigh, uint Uid, uint Gid, ulong Size,
        uint Links, ulong Blockcount, Ext4Inode.Timestamp Changed, Ext4Inode.Timestamp Accessed, Ext4Inode.Timestamp Modified, Ext4Inode.Timestamp? Born, uint? Checksum);

    internal static DebugfsInode ParseStat(string stat)
    {
        Match Find(string pattern)
        {
            var m = Regex.Match(stat, pattern, RegexOptions.Multiline);
            Assert.True(m.Success, $"debugfs printed no match for {pattern}:\n{stat}");
            return m;
        }
        static uint Hex(string text) => uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        static ulong Dec(Group g) => ulong.Parse(g.Value, CultureInfo.InvariantCulture);
        // A time as debugfs prints it, base:extra (or base alone), decoded as the kernel decodes it.
        Ext4Inode.Timestamp Time(string name)
        {
            var m = Find($@"(?:^|\s){name}: 0x([0-9a-f]{{8}})(?::([0-9a-f]{{8}}))? --");
            uint seconds = Hex(m.Groups[1].Value), extra = m.Groups[2].Success ? Hex(m.Groups[2].Value) : 0;
            return new Ext4Inode.Timestamp((int)seconds + ((long)(extra & 3) << 32), extra >> 2);
        }
        var mode = Find(@"Mode:\s+(\d+)\s+Flags: 0x([0-9a-f]+)");
        var version = Find(@"Generation: (\d+)\s+Version: 0x([0-9a-f]{8})(?::([0-9a-f]{8}))?");
        var owner = Find(@"User:\s+(\d+)\s+Group:\s+(\d+).*Size: (\d+)");
        var links = Find(@"Links: (\d+)\s+Blockcount: (\d+)");
        var checksum = Regex.Match(stat, @"Inode checksum: 0x([0-9a-f]{8})");
        bool high = version.Groups[3].Success;
        return new DebugfsInode(Convert.ToInt32(mode.Groups[1].Value, 8), Hex(mode.Groups[2].Value), (uint)Dec(version.Groups[1]),
            high ? (ulong)Hex(version.Groups[2].Value) << 32 | Hex(version.Groups[3].Value) : Hex(version.Groups[2].Value), high,
            (uint)Dec(owner.Groups[1]), (uint)Dec(owner.Groups[2]), Dec(owner.Groups[3]), (uint)Dec(links.Groups[1]), Dec(links.Groups[2]),
            Time("ctime"), Time("atime"), Time("mtime"), Regex.IsMatch(stat, "crtime: ") ? Time("crtime") : null,
            checksum.Success ? Hex(checksum.Groups[1].Value) : null);
    }

    /// <summary>Asserts that the inode FileCat read says what debugfs says of it, and that its checksum is its bytes'.</summary>
    internal static void AssertAgrees(DebugfsInode expected, Ext4Inode inode, string where)
    {
        string what = $"{where}, inode {inode.Number}";
        Assert.True(expected.Permissions == (inode.Mode & 0xFFF), $"{what}: mode");
        Assert.True(expected.Flags == inode.Flags, $"{what}: flags 0x{expected.Flags:X} (debugfs) and 0x{inode.Flags:X}");
        Assert.True(expected.Generation == inode.Generation, $"{what}: generation {expected.Generation} (debugfs) and {inode.Generation}");
        Assert.True(expected.Version == inode.Version, $"{what}: i_version 0x{expected.Version:X} (debugfs) and 0x{inode.Version:X}");
        Assert.True(expected.VersionHigh == inode.VersionHasHigh, $"{what}: i_version_hi kept");
        Assert.True((expected.Uid, expected.Gid, expected.Links) == (inode.Uid, inode.Gid, inode.Links), $"{what}: owner, group, links");
        // debugfs prints a symbolic link's size as its low 32 bits, which is all a link's size has.
        Assert.True(expected.Size == inode.Size, $"{what}: size {expected.Size} (debugfs) and {inode.Size}");
        if ((inode.Flags & 0x40000) == 0) Assert.True(expected.Blockcount == inode.Blocks, $"{what}: blocks {expected.Blockcount} (debugfs) and {inode.Blocks}");
        Assert.True(expected.Changed == inode.Changed, $"{what}: ctime {expected.Changed} (debugfs) and {inode.Changed}");
        Assert.True(expected.Accessed == inode.Accessed, $"{what}: atime {expected.Accessed} (debugfs) and {inode.Accessed}");
        Assert.True(expected.Modified == inode.Modified, $"{what}: mtime {expected.Modified} (debugfs) and {inode.Modified}");
        Assert.True(expected.Born == inode.Born, $"{what}: crtime {expected.Born} (debugfs) and {inode.Born}");
        if (expected.Checksum is { } stored)
        {
            Assert.True(stored == inode.StoredChecksum, $"{what}: stored checksum 0x{stored:X8} (debugfs) and 0x{inode.StoredChecksum:X8}");
            Assert.True(inode.ChecksumMatches == true, $"{what}: computed checksum 0x{inode.ComputedChecksum:X8} for the stored 0x{inode.StoredChecksum:X8}");
        }
        else Assert.True(inode.ComputedChecksum is null, $"{what}: debugfs prints no checksum, FileCat computed one");
    }

    private static Ext4Inode ReadImage(string image, uint number)
    {
        using var handle = File.OpenHandle(image);
        var inode = Ext4Inode.Read((offset, count) =>
        {
            Assert.InRange(count, 1, 64 * 1024);
            var buffer = new byte[count];
            int got = 0, n;
            while (got < count && (n = RandomAccess.Read(handle, buffer.AsSpan(got), offset + got)) > 0) got += n;
            return buffer[..got];
        }, number, out string? problem);
        Assert.True(inode is not null, $"{image}, inode {number}: {problem}");
        return inode;
    }

    /// <summary>Each inode named, with what debugfs's stat printed for it (one debugfs run per volume, checksums not checked).</summary>
    private static Dictionary<uint, string> Stats(string debugfs, string image, IEnumerable<uint> numbers, string dir)
    {
        string commands = Path.Combine(dir, "stat-commands.txt");
        File.WriteAllLines(commands, numbers.Select(n => $"stat <{n}>"));
        var (_, output) = Run(debugfs, "-n", "-f", commands, image);
        var stats = new Dictionary<uint, string>();
        foreach (Match m in Regex.Matches(output, @"debugfs: stat <(\d+)>\n(.*?)(?=debugfs: |\z)", RegexOptions.Singleline))
            stats[uint.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)] = m.Groups[2].Value;
        return stats;
    }

    /// <summary>The inodes debugfs lists in a folder of the volume ("ls -p": /inode/mode/uid/gid/name/size/), with their modes.</summary>
    private static List<(uint Inode, int Mode)> Listed(string debugfs, string image, string folder)
    {
        var (_, output) = Run(debugfs, "-R", $"ls -p {folder}", image);
        var entries = Regex.Matches(output, @"^/(\d+)/(\d+)/", RegexOptions.Multiline)
            .Select(m => (uint.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Convert.ToInt32(m.Groups[2].Value, 8))).ToList();
        Assert.True(entries.Count > 0, $"debugfs listed nothing in {folder}: {output}");
        return entries;
    }

    private static readonly (string Name, string[] Options, bool Many)[] Images =
    [
        ("4k-256-csum", ["-t", "ext4", "-b", "4096", "-I", "256", "-O", "metadata_csum,64bit"], false),
        ("1k-128-csum", ["-t", "ext4", "-b", "1024", "-I", "128", "-O", "metadata_csum,64bit"], false),
        ("1k-8-groups-32-bit-no-csum", ["-t", "ext4", "-b", "1024", "-I", "256", "-O", "^metadata_csum,^64bit", "-g", "1024", "-N", "256"], true),
        ("1k-meta_bg", ["-t", "ext4", "-b", "1024", "-I", "256", "-O", "metadata_csum,64bit,meta_bg,^resize_inode", "-g", "256", "-N", "256"], true),
        ("4k-512-csum-seed", ["-t", "ext4", "-b", "4096", "-I", "512", "-O", "metadata_csum,64bit,metadata_csum_seed"], false),
        ("ext2", ["-t", "ext2", "-b", "1024", "-I", "128"], false),
    ];

    [Fact]
    public void Inodes_of_volumes_made_by_mke2fs_say_what_debugfs_says()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("mke2fs and debugfs run on Linux.");
        string? mke2fs = Tool("mke2fs"), debugfs = Tool("debugfs"), tune2fs = Tool("tune2fs"), e2fsck = Tool("e2fsck");
        if (mke2fs is null || debugfs is null || tune2fs is null || e2fsck is null)
        {
            Assert.Skip("e2fsprogs (mke2fs, debugfs, tune2fs, e2fsck) is not installed.");
            return;
        }
        string dir = Path.Combine(Path.GetTempPath(), "filecat-ext4-tests", Guid.NewGuid().ToString("N"));
        try
        {
            // A few kinds of item, and a folder of many files that spread over the block groups.
            string few = Directory.CreateDirectory(Path.Combine(dir, "few")).FullName, many = Directory.CreateDirectory(Path.Combine(dir, "many")).FullName;
            File.WriteAllText(Path.Combine(few, "empty.txt"), "");
            File.WriteAllText(Path.Combine(few, "small.txt"), "small");
            File.WriteAllBytes(Path.Combine(few, "large.bin"), new byte[300_000]);
            string sub = Directory.CreateDirectory(Path.Combine(few, "sub")).FullName;
            File.WriteAllText(Path.Combine(sub, "inner.txt"), new string('x', 5000));
            File.CreateSymbolicLink(Path.Combine(few, "short link"), "small.txt");
            File.CreateSymbolicLink(Path.Combine(few, "long link"), new string('d', 100) + "/target");
            for (int i = 0; i < 150; i++) File.WriteAllText(Path.Combine(many, $"file {i:D3}.txt"), new string('m', i * 13));
            int checkedInodes = 0;
            foreach (var (name, options, spread) in Images)
            {
                string image = Path.Combine(dir, name + ".img");
                var (made, output) = Run(mke2fs, [.. options, "-q", "-F", "-d", spread ? many : few, image, "8M"]);
                Assert.True(made == 0, $"mke2fs {name}: {output}");
                if (name.EndsWith("seed", StringComparison.Ordinal))
                {
                    // With metadata_csum_seed the seed stays in the superblock when the UUID changes, so the UUID's crc no longer gives it.
                    var (changed, said) = Run(tune2fs, "-U", "0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0", image);
                    Assert.True(changed == 0, $"tune2fs -U {name}: {said}");
                }
                var listed = Listed(debugfs, image, "/").Concat(spread ? [] : Listed(debugfs, image, "/sub")).ToList();
                // Generations and an i_version that are not zero, set the way debugfs sets fields (with the checksum written
                // again). Files only: a folder's generation also seeds its blocks' checksums, which sif does not rewrite.
                var files = listed.Where(e => (e.Mode & 0xF000) == 0x8000).Select(e => e.Inode).Distinct().ToList();
                bool large = !options.Contains("128");
                string commands = Path.Combine(dir, "set-commands.txt");
                File.WriteAllLines(commands, [$"sif <{files[0]}> generation 3141592653", $"sif <{files[0]}> version {(large ? "0x0000000123456789" : "0x89abcdef")}", $"sif <{files[1]}> generation 7"]);
                var (set, setOutput) = Run(debugfs, "-w", "-f", commands, image);
                Assert.True(set == 0 && !setOutput.Contains("rror", StringComparison.Ordinal), $"debugfs sif {name}: {setOutput}");
                // e2fsck finds every checksum good, so a computed checksum that matches agrees with e2fsprogs' own.
                var (fsck, fsckOutput) = Run(e2fsck, "-f", "-n", image);
                Assert.True(fsck == 0, $"e2fsck {name}: {fsckOutput}");

                var numbers = Enumerable.Range(1, 11).Select(n => (uint)n).Concat(listed.Select(e => e.Inode)).Distinct().Order().ToList();
                var stats = Stats(debugfs, image, numbers, dir);
                var inodes = new List<Ext4Inode>();
                foreach (uint number in numbers)
                {
                    Assert.True(stats.TryGetValue(number, out string? stat), $"{name}: debugfs said nothing of inode {number}");
                    var inode = ReadImage(image, number);
                    AssertAgrees(ParseStat(stat!), inode, name);
                    inodes.Add(inode);
                }
                checkedInodes += inodes.Count;
                TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {inodes.Count} inodes ({numbers[0]} to {numbers[^1]}) in block groups 0 to {inodes.Max(i => i.Group)}, "
                    + $"{inodes.Count(i => i.ChecksumMatches == true)} checksums matched, {inodes.Count(i => i.Version != 0)} with an i_version, {inodes.Count(i => i.Generation != 0)} with a generation");
                var set1 = inodes.Single(i => i.Number == files[0]);
                Assert.Equal(3_141_592_653u, set1.Generation);
                Assert.Equal(large ? 0x1_2345_6789UL : 0x89AB_CDEFUL, set1.Version);
                Assert.Equal(7u, inodes.Single(i => i.Number == files[1]).Generation);
                // The spread volumes reach past the first descriptor block of meta_bg (16 per 1 KiB block), or into the later groups.
                if (spread) Assert.Contains(inodes, i => i.Group >= (name.Contains("meta_bg", StringComparison.Ordinal) ? 16 : 4));
            }
            Assert.True(checkedInodes > 200, $"Only {checkedInodes} inodes were compared.");
        }
        finally { TestCleanup.DeleteTree(dir); }
    }

    [Fact]
    public void An_inode_changed_outside_ext4_fails_its_checksum_as_debugfs_says()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("mke2fs and debugfs run on Linux.");
        string? mke2fs = Tool("mke2fs"), debugfs = Tool("debugfs");
        if (mke2fs is null || debugfs is null)
        {
            Assert.Skip("e2fsprogs (mke2fs, debugfs) is not installed.");
            return;
        }
        string dir = Path.Combine(Path.GetTempPath(), "filecat-ext4-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(dir, "source")).FullName;
            File.WriteAllText(Path.Combine(source, "victim.txt"), "x");
            string image = Path.Combine(dir, "volume.img");
            var (made, output) = Run(mke2fs, "-t", "ext4", "-I", "256", "-O", "metadata_csum,64bit", "-q", "-F", "-d", source, image, "8M");
            Assert.True(made == 0, output);
            uint victim = Listed(debugfs, image, "/").Single(e => (e.Mode & 0xF000) == 0x8000).Inode;
            var before = ReadImage(image, victim);
            Assert.True(before.ChecksumMatches);
            // One byte of i_obso_faddr, which nothing reads, changed in place.
            using (var stream = new FileStream(image, FileMode.Open, FileAccess.ReadWrite))
            {
                stream.Position = before.Offset + 0x70;
                stream.WriteByte(0x5A);
            }
            var after = ReadImage(image, victim);
            Assert.False(after.ChecksumMatches);
            Assert.Equal(before.StoredChecksum, after.StoredChecksum);
            // debugfs checking checksums refuses the inode; told not to, it prints the same stored checksum.
            Assert.Contains("checksum does not match", Run(debugfs, "-R", $"stat <{victim}>", image).Output, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(after.StoredChecksum, ParseStat(Stats(debugfs, image, [victim], dir)[victim]).Checksum);
        }
        finally { TestCleanup.DeleteTree(dir); }
    }
}
