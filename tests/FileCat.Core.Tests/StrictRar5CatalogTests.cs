using System.Reflection;
using System.Security.Cryptography;
using FileCat.Archives;
using FileCat.Archives.Rar;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using static FileCat.Core.Tests.StrictRar5FramingTests;

namespace FileCat.Core.Tests;

public sealed class StrictRar5CatalogTests
{
    private const string PublicPassword = "Public-logical-RAR-暗号-42829306";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Digest(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    [Theory]
    [MemberData(nameof(Profiles), MemberType = typeof(StrictRar5FramingTests))]
    public void All_methods_and_password_modes_verify_every_member_and_both_full_physical_passes(int method, int protection)
    {
        byte[] data = new byte[131089]; new Random(42829306).NextBytes(data);
        for (int i = 4096; i < data.Length; i++) data[i] = data[i % 4096];
        byte[] archive = Write(data, method, protection);
        using var source = new ObservedSource(archive, chunk: 7);
        using var password = protection == 0 ? null : new Rar5ReadPasswordSession(PublicPassword, new(), Ct);
        var catalog = StrictRar5Catalog.Read(source, Ct, password);
        Assert.Equal(3, catalog.Members.Count); Assert.Equal(Digest(archive), catalog.Physical.Revision);
        Assert.Equal(new[] { Digest([]), Digest(data), Digest([]) }, catalog.Members.Select(m => m.Sha256));
        Assert.All(catalog.Members, m => Assert.Equal(Rar5FilterFeatures.None, m.Filters));
        Assert.Equal(new ArchiveTimestamp(1700000000, 123456789), catalog.Members[1].Physical.Modified);
        Assert.Equal(2L * archive.Length, source.BytesRead); Assert.Equal(1, source.Resets);
        Assert.InRange(source.MaximumRead, 1, ArchiveIoAuthority.MaximumReadBytes); Assert.True(source.CanRead);
        if (method == 0 && protection == 0 && Environment.GetEnvironmentVariable("FILECAT_RAR5_LOGICAL_EXPORT_ROOT") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "runtime.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                runtime_directory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                archive_sha256 = catalog.Physical.Revision,
            }));
        }
    }

    [Fact]
    public void Duplicate_occurrences_and_absent_metadata_survive_complete_content_verification()
    {
        byte[] owner = Extra(6, Var(3), Var(2), [255, 0], Var(0));
        byte[] file = Header(2, 3, FileBody("same", length: 3), owner, [1, 2, 3]);
        using var source = new MemoryStream(Container(file, file));
        var catalog = StrictRar5Catalog.Read(source, Ct);
        Assert.Equal(new[] { 0, 1 }, catalog.Members.Select(m => m.Physical.Occurrence));
        Assert.All(catalog.Members, m =>
        {
            Assert.Equal(Digest([1, 2, 3]), m.Sha256); Assert.Null(m.Physical.Crc32); Assert.Null(m.Physical.Modified);
            Assert.Equal(new byte[] { 255, 0 }, m.Physical.Owner!.UserName); Assert.Empty(m.Physical.Owner.GroupName!);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stored_checksums_are_verified_even_when_all_declared_bytes_are_present(bool blake)
    {
        // Empty-message BLAKE2sp independently covered by the reader's published vectors.
        byte[] expected = Convert.FromHexString("dd0e891776933f43c7d032b08a917e25741f8aa9a12c12e1cac8801500f2ca4f");
        byte[] extra = blake ? Extra(2, Var(0), expected) : [];
        byte[] valid = Container(Header(2, blake ? 3UL : 2UL, FileBody("checked", flags: 4, crc: 0), extra));
        using (var source = new MemoryStream(valid)) Assert.Equal(Digest([]), Assert.Single(StrictRar5Catalog.Read(source, Ct).Members).Sha256);
        if (blake) expected[0] ^= 1;
        byte[] invalid = Container(Header(2, blake ? 3UL : 2UL, FileBody("checked", flags: 4, crc: blake ? 0u : 1u),
            blake ? Extra(2, Var(0), expected) : []));
        Refuse(invalid, ArchiveWriteReason.InvalidArchive);
    }

    [Fact]
    public void Every_truncated_compressed_prefix_refuses_even_with_valid_rebuilt_container_headers()
    {
        byte[] packed = Encode([97]);
        for (int n = 0; n < packed.Length; n++) Refuse(Compressed(packed[..n], 1), ArchiveWriteReason.InvalidArchive);
        using var valid = new MemoryStream(Compressed(packed, 1));
        Assert.Equal(Digest([97]), Assert.Single(StrictRar5Catalog.Read(valid, Ct).Members).Sha256);
    }

    [Theory]
    [InlineData("Rar5.rar")]
    [InlineData("Rar5.none.rar")]
    [InlineData("Rar5.crc_blake2.rar")]
    public void Existing_independent_RAR5_fixtures_match_the_test_only_reference_reader(string fixture)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", fixture);
        // These fixture envelopes carry unsupported archive-index metadata. Preserve the refusal, then isolate
        // their unchanged independently encoded member streams for decoder interoperability; no production
        // normalization/conversion route is introduced by this test helper.
        using var file = File.OpenRead(path);
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() => StrictRar5Catalog.Read(file, Ct)).Reason);
        file.Position = 0; var raw = RarArchive.Read([file], Ct).Members; var records = new List<byte[]>();
        foreach (var member in raw)
        {
            Assert.False(member.SolidContinuation); Assert.False(member.Encrypted);
            Assert.True(member.Kind is MemberKind.File or MemberKind.Directory);
            var extent = Assert.Single(member.Segments); byte[] packed = new byte[extent.Length]; file.Position = extent.Offset; file.ReadExactly(packed);
            int dictionaryBits = 0;
            while ((0x20000L << dictionaryBits) < member.Dictionary) dictionaryBits++;
            Assert.True(dictionaryBits <= 15);
            ulong compression = (ulong)member.Method << 7 | (ulong)dictionaryBits << 10;
            byte[] extra = member.Blake2sp is null ? [] : Extra(2, Var(0), member.Blake2sp);
            ulong flags = (member.Kind == MemberKind.Directory ? 1UL : 0UL) | (member.Crc32 is null ? 0UL : 4UL);
            records.Add(Header(2, extra.Length == 0 ? 2UL : 3UL,
                FileBody(member.Name.Replace('\\', '/'), flags, member.UnpackedSize,
                    member.Kind == MemberKind.Directory ? 0x41edUL : 0x81a4UL, crc: member.Crc32 ?? 0, compression: compression), extra, packed));
        }
        using var minimal = new MemoryStream(Container(records.ToArray())); var catalog = StrictRar5Catalog.Read(minimal, Ct);
        using var reference = UpstreamRarReader.Open(path);
        var expected = reference.List(_ => { }, Ct).ToArray();
        Assert.Equal(expected.Length, catalog.Members.Count);
        Assert.Equal(3, catalog.Members.Count(m => m.Physical.Type == ArchiveMemberType.File));
        foreach (var (member, index) in catalog.Members.Select((m, i) => (m, i)))
        {
            Assert.Equal(expected[index].Path.Replace('\\', '/'), member.Physical.Name);
            if (expected[index].Kind != MemberKind.File) continue;
            using var data = reference.Open(expected[index].Index, Ct);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), member.Sha256);
        }
    }

    [Theory]
    [InlineData(0)] // Appended bytes hidden in the packed extent, including prefetched bytes.
    [InlineData(1)] // The final declared symbol is incomplete.
    [InlineData(2)] // Output size reached, but no last-block marker.
    [InlineData(3)] // Valid compressed output exceeds the declared size.
    [InlineData(4)] // A real, incomplete symbol appended after the declared output.
    [InlineData(5)] // Reserved block flags.
    public void Declared_output_size_never_substitutes_for_a_complete_compressed_ending(int damage)
    {
        byte[] packed = Encode([97]); long length = 1;
        Assert.Equal(7, packed[0] & 7); Assert.Equal(1, ((packed[0] >> 3) & 3) + 1);
        switch (damage)
        {
            case 0: packed = [.. packed, 0x42]; break;
            case 1: packed[0]--; FixBlockChecksum(packed); break;
            case 2: packed[0] &= 0xbf; FixBlockChecksum(packed); break;
            case 3: length = 0; break;
            case 4: packed = [.. packed, 0]; packed[0] &= 0xf8; packed[2]++; FixBlockChecksum(packed); break;
            case 5: packed[0] |= 0x20; FixBlockChecksum(packed); break;
        }
        Refuse(Compressed(packed, length), damage == 5 ? ArchiveWriteReason.UnsupportedFeature : ArchiveWriteReason.InvalidArchive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Filter_features_are_observed_after_complete_filtered_content_verification(int type)
    {
        using var source = new MemoryStream(Compressed(FilterBlock(type, 0, 4), 4));
        var member = Assert.Single(StrictRar5Catalog.Read(source, Ct).Members);
        Assert.Equal((Rar5FilterFeatures)(1 << type), member.Filters); Assert.Equal(Digest(new byte[4]), member.Sha256);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(10, 4)]
    public void Filters_extending_or_starting_beyond_the_member_are_not_silently_dropped(int offset, int length)
        => Refuse(Compressed(FilterBlock(0, offset, length), 4), ArchiveWriteReason.InvalidArchive);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Missing_wrong_and_budget_limited_passwords_have_controlled_outcomes(int protection)
    {
        byte[] archive = Write([1, 2, 3], 3, protection);
        Refuse(archive, ArchiveWriteReason.UnsupportedFeature);
        using (var source = new MemoryStream(archive))
        using (var wrong = new Rar5ReadPasswordSession("wrong-public-password", new(), Ct))
            Assert.Equal(ArchiveWriteReason.InvalidArchive, Assert.Throws<ArchiveWriteException>(() => StrictRar5Catalog.Read(source, Ct, wrong)).Reason);
        using (var source = new MemoryStream(archive))
        using (var limited = new Rar5ReadPasswordSession(PublicPassword, new(maxRounds: 1), Ct))
            Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => StrictRar5Catalog.Read(source, Ct, limited)).Reason);
    }

    [Fact]
    public void Same_length_source_changes_between_passes_are_detected_including_header_only_changes()
    {
        byte[] archive = Write([1, 2, 3], 3, 0);
        using var source = new ObservedSource(archive, onReset: () => archive[8] ^= 1);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => StrictRar5Catalog.Read(source, Ct)).Reason);
    }

    [Fact]
    public void Cancellation_in_the_second_pass_preserves_source_ownership()
    {
        byte[] archive = Write(new byte[131089], 0, 0); using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var source = new ObservedSource(archive, chunk: 7, onReset: stop.Cancel);
        Assert.ThrowsAny<OperationCanceledException>(() => StrictRar5Catalog.Read(source, stop.Token)); Assert.True(source.CanRead);
    }

    [Fact]
    public void Empty_next_member_resets_the_previous_header_state_in_a_reused_browsing_decoder()
    {
        using var bits = new RarBitReader(); using var packed = new MemoryStream(Encode([97])); bits.Reset(packed);
        var decoder = new Rar5Decoder(false); using var first = new RarWindow(0x20000, 1); decoder.StartMember(bits, false);
        Assert.True(decoder.Decode(first)); bits.Reset(Stream.Null); decoder.StartMember(bits, true);
        using var empty = new RarWindow(0x20000, 0); Assert.True(decoder.Decode(empty)); Assert.Equal(0, empty.Written);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RAR7_dictionary_fractions_are_independent_from_the_v5_compatible_algorithm(bool compatible)
    {
        ulong compression = 1 | (3UL << 7) | (7UL << 10) | (16UL << 15) | (compatible ? 1UL << 20 : 0);
        byte[] archive = Container(Header(2, 2, FileBody("fraction", compression: compression)));
        using var source = new MemoryStream(archive); var member = Assert.Single(RarArchive.Read([source], Ct).Members);
        Assert.Equal(24L << 20, member.Dictionary); Assert.Equal(compatible ? 50 : 70, member.Version);
        ulong excessive = 1 | (3UL << 7) | (10UL << 10) | (compatible ? 1UL << 20 : 0);
        Refuse(Container(Header(2, 2, FileBody("large", compression: excessive))), ArchiveWriteReason.BudgetExceeded);
    }

    [Fact]
    public void Owned_decoder_buffers_are_cleared_on_growth_reset_and_disposal_without_disposing_the_source()
    {
        using var source = new MemoryStream(new byte[] { 1, 2, 3 }); var bits = new RarBitReader(); bits.Reset(source); Assert.Equal(1u, bits.Read(8));
        byte[] input = Field<byte[]>(bits, "_buffer"); Assert.Contains((byte)2, input); bits.Reset(Stream.Null); Assert.All(input, b => Assert.Equal(0, b));
        bits.Dispose(); bits.Dispose(); Assert.All(input, b => Assert.Equal(0, b)); Assert.True(source.CanRead);
        Assert.Throws<ObjectDisposedException>(() => bits.Reset(source));
        var window = new RarWindow(1 << 20, 1); byte[] old = Field<byte[]>(window, "_buffer");
        for (int i = 0; i <= old.Length; i++) window.Put(42);
        Assert.All(old, b => Assert.Equal(0, b)); byte[] grown = Field<byte[]>(window, "_buffer"); Assert.Contains((byte)42, grown);
        window.Dispose(); window.Dispose(); Assert.All(grown, b => Assert.Equal(0, b)); Assert.Throws<ObjectDisposedException>(() => window.Put(1));
        var hash = new Blake2sp(); hash.Append([1, 2, 3]); byte[] buffered = Field<byte[]>(hash, "_buffer"); hash.Dispose();
        Assert.All(buffered, b => Assert.Equal(0, b)); Assert.Throws<ObjectDisposedException>(() => hash.Finish());
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

    [Fact]
    public void Opening_an_independent_member_keeps_an_earlier_borrowed_stream_alive_and_reader_disposal_clears_both()
    {
        byte[] data = new byte[131089]; new Random(42829307).NextBytes(data); byte[] archive = Write(data, 3, 0);
        var reader = RarReader.Open(new List<Stream> { new MemoryStream(archive) }, false, Ct);
        try
        {
            using var first = reader.Open(1, Ct); Assert.Equal(data[0], first.ReadByte());
            using var second = reader.Open(1, Ct); Assert.Equal(data[0], second.ReadByte());
            var windows = Field<HashSet<RarWindow>>(reader, "_windows").ToArray(); Assert.Equal(2, windows.Length);
            byte[][] buffers = windows.Select(w => Field<byte[]>(w, "_buffer")).ToArray();
            using var rest = new MemoryStream(); first.CopyTo(rest); Assert.Equal(data[1..], rest.ToArray());
            rest.SetLength(0); second.CopyTo(rest); Assert.Equal(data[1..], rest.ToArray());
            using var third = reader.Open(1, Ct); Assert.Equal(data[0], third.ReadByte());
            Assert.Single(Field<HashSet<RarWindow>>(reader, "_windows"));
            Assert.All(buffers, bytes => Assert.All(bytes, b => Assert.Equal(0, b)));
            byte[] current = Field<byte[]>(Assert.Single(Field<HashSet<RarWindow>>(reader, "_windows")), "_buffer");
            reader.Dispose(); Assert.All(buffers, bytes => Assert.All(bytes, b => Assert.Equal(0, b)));
            Assert.All(current, b => Assert.Equal(0, b));
        }
        finally { reader.Dispose(); }
    }

    private static byte[] Write(byte[] data, int method, int protection)
    {
        using var stage = new MemoryStream(); using var encryption = protection == 0 ? null : new Rar5EncryptionSession(PublicPassword, protection == 2, new(), Ct);
        Rar5ArchiveWriter.Create(stage, [new("folder", ArchiveMemberType.Directory, new(0, 0), 0x1ed),
            new("folder/暗号", ArchiveMemberType.File, new(1700000000, 123456789), 0x180, 1),
            new("empty", ArchiveMemberType.File, new(0, 0), 0x180, 2)],
            new Dictionary<int, Rar5InputDescriptor> { [1] = new(1, data.Length, Digest(data)), [2] = new(2, 0, Digest([])) },
            id => new MemoryStream(id == 1 ? data : [], false), (Rar5Compression)method, Ct, encryption);
        return stage.ToArray();
    }
    private static byte[] Encode(byte[] data)
    { using var output = new MemoryStream(); using var source = new MemoryStream(data); Rar5Encoder.Encode(source, data.Length, output, Rar5Compression.Fast, Ct); return output.ToArray(); }
    private static byte[] Compressed(byte[] packed, long length) => Container(Header(2, 2, FileBody("compressed", length: length, compression: 3UL << 7), data: packed));
    private static void Refuse(byte[] archive, ArchiveWriteReason reason)
    { using var source = new MemoryStream(archive); Assert.Equal(reason, Assert.Throws<ArchiveWriteException>(() => StrictRar5Catalog.Read(source, Ct)).Reason); Assert.True(source.CanRead); }
    private static void FixBlockChecksum(byte[] packed)
    { int bytes = ((packed[0] >> 3) & 3) + 1; byte sum = (byte)(0x5a ^ packed[0]); for (int i = 0; i < bytes; i++) sum ^= packed[2 + i]; packed[1] = sum; }
    private static byte[] FilterBlock(int type, int offset, int length)
    {
        var bits = new List<int>();
        void Put(int value, int count) { for (int i = count - 1; i >= 0; i--) bits.Add((value >> i) & 1); }
        for (int i = 0; i < 20; i++) Put(i < 12 ? 4 : 5, 4);
        for (int i = 0; i < 306; i++) Put(i < 206 ? 8 : 9, 4);
        for (int i = 0; i < 64; i++) Put(6, 4);
        for (int i = 0; i < 16; i++) Put(4, 4);
        for (int i = 0; i < 44; i++) Put(i < 20 ? 5 : 6, 4);
        Put(462, 9); Put(0, 2); Put(offset, 8); Put(0, 2); Put(length, 8); Put(type, 3); if (type == 0) Put(0, 5);
        for (int i = 0; i < 4; i++) Put(0, 8);
        int bytes = (bits.Count + 7) / 8; Assert.True(bytes < 256);
        byte[] packed = new byte[bytes + 3]; packed[0] = (byte)(0xc0 | ((bits.Count - 1) & 7)); packed[2] = (byte)bytes;
        for (int i = 0; i < bits.Count; i++) packed[3 + i / 8] |= (byte)(bits[i] << (7 - i % 8));
        FixBlockChecksum(packed); return packed;
    }
    private sealed class ObservedSource(byte[] data, int chunk = 65524, Action? onReset = null) : Stream
    {
        private readonly MemoryStream inner = new(data, false);
        internal long BytesRead; internal int MaximumRead; internal int Resets;
        public override int Read(Span<byte> buffer) { MaximumRead = Math.Max(MaximumRead, buffer.Length); int n = inner.Read(buffer[..Math.Min(buffer.Length, chunk)]); BytesRead += n; return n; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override long Position { get => inner.Position; set { if (value == 0 && inner.Position > 0) { Resets++; onReset?.Invoke(); } inner.Position = value; } }
        public override long Length => inner.Length; public override bool CanRead => inner.CanRead; public override bool CanSeek => true; public override bool CanWrite => false;
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
