using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FileCat.Archives;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using SharpCompress.Common.SevenZip;
using SharpCompress.Compressors.LZMA;
using SharpCompress.Compressors.LZMA.Utilities;

namespace FileCat.Core.Tests;

public sealed class StrictSevenZipFramingTests
{
    private const string PublicPassword = "Public-7z-framing-暗号-42829308";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static IEnumerable<object[]> Profiles() => from codec in Enumerable.Range(0, 7) from header in Enumerable.Range(0, 2)
        from protection in Enumerable.Range(0, 3) select new object[] { codec, header, protection };

    [Theory]
    [MemberData(nameof(Profiles))]
    public void All_codecs_header_modes_and_password_modes_preserve_exact_metadata_graphs_extents_and_revision(int codec, int header, int protection)
    {
        byte[] data = new byte[65553]; new Random(42829308).NextBytes(data);
        for (int index = 8192; index < data.Length; index++) data[index] = data[index % 8192];
        var entries = new SevenZipWriteEntry[]
        {
            new("folder", ArchiveMemberType.Directory, new(0, 0), 0x1ed),
            new("folder/é-界", ArchiveMemberType.File, new(1700000000, 123456700), 0x1a0, 1, new(-1, 999999900), new(4294967296, 100)),
            new("empty", ArchiveMemberType.File, new(-1, 999999900), 0x180, 2),
        };
        using var output = new MemoryStream(); using var encryption = protection == 0 ? null : new SevenZipEncryptionSession(PublicPassword, protection == 2, new(), Ct);
        var written = SevenZipArchiveWriter.Create(output, entries,
            new Dictionary<int, SevenZipInputDescriptor> { [1] = new(1, data.Length, Digest(data)), [2] = new(2, 0, Digest([])) },
            id => new MemoryStream(id == 1 ? data : [], false), new((SevenZipCodec)codec), Ct, (SevenZipHeaderMode)header, encryption);
        byte[] archive = output.ToArray(); using var source = new Observed(archive, chunk: 7);
        using var password = protection == 0 ? null : new SevenZipReadPasswordSession(PublicPassword, new(), Ct);
        var observed = StrictSevenZipFraming.Read(source, Ct, password);
        Assert.Equal(Digest(archive), observed.Revision); Assert.Equal(archive.Length, observed.Length); Assert.Equal(4, observed.MinorVersion);
        Assert.InRange(source.MaximumRead, 1, ArchiveIoAuthority.MaximumReadBytes); Assert.True(source.CanRead);
        Assert.True(source.BytesRead >= 2L * archive.Length); Assert.Equal(archive.Length, source.Position);
        Assert.Equal(header != 0 || protection == 2, observed.EncodedHeader is not null);
        Assert.True(observed.EncodedHeaderEndVerified);
        Assert.Equal(entries.Length, observed.Files.Members.Count);
        for (int index = 0; index < entries.Length; index++)
        {
            var actual = observed.Files.Members[index]; var expected = entries[index];
            Assert.Equal(expected.Name, actual.Name); Assert.Equal(expected.Type, actual.Type); Assert.Equal(0, actual.Occurrence);
            Assert.Equal(FileTime(expected.Modified), actual.ModifiedFileTime); Assert.Equal(expected.Created is { } c ? FileTime(c) : null, actual.CreatedFileTime);
            Assert.Equal(expected.Accessed is { } a ? FileTime(a) : null, actual.AccessedFileTime);
            Assert.Equal((uint)((expected.Mode | (expected.Type == ArchiveMemberType.File ? 0x8000 : 0x4000)) << 16) | 0x8000u |
                (expected.Type == ArchiveMemberType.Directory ? 0x10u : 0x20u), actual.Attributes);
            Assert.Equal(index == 1 ? data.Length : 0, actual.Length);
            Assert.Equal(index == 1 ? 0 : (int?)null, actual.Folder);
        }
        var folder = Assert.Single(observed.Streams.Folders); var content = written.Members[1].Content!;
        Assert.Equal(protection != 0, folder.Encrypted); Assert.Equal(content.Crc32, folder.Crc32);
        Assert.Equal(content.UnpackedSizes, folder.UnpackedSizes); Assert.Equal(content.Bindings, folder.Bindings);
        Assert.Equal(content.Coders.Count, folder.Coders.Count);
        for (int index = 0; index < content.Coders.Count; index++)
        { Assert.Equal(content.Coders[index].MethodId, folder.Coders[index].MethodId); Assert.Equal(content.Coders[index].Properties, folder.Coders[index].Properties); }
        Assert.Equal(content.PackedStreams.Count, observed.Streams.Packed.Count);
        Assert.Equal(content.PackedLength, observed.Streams.Packed.Sum(pack => pack.Length));
        if (Environment.GetEnvironmentVariable("FILECAT_7Z_FRAMING_EXPORT_ROOT") is { Length: > 0 } exports)
        {
            string fixtureDirectory = Path.Combine(exports, $"codec-{codec}-header-{header}-protection-{protection}"); Directory.CreateDirectory(fixtureDirectory);
            File.WriteAllBytes(Path.Combine(fixtureDirectory, "members.7z"), archive);
            File.WriteAllText(Path.Combine(fixtureDirectory, "observed.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                synthetic_public_vector = true, public_synthetic_password = PublicPassword, encrypted = protection != 0,
                archive_sha256 = observed.Revision,
                members = observed.Files.Members.Select(member => new { name = member.Name, directory = member.Type == ArchiveMemberType.Directory,
                    length = member.Length, attributes = member.Attributes, created = member.CreatedFileTime, accessed = member.AccessedFileTime,
                    modified = member.ModifiedFileTime, sha256 = Digest(member.Folder is null ? [] : data) }),
                packs = observed.Streams.Packed,
                folders = observed.Streams.Folders.Select(folder => new { coders = folder.Coders.Select(coder => new { method = Convert.ToHexString(coder.MethodId),
                    properties = Convert.ToHexString(coder.Properties), inputs = coder.Inputs, outputs = coder.Outputs }), bindings = folder.Bindings,
                    packed_inputs = folder.PackedInputs, unpacked_sizes = folder.UnpackedSizes, crc32 = folder.Crc32 }),
            }));
        }
        if (codec == 0 && header == 0 && protection == 0 && Environment.GetEnvironmentVariable("FILECAT_7Z_FRAMING_EXPORT_ROOT") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "runtime.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                runtime_directory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            }));
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public void Every_filter_including_four_input_BCJ2_has_an_exact_observed_graph(int filter)
    {
        byte[] data = new byte[65553]; new Random(42829309).NextBytes(data);
        using var source = new MemoryStream(Create(data, new(SevenZipCodec.Copy, Filter: (SevenZipFilter)filter), 0, false));
        var catalog = StrictSevenZipFraming.Read(source, Ct); var folder = Assert.Single(catalog.Streams.Folders);
        Assert.Equal(filter == 0 ? 1 : filter == 12 ? 4 : 2, folder.Coders.Count); Assert.Equal(filter == 12 ? 4 : 1, catalog.Streams.Packed.Count);
        Assert.Equal(data.Length, folder.Length); Assert.Equal(0, folder.FirstPack);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void Encrypted_BCJ2_observations_keep_all_four_cipher_streams(int protection)
    {
        using var source = new MemoryStream(Create(new byte[8193], new(SevenZipCodec.Copy, Filter: SevenZipFilter.Bcj2), protection, true));
        using var password = new SevenZipReadPasswordSession(PublicPassword, new(), Ct);
        var catalog = StrictSevenZipFraming.Read(source, Ct, password); var folder = Assert.Single(catalog.Streams.Folders);
        Assert.Equal(8, folder.Coders.Count); Assert.Equal(4, folder.Coders.Count(coder => SevenZipStrictMetadata.Method(coder) == 0x06f10701));
        Assert.Equal(4, folder.PackedInputs.Count); Assert.All(catalog.Streams.Packed, pack => Assert.Equal(0, pack.Length & 15));
    }

    [Fact]
    public void Raw_optional_times_attributes_occurrences_and_unicode_spelling_are_not_projected_or_normalized()
    {
        byte[] names = [.. Encoding.Unicode.GetBytes("é\0é\0é\0")];
        byte[] files = [5, 3, 14, 1, 0xe0, 15, 1, 0xe0, 17, .. Number(names.Length + 1), 0, .. names,
            20, 19, 0, 0xa0, 0, .. U64(0), .. U64(ulong.MaxValue), 21, 7, 0, 0x40, 0, .. U32(0x81a48020), 0];
        using var source = new MemoryStream(Container([], [1, .. files, 0])); var members = StrictSevenZipFraming.Read(source, Ct).Files.Members;
        Assert.Equal(new[] { "é", "é", "é" }, members.Select(member => member.Name)); Assert.Equal(new[] { 0, 0, 1 }, members.Select(member => member.Occurrence));
        Assert.Equal(0UL, members[0].ModifiedFileTime); Assert.Null(members[1].ModifiedFileTime); Assert.Equal(ulong.MaxValue, members[2].ModifiedFileTime);
        Assert.Null(members[0].Attributes); Assert.Equal(0x81a48020u, members[1].Attributes); Assert.Null(members[2].Attributes);
        Assert.All(members, member => { Assert.Null(member.Crc32); Assert.Null(member.Folder); });
    }

    [Fact]
    public void Every_truncated_prefix_and_trailing_byte_refuses_without_owning_the_source()
    {
        byte[] archive = Container([1, 2, 3], RawHeader(Streams([1, 1, 0], 3, 3), FileRecords("file")));
        for (int length = 0; length < archive.Length; length++) Refuse(archive[..length], ArchiveWriteReason.InvalidArchive);
        Refuse([.. archive, 0], ArchiveWriteReason.InvalidArchive);
    }

    public static IEnumerable<object[]> MalformedMetadata()
    {
        yield return [RawHeader([], FileRecords("../evil", empty: true)), ArchiveWriteReason.UnsupportedFeature];
        yield return [RawHeader([], FileRecords("file", empty: true, extra: [17, 5, 0, 0x78, 0, 0, 0])), ArchiveWriteReason.InvalidArchive];
        yield return [new byte[] { 1, 5, 1, 14, 1, 0x80, 15, 1, 0x80, 17, 5, 0, 0, 0xd8, 0, 0, 0, 0 }, ArchiveWriteReason.InvalidArchive];
        yield return [RawHeader([], FileRecords("file", empty: true, extra: [24, 1, 0])), ArchiveWriteReason.UnsupportedFeature];
        yield return [RawHeader([], FileRecords("file", empty: true, extra: [16, 1, 0x80])), ArchiveWriteReason.UnsupportedFeature];
        yield return [RawHeader([], FileRecords("file", empty: true, extra: [21, 6, 1, 0, .. U32(0x400)])), ArchiveWriteReason.UnsupportedFeature];
        yield return [(byte[])[1, 5, .. Number(100001), 0, 0], ArchiveWriteReason.BudgetExceeded];
        yield return [RawHeader(Streams([0], 0, 0), FileRecords("file")), ArchiveWriteReason.InvalidArchive];
        yield return [RawHeader(Streams([1, 0x41, 0], 0, 0), FileRecords("file")), ArchiveWriteReason.UnsupportedFeature];
        yield return [RawHeader(Streams([1, 1, 0xff], 0, 0), FileRecords("file")), ArchiveWriteReason.UnsupportedFeature];
        yield return [RawHeader(Streams([.. Number(33)], 0, 0), FileRecords("file")), ArchiveWriteReason.BudgetExceeded];
        yield return [RawHeader(Streams([2, 1, 0, 1, 0, 0, 0], 0, 0, outputs: 2), FileRecords("file")), ArchiveWriteReason.InvalidArchive];
    }
    [Theory]
    [MemberData(nameof(MalformedMetadata))]
    public void Unknown_ambiguous_hostile_or_over_budget_metadata_refuses(byte[] header, ArchiveWriteReason reason)
        => Refuse(Container([], header), reason);

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void Header_password_and_KDF_failures_keep_controlled_reasons(int protection)
    {
        byte[] archive = Create([1, 2, 3], new(SevenZipCodec.Copy), protection, true);
        if (protection == 1)
        { using var source = new MemoryStream(archive); Assert.True(Assert.Single(StrictSevenZipFraming.Read(source, Ct).Streams.Folders).Encrypted); return; }
        Refuse(archive, ArchiveWriteReason.UnsupportedFeature);
        using (var source = new MemoryStream(archive)) using (var wrong = new SevenZipReadPasswordSession("wrong-public-password", new(), Ct))
            Assert.Equal(ArchiveWriteReason.InvalidArchive, Assert.Throws<ArchiveWriteException>(() => StrictSevenZipFraming.Read(source, Ct, wrong)).Reason);
        using (var source = new MemoryStream(archive)) using (var limited = new SevenZipReadPasswordSession(PublicPassword, new(maxRounds: 1), Ct))
            Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => StrictSevenZipFraming.Read(source, Ct, limited)).Reason);
    }

