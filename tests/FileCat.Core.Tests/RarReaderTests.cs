using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FileCat.Archives;
using FileCat.Archives.Rar;
using FileCat.Archives.Rar15;
using FileCat.Core.Jobs;
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

    // The fixtures with RAR 2.0 compressed members (unpack version 20; none uses 26). Rar.Audio_program.rar is RAR 3
    // (version 29) data and no fixture has a RAR 2.0 audio block or solid run: the synthetic RAR 2.0 tests cover those.
    public static TheoryData<string> Rar2Fixtures() => new() { "Rar2.rar", "Rar2.multi.rar" };

    [Theory]
    [MemberData(nameof(Rar5Fixtures))]
    [MemberData(nameof(Rar2Fixtures))]
    public void Members_and_bytes_match_the_reference_reader(string fixture)
    {
        string path = Path.Combine(Fixtures, fixture);
        var reference = Snapshot(() => UpstreamRarReader.Open(path));
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
        var reference = Listing(() => UpstreamRarReader.Open(path));
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
        var reference = Snapshot(() => UpstreamRarReader.Open(path));
        var ours = Snapshot(() => OpenOurs(path));
        Assert.True(reference.Error is null == ours.Error is null, $"reference: {reference.Error?.Message ?? "ok"}; ours: {ours.Error?.Message ?? "ok"}");
        if (ours.Error is not null) Assert.IsType<InvalidDataException>(ours.Error);
        Assert.Equal(reference.Contents, ours.Contents);
    }

    [Fact]
    public void Rar4_stored_members_match_the_reference_bytes()
    {
        string path = Path.Combine(Fixtures, "Rar.none.rar");
        var reference = Snapshot(() => UpstreamRarReader.Open(path));
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
        var reference = Snapshot(() => UpstreamRarReader.Open(path));
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
        using var reader = UpstreamRarReader.Open(path);
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
    public void Rar15_members_decode_byte_identically_to_the_reference_reader()
    {
        string path = Path.Combine(Fixtures, "Rar15.rar");
        var reference = Snapshot(() => UpstreamRarReader.Open(path));
        var ours = Snapshot(() => OpenOurs(path));
        Assert.Null(reference.Error);
        Assert.Null(ours.Error);
        // EXE\TEST.EXE, JPG\TEST.JPG and TECT.TXT; the two folders have no content.
        Assert.Equal(3, ours.Contents.Count);
        Assert.Equal(reference.Contents, ours.Contents);
    }

    [Theory]
    // The same three files as Rar2.multi, whose content 7-Zip 24.01 verified (ArchiveLegacyRarTests).
    [InlineData("EXE/TEST.EXE", "8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106")]
    [InlineData("JPG/TEST.JPG", "b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53")]
    [InlineData("TECT.TXT", "4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9")]
    public void Rar15_decoder_reproduces_each_member_in_any_read_size(string name, string sha256)
    {
        var (packed, size, crc) = Rar15Member(name);
        foreach (int chunk in new[] { 1, 7, 265, 266, 267, 4096, 65536, 1 << 20 })
        {
            byte[] output = DecodeRar15(packed, size, chunk);
            Assert.Equal(size, output.Length);
            Assert.Equal(crc, Crc32.HashToUInt32(output));
            Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(output)));
        }
    }

    [Fact]
    public async Task Rar15_truncated_packed_data_is_refused()
    {
        foreach (string name in new[] { "EXE/TEST.EXE", "JPG/TEST.JPG", "TECT.TXT" })
        {
            var (packed, size, _) = Rar15Member(name);
            foreach (int keep in new[] { 1, 2, 3, 16, 100, packed.Length / 4, packed.Length / 2, packed.Length * 9 / 10 })
                await AssertRefusedWithinTenSeconds(() => DecodeRar15(packed[..keep], size));
        }
    }

    [Fact]
    public async Task Rar15_random_packed_data_is_refused()
    {
        // A gibibyte cannot come out of at most 64 KiB, so every input must fail, whether on its content or its end.
        for (int seed = 0; seed < 40; seed++)
        {
            var random = new Random(seed);
            byte[] packed = new byte[new[] { 1, 2, 3, 5, 16, 100, 1000, 4096, 65536 }[seed % 9]];
            random.NextBytes(packed);
            await AssertRefusedWithinTenSeconds(() => DecodeRar15(packed, 1L << 30));
        }
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(15498L)]
    [InlineData(long.MaxValue)]
    public async Task Rar15_empty_packed_data_with_a_size_is_refused(long size)
    {
        await AssertRefusedWithinTenSeconds(() => DecodeRar15([], size));
    }

    [Fact]
    public void Rar15_fixed_codes_are_complete()
    {
        // Every bit pattern decodes: transcription damage to a code-length table would leave a gap or an overlap.
        foreach (var code in new[]
                 {
                     Rar15Codes.LengthCode1, Rar15Codes.LengthCode2, Rar15Codes.HuffmanCode0, Rar15Codes.HuffmanCode1,
                     Rar15Codes.HuffmanCode2, Rar15Codes.HuffmanCode3, Rar15Codes.HuffmanCode4, Rar15Codes.ShortMatchCode0,
                     Rar15Codes.ShortMatchCode1, Rar15Codes.ShortMatchCode2, Rar15Codes.ShortMatchCode3,
                 })
            Assert.True(code.IsComplete);
    }

    [Fact]
    public void Rar15_solid_members_continue_the_window_and_tables()
    {
        var (members, contents) = Rar15SolidPlan();
        var decoder = new Rar15Decoder();
        for (int i = 0; i < members.Count; i++)
        {
            decoder.StartMember(new MemoryStream(members[i], writable: false), contents[i].Length, solid: i > 0);
            Assert.Equal(contents[i], ReadAll(decoder, 4093));
        }
        // On its own, the second member reaches back into bytes it does not have.
        decoder.StartMember(new MemoryStream(members[1], writable: false), contents[1].Length, solid: false);
        Assert.Throws<InvalidDataException>(() => ReadAll(decoder, 4096));
        // A solid member after a failed one cannot be decoded either.
        Assert.Throws<InvalidDataException>(() => decoder.StartMember(new MemoryStream(members[2], writable: false), contents[2].Length, solid: true));
    }

    [Fact]
    public void Rar15_matches_outside_the_produced_bytes_or_the_member_are_refused()
    {
        var before = new Rar15TestEncoder();
        before.Literals("abc"u8);
        before.Match(distance: 4, length: 12);
        byte[] packed = before.EndMember();
        var error = Assert.Throws<InvalidDataException>(() => DecodeRar15(packed, 15));
        Assert.Contains("before the start", error.Message);

        var past = new Rar15TestEncoder();
        past.Literals("abc"u8);
        past.Match(distance: 3, length: 12);
        packed = past.EndMember();
        Assert.Equal(past.Output, DecodeRar15(packed, 15));
        error = Assert.Throws<InvalidDataException>(() => DecodeRar15(packed, 14));
        Assert.Contains("past the end of the member", error.Message);
    }

    [Fact]
    public void Rar15_solid_archive_members_read_in_any_order()
    {
        var (members, contents) = Rar15SolidPlan();
        var files = members.Select((packed, i) => ($"FILE{i}.BIN", packed, contents[i])).ToArray();
        using var reader = RarReader.Open([new MemoryStream(Rar15Archive(solid: true, files), writable: false)], numberedVolumeGap: false, TestContext.Current.CancellationToken);
        Assert.True(reader.SharesCursor);
        var listed = reader.List(_ => { }, TestContext.Current.CancellationToken).ToList();
        Assert.Equal(files.Length, listed.Count);
        // A later member first (the run is decoded up to it), then the first one (the run restarts), then the rest.
        foreach (int index in new[] { 1, 0, 3, 2, 1 })
        {
            using var content = reader.Open(index, TestContext.Current.CancellationToken);
            var copy = new MemoryStream();
            content.CopyTo(copy);
            Assert.Equal(contents[index], copy.ToArray());
        }
    }

    [Fact]
    public void Rar15_archive_without_the_solid_flag_decodes_each_member_alone()
    {
        // RAR 1.5 takes solidity from the archive header: without it the second member cannot reach the first's bytes.
        var (members, contents) = Rar15SolidPlan();
        var files = members.Select((packed, i) => ($"FILE{i}.BIN", packed, contents[i])).ToArray();
        using var reader = RarReader.Open([new MemoryStream(Rar15Archive(solid: false, files), writable: false)], numberedVolumeGap: false, TestContext.Current.CancellationToken);
        Assert.False(reader.SharesCursor);
        using (var first = reader.Open(0, TestContext.Current.CancellationToken))
        {
            var copy = new MemoryStream();
            first.CopyTo(copy);
            Assert.Equal(contents[0], copy.ToArray());
        }
        Assert.Throws<InvalidDataException>(() =>
        {
            using var second = reader.Open(1, TestContext.Current.CancellationToken);
            second.CopyTo(Stream.Null);
        });
    }

    [Fact]
    public async Task Rar15_damaged_solid_archives_are_refused_never_crashed_or_hung()
    {
        // The fuzz seed Rar15.rar is not solid: damage a solid run too, read in random orders so runs restart.
        var (members, contents) = Rar15SolidPlan();
        byte[] original = Rar15Archive(solid: true, members.Select((packed, i) => ($"FILE{i}.BIN", packed, contents[i])).ToArray());
        var outcomes = new Dictionary<string, int>();
        for (int round = 0; round < 300; round++)
        {
            var random = new Random(round);
            byte[] damaged = (byte[])original.Clone();
            if (round % 3 == 2) damaged = damaged[..random.Next(7, damaged.Length)];
            else for (int i = random.Next(1, 9); i > 0; i--) damaged[random.Next(7, damaged.Length)] ^= (byte)(1 << random.Next(8));
            int[] order = Enumerable.Range(0, members.Count).Concat(Enumerable.Range(0, members.Count)).OrderBy(_ => random.Next()).ToArray();
            string outcome = await Task.Run(() => ReadDamaged(damaged, order), TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(", ", outcomes.OrderBy(o => o.Key).Select(o => $"{o.Key} {o.Value}")));
        Assert.True(outcomes.GetValueOrDefault("members refused") > 0);
    }

    /// <summary>Lists and reads every member in <paramref name="order"/>; only refusals may end it.</summary>
    private static string ReadDamaged(byte[] archive, int[] order)
    {
        try
        {
            using var reader = RarReader.Open([new MemoryStream(archive, writable: false)], numberedVolumeGap: false);
            var members = reader.List(_ => { }, CancellationToken.None).ToList();
            int refused = 0;
            foreach (int index in order.Where(i => i < members.Count))
            {
                try
                {
                    using var content = reader.Open(members[index].Index, CancellationToken.None);
                    content.CopyTo(Stream.Null);
                }
                catch (InvalidDataException)
                {
                    refused++;
                }
            }
            return refused == 0 ? "intact" : "members refused";
        }
        catch (InvalidDataException)
        {
            return "archive refused";
        }
    }

    private static (byte[] Packed, long Size, uint Crc) Rar15Member(string name)
    {
        byte[] archive = File.ReadAllBytes(Path.Combine(Fixtures, "Rar15.rar"));
        var parsed = RarArchive.Read([new MemoryStream(archive, writable: false)], TestContext.Current.CancellationToken);
        var member = parsed.Members.Single(m => m.Name.Replace('\\', '/') == name);
        Assert.Equal(15, member.Version);
        var segment = Assert.Single(member.Segments);
        return (archive.AsSpan((int)segment.Offset, (int)segment.Length).ToArray(), member.UnpackedSize, member.Crc32!.Value);
    }

    private static byte[] DecodeRar15(byte[] packed, long size, int chunk = 64 * 1024)
    {
        var decoder = new Rar15Decoder();
        decoder.StartMember(new MemoryStream(packed, writable: false), size, solid: false);
        return ReadAll(decoder, chunk);
    }

    private static byte[] ReadAll(Rar15Decoder decoder, int chunk)
    {
        var output = new MemoryStream();
        var buffer = new byte[chunk];
        int n;
        while ((n = decoder.Read(buffer)) > 0) output.Write(buffer, 0, n);
        return output.ToArray();
    }

    private static async Task AssertRefusedWithinTenSeconds(Action decode)
    {
        var run = Task.Factory.StartNew(() => Record.Exception(decode), TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var error = await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.IsType<InvalidDataException>(error);
    }

    /// <summary>
    /// Four members of one solid stream: literals in phases of uniform, skewed and text-like bytes between matches near
    /// and far, so every literal, length and offset code is used and the adaptive tables reset; the second member
    /// opens with a match into the first, and the third is empty.
    /// </summary>
    private static (List<byte[]> Members, List<byte[]> Contents) Rar15SolidPlan()
    {
        var encoder = new Rar15TestEncoder();
        var random = new Random(1515);
        var members = new List<byte[]>();
        var contents = new List<byte[]>();
        int start = 0;
        void Close()
        {
            members.Add(encoder.EndMember());
            contents.Add(encoder.Output.Skip(start).ToArray());
            start = encoder.Output.Count;
        }
        void Fill(int bytes)
        {
            int end = encoder.Output.Count + bytes;
            for (int phase = 0; encoder.Output.Count < end; phase++)
            {
                for (int step = 0; step < 40 && encoder.Output.Count < end; step++)
                {
                    for (int n = random.Next(1, 17); n > 0; n--)
                        encoder.Literal((phase % 3) switch
                        {
                            0 => (byte)random.Next(256),
                            1 => (byte)random.Next(4),
                            _ => (byte)" etaoinshrdlu\n"[random.Next(14)],
                        });
                    int reach = Math.Min(encoder.Output.Count, 0x7fff);
                    int distance = random.Next(3) switch
                    {
                        0 => random.Next(1, Math.Min(reach, 256) + 1),
                        1 => random.Next(1, Math.Min(reach, 0x2000) + 1),
                        _ => random.Next(1, reach + 1),
                    };
                    int shortest = encoder.ShortestMatch(distance);
                    encoder.Match(distance, shortest + (random.Next(2) == 0 ? random.Next(8) : random.Next(256)));
                }
            }
        }
        Fill(40_000);
        Close();
        encoder.Match(distance: 30_000, length: 200);
        Fill(20_000);
        Close();
        Close();
        Fill(10_000);
        Close();
        return (members, contents);
    }

    /// <summary>A RAR 1.5 archive (format 1.5 headers, unpack version 15, method 3) of the given members.</summary>
    private static byte[] Rar15Archive(bool solid, params (string Name, byte[] Packed, byte[] Content)[] files)
    {
        var archive = new MemoryStream();
        archive.Write("Rar!\x1A\x07\x00"u8);
        WriteRar4Block(archive, 0x73, (ushort)(solid ? 0x0008 : 0), new byte[6]);
        foreach (var (name, packed, content) in files)
        {
            var body = new byte[25 + name.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(body, (uint)packed.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), (uint)content.Length);
            body[8] = 0; // MS-DOS
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(9), Crc32.HashToUInt32(content));
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(13), 0x5B5D0000); // 2025-10-29
            body[17] = 15;   // unpack version
            body[18] = 0x33; // normal compression
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(19), (ushort)name.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(21), 0x20);
            Encoding.ASCII.GetBytes(name, body.AsSpan(25));
            WriteRar4Block(archive, 0x74, 0x8000, body);
            archive.Write(packed);
        }
        WriteRar4Block(archive, 0x7B, 0x4000, []);
        return archive.ToArray();
    }

    private static void WriteRar4Block(Stream archive, byte type, ushort flags, byte[] body)
    {
        var header = new byte[7 + body.Length];
        header[2] = type;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(3), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), (ushort)header.Length);
        body.CopyTo(header, 7);
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)Crc32.HashToUInt32(header.AsSpan(2)));
        archive.Write(header);
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

    [Theory]
    // RAR 2.0 fixtures that must decode, with their content counts: a refusal on both sides must not pass silently.
    [InlineData("Rar2.rar", 3)]
    [InlineData("Rar2.multi.rar", 3)]
    public void Rar2_fixtures_decode_completely(string fixture, int files)
    {
        var ours = Snapshot(() => OpenOurs(Path.Combine(Fixtures, fixture)));
        Assert.Null(ours.Error);
        Assert.Equal(files, ours.Contents.Count);
    }

    [Fact]
    public void Rar2_lz_and_audio_blocks_match_the_reference_reader()
    {
        var rng = new Random(2020);
        var members = new List<Rar2Member>();

        // Every LZ symbol kind, with distances up to RAR 2.0's 1 MiB maximum (both length bonuses).
        var far = new Rar2Writer();
        far.LzHeader(rng, keepOld: false);
        while (far.Written < (1 << 20) + 70_000) far.RandomLz(rng, 1000);
        members.Add(far.TakeMember("far.bin", version: 20));

        // Block switches, "keep the old table" across block kinds and sizes (the LZ table after the 1-channel one is
        // where rardecode's rule fails), and one to four channels; the 3-channel header comes when the next byte
        // belongs to channel 3, which then starts over at channel 0.
        var mixed = new Rar2Writer();
        mixed.LzHeader(rng, keepOld: false);
        mixed.RandomLz(rng, 3000);
        mixed.EndLzBlock();
        mixed.AudioHeader(rng, keepOld: true, channels: 2);
        mixed.RandomAudio(rng, 3001);
        mixed.EndAudioBlock();
        mixed.LzHeader(rng, keepOld: true);
        mixed.RandomLz(rng, 2000);
        mixed.EndLzBlock();
        mixed.AudioHeader(rng, keepOld: false, channels: 4);
        mixed.RandomAudio(rng, 2003);
        mixed.EndAudioBlock();
        mixed.AudioHeader(rng, keepOld: true, channels: 3);
        mixed.RandomAudio(rng, 1500);
        mixed.EndAudioBlock();
        mixed.AudioHeader(rng, keepOld: true, channels: 1);
        mixed.RandomAudio(rng, 700);
        mixed.EndAudioBlock();
        mixed.LzHeader(rng, keepOld: true);
        mixed.RandomLz(rng, 1000);
        mixed.EndLzBlock();
        mixed.AudioHeader(rng, keepOld: true, channels: 4);
        mixed.RandomAudio(rng, 1000);
        members.Add(mixed.TakeMember("mixed.bin", version: 26));

        var audio = new Rar2Writer();
        audio.AudioHeader(rng, keepOld: true, channels: 1); // a member's first "old table" is all zeros
        audio.RandomAudio(rng, 5000);
        members.Add(audio.TakeMember("audio.wav", version: 20));

        // Deltas that push the predictor's weights against both of their limits (-17 and 16) many times.
        var patterns = new (int Channels, Func<int, int> Delta)[]
        {
            (1, n => rng.Next(256)),
            (2, n => (int)(40 * Math.Sin(n / 10.0))),
            (1, n => n % 2 == 0 ? 64 : 192),
            (1, n => 128),
        };
        foreach (var (channels, delta) in patterns)
        {
            var clamps = new Rar2Writer();
            clamps.AudioHeader(rng, keepOld: false, channels);
            clamps.AudioDeltas(delta, 60_000);
            members.Add(clamps.TakeMember($"clamps{members.Count}.wav", version: 20));
        }

        AssertRar2MatchesReference(members, solid: false);
    }

    [Fact]
    public void Rar2_random_block_sequences_match_the_reference_reader()
    {
        // Random switches between LZ and 1-4 channel audio blocks, mostly keeping the old table: in separate members,
        // and in one solid run whose members sometimes end with a switch after their last byte.
        var rng = new Random(1966);
        var separate = new List<Rar2Member>();
        for (int i = 0; i < 60; i++)
        {
            var writer = new Rar2Writer();
            writer.RandomHeader(rng, keepOld: rng.Next(2) == 0);
            RandomBlocks(writer, rng, switches: rng.Next(3, 10));
            separate.Add(writer.TakeMember($"m{i}.bin", version: rng.Next(2) == 0 ? 20 : 26));
        }
        AssertRar2MatchesReference(separate, solid: false);

        var run = new Rar2Writer();
        var solid = new List<Rar2Member>();
        run.RandomHeader(rng, keepOld: false);
        for (int i = 0; i < 40; i++)
        {
            RandomBlocks(run, rng, switches: rng.Next(0, 3));
            if (rng.Next(3) == 0)
            {
                run.EndBlock();
                run.RandomHeader(rng, keepOld: rng.Next(10) < 7);
            }
            solid.Add(run.TakeMember($"s{i}.bin", version: 20));
        }
        AssertRar2MatchesReference(solid, solid: true);
    }

    [Fact]
    public void Rar2_block_switch_after_a_members_last_byte_applies_to_the_next_member()
    {
        // The reference reader reads an end of block and a whole header that follow a member's last byte, and the
        // next member of the solid run decodes with them (rardecode ignores them and misreads that member). Other
        // bits there are no data.
        var rng = new Random(31);
        var writer = new Rar2Writer();
        var members = new List<Rar2Member>();
        writer.LzHeader(rng, keepOld: false);
        writer.RandomLz(rng, 800);
        writer.EndBlock();
        writer.AudioHeader(rng, keepOld: true, channels: 2); // LZ to audio
        members.Add(writer.TakeMember("a.bin", version: 20));
        writer.RandomAudio(rng, 701);
        writer.EndBlock(); // coded with channel 1's table
        writer.LzHeader(rng, keepOld: true); // audio to LZ
        writer.Bits(0, 8); // and a byte of padding
        members.Add(writer.TakeMember("b.wav", version: 20));
        writer.RandomLz(rng, 600); // distances from a.bin again
        writer.UncountedLiterals(rng, 3);
        members.Add(writer.TakeMember("c.bin", version: 20));
        writer.RandomLz(rng, 400);
        members.Add(writer.TakeMember("d.bin", version: 20));
        AssertRar2MatchesReference(members, solid: true);
    }

    [Fact]
    public void Rar2_solid_run_matches_the_reference_reader()
    {
        var rng = new Random(2026);
        var writer = new Rar2Writer();
        var members = new List<Rar2Member>();
        writer.LzHeader(rng, keepOld: false);
        writer.RandomLz(rng, 2000);
        members.Add(writer.TakeMember("a.txt", version: 20));
        writer.RandomLz(rng, 1500); // no header: the previous member's tables and distances
        members.Add(writer.TakeMember("b.txt", version: 20));
        members.Add(writer.TakeMember("empty.txt", version: 20));
        writer.EndLzBlock();
        writer.AudioHeader(rng, keepOld: true, channels: 3);
        writer.RandomAudio(rng, 1000); // ends inside a round of channels
        members.Add(writer.TakeMember("c.wav", version: 20));
        writer.RandomAudio(rng, 999);
        writer.EndAudioBlock();
        writer.LzHeader(rng, keepOld: true);
        writer.RandomLz(rng, 500);
        members.Add(writer.TakeMember("d.txt", version: 20));
        AssertRar2MatchesReference(members, solid: true);
    }

    [Fact]
    public void Rar2_member_ends_at_its_size_whatever_follows()
    {
        // As in rardecode: RAR 2.0 data has no end marker, so nothing after the member's last byte becomes output.
        var rng = new Random(4);
        var writer = new Rar2Writer();
        writer.LzHeader(rng, keepOld: false);
        writer.RandomLz(rng, 3000);
        var member = writer.TakeMember("m", version: 20);
        byte[] content = DecodeRar2(member.Packed, member.Size);
        Assert.Equal(member.Size, content.Length);
        var trailing = new byte[64];
        rng.NextBytes(trailing);
        Assert.Equal(content, DecodeRar2([.. member.Packed, .. trailing], member.Size));
    }

    [Fact]
    public void Rar2_match_past_the_member_size_is_refused()
    {
        var rng = new Random(3);
        var writer = new Rar2Writer();
        writer.LzHeader(rng, keepOld: false);
        for (int i = 0; i < 64; i++) writer.Literal(rng);
        writer.ShortMatch(slot: 0, extra: 0); // two bytes from one back
        var member = writer.TakeMember("m", version: 20);
        Assert.Equal(66, DecodeRar2(member.Packed, member.Size).Length);
        Assert.Throws<InvalidDataException>(() => DecodeRar2(member.Packed, member.Size - 1));
    }

    [Fact]
    public void Rar2_data_of_unknown_size_is_refused()
    {
        var decoder = new Rar2Decoder();
        var bits = new RarBitReader();
        bits.Reset(new MemoryStream(new byte[64]));
        decoder.StartMember(bits, solid: false, unpackedSize: -1);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(new RarWindow(Rar2Dictionary, 0)));
    }

    [Fact]
    public async Task Rar2_garbage_is_refused_as_invalid_data()
    {
        await WithinTenSeconds(() =>
        {
            var rng = new Random(77);
            for (int round = 0; round < 3000; round++)
            {
                var packed = new byte[rng.Next(0, 2048)];
                rng.NextBytes(packed);
                Assert.Throws<InvalidDataException>(() => DecodeRar2(packed, rng.Next(1, 1 << 16), crc: 0x5EED5EED));
            }
        });
    }

    [Fact]
    public async Task Rar2_garbage_members_fail_as_invalid_data_through_the_reader()
    {
        await WithinTenSeconds(() =>
        {
            var rng = new Random(11);
            using var temp = new TempDir();
            for (int round = 0; round < 20; round++)
            {
                var members = new List<Rar2Member>();
                for (int i = 0; i < 3; i++)
                {
                    var packed = new byte[rng.Next(1, 3000)];
                    rng.NextBytes(packed);
                    members.Add(new Rar2Member($"g{i}", packed, rng.Next(1, 100_000), 20));
                }
                string path = Path.Combine(temp.Path, $"garbage{round}.rar");
                File.WriteAllBytes(path, Rar2Archive(members, [.. members.Select(_ => (uint)rng.Next())], solid: round % 2 == 1));
                var ours = Snapshot(() => OpenOurs(path));
                Assert.IsType<InvalidDataException>(ours.Error);
            }
        });
    }

    [Fact]
    public async Task Rar2_truncated_data_is_refused()
    {
        await WithinTenSeconds(() =>
        {
            var rng = new Random(5);
            var writer = new Rar2Writer();
            writer.LzHeader(rng, keepOld: false);
            writer.RandomLz(rng, 1500);
            writer.EndLzBlock();
            writer.AudioHeader(rng, keepOld: true, channels: 2);
            writer.RandomAudio(rng, 300);
            var member = writer.TakeMember("m", version: 20);
            Assert.Equal(member.Size, DecodeRar2(member.Packed, member.Size).Length);
            // Every byte carries bits the member needs, so every shorter prefix ends early.
            for (int length = 0; length < member.Packed.Length; length++)
                Assert.Throws<InvalidDataException>(() => DecodeRar2(member.Packed[..length], member.Size));
            // So does the whole data under a larger size.
            Assert.Throws<InvalidDataException>(() => DecodeRar2(member.Packed, member.Size + 1));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rar2_tables_of_zero_lengths_are_refused(bool audio)
    {
        await WithinTenSeconds(() =>
        {
            // All-zero data: an LZ block whose code for the lengths has no symbol at all.
            Assert.Throws<InvalidDataException>(() => DecodeRar2(new byte[4096], 100));
            // A valid code for the lengths (only "11-138 zeros", one bit) that sets every length to zero: the first
            // symbol of the block has no code to match.
            var writer = new Rar2Writer();
            writer.Bits(audio ? 1 : 0, 1);
            writer.Bits(0, 1);
            if (audio) writer.Bits(3, 2); // four channels
            for (int i = 0; i < 19; i++) writer.Bits(i == 18 ? 1 : 0, 4);
            for (int left = audio ? 4 * 257 : 298 + 48 + 28; left > 0; left -= 138)
            {
                writer.Bits(0, 1);
                writer.Bits(127, 7);
            }
            byte[] tableOnly = writer.TakeBytes();
            Assert.Throws<InvalidDataException>(() => DecodeRar2(tableOnly, 100));
            Assert.Throws<InvalidDataException>(() => DecodeRar2([.. tableOnly, .. Enumerable.Repeat((byte)0xA5, 64)], 100));
        });
    }

    [Theory]
    // The channel count has two bits, so 1-4 is all a header can say; the decoder checks it all the same.
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Rar2_audio_headers_with_damaged_tables_are_refused(int channels)
    {
        await WithinTenSeconds(() =>
        {
            var writer = new Rar2Writer();
            writer.Bits(1, 1);
            writer.Bits(0, 1);
            writer.Bits(channels - 1, 2);
            byte[] cut = writer.TakeBytes(); // ends right after the channel count
            Assert.Throws<InvalidDataException>(() => DecodeRar2(cut, 10));

            writer.Bits(1, 1);
            writer.Bits(0, 1);
            writer.Bits(channels - 1, 2);
            for (int i = 0; i < 19; i++) writer.Bits(1, 4); // 19 one-bit codes: over-subscribed
            writer.Bits(0, 32);
            Assert.Throws<InvalidDataException>(() => DecodeRar2(writer.TakeBytes(), 10));

            var rng = new Random(channels);
            writer.AudioHeader(rng, keepOld: false, channels);
            writer.RandomAudio(rng, 200);
            var member = writer.TakeMember("m", version: 20);
            Assert.Equal(200, DecodeRar2(member.Packed, 200).Length);
            Assert.Throws<InvalidDataException>(() => DecodeRar2(member.Packed[..(member.Packed.Length / 2)], 200));
        });
    }

    [Fact]
    public async Task Rar2_damaged_data_decodes_correctly_or_is_refused()
    {
        await WithinTenSeconds(() =>
        {
            var rng = new Random(99);
            var writer = new Rar2Writer();
            writer.LzHeader(rng, keepOld: false);
            writer.RandomLz(rng, 1500);
            writer.EndLzBlock();
            writer.AudioHeader(rng, keepOld: true, channels: 4);
            writer.RandomAudio(rng, 600);
            writer.EndAudioBlock();
            writer.LzHeader(rng, keepOld: true);
            writer.RandomLz(rng, 500);
            var member = writer.TakeMember("m", version: 20);
            byte[] content = DecodeRar2(member.Packed, member.Size);
            uint crc = FileCat.Core.Jobs.Crc32.HashToUInt32(content);
            for (int round = 0; round < 2000; round++)
            {
                byte[] damaged = (byte[])member.Packed.Clone();
                for (int flips = 1 + rng.Next(3); flips > 0; flips--) damaged[rng.Next(damaged.Length)] ^= (byte)(1 << rng.Next(8));
                long size = round % 10 == 0 ? member.Size + rng.Next(-300, 300) : member.Size;
                try
                {
                    // Only bits the decoder never reads (the last byte's padding) may change without notice.
                    Assert.Equal(content, DecodeRar2(damaged, Math.Max(size, 0), crc));
                }
                catch (InvalidDataException)
                {
                }
            }
        });
    }

    internal static RarReader OpenOurs(string path)
    {
        var volumes = new List<FileStream>();
        foreach (string volume in RarVolumes.Of(path)) volumes.Add(ArchiveFormats.OpenShared(volume));
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

    private const long Rar2Dictionary = 1 << 20;

    private static Task WithinTenSeconds(Action test) =>
        Task.Run(test, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    /// <summary>
    /// Decodes the members with FileCat's decoder (their checksums come from that), writes them as a RAR archive and
    /// requires the reference reader to read the same bytes from it.
    /// </summary>
    private static void AssertRar2MatchesReference(List<Rar2Member> members, bool solid)
    {
        byte[][] contents = DecodeRar2(members, solid);
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "synthetic.rar");
        File.WriteAllBytes(path, Rar2Archive(members, [.. contents.Select(c => FileCat.Core.Jobs.Crc32.HashToUInt32(c))], solid));
        var reference = Snapshot(() => UpstreamRarReader.Open(path));
        var ours = Snapshot(() => OpenOurs(path));
        Assert.Null(reference.Error);
        Assert.Null(ours.Error);
        Assert.Equal(members.Count, ours.Contents.Count);
        Assert.Equal(reference.Contents, ours.Contents);
    }

    /// <summary>Continues the current block, then switches <paramref name="switches"/> times to a random block.</summary>
    private static void RandomBlocks(Rar2Writer writer, Random rng, int switches)
    {
        writer.RandomSymbols(rng, rng.Next(20, 300));
        for (int i = 0; i < switches; i++)
        {
            writer.EndBlock();
            writer.RandomHeader(rng, keepOld: rng.Next(10) < 7);
            writer.RandomSymbols(rng, rng.Next(20, 300));
        }
    }

    private static byte[] DecodeRar2(byte[] packed, long size, uint? crc = null) =>
        DecodeRar2([new Rar2Member("m", packed, size, 20)], solid: false, crc)[0];

    /// <summary>Decodes members through <see cref="Rar2Decoder"/> as RarReader does: a solid run shares one decoder and window.</summary>
    private static byte[][] DecodeRar2(List<Rar2Member> members, bool solid, uint? crc = null)
    {
        var results = new byte[members.Count][];
        Rar2Decoder? decoder = null;
        RarWindow? window = null;
        var bits = new RarBitReader();
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            if (!solid || decoder is null)
            {
                decoder = new Rar2Decoder();
                window = new RarWindow(Rar2Dictionary, solid ? members.Sum(x => x.Size) : m.Size);
            }
            bits.Reset(new MemoryStream(m.Packed));
            decoder.StartMember(bits, solid && i > 0, m.Size);
            var member = new RarMember { Name = m.Name, Kind = MemberKind.File, UnpackedSize = m.Size, Method = 3, Version = m.Version, Dictionary = Rar2Dictionary, Crc32 = crc };
            using var stream = RarMemberStream.Decoded(member, decoder, window!, CancellationToken.None);
            using var output = new MemoryStream();
            stream.CopyTo(output);
            results[i] = output.ToArray();
        }
        return results;
    }

    /// <summary>A RAR 1.5-4.x archive of RAR 2.0 members (1 MiB dictionary), continuing one another when <paramref name="solid"/>.</summary>
    private static byte[] Rar2Archive(List<Rar2Member> members, uint[] crcs, bool solid)
    {
        using var archive = new MemoryStream();
        archive.Write("Rar!\x1A\x07\x00"u8);
        WriteRar4Block(archive, 0x73, (ushort)(solid ? 0x0008 : 0), new byte[6]);
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            byte[] name = System.Text.Encoding.ASCII.GetBytes(m.Name);
            var body = new byte[25 + name.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body, (uint)m.Packed.Length);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), (uint)m.Size);
            body[8] = 2; // Win32
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(9), crcs[i]);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(13), 0x50210000); // 2020-01-01 00:00
            body[17] = (byte)m.Version;
            body[18] = 0x33; // normal compression
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(19), (ushort)name.Length);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(21), 0x20); // archive attribute
            name.CopyTo(body, 25);
            WriteRar4Block(archive, 0x74, (ushort)(0x8000 | 0x0080 | (solid && i > 0 ? 0x0010 : 0)), body);
            archive.Write(m.Packed);
        }
        WriteRar4Block(archive, 0x7B, 0x4000, []);
        return archive.ToArray();
    }

    private sealed record Rar2Member(string Name, byte[] Packed, long Size, int Version);

    /// <summary>
    /// Writes RAR 2.0 packed data for tests, most significant bit first, with canonical codes from random lengths. It
    /// tracks what the format makes a decoder track (the old code-length table, the audio channel, the last four
    /// distances) so that every match it writes is valid and every member's size is exact.
    /// </summary>
    private sealed class Rar2Writer
    {
        private static readonly int[] LengthBase = [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 24, 28, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224];
        private static readonly int[] LengthBits = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5];
        private static readonly int[] OffsetBase =
        [
            0, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 1536, 2048, 3072, 4096,
            6144, 8192, 12288, 16384, 24576, 32768, 49152, 65536, 98304, 131072, 196608, 262144, 327680, 393216, 458752,
            524288, 589824, 655360, 720896, 786432, 851968, 917504, 983040,
        ];
        private static readonly int[] OffsetBits =
        [
            0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15,
            16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
        ];
        private static readonly int[] ShortBase = [0, 4, 8, 16, 32, 64, 128, 192];
        private static readonly int[] ShortBits = [2, 2, 3, 4, 5, 6, 6, 6];

        private readonly List<byte> _bytes = [];
        private int _pending, _pendingBits;
        private readonly byte[] _table = new byte[4 * 257];
        private (int Code, int Bits)[] _main = [], _offset = [], _length = [];
        private readonly (int Code, int Bits)[][] _audio = [[], [], [], []];
        private int _channels = 1, _channel;
        private readonly int[] _offsets = new int[4];
        private int _lastLength;
        private long _memberStart;

        /// <summary>Bytes the data written so far decodes to, over the whole (solid) run.</summary>
        public long Written { get; private set; }

        /// <summary>True in an audio block, false in an LZ block.</summary>
        public bool Audio { get; private set; }

        public void Bits(int value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                _pending = _pending << 1 | (value >> i & 1);
                if (++_pendingBits < 8) continue;
                _bytes.Add((byte)_pending);
                _pending = _pendingBits = 0;
            }
        }

        /// <summary>The bytes written since the last take, the last one zero-padded; the coding state carries on.</summary>
        public byte[] TakeBytes()
        {
            if (_pendingBits > 0) Bits(0, 8 - _pendingBits);
            byte[] bytes = [.. _bytes];
            _bytes.Clear();
            return bytes;
        }

        public Rar2Member TakeMember(string name, int version)
        {
            long size = Written - _memberStart;
            _memberStart = Written;
            return new Rar2Member(name, TakeBytes(), size, version);
        }

        public void LzHeader(Random rng, bool keepOld)
        {
            Bits(0, 1);
            Bits(keepOld ? 1 : 0, 1);
            if (!keepOld) Array.Clear(_table);
            var lengths = new byte[298 + 48 + 28];
            RandomLengths(rng, lengths.AsSpan(0, 298), 8, 12, [(40, 6), (150, 40)]); // unused literals: both zero-run codes
            RandomLengths(rng, lengths.AsSpan(298, 48), 5, 10, []);
            RandomLengths(rng, lengths.AsSpan(346, 28), 4, 9, []);
            WriteLengths(rng, lengths);
            Audio = false;
            _main = Canonical(lengths.AsSpan(0, 298));
            _offset = Canonical(lengths.AsSpan(298, 48));
            _length = Canonical(lengths.AsSpan(346, 28));
        }

        public void AudioHeader(Random rng, bool keepOld, int channels)
        {
            Bits(1, 1);
            Bits(keepOld ? 1 : 0, 1);
            if (!keepOld) Array.Clear(_table);
            Bits(channels - 1, 2);
            _channels = channels;
            if (_channel >= channels) _channel = 0;
            var lengths = new byte[257 * channels];
            for (int c = 0; c < channels; c++) RandomLengths(rng, lengths.AsSpan(c * 257, 257), 8, 12, [(7, 4), (100, 20)]);
            WriteLengths(rng, lengths);
            Audio = true;
            for (int c = 0; c < channels; c++) _audio[c] = Canonical(lengths.AsSpan(c * 257, 257));
        }

        public void RandomHeader(Random rng, bool keepOld)
        {
            if (rng.Next(2) == 0) AudioHeader(rng, keepOld, channels: rng.Next(1, 5));
            else LzHeader(rng, keepOld);
        }

        public void RandomSymbols(Random rng, int count)
        {
            if (Audio) RandomAudio(rng, count);
            else RandomLz(rng, count);
        }

        /// <summary>Literal codes the data does not count: bits after a member's last byte that are no block switch.</summary>
        public void UncountedLiterals(Random rng, int count)
        {
            for (int n = 0; n < count; n++)
            {
                int value;
                do value = rng.Next(256); while (_main[value].Bits == 0);
                Symbol(_main, value);
            }
        }

        public void RandomLz(Random rng, int symbols)
        {
            for (int n = 0; n < symbols; n++)
            {
                int kind = rng.Next(10);
                if (Written < 1024 || kind < 4) Literal(rng);
                else if (kind < 6) LongMatch(rng);
                else if (kind < 7) ShortMatch(rng.Next(8), rng);
                else if (kind < 9) OldOffsetMatch(rng);
                else RepeatMatch(rng);
            }
        }

        public void Literal(Random rng)
        {
            int value;
            do value = rng.Next(256); while (_main[value].Bits == 0);
            Symbol(_main, value);
            Written++;
        }

        private void LongMatch(Random rng)
        {
            int top = 0; // the largest distance slot whose every distance is already decoded
            while (top < 47 && OffsetBase[top + 1] + (1 << OffsetBits[top + 1]) <= Written) top++;
            int offsetSlot = rng.Next(3) == 0 ? top : rng.Next(top + 1);
            int offsetExtra = rng.Next(1 << OffsetBits[offsetSlot]);
            int lengthSlot = rng.Next(28);
            int lengthExtra = rng.Next(1 << LengthBits[lengthSlot]);
            int offset = OffsetBase[offsetSlot] + 1 + offsetExtra;
            Symbol(_main, 270 + lengthSlot);
            Bits(lengthExtra, LengthBits[lengthSlot]);
            Symbol(_offset, offsetSlot);
            Bits(offsetExtra, OffsetBits[offsetSlot]);
            Match(offset, LengthBase[lengthSlot] + 3 + lengthExtra + (offset >= 0x2000 ? 1 : 0) + (offset >= 0x40000 ? 1 : 0));
        }

        private void ShortMatch(int slot, Random rng) => ShortMatch(slot, rng.Next(1 << ShortBits[slot]));

        public void ShortMatch(int slot, int extra)
        {
            Symbol(_main, 261 + slot);
            Bits(extra, ShortBits[slot]);
            Match(ShortBase[slot] + 1 + extra, 2);
        }

        private void OldOffsetMatch(Random rng)
        {
            int index = rng.Next(4);
            int offset = _offsets[index];
            if (offset == 0)
            {
                Literal(rng);
                return;
            }
            int slot = rng.Next(28);
            int extra = rng.Next(1 << LengthBits[slot]);
            Symbol(_main, 257 + index);
            Symbol(_length, slot);
            Bits(extra, LengthBits[slot]);
            Match(offset, LengthBase[slot] + 2 + extra + (offset >= 0x101 ? 1 : 0) + (offset >= 0x2000 ? 1 : 0) + (offset >= 0x40000 ? 1 : 0));
        }

        private void RepeatMatch(Random rng)
        {
            if (_lastLength == 0)
            {
                Literal(rng);
                return;
            }
            Symbol(_main, 256);
            Match(_offsets[0], _lastLength);
        }

        private void Match(int offset, int length)
        {
            if (offset > Written) throw new InvalidOperationException("The test writer made a distance before the data.");
            _offsets[3] = _offsets[2];
            _offsets[2] = _offsets[1];
            _offsets[1] = _offsets[0];
            _offsets[0] = offset;
            _lastLength = length;
            Written += length;
        }

        public void EndLzBlock() => Symbol(_main, 269);

        public void EndAudioBlock() => Symbol(_audio[_channel], 256);

        public void EndBlock()
        {
            if (Audio) EndAudioBlock();
            else EndLzBlock();
        }

        /// <summary>Audio bytes with chosen deltas; a delta without a code becomes the next one that has one.</summary>
        public void AudioDeltas(Func<int, int> delta, int count)
        {
            for (int n = 0; n < count; n++)
            {
                int value = delta(n) & 0xFF;
                while (_audio[_channel][value].Bits == 0) value = (value + 1) & 0xFF;
                Symbol(_audio[_channel], value);
                Written++;
                if (++_channel >= _channels) _channel = 0;
            }
        }

        public void RandomAudio(Random rng, int count)
        {
            for (int n = 0; n < count; n++)
            {
                int delta;
                // Mostly small signed deltas, as sound has; sometimes any byte.
                do delta = rng.Next(4) == 0 ? rng.Next(256) : rng.Next(-6, 7) & 0xFF;
                while (_audio[_channel][delta].Bits == 0);
                Symbol(_audio[_channel], delta);
                Written++;
                if (++_channel >= _channels) _channel = 0;
            }
        }

        private void Symbol((int Code, int Bits)[] code, int symbol)
        {
            var (value, bits) = code[symbol];
            if (bits == 0) throw new InvalidOperationException($"The test writer has no code for symbol {symbol}.");
            Bits(value, bits);
        }

        private static void RandomLengths(Random rng, Span<byte> lengths, int min, int max, (int Start, int Count)[] unused)
        {
            for (int i = 0; i < lengths.Length; i++)
                lengths[i] = i > 0 && rng.Next(4) == 0 ? lengths[i - 1] : (byte)rng.Next(min, max + 1); // runs for the repeat code
            foreach (var (start, count) in unused) lengths.Slice(start, count).Clear();
            while (Kraft(lengths) > 1 << 15)
                for (int i = 0; i < lengths.Length; i++)
                    if (lengths[i] is > 0 and < 15) lengths[i]++;
        }

        private static long Kraft(ReadOnlySpan<byte> lengths)
        {
            long sum = 0;
            foreach (byte length in lengths) if (length != 0) sum += 1L << (15 - length);
            return sum;
        }

        /// <summary>
        /// RAR 2.0's code-length table for <paramref name="lengths"/>, as changes to the old table: the last table
        /// written, zero past its size (the reference reader's rule; rardecode would keep longer tables' values there).
        /// </summary>
        private void WriteLengths(Random rng, byte[] lengths)
        {
            var bitLengths = new byte[19];
            for (int i = 0; i < bitLengths.Length; i++) bitLengths[i] = (byte)(i < 11 ? 4 : 5); // 11/16 + 8/32 <= 1
            rng.Shuffle(bitLengths);
            foreach (byte length in bitLengths) Bits(length, 4);
            var code = Canonical(bitLengths);
            for (int i = 0; i < lengths.Length;)
            {
                int zeros = Run(lengths, i, 0);
                if (zeros >= 3 && rng.Next(4) != 0)
                {
                    int n = zeros >= 11 ? Math.Min(zeros, 138) : zeros;
                    if (n >= 11)
                    {
                        Symbol(code, 18);
                        Bits(n - 11, 7);
                    }
                    else
                    {
                        Symbol(code, 17);
                        Bits(n - 3, 3);
                    }
                    _table.AsSpan(i, n).Clear();
                    i += n;
                    continue;
                }
                int same = i > 0 ? Run(lengths, i, _table[i - 1]) : 0;
                if (same >= 3 && rng.Next(4) != 0)
                {
                    int n = Math.Min(same, 6);
                    Symbol(code, 16);
                    Bits(n - 3, 2);
                    _table.AsSpan(i, n).Fill(_table[i - 1]);
                    i += n;
                    continue;
                }
                Symbol(code, (lengths[i] - _table[i]) & 0xF);
                _table[i] = lengths[i];
                i++;
            }
            _table.AsSpan(lengths.Length).Clear();
        }

        private static int Run(byte[] lengths, int start, byte value)
        {
            int end = start;
            while (end < lengths.Length && lengths[end] == value) end++;
            return end - start;
        }

        private static (int Code, int Bits)[] Canonical(ReadOnlySpan<byte> lengths)
        {
            var count = new int[16];
            foreach (byte length in lengths) if (length != 0) count[length]++;
            var next = new int[16];
            for (int bits = 1, code = 0; bits <= 15; bits++)
            {
                code = (code + count[bits - 1]) << 1;
                next[bits] = code;
            }
            var codes = new (int, int)[lengths.Length];
            for (int symbol = 0; symbol < lengths.Length; symbol++)
                if (lengths[symbol] != 0) codes[symbol] = (next[lengths[symbol]]++, lengths[symbol]);
            return codes;
        }
    }
}
