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

    public static TheoryData<string> Rar4Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.EnumerateFiles(Fixtures).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            if (!name.StartsWith("Rar", StringComparison.Ordinal) || name.StartsWith("Rar5", StringComparison.Ordinal)) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\.part0*([2-9]|\d\d+)\.rar$|\.r\d\d$|\.\d\d\d$")) continue;
            if (!name.EndsWith(".rar", StringComparison.Ordinal) && !name.EndsWith(".001", StringComparison.Ordinal)) continue;
            data.Add(name);
        }
        return data;
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
        var reference = Snapshot(() => SharpArchiveReader.Open(path, rar: true));
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
