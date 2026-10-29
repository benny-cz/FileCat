using System.Buffers.Binary;
using System.Globalization;
using FileCat.Core.Inspect;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

/// <summary>
/// D-56: HFS+'s journal summary from synthetic volumes (journals of both byte orders, damaged and truncated ones),
/// checked against journal headers macOS itself wrote; and reads in whole sectors, as macOS's raw devices need them.
/// </summary>
public sealed class HfsJournalTests
{
    private const int BlockSize = 4096, TotalBlocks = 4096, InfoBlock = 2, JournalOffset = 12288, JournalSize = 524288;

    // Journal headers macOS 27 wrote on Apple silicon: newfs_hfs's on a new 16 MiB volume (start = end), and the
    // kernel's once the volume was mounted and written to (8,704 bytes of transactions). Their checksums are theirs.
    private static readonly byte[] NewfsHeader = Convert.FromHexString("784C4E4A7856341200440000000000000044000000000000000008000000000000200000DD52E708000200009A0F9600");
    private static readonly byte[] MountedHeader = Convert.FromHexString("784C4E4A7856341200440000000000000066000000000000000008000000000000200000BB924009000200009B0F9600");

    private static string Field(InspectionSection section, string name) => section.Fields.Single(f => f.Name == name).Value;

    private static Func<long, int, byte[]> Reader(byte[] image) =>
        (offset, count) => offset < 0 || offset >= image.Length ? [] : image.AsSpan((int)offset, (int)Math.Min(count, image.Length - offset)).ToArray();

    /// <summary>calc_checksum from xnu's vfs_journal.c, written out again here.</summary>
    private static uint XnuChecksum(ReadOnlySpan<byte> bytes)
    {
        uint cksum = 0;
        for (int i = 0; i < bytes.Length; i++) cksum = (cksum << 8) ^ (cksum + bytes[i]);
        return ~cksum;
    }

