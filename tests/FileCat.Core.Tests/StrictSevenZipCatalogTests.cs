using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Reflection;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.Filters;
using FileCat.Archives;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class StrictSevenZipCatalogTests
{
    private const string Password = "Public-7z-catalog-暗号-42829323";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static IEnumerable<object[]> Graphs() => from codec in Enumerable.Range(0, 7) from filter in Enumerable.Range(0, 13)
        from protection in Enumerable.Range(0, 3) select new object[] { codec, filter, protection };
    private static SevenZipCodecOptions Options(int codec, int filter) => new((SevenZipCodec)codec, Level: codec == 0 ? 0 : 1,
        DictionaryBytes: codec is 1 or 2 ? 1 << 20 : 8 << 20, ModelOrder: codec == 6 ? 4 : 0,
        ModelMemoryBytes: codec == 6 ? 1 << 20 : 0, Filter: (SevenZipFilter)filter, DeltaDistance: filter == 1 ? 7 : 1);
    private static byte[] Payload()
    {
        byte[] data = new byte[8197]; new Random(42829323).NextBytes(data);
        for (int i = 0; i + 16 < data.Length; i += 32)
        {
            data[i] = 0xe8; data[i + 4] = 0; data[i + 8] = 0xe9; data[i + 12] = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i + 16), 0x94000000u + (uint)i);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i + 20), 0x000000efu + ((uint)i << 8));
        }
        return data;
    }
    private static byte[] Create(byte[] data, SevenZipCodecOptions options, int protection, int copies = 1)
    {
        using var stage = new MemoryStream(); using var encryption = protection == 0 ? null : new SevenZipEncryptionSession(Password, protection == 2, new(), Ct);
        var entries = Enumerable.Range(0, copies).Select(i => new SevenZipWriteEntry("folder/file-" + i, ArchiveMemberType.File,
            new(1700000000, 123456700), 0x1a4, 1)).Concat([new("folder", ArchiveMemberType.Directory, new(1700000001, 100), 0x1ed),
            new("empty", ArchiveMemberType.File, new(0, 0), 0x180, 2)]).ToArray();
        SevenZipArchiveWriter.Create(stage, entries,
            new Dictionary<int, SevenZipInputDescriptor> { [1] = new(1, data.Length, Sha(data)), [2] = new(2, 0, Sha([])) },
            id => new MemoryStream(id == 1 ? data : [], false), options, Ct, SevenZipHeaderMode.Lzma2, encryption);
        return stage.ToArray();
    }

    [Theory] [MemberData(nameof(Graphs))]
    public void All_codecs_filters_and_password_modes_finish_complete_member_graphs_and_exact_packed_witnesses(int codec, int filter, int protection)
    {
        byte[] data = Payload(), archive = Create(data, Options(codec, filter), protection);
        using var source = new Observed(archive, 7); using var password = protection == 0 ? null : new SevenZipReadPasswordSession(Password, new(), Ct);
        var catalog = StrictSevenZipCatalog.Read(source, Ct, password); Assert.Equal(Sha(archive), catalog.Physical.Revision);
        Assert.Equal(3, catalog.Members.Count); Assert.Equal(Sha(data), catalog.Members[0].Sha256);
        Assert.All(catalog.Members.Skip(1), member => Assert.Equal(Sha([]), member.Sha256));
        Assert.Equal(filter == 12 ? 4 : 1, catalog.Members[0].Packed.Count);
        foreach (var packed in catalog.Members[0].Packed)
            Assert.Equal(Sha(archive.AsSpan((int)packed.Physical.Offset, (int)packed.Physical.Length)), packed.Sha256);
        Assert.True(catalog.Physical.EncodedHeaderEndVerified); Assert.True(source.CanRead); Assert.False(source.Disposed);
        Assert.Equal(archive.Length, source.Position); Assert.InRange(source.MaximumRead, 1, ArchiveIoAuthority.MaximumReadBytes);
        Assert.InRange(catalog.PeakDecoderHeapBytes, 1, ArchiveWritePolicy.MaxWorkerMemoryBytes);
        var options = Options(codec, filter);
        var profile = ArchiveWriteProfile.ForSevenZip(new((ArchiveSevenZipCodec)codec, (ArchiveSevenZipFilter)filter,
            options.ModelOrder, options.ModelMemoryBytes, options.DeltaDistance), options.Level, options.DictionaryBytes,
            protection == 0 ? null : new(protection == 2 ? ArchiveEncryptionMode.DataAndNames : ArchiveEncryptionMode.Data));
        var assessment = SevenZipSemanticCatalog.Assessment(catalog, profile, Ct);
        Assert.Equal(catalog.Physical.Revision, assessment.CatalogRevision);
        var observed = ArchiveSevenZipMetadata.Decode(assessment.Manifest.Members[0].Metadata);
        Assert.Equal(new ArchiveSevenZipMetadata(ArchiveMemberType.File, graph: ArchiveSevenZipEncoding.Payload(profile, data.Length)).Encode(),
            new ArchiveSevenZipMetadata(ArchiveMemberType.File, graph: observed.Graph).Encode());
        foreach (var member in assessment.Manifest.Members) Assert.Equal(member.Metadata.ToArray(), ArchiveSevenZipMetadata.Decode(member.Metadata).Encode());
        var features = ArchiveSevenZipFeatures.Decode(assessment.Manifest.Features); Assert.Equal(assessment.Manifest.Features.ToArray(), features.Encode());
        if (features.HeaderGraph is { } header)
            Assert.Equal(new ArchiveSevenZipMetadata(ArchiveMemberType.File, graph: ArchiveSevenZipEncoding.Header(profile)).Encode(),
                new ArchiveSevenZipMetadata(ArchiveMemberType.File, graph: header).Encode());
        if (Environment.GetEnvironmentVariable("FILECAT_7Z_CATALOG_EXPORT_ROOT") is { } root)
        {
            string dir = Path.Combine(root, $"codec-{codec}-filter-{filter}-protection-{protection}"); Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "members.7z"), archive);
            File.WriteAllText(Path.Combine(dir, "members.json"), JsonSerializer.Serialize(new { synthetic_public_vector = true,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                password = Password, encrypted = protection != 0, revision = catalog.Physical.Revision,
                members = catalog.Members.Select(member => new { name = member.Physical.Name, type = (int)member.Physical.Type,
                    length = member.Physical.Length, sha256 = member.Sha256, attributes = member.Physical.Attributes,
                    modified = member.Physical.ModifiedFileTime, packed = member.Packed }) }));
        }
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Windows_preflight_covers_copy_encryption_and_four_branch_Zstandard_graphs(int protection)
        => All_codecs_filters_and_password_modes_finish_complete_member_graphs_and_exact_packed_witnesses(protection == 0 ? 0 : 5, protection == 0 ? 0 : 12, protection);

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Windows_preflight_covers_Deflate_empty_BCJ2_branches(int protection)
        => All_codecs_filters_and_password_modes_finish_complete_member_graphs_and_exact_packed_witnesses(4, 12, protection);

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void Windows_preflight_covers_each_codec(int codec)
        => All_codecs_filters_and_password_modes_finish_complete_member_graphs_and_exact_packed_witnesses(codec, 0, 0);

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Deflate_empty_input_has_a_complete_bitstream_at_every_effort_and_keeps_the_stage_open(int level)
    {
        using var packed = new MemoryStream();
        var result = SevenZipCodecEncoder.Encode(new MemoryStream([], false), 0, packed, new(SevenZipCodec.Deflate, Level: level), Ct);
        Assert.Equal(Sha([]), result.Sha256); Assert.Equal(0, result.Length); Assert.Equal(2, result.PackedLength); Assert.True(packed.CanWrite);
        using var input = new MemoryStream(packed.ToArray(), false); using var decoder = new StrictDeflateReadStream(input, packed.Length, 0, Ct);
        Assert.Equal(-1, decoder.ReadByte()); Assert.True(decoder.Complete); Assert.Equal(2, decoder.CompressedBytesConsumed);
    }

    [Theory] [InlineData(1)] [InlineData(2)]
    public void Wrong_passwords_cannot_produce_a_complete_logical_catalog(int mode)
    {
        using var source = new MemoryStream(Create(Payload(), Options(0, 0), mode));
        using var password = new SevenZipReadPasswordSession("A different public password", new(), Ct);
        Assert.Equal(ArchiveWriteReason.InvalidArchive, Assert.Throws<ArchiveWriteException>(() => StrictSevenZipCatalog.Read(source, Ct, password)).Reason);
        Assert.True(source.CanRead);
    }

    [Fact]
    public void A_later_member_does_not_retain_the_previous_decoder_reservation()
    {
        byte[] data = Payload(); using var source = new MemoryStream(Create(data, Options(5, 12), 0, copies: 5));
        var catalog = StrictSevenZipCatalog.Read(source, Ct); Assert.Equal(7, catalog.Members.Count);
        // Five cumulative reservations could not fit the worker envelope, so completing proves each one was released.
        Assert.True(5 * catalog.PeakDecoderHeapBytes > ArchiveWritePolicy.MaxWorkerMemoryBytes - (64 << 20));
        Assert.True(catalog.PeakDecoderHeapBytes < ArchiveWritePolicy.MaxWorkerMemoryBytes);
        Assert.All(catalog.Members.Take(5), member => Assert.Equal(Sha(data), member.Sha256));
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Packed_truncation_trailing_bytes_and_wrong_sizes_refuse_after_real_codec_processing(int damage)
    {
        byte[] data = Payload(); using var output = new MemoryStream();
        var encoded = SevenZipCodecEncoder.Encode(new MemoryStream(data, false), data.Length, output, Options(1, 0), Ct);
        byte[] packed = output.ToArray(); long size = data.Length;
        if (damage == 0) size--; if (damage == 1) size++;
        if (damage == 2) packed = [.. packed, 0]; if (damage == 3) packed = packed[..^1];
        using var source = new Observed([.. new byte[32], .. packed], 3);
        var folder = new SevenZipRawFolder(encoded.Coders, [], [0], [size], 0, encoded.Crc32, 0);
        Assert.Throws<ArchiveWriteException>(() =>
        {
            using var decoder = new StrictSevenZipFolderDecoder(source, folder, [new(32, packed.Length, ArchiveCrc32.Compute(packed))], null, new(), Ct);
            using var target = new MemoryStream(); decoder.CopyTo(target); _ = decoder.Witnesses;
        });
        Assert.True(source.CanRead);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Cyclic_disconnected_missing_password_and_over_budget_graphs_refuse_before_packed_IO(int damage)
    {
        var copy = new SevenZipCoder([0], []); using var source = new Observed(new byte[64], 3);
        SevenZipRawFolder folder = damage switch
        {
            0 => new([copy, copy], [new(0, 0)], [1], [1, 1], 1, null, 0),
            1 => new([copy, copy], [new(1, 1)], [0], [1, 1], 0, null, 0),
            2 => new([new([6, 0xf1, 7, 1], [19])], [], [0], [1], 0, null, 0),
            _ => new(Enumerable.Repeat(new SevenZipCoder([0x21], [28]), 20).ToArray(),
                Enumerable.Range(1, 19).Select(i => new SevenZipBinding(i, i - 1)).ToArray(), [0], Enumerable.Repeat(1L, 20).ToArray(), 19, null, 0),
        };
        Assert.Throws<ArchiveWriteException>(() => { using var decoder = new StrictSevenZipFolderDecoder(source, folder, [new(32, 16, null)], null, new(), Ct); });
        Assert.Equal(0, source.BytesRead); Assert.True(source.CanRead);
    }

    [Fact]
    public void A_changed_source_header_during_member_decoding_cannot_return_a_catalog()
    {
        byte[] archive = Create(Payload(), Options(0, 0), 0); using var source = new Observed(archive, 4096, mutate: true);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => StrictSevenZipCatalog.Read(source, Ct)).Reason);
    }

    [Fact]
    public void A_signature_change_before_the_first_whole_hash_cannot_leave_stale_public_header_facts()
    {
        byte[] archive = Create(Payload(), Options(0, 0), 0);
        using var source = new Observed(archive, 4096, mutateSignatureBeforeBaseline: true);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => StrictSevenZipCatalog.Read(source, Ct)).Reason);
    }

    [Theory] [InlineData(10, 4u)] [InlineData(10, 65536u)] [InlineData(10, 0xfffffffcu)]
    [InlineData(11, 4u)] [InlineData(11, 65536u)] [InlineData(11, 0xfffffffcu)]
    public void Aligned_branch_offsets_survive_fragmentation_and_export_independent_SDK_vectors(int method, uint offset)
    {
        byte[] data = Payload(), properties = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(properties, offset);
        using var packed = new MemoryStream();
        using (Filter encoder = method == 10 ? new BCJFilterARM64(true, packed, offset) : new BCJFilterRISCV(true, packed, offset)) encoder.Write(data);
        byte[] encoded = packed.ToArray(); Assert.NotEqual(data, encoded);
        using var source = new Observed([.. new byte[32], .. encoded], 3);
        var folder = new SevenZipRawFolder([new([(byte)method], properties)], [], [0], [data.Length], 0, ArchiveCrc32.Compute(data), 0);
        using var decoder = new StrictSevenZipFolderDecoder(source, folder, [new(32, encoded.Length, ArchiveCrc32.Compute(encoded))], null, new(), Ct);
        using var output = new MemoryStream(); decoder.CopyTo(output); Assert.Equal(data, output.ToArray()); Assert.True(decoder.Complete);
        Assert.Equal(Sha(encoded), Assert.Single(decoder.Witnesses).Sha256);
        if (Environment.GetEnvironmentVariable("FILECAT_7Z_CATALOG_EXPORT_ROOT") is { } root)
        {
            string dir = Path.Combine(root, "branch-offsets", (method == 10 ? "Arm64" : "RiscV") + "-" + offset); Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "input.bin"), data); File.WriteAllBytes(Path.Combine(dir, "encoded.bin"), encoded);
        }
    }

    [Theory] [InlineData(10, 1u)] [InlineData(10, 2u)] [InlineData(11, 1u)]
    public void Unaligned_branch_offsets_refuse_before_packed_IO(int method, uint offset)
    {
        byte[] properties = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(properties, offset); using var source = new Observed(new byte[64], 3);
        var folder = new SevenZipRawFolder([new([(byte)method], properties)], [], [0], [16], 0, null, 0);
        Assert.Throws<InvalidDataException>(() => { using var decoder = new StrictSevenZipFolderDecoder(source, folder, [new(32, 16, null)], null, new(), Ct); });
        Assert.Equal(0, source.BytesRead);
    }

    [Theory] [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Bzip2_releases_plaintext_state_even_when_the_source_is_borrowed(bool asynchronous, bool leaveOpen)
    {
        byte[] data = Payload(); using var packed = new MemoryStream();
        SevenZipCodecEncoder.Encode(new MemoryStream(data, false), data.Length, packed, Options(3, 0), Ct);
        using var source = new MemoryStream(packed.ToArray(), false);
        var decoder = asynchronous ? await CBZip2InputStream.CreateAsync(source, false, leaveOpen, cancellationToken: Ct) : CBZip2InputStream.Create(source, false, leaveOpen);
        using var output = new MemoryStream(); if (asynchronous) await decoder.CopyToAsync(output, Ct); else decoder.CopyTo(output);
        Assert.Equal(data, output.ToArray());
        Array[] states = typeof(CBZip2InputStream).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Where(field => field.FieldType.IsArray)
            .Select(field => field.GetValue(decoder)).OfType<Array>().ToArray();
        Assert.Contains(states.SelectMany(Flatten), value => !value.Equals(Activator.CreateInstance(value.GetType())));
        if (asynchronous) await decoder.DisposeAsync(); else decoder.Dispose();
        Assert.All(states.SelectMany(Flatten), value => Assert.Equal(Activator.CreateInstance(value.GetType()), value));
        Assert.Equal(leaveOpen, source.CanRead); Assert.False(decoder.CanRead); Assert.Throws<ObjectDisposedException>(() => decoder.ReadByte()); decoder.Dispose();
    }

    [Fact]
    public void Delta_plaintext_and_filter_buffers_are_cleared_even_when_source_disposal_fails()
    {
        using var source = new ThrowingDispose(); var filter = new DeltaFilter(true, source, [6]); filter.Write(Payload());
        var states = new List<byte[]> { (byte[])typeof(DeltaFilter).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(filter)! };
        states.AddRange(typeof(Filter).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Where(field => field.FieldType == typeof(byte[]))
            .Select(field => field.GetValue(filter)).OfType<byte[]>());
        Assert.Contains(states.SelectMany(bytes => bytes), value => value != 0);
        Assert.Throws<IOException>(() => filter.Dispose()); Assert.All(states.SelectMany(bytes => bytes), value => Assert.Equal(0, value)); filter.Dispose();
    }

    private static IEnumerable<object> Flatten(Array values) => values.Cast<object>().SelectMany(value => value is Array nested ? Flatten(nested) : [value]);
    private sealed class ThrowingDispose : MemoryStream
    {
        private bool disposed;
        protected override void Dispose(bool disposing) { if (disposed) return; disposed = true; base.Dispose(disposing); throw new IOException("Synthetic disposal failure"); }
    }

    private sealed class Observed(byte[] bytes, int chunk, bool mutate = false, bool mutateSignatureBeforeBaseline = false) : Stream
    {
        private readonly MemoryStream inner = new(bytes, writable: false); private int wholeScans;
        internal long BytesRead; internal int MaximumRead; internal bool Disposed;
        public override int Read(Span<byte> b) { MaximumRead = Math.Max(MaximumRead, b.Length); int n = inner.Read(b[..Math.Min(b.Length, chunk)]); BytesRead += n; return n; }
        public override int Read(byte[] b, int o, int c) => Read(b.AsSpan(o, c));
        protected override void Dispose(bool disposing) { Disposed = true; if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => !Disposed; public override bool CanSeek => !Disposed; public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set { if (value == 0) wholeScans++; if (mutate && wholeScans == 3 && value == 32 || mutateSignatureBeforeBaseline && wholeScans == 1 && value == 0) bytes[7] ^= 1; inner.Position = value; } }
        public override long Seek(long o, SeekOrigin s) => Position = s switch { SeekOrigin.Begin => o, SeekOrigin.Current => Position + o, _ => Length + o };
        public override void Flush() { } public override void SetLength(long n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}
