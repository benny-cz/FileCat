using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FileCat.Archives.Rar;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class StrictRar5FramingTests
{
    private const string PublicPassword = "FileCat-Public-Rar-Framing-暗号-42829305";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Signature = [0x52, 0x61, 0x72, 0x21, 0x1a, 7, 1, 0];
    private static string Digest(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    public static IEnumerable<object[]> Profiles() => from method in Enumerable.Range(0, 6) from secret in Enumerable.Range(0, 3) select new object[] { method, secret };

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_method_and_password_mode_has_exact_physical_extents_metadata_and_revision(int method, int secret)
    {
        byte[] data = new byte[131089]; new Random(42829305).NextBytes(data);
        for (int i = 8192; i < data.Length; i++) data[i] = data[i % 8192];
        var entries = new[]
        {
            new Rar5WriteEntry("folder", ArchiveMemberType.Directory, new(1700000000, 123456789), 0x1ed),
            new Rar5WriteEntry("folder/暗号.bin", ArchiveMemberType.File, new(0, 999999999), 0x1a0, 1),
            new Rar5WriteEntry("empty", ArchiveMemberType.File, new(-1, 123456700), 0x180, 2),
        };
        var inputs = new Dictionary<int, Rar5InputDescriptor> { [1] = new(1, data.Length, Digest(data)), [2] = new(2, 0, Digest([])) };
        using var stage = new MemoryStream();
        using var encryption = secret == 0 ? null : new Rar5EncryptionSession(PublicPassword, secret == 2, new(), Ct);
        var manifest = Rar5ArchiveWriter.Create(stage, entries, inputs, id => new MemoryStream(id == 1 ? data : [], false), (Rar5Compression)method, Ct, encryption);
        byte[] archive = stage.ToArray(); using var source = new ShortSource(archive, 7);
        using var password = secret == 0 ? null : new Rar5ReadPasswordSession(PublicPassword, new(), Ct);
        var observed = StrictRar5Framing.Read(source, Ct, password);
        Assert.Equal(Digest(archive), observed.Revision); Assert.Equal(archive.Length, observed.Length);
        Assert.Equal(secret == 2, observed.HiddenNames); Assert.Equal(secret == 2 ? 19 : (int?)null, observed.HeaderKdfPower);
        Assert.Equal(entries.Length, observed.Members.Count); Assert.Equal(archive.Length, source.Position);
        Assert.InRange(source.MaximumRequest, 1, ArchiveIoAuthority.MaximumReadBytes); Assert.True(source.CanRead);
        for (int i = 0; i < entries.Length; i++)
        {
            var actual = observed.Members[i]; var expected = entries[i];
            Assert.Equal(expected.Name, actual.Name); Assert.Equal(expected.Type, actual.Type); Assert.Equal(0, actual.Occurrence);
            Assert.Equal(expected.Modified, actual.Modified); Assert.Null(actual.BasicModified); Assert.NotNull(actual.Times);
            Assert.Equal(expected.Mode | (expected.Type == ArchiveMemberType.File ? 0x8000 : 0x4000), (int)actual.Attributes);
            Assert.Equal(1, actual.Host); Assert.Equal(manifest.Members[i].Content.Method, actual.Method); Assert.Equal(0, actual.AlgorithmVersion);
            Assert.Equal(manifest.Members[i].Content.Length, actual.Length); Assert.NotNull(actual.Crc32);
            Assert.InRange(actual.HeaderOffset, 8, archive.Length - 1); Assert.True(actual.DataOffset > actual.HeaderOffset);
            Assert.True(actual.DataOffset + actual.PackedLength <= archive.Length); Assert.Null(actual.Owner); Assert.Null(actual.Blake2sp);
            if (secret != 0 && expected.Type == ArchiveMemberType.File)
            {
                Assert.NotNull(actual.Encryption); Assert.True(actual.Encryption.UseMac); Assert.Equal(19, actual.Encryption.CountPower);
                Assert.Equal(16, actual.Encryption.Iv.Length); Assert.Equal(16, actual.Encryption.Salt.Length); Assert.Equal(0, actual.PackedLength & 15);
                Assert.Equal((manifest.Members[i].Content.PackedLength + 15) & ~15L, actual.PackedLength);
            }
            else { Assert.Null(actual.Encryption); Assert.Equal(manifest.Members[i].Content.PackedLength, actual.PackedLength); }
        }
    }

    [Fact]
    public void Raw_catalog_keeps_occurrences_absent_fields_native_owner_bytes_and_nanosecond_times()
    {
        byte[] owner = Extra(6, Var(15), Var(3), [0xff, 0, 0x80], Var(0), Var(ulong.MaxValue), Var(0));
        byte[] times = Extra(3, Var(31), U32(1), U32(2), U32(3), U32(123456789), U32(987654321), U32(999999999));
        byte[] hash = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        byte[] archive = Container(File("same", extra: [.. owner, .. times, .. Extra(2, Var(0), hash)]), File("same"));
        using var input = new ShortSource(archive, 3); var catalog = StrictRar5Framing.Read(input, Ct);
        var first = catalog.Members[0]; var second = catalog.Members[1];
        Assert.Equal(0, first.Occurrence); Assert.Equal(1, second.Occurrence); Assert.Null(first.Crc32); Assert.Null(first.BasicModified);
        Assert.Equal(new ArchiveTimestamp(1, 123456789), first.Modified); Assert.Equal(new ArchiveTimestamp(2, 987654321), first.Times!.Created);
        Assert.Equal(new ArchiveTimestamp(3, 999999999), first.Times.Accessed); Assert.Equal(hash, first.Blake2sp);
        Assert.Equal(new byte[] { 0xff, 0, 0x80 }, first.Owner!.UserName); Assert.Empty(first.Owner.GroupName!);
        Assert.Equal(ulong.MaxValue, first.Owner.UserId); Assert.Equal(0UL, first.Owner.GroupId);
        Assert.Null(second.Modified); Assert.Null(second.Times); Assert.Null(second.Owner); Assert.Null(second.Blake2sp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FileTime_and_basic_unix_fields_preserve_presence_and_exact_instants(int layout)
    {
        byte[] extra = layout == 0 ? Extra(3, Var(14), U64(116444735991234567), U64(116444736000000001), U64(0)) : [];
        byte[] archive = Container(File("time", flags: 2, basic: 4294967295, extra: extra));
        using var source = new MemoryStream(archive); var entry = Assert.Single(StrictRar5Framing.Read(source, Ct).Members);
        Assert.Equal(new ArchiveTimestamp(4294967295, 0), entry.BasicModified);
        if (layout == 0)
        {
            Assert.Equal(new ArchiveTimestamp(-1, 123456700), entry.Modified); Assert.Equal(new ArchiveTimestamp(0, 100), entry.Times!.Created);
            Assert.Equal(new ArchiveTimestamp(-11644473600, 0), entry.Times.Accessed);
        }
        else { Assert.Equal(entry.BasicModified, entry.Modified); Assert.Null(entry.Times); }
    }

    [Fact]
    public void Recognized_RAR7_properties_are_observed_without_allocating_the_claimed_dictionary()
    {
        ulong compression = 1UL | (3UL << 7) | (23UL << 10) | (31UL << 15) | (1UL << 20);
        using var source = new MemoryStream(Container(File("rar7", compression: compression)));
        var entry = Assert.Single(StrictRar5Framing.Read(source, Ct).Members);
        Assert.Equal(compression, entry.Compression); Assert.Equal(1, entry.AlgorithmVersion); Assert.Equal(3, entry.Method);
        Assert.Equal((0x20000L << 23) * 63 / 32, entry.DictionaryBytes);
        // This is a physical observation only: an empty, compressed payload still needs a strict decoder pass.
    }

    [Fact]
    public void Legal_overprovisioned_header_integers_are_not_mistaken_for_unknown_metadata()
    {
        byte[] body = FileBody("padded", length: 2);
        byte[] header = [.. Var(2), 0x82, 0, .. Var(2), .. body]; // Padded general data-present flags.
        Assert.True(header.Length < 128);
        byte[] crcBody = [(byte)(header.Length | 128), 0, .. header]; // Padded header-size field.
        byte[] archive = ContainerWith([MainHeader(), [.. U32(ArchiveCrc32.Compute(crcBody)), .. crcBody, 1, 2], End()]);
        using var source = new ShortSource(archive, 1); var catalog = StrictRar5Framing.Read(source, Ct);
        Assert.Equal("padded", Assert.Single(catalog.Members).Name); Assert.Equal(2, catalog.Members[0].PackedLength);
        Assert.Equal(Digest(archive), catalog.Revision);
    }

    [Fact]
    public void A_physical_scan_does_not_pretend_to_verify_member_content_or_a_data_only_password()
    {
        byte[] payload = "not checked by the framing observer"u8.ToArray();
        byte[] archive = Container(File("stored", flags: 4, crc: 123, data: payload));
        using var source = new MemoryStream(archive); var catalog = StrictRar5Framing.Read(source, Ct);
        Assert.Equal(123u, Assert.Single(catalog.Members).Crc32); Assert.Equal(Digest(archive), catalog.Revision);
        using var encryption = new Rar5EncryptionSession(PublicPassword, false, new(), Ct); using var stage = new MemoryStream();
        Rar5ArchiveWriter.Create(stage, [new("secret", ArchiveMemberType.File, new(0, 0), 0x180, 1)],
            new Dictionary<int, Rar5InputDescriptor> { [1] = new(1, payload.Length, Digest(payload)) }, _ => new MemoryStream(payload), Rar5Compression.Fast, Ct, encryption);
        stage.Position = 0; using var wrong = new Rar5ReadPasswordSession("incorrect public synthetic password", new(), Ct);
        Assert.NotNull(Assert.Single(StrictRar5Framing.Read(stage, Ct, wrong).Members).Encryption);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_truncated_prefix_refuses_without_consuming_caller_ownership(bool hidden)
    {
        byte[] payload = [1, 2, 3, 4, 5, 6, 7]; using var stage = new MemoryStream();
        using var encryption = hidden ? new Rar5EncryptionSession(PublicPassword, true, new(), Ct) : null;
        Rar5ArchiveWriter.Create(stage, [new("short", ArchiveMemberType.File, new(0, 123456700), 0x180, 1)],
            new Dictionary<int, Rar5InputDescriptor> { [1] = new(1, payload.Length, Digest(payload)) }, _ => new MemoryStream(payload), Rar5Compression.Fast, Ct, encryption);
        byte[] archive = stage.ToArray(); using var password = hidden ? new Rar5ReadPasswordSession(PublicPassword, new(), Ct) : null;
        for (int prefix = 0; prefix < archive.Length; prefix++)
        {
            Ct.ThrowIfCancellationRequested(); using var source = new MemoryStream(archive, 0, prefix, false);
            Assert.Throws<ArchiveWriteException>(() => StrictRar5Framing.Read(source, Ct, password)); Assert.True(source.CanRead);
        }
    }

    public static IEnumerable<object[]> RefusedStructures()
    {
        yield return [ContainerWith([Header(2, 2, FileBody("before-main")), End()]), ArchiveWriteReason.InvalidArchive];
        yield return [ContainerWith([MainHeader(), MainHeader(), End()]), ArchiveWriteReason.UnsupportedFeature];
        yield return [ContainerWith([MainHeader(1), End()]), ArchiveWriteReason.UnsupportedFeature];
        yield return [ContainerWith([MainHeader(4), End()]), ArchiveWriteReason.UnsupportedFeature];
        yield return [ContainerWith([MainHeader(8), End()]), ArchiveWriteReason.UnsupportedFeature];
        yield return [ContainerWith([MainHeader(16), End()]), ArchiveWriteReason.UnsupportedFeature];
        yield return [ContainerWith([MainHeader(), Header(3, 0, []), End()]), ArchiveWriteReason.UnsupportedFeature];
        yield return [ContainerWith([MainHeader(), Header(99, 4, []), End()]), ArchiveWriteReason.UnsupportedFeature];
        yield return [ContainerWith([MainHeader(), Header(5, 0, Var(1))]), ArchiveWriteReason.UnsupportedFeature];
        yield return [ContainerWith([MainHeader(), Header(2, 8, FileBody("split")), End()]), ArchiveWriteReason.UnsupportedFeature];
        yield return [Container(File("unknown-size", flags: 8)), ArchiveWriteReason.UnsupportedFeature];
        yield return [Container(File("solid", compression: 64)), ArchiveWriteReason.UnsupportedFeature];
        yield return [Container(File("method", compression: 6UL << 7)), ArchiveWriteReason.UnsupportedFeature];
        yield return [Container(File("algorithm", compression: 2)), ArchiveWriteReason.UnsupportedFeature];
        yield return [Container(File("host", host: 2)), ArchiveWriteReason.UnsupportedFeature];
        yield return [Container(File("special", attributes: 0xa1a4)), ArchiveWriteReason.UnsupportedFeature];
        yield return [Container(File("kind", flags: 1)), ArchiveWriteReason.InvalidArchive];
        yield return [Container(File("mismatch", flags: 1, attributes: 0x41ed, data: [1])), ArchiveWriteReason.InvalidArchive];
        yield return [Container(File("owner", extra: Extra(6, Var(16)))), ArchiveWriteReason.UnsupportedFeature];
        yield return [Container(File("unknown", extra: Extra(77, [0]))), ArchiveWriteReason.UnsupportedFeature];
        yield return [Container(File("version", extra: Extra(4, Var(0), Var(1)))), ArchiveWriteReason.UnsupportedFeature];
        yield return [Container(File("link", extra: Extra(5, Var(1), Var(0), Var(1), [120]))), ArchiveWriteReason.UnsupportedFeature];
        yield return [Container(File("repeated", extra: [.. Extra(3, Var(2), U64(0)), .. Extra(3, Var(2), U64(0))])), ArchiveWriteReason.InvalidArchive];
        yield return [Container(File("bad-nanos", extra: Extra(3, Var(19), U32(1), U32(1000000000)))), ArchiveWriteReason.InvalidArchive];
        yield return [Container(File("time-flags", extra: Extra(3, Var(18), U64(0)))), ArchiveWriteReason.UnsupportedFeature];
        yield return [Container(File("trailing", extra: Extra(3, Var(0), [1]))), ArchiveWriteReason.UnsupportedFeature];
        yield return [Container(File("mapped-\ufffe")), ArchiveWriteReason.UnsupportedFeature];
        yield return [(byte[])[.. Container(File("trailer")), 1], ArchiveWriteReason.UnsupportedFeature];
        yield return [ContainerWith([MainHeader(), File("missing-end")]), ArchiveWriteReason.InvalidArchive];
    }

    [Theory]
    [MemberData(nameof(RefusedStructures))]
    public void Unsupported_or_ambiguous_structures_never_produce_a_complete_catalog(byte[] archive, ArchiveWriteReason reason)
    {
        using var source = new ShortSource(archive, 2);
        Assert.Equal(reason, Assert.Throws<ArchiveWriteException>(() => StrictRar5Framing.Read(source, Ct)).Reason);
        Assert.True(source.CanRead); Assert.InRange(source.MaximumRequest, 1, ArchiveIoAuthority.MaximumReadBytes);
    }

    [Fact]
    public void Header_crc_utf8_and_declared_extents_are_checked_before_any_catalog_is_returned()
    {
        byte[] corrupt = Container(File("crc")); corrupt[8] ^= 1;
        byte[] badNameBody = [.. Var(0), .. Var(0), .. Var(0x81a4), .. Var(0), .. Var(1), .. Var(1), 0xff];
        foreach (byte[] data in new[] { corrupt, ContainerWith([MainHeader(), Header(2, 2, badNameBody), End()]),
            ContainerWith([MainHeader(), Header(2, 2, FileBody("extent", length: 100), declaredDataSize: 100), End()]) })
        {
            using var input = new MemoryStream(data); Assert.Throws<ArchiveWriteException>(() => StrictRar5Framing.Read(input, Ct)); Assert.True(input.CanRead);
        }
    }

    [Fact]
    public void Header_claims_and_cumulative_member_metadata_are_bounded_before_uncontrolled_reads()
    {
        byte[] hugeHeader = [.. Signature, 0, 0, 0, 0, .. Var(ArchiveWritePolicy.MaxEntryMetadataBytes + 1)];
        using (var source = new ShortSource(hugeHeader, 1))
            Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => StrictRar5Framing.Read(source, Ct)).Reason);
        byte[] largeOwner = Extra(6, Var(1), Var(512 << 10), new byte[512 << 10]);
        using var repeated = new RepeatedRecords(File("repeated", extra: largeOwner), 300);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => StrictRar5Framing.Read(repeated, Ct)).Reason);
        Assert.InRange(repeated.Position, ArchiveWritePolicy.MaxMetadataBytes - (1 << 20), ArchiveWritePolicy.MaxMetadataBytes + (1 << 20));
        Assert.True(repeated.CanRead);
    }

    [Fact]
    public void A_member_count_above_the_profile_limit_refuses_even_with_tiny_repeated_names()
    {
        using var source = new RepeatedRecords(File("n"), ArchiveWritePolicy.MaxMembers + 1);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => StrictRar5Framing.Read(source, Ct)).Reason);
        Assert.True(source.Position < source.Length); Assert.True(source.CanRead);
    }

    [Fact]
    public void Cancellation_during_a_payload_read_never_returns_a_catalog_or_closes_the_grant()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var source = new ShortSource(Container(File("payload", data: new byte[131089])), 65524,
            position => { if (position >= 100) cancellation.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => StrictRar5Framing.Read(source, cancellation.Token));
        Assert.InRange(source.Position, 100, source.Length - 1); Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Encrypted_header_limits_and_reservations_stop_after_the_first_block(bool reservationFails)
    {
        using var encryption = new Rar5EncryptionSession(PublicPassword, true, new(), Ct); using var stage = new MemoryStream();
        Rar5ArchiveWriter.Create(stage, [new(new string('n', 400), ArchiveMemberType.Directory, new(0, 0), 0x1ed)],
            new Dictionary<int, Rar5InputDescriptor>(), _ => throw new InvalidOperationException(), Rar5Compression.Store, Ct, encryption);
        stage.Position = 0; using var password = new Rar5ReadPasswordSession(PublicPassword, new(), Ct);
        var catalog = StrictRar5Framing.Read(stage, Ct, password); long offset = Assert.Single(catalog.Members).HeaderOffset;
        var parameters = new Rar5CryptoParameters(encryption.CountPower, encryption.HeaderSalt.ToArray(), [], encryption.HeaderCheck.ToArray(), false);
        using var source = new ShortSource(stage.ToArray(), 65524); source.Position = offset; long before = source.Position; bool reserved = false;
        Action<int>? reserve = reservationFails ? _ => { reserved = true; throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "Synthetic metadata reservation failure."); } : null;
        var error = Assert.Throws<ArchiveWriteException>(() => password.ReadHeader(source, offset, parameters, out _, out _, out _, Ct,
            reservationFails ? ArchiveWritePolicy.MaxEntryMetadataBytes : 128, reserve));
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, error.Reason); Assert.Equal(reservationFails, reserved);
        Assert.Equal(before + 32, source.Position); Assert.True(source.CanRead);
    }

    [Fact]
    public void Wrong_or_missing_header_password_and_cancellation_refuse_without_closing_source()
    {
        using var encryption = new Rar5EncryptionSession(PublicPassword, true, new(), Ct); using var stage = new MemoryStream();
        Rar5ArchiveWriter.Create(stage, [], new Dictionary<int, Rar5InputDescriptor>(), _ => throw new InvalidOperationException(), Rar5Compression.Store, Ct, encryption);
        stage.Position = 0; Assert.Throws<ArchiveWriteException>(() => StrictRar5Framing.Read(stage, Ct));
        stage.Position = 0; using var wrong = new Rar5ReadPasswordSession("different public synthetic password", new(), Ct);
        Assert.Throws<ArchiveWriteException>(() => StrictRar5Framing.Read(stage, Ct, wrong)); Assert.True(stage.CanRead);
        stage.Position = 0; using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => StrictRar5Framing.Read(stage, cancel.Token)); Assert.Equal(0, stage.Position);
    }

    private static byte[] Var(ulong value)
    {
        using var result = new MemoryStream(); do { byte item = (byte)(value & 127); value >>= 7; result.WriteByte((byte)(item | (value == 0 ? 0 : 128))); } while (value != 0);
        return result.ToArray();
    }
    internal static byte[] Var(int value) => Var(checked((ulong)value));
    private static byte[] U32(uint value) { byte[] bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); return bytes; }
    private static byte[] U64(ulong value) { byte[] bytes = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(bytes, value); return bytes; }
    internal static byte[] Extra(int kind, params byte[][] fields)
    { byte[] body = [.. Var(kind), .. fields.SelectMany(x => x)]; return [.. Var(body.Length), .. body]; }
    internal static byte[] FileBody(string name, ulong flags = 0, long length = 0, ulong attributes = 0x81a4,
        uint basic = 0, uint crc = 0, ulong compression = 0, int host = 1)
    {
        byte[] text = Encoding.UTF8.GetBytes(name);
        return [.. Var(flags), .. Var((ulong)length), .. Var(attributes), .. ((flags & 2) != 0 ? U32(basic) : []),
            .. ((flags & 4) != 0 ? U32(crc) : []), .. Var(compression), .. Var(host), .. Var(text.Length), .. text];
    }
    private static byte[] File(string name, ulong flags = 0, ulong attributes = 0x81a4, uint basic = 0, uint crc = 0,
        ulong compression = 0, int host = 1, byte[]? extra = null, byte[]? data = null)
    { data ??= []; return Header(2, extra is { Length: > 0 } ? 3UL : 2UL, FileBody(name, flags, data.Length, attributes, basic, crc, compression, host), extra, data); }
    private static byte[] MainHeader(ulong flags = 0) => Header(1, 0, Var(flags));
    private static byte[] End() => Header(5, 0, Var(0));
    internal static byte[] Container(params byte[][] files) => ContainerWith([MainHeader(), .. files, End()]);
    private static byte[] ContainerWith(byte[][] records) => [.. Signature, .. records.SelectMany(x => x)];
    internal static byte[] Header(int kind, ulong flags, byte[] body, byte[]? extra = null, byte[]? data = null, long? declaredDataSize = null)
    {
        extra ??= []; data ??= [];
        byte[] header = [.. Var(kind), .. Var(flags), .. ((flags & 1) != 0 ? Var(extra.Length) : []),
            .. ((flags & 2) != 0 ? Var((ulong)(declaredDataSize ?? data.Length)) : []), .. body, .. extra];
        byte[] crcBody = [.. Var(header.Length), .. header]; return [.. U32(ArchiveCrc32.Compute(crcBody)), .. crcBody, .. data];
    }
    private sealed class ShortSource(byte[] data, int chunk, Action<long>? afterRead = null) : Stream
    {
        private readonly MemoryStream inner = new(data, false);
        internal int MaximumRequest;
        public override int Read(Span<byte> buffer)
        {
            MaximumRequest = Math.Max(MaximumRequest, buffer.Length);
            int count = inner.Read(buffer[..Math.Min(chunk, buffer.Length)]); afterRead?.Invoke(inner.Position); return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override bool CanRead => inner.CanRead; public override bool CanSeek => true; public override bool CanWrite => false;
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class RepeatedRecords(byte[] record, int count) : Stream
    {
        private readonly byte[] start = [.. Signature, .. MainHeader()]; private readonly byte[] end = End(); private long position;
        public override int Read(Span<byte> target)
        {
            int written = 0;
            while (written < target.Length && position < Length)
            {
                long inside = position - start.Length; ReadOnlySpan<byte> part;
                if (inside < 0) part = start.AsSpan((int)position);
                else if (inside < (long)count * record.Length) part = record.AsSpan((int)(inside % record.Length));
                else part = end.AsSpan((int)(inside - (long)count * record.Length));
                int take = Math.Min(part.Length, target.Length - written); part[..take].CopyTo(target[written..]); written += take; position += take;
            }
            return written;
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override long Length => start.Length + (long)record.Length * count + end.Length;
        public override long Position { get => position; set { if (value != position) throw new NotSupportedException(); } }
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