    [Fact]
    public void Packed_CRC_and_whole_source_revision_are_checked_separately_from_plaintext_content()
    {
        byte[] archive = Create([1, 2, 3], new(SevenZipCodec.Copy), 0, false); byte[] bad = (byte[])archive.Clone(); bad[32] ^= 1;
        Refuse(bad, ArchiveWriteReason.InvalidArchive);
        long header = 32 + BinaryPrimitives.ReadInt64LittleEndian(archive.AsSpan(12)); bool changed = false;
        using var source = new Observed(archive, onPosition: position => { if (position == header && !changed) { archive[7] = 3; changed = true; } });
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => StrictSevenZipFraming.Read(source, Ct)).Reason);
    }

    [Fact]
    public void Cancellation_and_header_size_limits_stop_before_claimed_large_allocation()
    {
        byte[] archive = Create([1, 2, 3], new(SevenZipCodec.Copy), 0, false); using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var source = new Observed(archive, onPosition: _ => stop.Cancel());
        Assert.ThrowsAny<OperationCanceledException>(() => StrictSevenZipFraming.Read(source, stop.Token)); Assert.True(source.CanRead);
        byte[] signature = Container([], []); BinaryPrimitives.WriteUInt64LittleEndian(signature.AsSpan(20), (ulong)ArchiveWritePolicy.MaxMetadataBytes + 1);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(8), ArchiveCrc32.Compute(signature.AsSpan(12)));
        using var large = new ClaimedHeader(signature, 33 + ArchiveWritePolicy.MaxMetadataBytes);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => StrictSevenZipFraming.Read(large, Ct)).Reason); Assert.Equal(32, large.Position);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void Empty_created_archives_preserve_header_mode_and_have_no_invented_payload_folders(bool hidden, bool compressed)
    {
        using var output = new MemoryStream(); using var secret = hidden ? new SevenZipEncryptionSession(PublicPassword, true, new(), Ct) : null;
        SevenZipArchiveWriter.Create(output, [], new Dictionary<int, SevenZipInputDescriptor>(), _ => throw new InvalidOperationException(),
            new(SevenZipCodec.Copy), Ct, compressed ? SevenZipHeaderMode.Lzma2 : SevenZipHeaderMode.Raw, secret);
        output.Position = 0; using var password = hidden ? new SevenZipReadPasswordSession(PublicPassword, new(), Ct) : null;
        var catalog = StrictSevenZipFraming.Read(output, Ct, password);
        Assert.Empty(catalog.Files.Members); Assert.Empty(catalog.Streams.Folders); Assert.Empty(catalog.Streams.Packed);
        Assert.False(catalog.MainStreamsPresent); Assert.Equal(hidden || compressed, catalog.EncodedHeader is not null);
    }

    [Fact]
    public void A_signature_only_empty_container_and_explicit_empty_streams_are_accounted_completely()
    {
        using var minimal = new MemoryStream(Container([], [])); var bare = StrictSevenZipFraming.Read(minimal, Ct);
        Assert.Empty(bare.Files.Members); Assert.False(bare.MainStreamsPresent); Assert.Equal(0, bare.NextHeaderLength);
        using var explicitStreams = new MemoryStream(Container([], [1, 4, 0, 5, 0, 0, 0]));
        var explicitCatalog = StrictSevenZipFraming.Read(explicitStreams, Ct); Assert.True(explicitCatalog.MainStreamsPresent); Assert.Empty(explicitCatalog.Streams.Packed);
    }

    [Fact]
    public void Legal_padded_record_identifiers_and_numbers_are_not_treated_as_unknown_metadata()
    {
        byte[] header = [0x80, 1, 0x80, 5, 0x80, 1, 0x80, 14, 0x80, 1, 0x80, 0x80, 15, 0x80, 1, 0x80,
            0x80, 17, 0x80, 5, 0, 0x78, 0, 0, 0, 0x80, 0, 0x80, 0];
        byte[] archive = Container([], header); using var source = new MemoryStream(archive); var member = Assert.Single(StrictSevenZipFraming.Read(source, Ct).Files.Members);
        Assert.Equal("x", member.Name); Assert.Equal(ArchiveMemberType.File, member.Type); Assert.Equal(0, member.Length);
        ExportPadded("raw", archive, "x", []);
    }

    [Fact]
    public void Encoded_header_record_identifiers_use_the_same_padded_integer_rules()
    {
        byte[] archive = Create([1, 2, 3], new(SevenZipCodec.Copy), 0, true);
        int start = 32 + (int)BinaryPrimitives.ReadInt64LittleEndian(archive.AsSpan(12)); Assert.Equal(23, archive[start]);
        byte[] padded = Container(archive[32..start], [0x80, 23, .. archive[(start + 1)..]]);
        using var source = new MemoryStream(padded);
        Assert.Equal("file", Assert.Single(StrictSevenZipFraming.Read(source, Ct).Files.Members).Name);
        ExportPadded("encoded", padded, "file", [1, 2, 3]);
    }

    [Fact]
    public void Physical_observations_do_not_pretend_to_verify_a_decoded_member_checksum()
    {
        using var source = new MemoryStream(Container([1, 2, 3], RawHeader(Streams([1, 1, 0], 3, 3, folderCrc: 1), FileRecords("file"))));
        var catalog = StrictSevenZipFraming.Read(source, Ct); Assert.Equal(1u, Assert.Single(catalog.Files.Members).Crc32);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Encoded_header_end_and_extent_damage_refuses_even_when_the_outer_checksums_are_valid(int damage)
    {
        byte[] inner = RawHeader([], FileRecords("empty", empty: true));
        byte[] packed = [1, 0, (byte)(inner.Length - 1), .. inner, 0]; int declared = inner.Length;
        using (var valid = new MemoryStream(Container(packed, [23, .. Streams([1, 0x21, 0x21, 1, 0], packed.Length, declared)])))
            Assert.True(StrictSevenZipFraming.Read(valid, Ct).EncodedHeaderEndVerified);
        if (damage == 0) packed = [.. packed, 0x7e];
        if (damage == 1) packed = packed[..^1];
        if (damage == 2) declared--;
        if (damage == 3) declared++;
        if (damage == 4) packed = [.. packed, 1, 0, 0, 0];
        Refuse(Container(packed, [23, .. Streams([1, 0x21, 0x21, 1, 0], packed.Length, declared)]), ArchiveWriteReason.InvalidArchive);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Failed_decoder_construction_disposes_preceding_AES_transforms(bool asynchronous)
    {
        using var source = new MemoryStream(new byte[16]); var transform = new TestTransform(); var provider = new TestPassword(transform);
        var folder = new CFolder { _coders = [new() { _methodId = new(0x06f10701), _props = [0], _numInStreams = 1, _numOutStreams = 1 },
            new() { _methodId = new(0x123456), _props = [], _numInStreams = 1, _numOutStreams = 1 }],
            _bindPairs = [new() { _inIndex = 1, _outIndex = 0 }], _packStreams = [0], _unpackSizes = [16, 16] };
        if (asynchronous) await Assert.ThrowsAnyAsync<Exception>(async () => { using var decoded = await DecoderStreamHelper.CreateDecoderStreamAsync(source, 0, [16], folder, provider, Ct); });
        else Assert.ThrowsAny<Exception>(() => DecoderStreamHelper.CreateDecoderStream(source, 0, [16], folder, provider));
        Assert.Equal(1, transform.Disposals); Assert.True(source.CanRead);
    }

    private static byte[] Create(byte[] data, SevenZipCodecOptions options, int protection, bool compressed)
    {
        using var output = new MemoryStream(); using var encryption = protection == 0 ? null : new SevenZipEncryptionSession(PublicPassword, protection == 2, new(), Ct);
        SevenZipArchiveWriter.Create(output, [new("file", ArchiveMemberType.File, new(0, 0), 0x180, 1)],
            new Dictionary<int, SevenZipInputDescriptor> { [1] = new(1, data.Length, Digest(data)) }, _ => new MemoryStream(data, false), options, Ct,
            compressed ? SevenZipHeaderMode.Lzma2 : SevenZipHeaderMode.Raw, encryption);
        return output.ToArray();
    }
    private static ulong FileTime(ArchiveTimestamp time) => checked((ulong)((time.Seconds + 11644473600) * 10000000 + time.Nanoseconds / 100));
    private static void ExportPadded(string kind, byte[] archive, string name, byte[] data)
    {
        if (Environment.GetEnvironmentVariable("FILECAT_7Z_FRAMING_EXPORT_ROOT") is not { Length: > 0 } root) return;
        string directory = Path.Combine(root, "padded-" + kind); Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "members.7z"), archive);
        File.WriteAllText(Path.Combine(directory, "expected.json"), System.Text.Json.JsonSerializer.Serialize(new { name, length = data.Length, sha256 = Digest(data) }));
    }
    private static void Refuse(byte[] archive, ArchiveWriteReason reason)
    { using var source = new MemoryStream(archive); Assert.Equal(reason, Assert.Throws<ArchiveWriteException>(() => StrictSevenZipFraming.Read(source, Ct)).Reason); Assert.True(source.CanRead); }
    private static byte[] Number(int value)
    {
        var bytes = new List<byte>(); byte first = 0, mask = 128; int extra = 0; ulong number = (ulong)value;
        for (; extra < 8; extra++) { if (number < 1UL << (7 * (extra + 1))) { first |= (byte)(number >> (8 * extra)); break; } first |= mask; mask >>= 1; }
        bytes.Add(first); for (int index = 0; index < extra; index++) bytes.Add((byte)(number >> (8 * index))); return bytes.ToArray();
    }
    private static byte[] U32(uint value) { byte[] bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); return bytes; }
    private static byte[] U64(ulong value) { byte[] bytes = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(bytes, value); return bytes; }
    private static byte[] RawHeader(byte[] streams, byte[] files) => [1, .. (streams.Length == 0 ? Array.Empty<byte>() : new byte[] { 4 }), .. streams, .. files, 0];
    private static byte[] Streams(byte[] folder, int packed, int unpacked, int outputs = 1, uint? folderCrc = null)
        => [6, 0, 1, 9, .. Number(packed), 0, 7, 11, 1, 0, .. folder, 12, .. Enumerable.Range(0, outputs).SelectMany(_ => Number(unpacked)),
            .. (folderCrc is { } crc ? new byte[] { 10, 1 }.Concat(U32(crc)) : []), 0, 8, 0, 0];
    private static byte[] FileRecords(string name, bool empty = false, byte[]? extra = null)
    {
        byte[] names = Encoding.Unicode.GetBytes(name + "\0");
        return [5, 1, .. (empty ? new byte[] { 14, 1, 0x80, 15, 1, 0x80 } : Array.Empty<byte>()), 17, .. Number(names.Length + 1), 0, .. names, .. (extra ?? []), 0];
    }
    private static byte[] Container(byte[] payload, byte[] header)
    {
        byte[] signature = new byte[32]; ((ReadOnlySpan<byte>)[0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c, 0, 4]).CopyTo(signature);
        BinaryPrimitives.WriteUInt64LittleEndian(signature.AsSpan(12), (ulong)payload.Length); BinaryPrimitives.WriteUInt64LittleEndian(signature.AsSpan(20), (ulong)header.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(28), ArchiveCrc32.Compute(header)); BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(8), ArchiveCrc32.Compute(signature.AsSpan(12)));
        return [.. signature, .. payload, .. header];
    }
    private sealed class TestPassword(TestTransform transform) : IPasswordProvider
    { public string? CryptoGetTextPassword() => throw new InvalidOperationException("Immutable password access is forbidden."); public SevenZipAesDecryptorFactory ArchiveDecryptorFactory => (_, _, _) => transform; }
    private sealed class TestTransform : ICryptoTransform
    {
        internal int Disposals; public int InputBlockSize => 16; public int OutputBlockSize => 16; public bool CanTransformMultipleBlocks => true; public bool CanReuseTransform => false;
        public int TransformBlock(byte[] input, int offset, int count, byte[] output, int start) { input.AsSpan(offset, count).CopyTo(output.AsSpan(start)); return count; }
        public byte[] TransformFinalBlock(byte[] input, int offset, int count) => input.AsSpan(offset, count).ToArray(); public void Dispose() => Disposals++;
    }
    private class Observed(byte[] data, int chunk = 65524, Action<long>? onPosition = null) : Stream
    {
        protected readonly MemoryStream Inner = new(data, false); internal long BytesRead; internal int MaximumRead;
        public override int Read(Span<byte> buffer) { MaximumRead = Math.Max(MaximumRead, buffer.Length); int n = Inner.Read(buffer[..Math.Min(buffer.Length, chunk)]); BytesRead += n; return n; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override long Position { get => Inner.Position; set { onPosition?.Invoke(value); Inner.Position = value; } }
        public override long Length => Inner.Length; public override bool CanRead => Inner.CanRead; public override bool CanSeek => true; public override bool CanWrite => false;
        protected override void Dispose(bool disposing) { if (disposing) Inner.Dispose(); base.Dispose(disposing); }
        public override void Flush() { } public override long Seek(long value, SeekOrigin origin) { Position = origin switch { SeekOrigin.Begin => value, SeekOrigin.Current => Position + value, SeekOrigin.End => Length + value, _ => throw new ArgumentOutOfRangeException(nameof(origin)) }; return Position; }
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int start, int count) => throw new NotSupportedException();
    }
    private sealed class ClaimedHeader(byte[] signature, long declaredLength) : Observed(signature)
    { public override long Length => declaredLength; }
}
