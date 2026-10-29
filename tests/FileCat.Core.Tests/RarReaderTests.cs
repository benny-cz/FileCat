using System.Security.Cryptography;
using FileCat.Archives;
using FileCat.Archives.Rar;
using Xunit;

namespace FileCat.Core.Tests;

/// <summary>
/// FileCat's own RAR reader against the retired SharpCompress path on every RAR fixture: the same members, the same
/// refusals and byte-identical content (I14 / REL-RAR-LICENSE).
/// </summary>
public sealed class RarReaderTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives");

    public static TheoryData<string> Rar5Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.EnumerateFiles(Fixtures, "Rar5*.rar").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            var part = System.Text.RegularExpressions.Regex.Match(name, @"\.part(\d+)\.rar$");
            if (part.Success && part.Groups[1].Value.TrimStart('0') != "1") continue;
            data.Add(name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Rar5Fixtures))]
    public void Rar5_members_and_bytes_match_the_reference_reader(string fixture)
    {
        string path = Path.Combine(Fixtures, fixture);
        var reference = Snapshot(() => SharpArchiveReader.Open(path, rar: true));
        var ours = Snapshot(() => OpenOurs(path));
        Assert.Equal(reference.Error is null, ours.Error is null);
        if (reference.Error is not null)
        {
            Assert.IsType<InvalidDataException>(ours.Error);
            return;
        }
        Assert.Equal(reference.Members.Select(m => (m.Path.Replace('\\', '/'), m.Kind == MemberKind.Directory, m.Encrypted)),
            ours.Members.Select(m => (m.Path.Replace('\\', '/'), m.Kind == MemberKind.Directory, m.Encrypted)));
        Assert.Equal(reference.Contents, ours.Contents);
    }

    public static TheoryData<string> Rar4Fixtures() => new(Rar4FixtureNames());

    /// <summary>The first volume of every fixture not named for RAR 5 (some of them are RAR 5 archives all the same).</summary>
    private static IEnumerable<string> Rar4FixtureNames()
    {
        foreach (string path in Directory.EnumerateFiles(Fixtures).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            if (!name.StartsWith("Rar", StringComparison.Ordinal) || name.StartsWith("Rar5", StringComparison.Ordinal)) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\.part0*([2-9]|\d\d+)\.rar$|\.r\d\d$|\.\d\d\d$")) continue;
            if (!name.EndsWith(".rar", StringComparison.Ordinal) && !name.EndsWith(".001", StringComparison.Ordinal)) continue;
            yield return name;
        }
    }

    [Theory]
    [MemberData(nameof(Rar4Fixtures))]
    public void Rar4_listings_match_the_reference_reader(string fixture)
    {
        string path = Path.Combine(Fixtures, fixture);
        var reference = Listing(() => SharpArchiveReader.Open(path, rar: true));
        var ours = Listing(() => OpenOurs(path));
        Assert.Equal(reference.Error is null, ours.Error is null);
        if (reference.Error is not null) return;
        Assert.Equal(reference.Members.Select(m => (m.Path.Replace('\\', '/'), m.Kind == MemberKind.Directory, m.Encrypted, m.Kind == MemberKind.Directory ? 0 : m.Size)),
            ours.Members.Select(m => (m.Path.Replace('\\', '/'), m.Kind == MemberKind.Directory, m.Encrypted, m.Kind == MemberKind.Directory ? 0 : m.Size)));
    }

    /// <summary>RAR 1.5–4.x fixtures whose compressed members are RAR 2.9 (or that store, encrypt or damage them).</summary>
    public static TheoryData<string> Rar29Fixtures()
    {
        // RAR 1.5 and RAR 2.0 compression (unpack versions 15, 20 and 26) belong to their own decoders.
        string[] otherVersions = ["Rar15.rar", "Rar2.rar", "Rar2.multi.rar"];
        return new(Rar4FixtureNames().Where(name => !otherVersions.Contains(name)));
    }

    [Theory]
    [MemberData(nameof(Rar29Fixtures))]
    public void Rar29_members_and_bytes_match_the_reference_reader(string fixture)
    {
        string path = Path.Combine(Fixtures, fixture);
        var reference = Snapshot(() => SharpArchiveReader.Open(path, rar: true));
        var ours = Snapshot(() => OpenOurs(path));
        Assert.True(reference.Error is null == ours.Error is null, $"reference: {reference.Error?.Message ?? "ok"}; ours: {ours.Error?.Message ?? "ok"}");
        if (ours.Error is not null) Assert.IsType<InvalidDataException>(ours.Error);
        Assert.Equal(reference.Contents, ours.Contents);
    }

    [Fact]
    public void Rar4_stored_members_match_the_reference_bytes()
    {
        string path = Path.Combine(Fixtures, "Rar.none.rar");
        var reference = Snapshot(() => SharpArchiveReader.Open(path, rar: true));
        var ours = Snapshot(() => OpenOurs(path));
        Assert.Null(ours.Error);
        Assert.NotEmpty(ours.Contents);
        Assert.Equal(reference.Contents, ours.Contents);
    }

    [Theory]
    // Fixtures that must decode, with their content counts: a refusal on both sides must not pass silently. Their
    // members use LZ with the E8, delta and audio filters. Rar.multi.solid and Rar.issue1050 are RAR 5 archives
    // despite their names; Rar4.split.001 is a byte-split archive that neither reader joins, so both refuse it.
    [InlineData("Rar.rar", 3)]
    [InlineData("Rar4.rar", 3)]
    [InlineData("Rar.solid.rar", 3)]
    [InlineData("Rar.multi.part01.rar", 3)]
    [InlineData("Rar4.multi.part01.rar", 3)]
    [InlineData("Rar.comment.rar", 3)]
    [InlineData("Rar.Audio_program.rar", 1)]
    [InlineData("Rar.test_invalid_exttime.rar", 1)]
    [InlineData("Rar.none.rar", 3)]
    [InlineData("Rar.multi.solid.part01.rar", 3)]
    [InlineData("Rar.issue1050.rar", 18)]
    public void Rar29_fixtures_decode_completely(string fixture, int files)
    {
        var ours = Snapshot(() => OpenOurs(Path.Combine(Fixtures, fixture)));
        Assert.Null(ours.Error);
        Assert.Equal(files, ours.Contents.Count);
    }

    // ---- RAR 2.9 PPMd: no fixture holds PPMd data and current RAR versions no longer write RAR 4 archives, so these
    // archives are made here (Rar3PpmdTestStream) and SharpCompress, an independent PPMd-H, is the reference. ----

    [Theory]
    [InlineData(2, 1, 20_000, 1)]
    [InlineData(4, 1, 1_500_000, 2)] // fills a 1 MiB model again and again: restarts, gluing and splitting free blocks
    [InlineData(6, 2, 400_000, 3)]
    [InlineData(8, 1, 900_000, 4)]
    [InlineData(16, 8, 300_000, 5)]
    [InlineData(19, 4, 200_000, 6)]
    [InlineData(64, 16, 200_000, 7)]
    public void Rar29_ppmd_members_match_the_reference_reader(int order, int megabytes, int size, int seed)
    {
        byte[] content = SampleText(size, seed);
        var stream = new Rar3PpmdTestStream();
        stream.StartBlock(order, megabytes, reset: true);
        stream.Literals(content);
        stream.EndFile();
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: false, ("text.txt", content, stream.ToArray()));
        AssertBothDecode(path, content);
    }

    [Theory]
    // Incompressible data fills a 1 MiB model about every 50 KB: each run restarts the model a dozen times and glues
    // and splits free blocks thousands of times.
    [InlineData(3, 81)]
    [InlineData(64, 82)]
    public void Rar29_ppmd_model_restarts_match_the_reference_reader(int order, int seed)
    {
        byte[] content = RandomBytes(new Random(seed), 600_000);
        var stream = new Rar3PpmdTestStream();
        stream.StartBlock(order, 1, reset: true);
        stream.Literals(content);
        stream.EndFile();
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: false, ("noise.bin", content, stream.ToArray()));
        AssertBothDecode(path, content);
    }

    [Fact]
    public void Rar29_ppmd_escapes_and_blocks_match_the_reference_reader()
    {
        var expected = new List<byte>();
        var stream = new Rar3PpmdTestStream();
        stream.StartBlock(6, 1, reset: true);
        Add(SampleText(5000, 11));
        Add([2, 2, 0, 2, 255, 2]); // the escape byte itself, coded as escape 1
        stream.Run(100);
        expected.AddRange(Enumerable.Repeat(expected[^1], 100));
        stream.Run(259);
        expected.AddRange(Enumerable.Repeat(expected[^1], 259));
        Match(200, 1234);
        Match(32, 2);
        Match(287, expected.Count);
        stream.EndBlock();
        // A block that continues the model with another escape byte, then one that resets it.
        stream.StartBlock(6, 1, reset: false, escape: 0xE5);
        Add([0xE5, 1, 2, 3, 0xE5, 0xE5]);
        Add(SampleText(3000, 12));
        Match(100, 777);
        stream.EndBlock();
        stream.StartBlock(3, 2, reset: true);
        Add(SampleText(4000, 13));
        stream.Run(4);
        expected.AddRange(Enumerable.Repeat(expected[^1], 4));
        stream.EndFile();

        using var dir = new TempDir();
        byte[] content = [.. expected];
        string path = WriteRar29Archive(dir, solid: false, ("escapes.bin", content, stream.ToArray()));
        AssertBothDecode(path, content);

        void Add(byte[] bytes)
        {
            stream.Literals(bytes);
            expected.AddRange(bytes);
        }

        void Match(int length, int distance)
        {
            stream.Match(length, distance);
            for (int i = 0; i < length; i++) expected.Add(expected[^distance]);
        }
    }

    [Fact]
    public void Rar29_solid_ppmd_members_continue_the_model()
    {
        byte[] first = SampleText(120_000, 21), second = SampleText(90_000, 22), third = SampleText(50_000, 23);
        var stream = new Rar3PpmdTestStream();
        stream.StartBlock(8, 4, reset: true);
        stream.Literals(first);
        stream.EndFile();
        byte[] packedFirst = stream.Take();
        stream.StartBlock(8, 4, reset: false); // no reset: the second member depends on the first one's model
        stream.Literals(second);
        stream.EndFile();
        byte[] packedSecond = stream.Take();
        stream.StartBlock(5, 1, reset: true);
        stream.Literals(third);
        stream.EndFile();
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: true,
            ("a.txt", first, packedFirst), ("b.txt", second, packedSecond), ("c.txt", third, stream.ToArray()));
        AssertBothDecode(path, first, second, third);
        // A solid member opened on its own decodes its predecessors first; the last one alone, then all of them again.
        using var ours = OpenOurs(path);
        using (var content = ours.Open(2, CancellationToken.None)) Assert.Equal(third, ReadAll(content));
        using (var content = ours.Open(1, CancellationToken.None)) Assert.Equal(second, ReadAll(content));
    }

    [Theory]
    // The window asks the decoder to stop once 1 MiB is waiting; a member ending right there must still have its
    // end-of-file marker read, or the next member would start out of step.
    [InlineData((1 << 20) - 1)]
    [InlineData(1 << 20)]
    [InlineData((1 << 20) + 1)]
    public void Rar29_solid_lz_member_ending_where_the_window_stops_continues_the_block(int firstSize)
    {
        var lengths = new byte[LzTableSize];
        lengths.AsSpan(0, 257).Fill(9); // every literal and the end-of-block symbol
        var codes = CanonicalCodes(lengths.AsSpan(0, 299));
        byte[] first = RandomBytes(new Random(firstSize), firstSize), second = SampleText(70_000, 91);
        var bits = new BitWriter();
        bits.WriteLzTables(lengths);
        foreach (byte b in first) bits.Write(codes[b]);
        bits.Write(codes[256]);
        bits.Write(0b00, 2); // end of file: the next member continues this block with these tables
        byte[] packedFirst = bits.ToArray();
        bits = new BitWriter();
        foreach (byte b in second) bits.Write(codes[b]);
        bits.Write(codes[256]);
        bits.Write(0b01, 2);
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: true, ("a.bin", first, packedFirst), ("b.txt", second, bits.ToArray()));
        AssertBothDecode(path, first, second);
    }

    [Fact]
    public void Rar29_solid_ppmd_member_ending_where_the_window_stops_continues_the_model()
    {
        byte[] first = SampleText(1 << 20, 92), second = SampleText(60_000, 93);
        var stream = new Rar3PpmdTestStream();
        stream.StartBlock(4, 2, reset: true);
        stream.Literals(first);
        stream.EndFile();
        byte[] packedFirst = stream.Take();
        stream.StartBlock(4, 2, reset: false);
        stream.Literals(second);
        stream.EndFile();
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: true, ("a.txt", first, packedFirst), ("b.txt", second, stream.ToArray()));
        AssertBothDecode(path, first, second);
    }

    [Fact]
    public void Rar29_empty_solid_members_read_their_end_markers()
    {
        // An empty member's packed data is still its end-of-file marker, which the next member depends on: for PPMd
        // the escape codes update the model; for LZ "end of block and file" means the next member brings new tables.
        byte[] first = SampleText(30_000, 101), third = SampleText(20_000, 102);
        var stream = new Rar3PpmdTestStream();
        stream.StartBlock(6, 2, reset: true);
        stream.Literals(first);
        stream.EndFile();
        byte[] p1 = stream.Take();
        stream.StartBlock(6, 2, reset: false);
        stream.EndFile();
        byte[] p2 = stream.Take();
        stream.StartBlock(6, 2, reset: false);
        stream.Literals(third);
        stream.EndFile();
        using (var dir = new TempDir())
        {
            string path = WriteRar29Archive(dir, solid: true, ("a.txt", first, p1), ("empty.txt", [], p2), ("c.txt", third, stream.ToArray()));
            AssertBothDecode(path, first, [], third);
        }

        var lengths = new byte[LzTableSize];
        lengths.AsSpan(0, 257).Fill(9);
        var codes = CanonicalCodes(lengths.AsSpan(0, 299));
        var bits = new BitWriter();
        bits.WriteLzTables(lengths);
        foreach (byte b in first) bits.Write(codes[b]);
        bits.Write(codes[256]);
        bits.Write(0b00, 2); // end of file: the next member continues the block
        byte[] l1 = bits.ToArray();
        bits = new BitWriter();
        bits.Write(codes[256]);
        bits.Write(0b01, 2); // the empty member: end of block and file
        byte[] l2 = bits.ToArray();
        bits = new BitWriter();
        lengths[256] = 8; // new tables, so the codes change
        lengths['z'] = 0;
        bits.WriteLzTables(lengths);
        codes = CanonicalCodes(lengths.AsSpan(0, 299));
        byte[] lzThird = [.. third.Where(b => b != 'z')];
        foreach (byte b in lzThird) bits.Write(codes[b]);
        bits.Write(codes[256]);
        bits.Write(0b01, 2);
        using (var dir = new TempDir())
        {
            string path = WriteRar29Archive(dir, solid: true, ("a.txt", first, l1), ("empty.txt", [], l2), ("c.txt", lzThird, bits.ToArray()));
            AssertBothDecode(path, first, [], lzThird);
        }
    }

    [Fact]
    public void Rar29_empty_solid_member_without_packed_data_leaves_the_state_alone()
    {
        // No data at all: nothing to read, so the run goes on unchanged. (SharpCompress refuses the next member here;
        // rardecode, which drains every solid member to its marker, would too.)
        byte[] first = SampleText(30_000, 103), third = SampleText(20_000, 104);
        var stream = new Rar3PpmdTestStream();
        stream.StartBlock(6, 2, reset: true);
        stream.Literals(first);
        stream.EndFile();
        byte[] p1 = stream.Take();
        stream.StartBlock(6, 2, reset: false);
        stream.Literals(third);
        stream.EndFile();
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: true, ("a.txt", first, p1), ("empty.txt", [], []), ("c.txt", third, stream.ToArray()));
        var ours = Snapshot(() => OpenOurs(path));
        Assert.Null(ours.Error);
        Assert.Equal([Hash(first), Hash([]), Hash(third)], ours.Contents.Select(c => c.Hash));
    }

    [Fact]
    public void Rar29_standard_filters_in_ppmd_data_match_the_reference_reader()
    {
        var stream = new Rar3PpmdTestStream();
        stream.StartBlock(4, 2, reset: true);
        var random = new Random(31);
        // Slot 0: delta over 3 channels.
        stream.Filter(FilterRecord(0x80 | 0x20 | 0x10, bits =>
        {
            bits.WriteRar3UInt32(0);    // slot number 0: forget earlier slots, take a new one
            bits.WriteRar3UInt32(0);    // block offset
            bits.WriteRar3UInt32(3000); // block length
            bits.Write(1, 7);           // registers: R0
            bits.WriteRar3UInt32(3);
            bits.WriteProgram(DeltaProgram);
        }));
        stream.Literals(RandomBytes(random, 3000));
        // Slot 1: E8 call translation, starting 40 bytes ahead.
        stream.Filter(FilterRecord(0x80 | 0x20, bits =>
        {
            bits.WriteRar3UInt32(2);
            bits.WriteRar3UInt32(40);
            bits.WriteRar3UInt32(5000);
            bits.WriteProgram(E8Program);
        }));
        stream.Literals(SampleText(40, 32));
        stream.Literals(E8Sample(random, 5000));
        // Slot 2: audio over 2 channels, at offset 258 + 2 (flag 0x40).
        stream.Filter(FilterRecord(0x80 | 0x40 | 0x20 | 0x10, bits =>
        {
            bits.WriteRar3UInt32(3);
            bits.WriteRar3UInt32(2);
            bits.WriteRar3UInt32(4000);
            bits.Write(1, 7);
            bits.WriteRar3UInt32(2);
            bits.WriteProgram(AudioProgram);
        }));
        stream.Literals(SampleText(260, 33));
        stream.Literals(RandomBytes(random, 4000));
        // Slot 0 again: no program and no length (the slot's last one), 4 channels this time.
        stream.Filter(FilterRecord(0x80 | 0x10, bits =>
        {
            bits.WriteRar3UInt32(1);
            bits.WriteRar3UInt32(0);
            bits.Write(1, 7);
            bits.WriteRar3UInt32(4);
        }));
        stream.Literals(RandomBytes(random, 3000));
        // Without a slot number: the previous filter's slot (0) again.
        stream.Filter(FilterRecord(0x20 | 0x10, bits =>
        {
            bits.WriteRar3UInt32(10);
            bits.WriteRar3UInt32(500);
            bits.Write(1, 7);
            bits.WriteRar3UInt32(5);
        }));
        stream.Literals(RandomBytes(random, 2000));
        stream.EndFile();
        byte[] packed = stream.ToArray();
        // The filtered content is what the reference says it is: decode once for the header's size and checksum,
        // then both readers must agree on the archive that records them.
        byte[] content = DecodeWithoutChecksum(packed, 3000 + 40 + 5000 + 260 + 4000 + 3000 + 2000);
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: false, ("filtered.bin", content, packed));
        AssertBothDecode(path, content);
    }

    [Fact]
    public void Rar29_filter_global_data_is_read_and_ignored()
    {
        // The standard filters take only registers. SharpCompress throws on any record with global data (flag 0x08),
        // so the reference here is the same stream without it.
        byte[] withGlobal = Stream(global: true), without = Stream(global: false);
        byte[] content = DecodeWithoutChecksum(without, 1000 + 3000);
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: false, ("plain.bin", content, without));
        AssertBothDecode(path, content);
        Assert.Equal(content, DecodeWithoutChecksum(withGlobal, content.Length));

        static byte[] Stream(bool global)
        {
            var stream = new Rar3PpmdTestStream();
            stream.StartBlock(5, 1, reset: true);
            stream.Literals(SampleText(1000, 81));
            stream.Filter(FilterRecord(0x80 | 0x20 | 0x10 | (global ? 0x08 : 0), bits =>
            {
                bits.WriteRar3UInt32(0);
                bits.WriteRar3UInt32(0);
                bits.WriteRar3UInt32(3000);
                bits.Write(1, 7);
                bits.WriteRar3UInt32(2);
                bits.WriteProgram(DeltaProgram);
                if (!global) return;
                bits.WriteRar3UInt32(300);
                for (int i = 0; i < 300; i++) bits.Write((uint)i, 8);
            }));
            stream.Literals(RandomBytes(new Random(82), 3000));
            stream.EndFile();
            return stream.ToArray();
        }
    }

    [Fact]
    public void Rar29_lz_repeat_before_any_match_does_nothing()
    {
        var lengths = new byte[LzTableSize];
        lengths['A'] = 2;
        lengths[256] = 2;
        lengths[258] = 2;
        var codes = CanonicalCodes(lengths.AsSpan(0, 299));
        var bits = new BitWriter();
        bits.WriteLzTables(lengths);
        bits.Write(codes[258]);
        bits.Write(codes['A']);
        bits.Write(codes[258]);
        bits.Write(codes[256]);
        bits.Write(0b01, 2); // end of block and file
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: false, ("a.txt", "A"u8.ToArray(), bits.ToArray()));
        AssertBothDecode(path, "A"u8.ToArray());
    }

    // ---- Damaged and hostile RAR 2.9 data: every case ends in InvalidDataException, within ten seconds ----

    [Fact]
    public async Task Rar29_garbage_after_a_valid_lz_block_header_is_refused()
    {
        var lengths = new byte[LzTableSize];
        lengths.AsSpan(0, 299).Fill(9);
        lengths.AsSpan(299, 60).Fill(6);
        lengths.AsSpan(359, 17).Fill(5);
        lengths.AsSpan(376, 28).Fill(5);
        for (int seed = 0; seed < 40; seed++)
        {
            var bits = new BitWriter();
            bits.WriteLzTables(lengths);
            byte[] packed = [.. bits.ToArray(), .. RandomBytes(new Random(seed), 4000)];
            using var dir = new TempDir();
            string path = WriteRar29Archive(dir, solid: false, ("garbage.bin", new byte[100_000], packed));
            Assert.IsType<InvalidDataException>((await DecodeOursWithinTenSeconds(path)).Error);
        }
    }

    [Fact]
    public async Task Rar29_garbage_after_a_valid_ppmd_block_header_is_refused()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            byte[] packed = [0x80 | 0x20 | 5, 0, .. RandomBytes(new Random(seed), 3000)];
            using var dir = new TempDir();
            string path = WriteRar29Archive(dir, solid: false, ("garbage.bin", new byte[100_000], packed));
            Assert.IsType<InvalidDataException>((await DecodeOursWithinTenSeconds(path)).Error);
        }
    }

    [Fact]
    public async Task Rar29_truncated_lz_data_is_refused()
    {
        var (content, packed) = FixtureMember("Rar.rar", "test.exe");
        foreach (int keep in new[] { 1, 2, 100, packed.Length / 2, packed.Length - 50 })
        {
            using var dir = new TempDir();
            string path = WriteRar29Archive(dir, solid: false, ("test.exe", content, packed[..keep]));
            Assert.IsType<InvalidDataException>((await DecodeOursWithinTenSeconds(path)).Error);
        }
        // Cut at the very end, a member may lose only its end-of-file marker: like rardecode and UnRAR, FileCat then
        // stops where the data does, and the size and checksum decide.
        for (int cut = 1; cut <= 6; cut++)
        {
            using var dir = new TempDir();
            string path = WriteRar29Archive(dir, solid: false, ("test.exe", content, packed[..^cut]));
            var result = await DecodeOursWithinTenSeconds(path);
            if (result.Error is null) Assert.Equal(Hash(content), result.Contents.Single().Hash);
            else Assert.IsType<InvalidDataException>(result.Error);
        }
    }

    [Fact]
    public async Task Rar29_truncated_ppmd_data_is_refused()
    {
        byte[] content = SampleText(50_000, 41);
        var stream = new Rar3PpmdTestStream();
        stream.StartBlock(6, 1, reset: true);
        stream.Literals(content);
        stream.EndFile();
        byte[] packed = stream.ToArray();
        foreach (int keep in new[] { 1, 2, 5, 100, packed.Length / 2, packed.Length - 5 })
        {
            using var dir = new TempDir();
            string path = WriteRar29Archive(dir, solid: false, ("text.txt", content, packed[..keep]));
            Assert.IsType<InvalidDataException>((await DecodeOursWithinTenSeconds(path)).Error);
        }
    }

    [Fact]
    public async Task Rar29_ppmd_block_claiming_256_MiB_then_ending_is_refused()
    {
        // Order 6, reset, 256 MiB, then only the range coder's four bytes: the model is set up and the data ends.
        byte[] packed = [0x80 | 0x20 | 5, 255, 0, 0, 0, 0];
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: false, ("big.bin", new byte[1000], packed));
        var error = (await DecodeOursWithinTenSeconds(path)).Error;
        Assert.IsType<InvalidDataException>(error);
        Assert.Contains("ends early", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rar29_ppmd_block_continuing_a_missing_model_is_refused()
    {
        byte[] packed = [0x80 | 5, 1, 2, 3, 4, 5, 6, 7, 8];
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: false, ("x.bin", new byte[1000], packed));
        var error = (await DecodeOursWithinTenSeconds(path)).Error;
        Assert.IsType<InvalidDataException>(error);
        Assert.Contains("never set up", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rar29_unknown_filter_program_is_refused()
    {
        // A well-formed program (its checksum byte is the XOR of the rest) that is none of RAR's standard filters.
        byte[] program = [0, 0x12, 0x34, 0x56, 0x78, 0x9A];
        program[0] = (byte)(program[1] ^ program[2] ^ program[3] ^ program[4] ^ program[5]);
        var stream = new Rar3PpmdTestStream();
        stream.StartBlock(4, 1, reset: true);
        stream.Literals(SampleText(100, 51));
        stream.Filter(FilterRecord(0x80 | 0x20, bits =>
        {
            bits.WriteRar3UInt32(0);
            bits.WriteRar3UInt32(0);
            bits.WriteRar3UInt32(1000);
            bits.WriteProgram(program);
        }));
        stream.Literals(new byte[1000]);
        stream.EndFile();
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: false, ("x.bin", new byte[1100], stream.ToArray()));
        var error = (await DecodeOursWithinTenSeconds(path)).Error;
        Assert.IsType<InvalidDataException>(error);
        Assert.Contains("not one of RAR's standard filters", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rar29_filter_slot_number_out_of_range_is_refused()
    {
        // LZ data: symbol 257 with a record that names slot 4 while no slot exists yet.
        var lengths = new byte[LzTableSize];
        lengths['x'] = 1;
        lengths[257] = 1;
        var codes = CanonicalCodes(lengths.AsSpan(0, 299));
        var bits = new BitWriter();
        bits.WriteLzTables(lengths);
        bits.Write(codes['x']);
        bits.Write(codes[257]);
        foreach (byte b in FilterRecord(0x80 | 0x20, record =>
        {
            record.WriteRar3UInt32(5); // slot 4
            record.WriteRar3UInt32(0);
            record.WriteRar3UInt32(100);
            record.WriteProgram(DeltaProgram);
        }))
        {
            bits.Write(b, 8);
        }
        bits.Write(0, 8);
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: false, ("x.bin", new byte[200], bits.ToArray()));
        var error = (await DecodeOursWithinTenSeconds(path)).Error;
        Assert.IsType<InvalidDataException>(error);
        Assert.Contains("filter number", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rar29_delta_filter_without_channels_is_refused()
    {
        var stream = new Rar3PpmdTestStream();
        stream.StartBlock(4, 1, reset: true);
        stream.Filter(FilterRecord(0x80 | 0x20, bits =>
        {
            bits.WriteRar3UInt32(0);
            bits.WriteRar3UInt32(0);
            bits.WriteRar3UInt32(500);
            bits.WriteProgram(DeltaProgram); // no registers: R0, the channel count, is 0
        }));
        stream.Literals(new byte[500]);
        stream.EndFile();
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: false, ("x.bin", new byte[500], stream.ToArray()));
        var error = (await DecodeOursWithinTenSeconds(path)).Error;
        Assert.IsType<InvalidDataException>(error);
        Assert.Contains("channel count", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rar29_repeat_match_without_an_earlier_match_is_refused()
    {
        // Symbol 259 reuses the last distance, of which there is none yet.
        var lengths = new byte[LzTableSize];
        lengths[256] = 1;
        lengths[259] = 1;
        lengths[376] = 1; // length code: slot 0
        var codes = CanonicalCodes(lengths.AsSpan(0, 299));
        var bits = new BitWriter();
        bits.WriteLzTables(lengths);
        bits.Write(codes[259]);
        bits.Write(0, 1); // length slot 0: two bytes
        bits.Write(codes[256]);
        bits.Write(0b01, 2);
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: false, ("x.bin", new byte[2], bits.ToArray()));
        var error = (await DecodeOursWithinTenSeconds(path)).Error;
        Assert.IsType<InvalidDataException>(error);
        Assert.Contains("match distance", error!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rar29_over_subscribed_code_table_is_refused(bool mainTable)
    {
        var bits = new BitWriter();
        if (mainTable)
        {
            var lengths = new byte[LzTableSize];
            lengths[0] = lengths[1] = lengths[2] = 1; // three one-bit codes
            bits.WriteLzTables(lengths);
        }
        else
        {
            bits.Write(0, 2);                         // LZ, new tables
            for (int i = 0; i < 20; i++) bits.Write(1, 4); // twenty one-bit codes for the code lengths
        }
        bits.Write(0, 32);
        using var dir = new TempDir();
        string path = WriteRar29Archive(dir, solid: false, ("x.bin", new byte[10], bits.ToArray()));
        var error = (await DecodeOursWithinTenSeconds(path)).Error;
        Assert.IsType<InvalidDataException>(error);
        Assert.Contains("over-subscribed", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rar29_damaged_ppmd_data_never_escapes_as_another_exception()
    {
        // Byte damage anywhere after the block header: a data error, or (damage the coder never reads) the exact content.
        byte[] content = SampleText(60_000, 61);
        var stream = new Rar3PpmdTestStream();
        stream.StartBlock(8, 1, reset: true);
        stream.Literals(content);
        stream.EndFile();
        byte[] packed = stream.ToArray();
        var random = new Random(62);
        for (int round = 0; round < 60; round++)
        {
            byte[] damaged = (byte[])packed.Clone();
            for (int i = random.Next(1, 4); i > 0; i--) damaged[random.Next(2, damaged.Length)] ^= (byte)random.Next(1, 256);
            using var dir = new TempDir();
            string path = WriteRar29Archive(dir, solid: false, ("text.txt", content, damaged));
            var result = await DecodeOursWithinTenSeconds(path);
            if (result.Error is null) Assert.Equal(Hash(content), result.Contents.Single().Hash);
            else Assert.IsType<InvalidDataException>(result.Error);
        }
    }

    [Fact]
    public async Task Rar29_damaged_lz_data_never_escapes_as_another_exception()
    {
        var (content, packed) = FixtureMember("Rar.rar", "test.exe");
        var random = new Random(71);
        for (int round = 0; round < 60; round++)
        {
            byte[] damaged = (byte[])packed.Clone();
            for (int i = random.Next(1, 4); i > 0; i--) damaged[random.Next(damaged.Length)] ^= (byte)random.Next(1, 256);
            using var dir = new TempDir();
            string path = WriteRar29Archive(dir, solid: false, ("test.exe", content, damaged));
            var result = await DecodeOursWithinTenSeconds(path);
            if (result.Error is null) Assert.Equal(Hash(content), result.Contents.Single().Hash);
            else Assert.IsType<InvalidDataException>(result.Error);
        }
    }

    // ---- RAR 2.9 helpers ----

    private const int LzTableSize = 299 + 60 + 17 + 28;

    // RAR's standard filter programs, as RAR 3 archives carry them (captured from this suite's fixtures:
    // Rar.Audio_program.rar holds delta and audio, Rar.rar's test.exe holds E8).
    private static readonly byte[] DeltaProgram = Convert.FromHexString("2F019A4180EC27482F09766DD3EA415B5944E8175CE16C914C4E3F7700");

    private static readonly byte[] E8Program = Convert.FromHexString(
        "971B012807069808000000D13A101592EC50CB9920B925F0291915530312AE511035592B6004156D4066AB023449043602523E9700");

    private static readonly byte[] AudioProgram = Convert.FromHexString(
        "47019A4195E5720DC26482749324B14006D8384400A8013411DCA1BA01990CC4033119A4066622604D9A400D668E60D030401826C1C8" +
        "F6E62613789208E850BC5A07C6E9F520A9A0ED3733473966907019A39BCF258380C1BD30166E233493811609B050183B4DC84C059B88" +
        "C528E0769390980B37118A59C480424843A947EE43346047D44A0DBBD359A486EE05094026C9342476A0306A20EA022004A041509E50" +
        "3FE6E128944601BD8B40F0681136C9A1923811419CA89510EE50662B00209511040262AC668C6ACA2640B2671B4B26CC648A6271A2B8");

    private static string Hash(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static void AssertBothDecode(string path, params byte[][] contents)
    {
        var expected = contents.Select(Hash).ToList();
        var reference = Snapshot(() => SharpArchiveReader.Open(path, rar: true));
        Assert.Null(reference.Error);
        Assert.Equal(expected, reference.Contents.Select(c => c.Hash).ToList());
        var ours = Snapshot(() => OpenOurs(path));
        Assert.Null(ours.Error);
        Assert.Equal(expected, ours.Contents.Select(c => c.Hash).ToList());
    }

    private static async Task<ArchiveSnapshot> DecodeOursWithinTenSeconds(string path)
    {
        var decode = Task.Run(() => Snapshot(() => OpenOurs(path)));
        return await decode.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    /// <summary>A fixture member's content (from the reference reader) and packed data.</summary>
    private static (byte[] Content, byte[] Packed) FixtureMember(string fixture, string nameEnd)
    {
        string path = Path.Combine(Fixtures, fixture);
        using var file = File.OpenRead(path);
        var member = RarArchive.Read([file], CancellationToken.None).Members.First(m => m.Name.EndsWith(nameEnd, StringComparison.Ordinal));
        var segment = member.Segments.Single();
        var packed = new byte[segment.Length];
        file.Position = segment.Offset;
        file.ReadExactly(packed);
        using var reader = SharpArchiveReader.Open(path, rar: true);
        var info = reader.List(_ => { }, CancellationToken.None).First(m => m.Path.Replace('\\', '/').EndsWith(nameEnd, StringComparison.Ordinal));
        using var content = reader.Open(info.Index, CancellationToken.None);
        return (ReadAll(content), packed);
    }

    /// <summary>Decodes one non-solid RAR 2.9 member with FileCat's decoder alone, without a checksum to meet.</summary>
    private static byte[] DecodeWithoutChecksum(byte[] packed, int size)
    {
        var bits = new RarBitReader();
        bits.Reset(new MemoryStream(packed));
        var decoder = new Rar3Decoder();
        decoder.StartMember(bits, solid: false);
        var member = new RarMember { Name = "x", Kind = MemberKind.File, UnpackedSize = size, Method = 3, Version = 29, Dictionary = 4 << 20 };
        return ReadAll(RarMemberStream.Decoded(member, decoder, new RarWindow(4 << 20, size), CancellationToken.None));
    }

    /// <summary>Text-like data from a seeded vocabulary, with some binary noise (escape bytes included).</summary>
    private static byte[] SampleText(int size, int seed)
    {
        var random = new Random(seed);
        var words = new byte[600][];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = new byte[random.Next(1, 11)];
            for (int j = 0; j < words[i].Length; j++) words[i][j] = (byte)('a' + random.Next(26));
        }
        var text = new List<byte>(size);
        while (text.Count < size)
        {
            int roll = random.Next(100);
            if (roll < 3) text.AddRange(RandomBytes(random, random.Next(1, 40)));
            else if (roll < 6) text.Add((byte)'\n');
            else if (roll < 10) text.AddRange(", "u8.ToArray());
            else text.AddRange(words[(int)Math.Pow(random.Next(1, 600 * 600), 0.5)]);
            text.Add((byte)' ');
        }
        return [.. text.Take(size)];
    }

    private static byte[] RandomBytes(Random random, int count)
    {
        var bytes = new byte[count];
        random.NextBytes(bytes);
        return bytes;
    }

    /// <summary>Data with x86 call and jump opcodes followed by plausible relative addresses.</summary>
    private static byte[] E8Sample(Random random, int count)
    {
        var bytes = RandomBytes(random, count);
        for (int i = 0; i + 5 <= count; i += random.Next(5, 30))
        {
            bytes[i] = random.Next(2) == 0 ? (byte)0xE8 : (byte)0xE9;
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i + 1), random.Next(-100_000, 100_000));
        }
        return bytes;
    }

    /// <summary>A filter record as an LZ symbol 257 or a PPMd escape 3 carries it: flags with the length, then the fields.</summary>
    private static byte[] FilterRecord(int flags, Action<BitWriter> fields)
    {
        var bits = new BitWriter();
        fields(bits);
        byte[] data = bits.ToArray();
        if (data.Length <= 6) return [(byte)(flags | (data.Length - 1)), .. data];
        if (data.Length - 7 <= 255) return [(byte)(flags | 6), (byte)(data.Length - 7), .. data];
        return [(byte)(flags | 7), (byte)(data.Length >> 8), (byte)data.Length, .. data];
    }

    /// <summary>Canonical codes of RAR's Huffman tables: by length, then symbol.</summary>
    private static (uint Code, int Length)[] CanonicalCodes(ReadOnlySpan<byte> lengths)
    {
        var codes = new (uint, int)[lengths.Length];
        uint code = 0;
        for (int length = 1; length <= 15; length++, code <<= 1)
        {
            for (int symbol = 0; symbol < lengths.Length; symbol++)
            {
                if (lengths[symbol] == length) codes[symbol] = (code++, length);
            }
        }
        return codes;
    }

    /// <summary>A RAR 4 archive of RAR 2.9-compressed members (unpack version 29, 4 MiB dictionary).</summary>
    private static string WriteRar29Archive(TempDir dir, bool solid, params (string Name, byte[] Content, byte[] Packed)[] members)
    {
        var archive = new MemoryStream();
        archive.Write("Rar!\x1A\x07\x00"u8);
        WriteRar4Block(archive, 0x73, (ushort)(solid ? 0x0008 : 0), new byte[6]);
        for (int i = 0; i < members.Length; i++)
        {
            var (name, content, packed) = members[i];
            var body = new MemoryStream();
            var writer = new BinaryWriter(body);
            writer.Write((uint)packed.Length);
            writer.Write((uint)content.Length);
            writer.Write((byte)2);                                // Windows
            writer.Write(FileCat.Core.Jobs.Crc32.HashToUInt32(content));
            writer.Write((uint)((46 << 25) | (1 << 21) | (1 << 16))); // 2026-01-01 as a DOS time
            writer.Write((byte)29);
            writer.Write((byte)0x33);                             // "normal" compression
            writer.Write((ushort)name.Length);
            writer.Write(0x20u);                                  // archive attribute
            writer.Write(System.Text.Encoding.ASCII.GetBytes(name));
            ushort flags = (ushort)(0x8000 | (6 << 5) | (solid && i > 0 ? 0x0010 : 0));
            WriteRar4Block(archive, 0x74, flags, body.ToArray());
            archive.Write(packed);
        }
        WriteRar4Block(archive, 0x7B, 0x4000, []);
        string path = Path.Combine(dir.Path, "test.rar");
        File.WriteAllBytes(path, archive.ToArray());
        return path;
    }

    private static void WriteRar4Block(Stream archive, byte type, ushort flags, ReadOnlySpan<byte> body)
    {
        var header = new byte[7 + body.Length];
        header[2] = type;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(3), flags);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), (ushort)header.Length);
        body.CopyTo(header.AsSpan(7));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)FileCat.Core.Jobs.Crc32.HashToUInt32(header.AsSpan(2)));
        archive.Write(header);
    }

    private static (List<MemberInfo> Members, Exception? Error) Listing(Func<IMemberReader> open)
    {
        try
        {
            using var reader = open();
            return (reader.List(_ => { }, CancellationToken.None).ToList(), null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            return ([], ex);
        }
    }

    [Theory]
    // Fixtures that must decode, with their content counts: a refusal on both sides must not pass silently.
    [InlineData("Rar5.rar", 3)]
    [InlineData("Rar5.none.rar", 3)]
    [InlineData("Rar5.solid.rar", 3)]
    [InlineData("Rar5.crc_blake2.rar", 3)]
    [InlineData("Rar5.multi.part01.rar", 3)]
    [InlineData("Rar5.comment.rar", 3)]
    public void Rar5_fixtures_decode_completely(string fixture, int files)
    {
        var ours = Snapshot(() => OpenOurs(Path.Combine(Fixtures, fixture)));
        Assert.Null(ours.Error);
        Assert.Equal(files, ours.Contents.Count);
    }

    [Fact]
    public void Rar5_blake2sp_member_hashes_verify()
    {
        var snapshot = Snapshot(() => OpenOurs(Path.Combine(Fixtures, "Rar5.crc_blake2.rar")));
        Assert.Null(snapshot.Error);
        Assert.NotEmpty(snapshot.Contents);
    }

    [Theory]
    // Independent values: Python's hashlib.blake2s with BLAKE2sp's tree parameters (fanout 8, depth 2, inner length 32,
    // leaf i at node offset i, the last leaf and the root as last nodes), input byte i = (7i + 3) mod 256.
    [InlineData(0, "dd0e891776933f43c7d032b08a917e25741f8aa9a12c12e1cac8801500f2ca4f")]
    [InlineData(1, "456da349adc8eaa1ee5fd8397d9c181c21a8e9173ad90f22613fd34e1d576d44")]
    [InlineData(63, "fe713f6ccd82f0c81ccd7a7d0c8ef83c631e2e394217859d4af5025202923578")]
    [InlineData(64, "e375bba69fb1c84c7f202f06e71090695f81786b7413da0cd0126e15a0f80181")]
    [InlineData(65, "11a6af420901450dd27b0babec0c484c4f7710330040136a17e79df6c54106ae")]
    [InlineData(511, "63a8cc6d628b88d471d4f81035b367f62b8a14dbf55bd9cca0d945dae8e226cf")]
    [InlineData(512, "201135254f94f0cf5307da6ae28daf819258fd08df8249684ffbbded6c23e0e5")]
    [InlineData(513, "4524c7cfa3a7cde180691ba1a3c0d4b9fc20ced214f27e65f5c76dbb0ff44c91")]
    [InlineData(1024, "c172db3b0b7e122c3e1a5071348ce3bf39dab1772d9d3c1744e722c1eb0aed84")]
    [InlineData(1025, "3479ff3bda494d6fbdbd024d2ac68f71b140deb159e119e7a83b45eb59840f76")]
    [InlineData(5000, "797108ac66f04cf7f19cb474888ebdc6d60b2e5d34097bee42b6993e96518839")]
    public void Blake2sp_matches_independent_values_in_any_chunking(int length, string expected)
    {
        byte[] data = Enumerable.Range(0, length).Select(i => (byte)(i * 7 + 3)).ToArray();
        var whole = new Blake2sp();
        whole.Append(data);
        Assert.Equal(expected, Convert.ToHexStringLower(whole.Finish()));
        foreach (int chunk in new[] { 1, 7, 64, 100, 511, 512, 513 })
        {
            var pieces = new Blake2sp();
            for (int i = 0; i < data.Length; i += chunk) pieces.Append(data.AsSpan(i, Math.Min(chunk, data.Length - i)));
            Assert.Equal(expected, Convert.ToHexStringLower(pieces.Finish()));
        }
    }

    internal static RarReader OpenOurs(string path)
    {
        var volumes = new List<FileStream>();
        foreach (string volume in SharpArchiveReader.RarVolumes(path)) volumes.Add(ArchiveFormats.OpenShared(volume));
        return RarReader.Open(volumes, numberedVolumeGap: false);
    }

    private sealed record ArchiveSnapshot(List<MemberInfo> Members, List<(string Path, string Hash)> Contents, Exception? Error);

    private static ArchiveSnapshot Snapshot(Func<IMemberReader> open)
    {
        var members = new List<MemberInfo>();
        var contents = new List<(string, string)>();
        try
        {
            using var reader = open();
            members.AddRange(reader.List(_ => { }, CancellationToken.None));
            foreach (var member in members)
            {
                if (member.Kind != MemberKind.File || member.Encrypted) continue;
                using var content = reader.Open(member.Index, CancellationToken.None);
                contents.Add((member.Path.Replace('\\', '/'), Convert.ToHexStringLower(SHA256.HashData(content))));
            }
            return new ArchiveSnapshot(members, contents, null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            return new ArchiveSnapshot(members, contents, ex);
        }
    }
}