    /// <summary>A journaled HFS+ volume's first blocks: the volume header, the journal info block, and the journal header.</summary>
    private static byte[] Volume(byte[]? journalHeader = null, uint attributes = 0x8000_2000, uint infoFlags = 1, int at = 0)
    {
        var image = new byte[at + JournalOffset + 4096];
        int header = at + 1024;
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(header), 0x482B); // "H+"
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(header + 2), 4);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(header + 4), attributes);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(header + 8), 0x4846_534A); // "HFSJ"
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(header + 12), InfoBlock);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(header + 40), BlockSize);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(header + 44), TotalBlocks);
        int info = at + InfoBlock * BlockSize;
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(info), infoFlags);
        BinaryPrimitives.WriteUInt64BigEndian(image.AsSpan(info + 36), JournalOffset);
        BinaryPrimitives.WriteUInt64BigEndian(image.AsSpan(info + 44), JournalSize);
        (journalHeader ?? MountedHeader).CopyTo(image, at + JournalOffset);
        return image;
    }

    /// <summary>A journal header in either byte order, with its checksum.</summary>
    private static byte[] Header(bool big, long start, long end, uint sequence, long size = JournalSize, uint magic = 0x4A4E_4C78, uint endian = 0x1234_5678, int headerSize = 512, uint blockList = 8192)
    {
        var h = new byte[48];
        void U32(int at, uint value)
        {
            if (big) BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(at), value);
            else BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(at), value);
        }
        void I64(int at, long value)
        {
            if (big) BinaryPrimitives.WriteInt64BigEndian(h.AsSpan(at), value);
            else BinaryPrimitives.WriteInt64LittleEndian(h.AsSpan(at), value);
        }
        U32(0, magic);
        U32(4, endian);
        I64(8, start);
        I64(16, end);
        I64(24, size);
        U32(32, blockList);
        U32(40, (uint)headerSize);
        U32(44, sequence);
        U32(36, XnuChecksum(h.AsSpan(0, 44))); // over the header with the checksum still zero
        return h;
    }

    [Fact]
    public void Headers_macOS_wrote_check_with_xnus_checksum()
    {
        foreach (var (raw, stored) in new[] { (NewfsHeader, 0x08E7_52DDu), (MountedHeader, 0x0940_92BBu) })
        {
            Assert.Equal(stored, BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(36)));
            var zeroed = raw[..44];
            zeroed.AsSpan(36, 4).Clear();
            Assert.Equal(stored, XnuChecksum(zeroed));
            Assert.Equal(stored, HfsJournal.Checksum(zeroed));
        }
        var summary = HfsJournal.Read(Reader(Volume(NewfsHeader)));
        Assert.Empty(summary.Problems);
        var h = summary.Header!;
        Assert.False(h.BigEndian);
        Assert.True(h.ChecksumMatches);
        Assert.Equal((17408L, 17408L, 524288L, 512, 9_834_394u), (h.Start, h.End, h.Size, h.HeaderSize, h.Sequence));
        Assert.False(h.HasTransactions);
    }

    [Fact]
    public void The_section_says_where_the_journal_is_and_what_waits_in_it_to_be_replayed()
    {
        var summary = HfsJournal.Read(Reader(Volume()));
        Assert.Empty(summary.Problems);
        Assert.Equal("HFS+", summary.Kind);
        Assert.Equal(4, summary.Version);
        Assert.True(summary.Journaled);
        Assert.Equal((uint?)1, summary.InfoFlags);
        Assert.Equal((long?)JournalOffset, summary.JournalOffset);
        Assert.Equal((long?)JournalSize, summary.JournalSize);
        Assert.Equal((17408L, 26112L, 8704L, 9_834_395u), (summary.Header!.Start, summary.Header.End, summary.Header.Waiting, summary.Header.Sequence));

        var section = HfsJournal.Section(summary);
        Assert.Equal("Journal (HFS+)", section.Title);
        Assert.Equal($"HFS+ version 4 · allocation blocks of {RecordText.Bytes(4096)} · last mounted by “HFSJ” (Mac OS X or macOS, journaled)", Field(section, "Volume"));
        Assert.Equal("0x80002000: journaled, unused catalog nodes zeroed", Field(section, "Attributes"));
        Assert.Equal("yes (volume attribute bit 13)", Field(section, "Journaled"));
        Assert.Equal($"allocation block 2 (byte {8192:N0}) · flags 0x00000001: the journal is in this file system", Field(section, "Journal info block"));
        Assert.Equal($"byte {12288:N0} of the volume, {RecordText.Bytes(524288)}", Field(section, "Journal"));
        Assert.StartsWith("little-endian (written by an Intel or Apple silicon Mac) · magic “JNLx” · header block of 512 bytes", Field(section, "Header"), StringComparison.Ordinal);
        Assert.StartsWith($"{RecordText.Bytes(8704)} of transactions, from byte {17408:N0} to byte {26112:N0} of the journal: not yet marked as written in place", Field(section, "To replay"), StringComparison.Ordinal);
        Assert.Equal(9_834_395.ToString("N0", CultureInfo.CurrentCulture), Field(section, "Sequence number"));
        Assert.Equal("0x094092BB, correct", Field(section, "Checksum"));
        Assert.DoesNotContain(section.Lines, l => l.StartsWith('⚠'));

        var empty = HfsJournal.Describe(Reader(Volume(NewfsHeader)));
        Assert.Equal($"nothing: the journal is empty (its start and end are both byte {17408:N0})", Field(empty, "To replay"));
    }

    [Fact]
    public void A_big_endian_journal_reads_as_a_little_endian_one_does()
    {
        foreach (bool big in new[] { false, true })
        {
            // Transactions that wrap around the journal's end: from 500 KiB to its end, then from past its header to 4 KiB.
            var summary = HfsJournal.Read(Reader(Volume(Header(big, 512_000, 4096, 77))));
            Assert.Empty(summary.Problems);
            var h = summary.Header!;
            Assert.Equal(big, h.BigEndian);
            Assert.True(h.ChecksumMatches);
            Assert.Equal((512_000L, 4096L, (long)JournalSize, 77u), (h.Start, h.End, h.Size, h.Sequence));
            Assert.Equal(JournalSize - 512_000 + (4096 - 512), h.Waiting);
            var section = HfsJournal.Section(summary);
            Assert.StartsWith(big ? "big-endian (written by a PowerPC Mac)" : "little-endian", Field(section, "Header"), StringComparison.Ordinal);
            Assert.Contains("(around its end)", Field(section, "To replay"), StringComparison.Ordinal);
            Assert.EndsWith(", correct", Field(section, "Checksum"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_volume_without_a_journal_or_with_it_elsewhere_says_so()
    {
        var plain = HfsJournal.Read(Reader(Volume(attributes: 0x8000_0100)));
        Assert.Empty(plain.Problems);
        Assert.False(plain.Journaled);
        Assert.Null(plain.Header);
        var section = HfsJournal.Section(plain);
        Assert.Equal("no (volume attribute bit 13 is clear)", Field(section, "Journaled"));
        Assert.Equal("0x80000100: unmounted cleanly, unused catalog nodes zeroed", Field(section, "Attributes"));
        Assert.DoesNotContain(section.Fields, f => f.Name == "Journal");

        var image = Volume(infoFlags: 2);
        "8A2C4B1E-0F5D-4E7A-9C3B-2D6E8F1A7B40"u8.CopyTo(image.AsSpan(InfoBlock * BlockSize + 52));
        var elsewhere = HfsJournal.Read(Reader(image));
        Assert.Empty(elsewhere.Problems);
        Assert.Null(elsewhere.Header);
        Assert.Equal($"on another device (journal UUID 8A2C4B1E-0F5D-4E7A-9C3B-2D6E8F1A7B40), {RecordText.Bytes(JournalSize)}: its header is there, not on this volume",
            Field(HfsJournal.Section(elsewhere), "Journal"));

        var renewed = HfsJournal.Read(Reader(Volume(infoFlags: 5)));
        Assert.Empty(renewed.Problems);
        Assert.Null(renewed.Header);
        Assert.EndsWith("made anew at the next mount, so its header is not read", Field(HfsJournal.Section(renewed), "Journal"), StringComparison.Ordinal);
    }

    [Fact]
    public void An_HFS_plus_volume_in_an_HFS_wrapper_is_read_where_the_wrapper_puts_it()
    {
        // The wrapper's master directory block: allocation blocks of 4 KiB from sector 16, the embedded volume from its block 2.
        var image = Volume(at: 16384);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(1024), 0x4244); // "BD"
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(1024 + 20), 4096);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(1024 + 28), 16);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(1024 + 124), 0x482B);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(1024 + 126), 2);
        var summary = HfsJournal.Read(Reader(image));
        Assert.Empty(summary.Problems);
        Assert.Equal(16384, summary.VolumeOffset);
        Assert.True(summary.Header!.ChecksumMatches);
        Assert.Equal($"byte {16384 + 12288:N0} of the volume, {RecordText.Bytes(JournalSize)}", Field(HfsJournal.Section(summary), "Journal"));

        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(1024 + 124), 0);
        Assert.Contains("HFS (Mac OS Standard) volume, which keeps no journal", HfsJournal.Read(Reader(image)).Problems.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void Damaged_structures_end_with_a_line_saying_what_is_wrong()
    {
        string Problem(byte[] image) => string.Join(" ", HfsJournal.Read(Reader(image)).Problems);
        byte[] With(Action<byte[]> change, byte[]? header = null)
        {
            var image = Volume(header);
            change(image);
            return image;
        }
        Assert.Contains("signature is 0x5859", Problem(With(i => "XY"u8.CopyTo(i.AsSpan(1024)))), StringComparison.Ordinal);
        Assert.Contains("is not a power of two", Problem(With(i => BinaryPrimitives.WriteUInt32BigEndian(i.AsSpan(1064), 1000))), StringComparison.Ordinal);
        Assert.Contains("names no journal info block", Problem(With(i => BinaryPrimitives.WriteUInt32BigEndian(i.AsSpan(1036), 0))), StringComparison.Ordinal);
        Assert.Contains("lies past the volume's", Problem(With(i => BinaryPrimitives.WriteUInt32BigEndian(i.AsSpan(1036), TotalBlocks))), StringComparison.Ordinal);
        Assert.Contains("neither in this file system nor on another device", Problem(With(i => BinaryPrimitives.WriteUInt32BigEndian(i.AsSpan(InfoBlock * BlockSize), 0))), StringComparison.Ordinal);
        Assert.Contains("does not fit in the volume", Problem(With(i => BinaryPrimitives.WriteUInt64BigEndian(i.AsSpan(InfoBlock * BlockSize + 44), (ulong)TotalBlocks * BlockSize))), StringComparison.Ordinal);
        Assert.Contains("does not fit in the volume", Problem(With(i => BinaryPrimitives.WriteUInt64BigEndian(i.AsSpan(InfoBlock * BlockSize + 36), ulong.MaxValue))), StringComparison.Ordinal);
        Assert.Contains("in neither byte order", Problem(With(i => i[JournalOffset] ^= 0xFF)), StringComparison.Ordinal);
        Assert.Contains("endian field is 0x87654321", Problem(Volume(Header(false, 17408, 17408, 1, endian: 0x8765_4321))), StringComparison.Ordinal);
        Assert.Contains("lies outside the journal", Problem(Volume(Header(true, JournalSize + 512, 17408, 1))), StringComparison.Ordinal);
        Assert.Contains("lies outside the journal", Problem(Volume(Header(false, 17408, 0, 1))), StringComparison.Ordinal);
        Assert.Contains("is not a sector size", Problem(Volume(Header(false, 17408, 17408, 1, headerSize: 0))), StringComparison.Ordinal);
        Assert.Contains($"block list header size ({-1:N0} bytes) does not fit", Problem(Volume(Header(true, 17408, 17408, 1, blockList: uint.MaxValue))), StringComparison.Ordinal);
        Assert.Contains($"gives the journal {1_048_576:N0} bytes", Problem(Volume(Header(false, 17408, 17408, 1, size: 1_048_576))), StringComparison.Ordinal);

        // A header changed after its checksum was made: the checksum says so (the system only logs it).
        var changed = Volume();
        changed[JournalOffset + 17] ^= 0x01; // its end
        var summary = HfsJournal.Read(Reader(changed));
        Assert.False(summary.Header!.ChecksumMatches);
        Assert.StartsWith("0x094092BB, wrong: the header's bytes give 0x", Field(HfsJournal.Section(summary), "Checksum"), StringComparison.Ordinal);

        // The old "JHDR" magic: read, its checksum not checked.
        var old = HfsJournal.Read(Reader(Volume(Header(false, 17408, 17408, 1, magic: 0x4A48_4452))));
        Assert.True(old.Header!.OldStyle);
        Assert.Equal("not checked (an old-style header)", Field(HfsJournal.Section(old), "Checksum").Split(", ", 2)[1]);
    }

    [Fact]
    public void A_volume_cut_short_anywhere_or_unreadable_ends_with_a_reason()
    {
        var image = Volume();
        foreach (int length in new[] { 0, 1024, 1535, 1536, InfoBlock * BlockSize, InfoBlock * BlockSize + 88, JournalOffset, JournalOffset + 47 })
        {
            var summary = HfsJournal.Read(Reader(image[..length]));
            Assert.Matches(@"^(The .+ could not be read: the volume ends before it|Only \d+ of the .+ could be read: the volume ends inside it)\.$", Assert.Single(summary.Problems));
            Assert.NotNull(HfsJournal.Section(summary));
        }
        Assert.Empty(HfsJournal.Read(Reader(image[..(JournalOffset + 48)])).Problems);
        var failing = HfsJournal.Read((_, _) => throw new IOException("Input/output error"));
        Assert.Equal("The volume could not be read: Input/output error", Assert.Single(failing.Problems));
    }

    [Fact]
    public void Random_damage_never_throws()
    {
        var random = new Random(56);
        var good = Volume();
        int[] hot = [.. Enumerable.Range(1024, 48), .. Enumerable.Range(InfoBlock * BlockSize, 60), .. Enumerable.Range(JournalOffset, 48)];
        for (int round = 0; round < 3000; round++)
        {
            var image = (byte[])good.Clone();
            for (int n = random.Next(1, 6); n > 0; n--)
            {
                int at = random.Next(3) == 0 ? random.Next(image.Length) : hot[random.Next(hot.Length)];
                image[at] = (byte)random.Next(256);
            }
            var cut = random.Next(4) == 0 ? image[..random.Next(image.Length)] : image;
            var summary = HfsJournal.Read(Reader(cut));
            _ = HfsJournal.Section(summary);
        }
    }

    [Fact]
    public void Whole_sector_reads_give_any_range_and_stop_where_the_device_ends()
    {
        // 512-byte sectors, though not a whole number of 4 KiB units: the last aligned read comes back short.
        var data = new byte[3 * 1024 * 1024 + 1536];
        new Random(56).NextBytes(data);
        int reads = 0;
        int Sectors(long offset, Span<byte> buffer)
        {
            Assert.Equal(0, offset % 4096);
            Assert.Equal(0, buffer.Length % 4096);
            Assert.InRange(buffer.Length, 4096, 1 << 20);
            reads++;
            if (offset >= data.Length) return 0;
            int n = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }
        byte[] Read(long offset, int count) => UnixFileRecords.ReadCovering(Sectors, 4096, offset, count);
        Assert.Equal(data[..1], Read(0, 1));
        Assert.Equal(data[3..103], Read(3, 100));
        Assert.Equal(data[4095..4097], Read(4095, 2));
        Assert.Equal(data[1000..(1000 + 3 * 1024 * 1024)], Read(1000, 3 * 1024 * 1024));
        Assert.Equal(data[^10..], Read(data.Length - 10, 100));
        Assert.Equal(data[^1536..], Read(data.Length - 1536, 1 << 20));
        Assert.Empty(Read(data.Length, 10));
        Assert.Empty(Read(data.Length + 5000, 10));
        Assert.Empty(Read(-1, 10));
        Assert.Empty(Read(0, 0));
        Assert.Empty(Read(long.MaxValue - 5, 10));
        Assert.True(reads > 0);
        Assert.Throws<IOException>(() => UnixFileRecords.ReadCovering((_, _) => throw new IOException("Input/output error"), 4096, 0, 512));
    }

    [Theory]
    [InlineData("fat32", "Directory entry (FAT32)")]
    [InlineData("exfat", "Directory entry (exFAT)")]
    public void FAT_entries_read_the_same_through_whole_sector_reads(string fixture, string title)
    {
        var bytes = File.ReadAllBytes(RecoveryFixtures.Image(fixture));
        int Sectors(long offset, Span<byte> buffer)
        {
            Assert.Equal(0, offset % 4096);
            Assert.Equal(0, buffer.Length % 4096);
            if (offset >= bytes.Length) return 0;
            int n = (int)Math.Min(buffer.Length, bytes.Length - offset);
            bytes.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }
        foreach (string item in new[] { "keep.txt", "fill/zeros.bin" })
        {
            var whole = FatEntries.Describe((offset, count) => UnixFileRecords.ReadCovering(Sectors, 4096, offset, count), item, out string? problem);
            Assert.Null(problem);
            var direct = FatEntries.Describe(Reader(bytes), item, out _);
            Assert.Equal(title, whole!.Title);
            Assert.Equal(direct!.Fields, whole.Fields);
            Assert.Equal(direct.Lines, whole.Lines);
        }
    }
}
