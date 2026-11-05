using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveOriginEvidenceTests
{
    private static readonly Guid Nonce = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static ArchiveOriginEvidenceScope Scope(ArchiveOriginOperation operation = ArchiveOriginOperation.Create, int count = 2,
        ArchiveWriteFormat format = ArchiveWriteFormat.Tar) => new(Nonce, new(format), operation, count);
    private static ArchiveOriginSnapshot Absent => ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier);
    private static ArchiveOriginEvidenceRecord FileRecord(string identity = "file-é", ArchiveOriginSnapshot? mark = null) =>
        new(ArchiveOriginSourceKind.File, new(identity, 3, Hash([1, 2, 3])), mark ?? Absent);
    private static ArchiveOriginEvidenceRecord DirectoryRecord => new(ArchiveOriginSourceKind.Directory, new("directory", 0, Hash([])), Absent);
    private static (byte[] Bytes, ArchiveOriginEvidenceInfo Info, ArchiveOriginEvidenceScope Scope) Fixture()
    {
        var scope = Scope(ArchiveOriginOperation.Update, 3); using var output = new MemoryStream();
        using var writer = new ArchiveOriginEvidenceWriter(output, scope, TestContext.Current.CancellationToken);
        writer.Append(new(ArchiveOriginSourceKind.OriginalArchive, new("original", 2, Hash([8, 9])),
            ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/original\r\n"u8)));
        writer.Append(FileRecord()); writer.Append(DirectoryRecord); var info = writer.Complete();
        return (output.ToArray(), info, scope);
    }

    [Fact]
    public void Empty_creation_has_an_independently_specified_schema_one_wire_vector()
    {
        using var output = new MemoryStream(); using var writer = new ArchiveOriginEvidenceWriter(output, Scope(count: 0), TestContext.Current.CancellationToken);
        var info = writer.Complete();
        byte[] expected = Convert.FromHexString("46434F45564944310100000000112233445566778899AABBCCDDEEFF010000000100000000000000000080000000000046434F45");
        Assert.Equal(expected, output.ToArray()); Assert.Equal(expected.Length, info.Length); Assert.Equal(Hash(expected), info.Sha256);
    }

    public static IEnumerable<object[]> ProfilesAndFormats() => from profile in Enum.GetValues<ArchiveWriteFormat>()
        from format in Enum.GetValues<ArchiveOriginFormat>() select new object[] { profile, format };

    [Theory]
    [MemberData(nameof(ProfilesAndFormats))]
    public void Every_archive_profile_and_native_format_preserves_opaque_and_unavailable_evidence(ArchiveWriteFormat profile, ArchiveOriginFormat format)
    {
        var scope = Scope(count: 3, format: profile); using var output = new MemoryStream();
        ArchiveOriginEvidenceRecord[] records = [FileRecord("opaque", ArchiveOriginSnapshot.Present(format, [0xff, 0, 0x80])),
            FileRecord("absent", ArchiveOriginSnapshot.Absent(format)),
            FileRecord("unavailable", ArchiveOriginSnapshot.Unavailable(format, ArchiveOriginFailure.AccessDenied))];
        ArchiveOriginEvidenceInfo info;
        using (var writer = new ArchiveOriginEvidenceWriter(output, scope, TestContext.Current.CancellationToken))
        { foreach (var record in records) writer.Append(record); info = writer.Complete(); }
        output.Position = 0;
        using (var reader = new ArchiveOriginEvidenceReader(output, info, scope, TestContext.Current.CancellationToken))
        {
            foreach (var expected in records)
            {
                Assert.False(reader.IsComplete); Assert.Null(reader.VerifiedInfo);
                var actual = Assert.IsType<ArchiveOriginEvidenceRecord>(reader.ReadNext());
                Assert.Equal(expected.Kind, actual.Kind); Assert.Equal(expected.Source, actual.Source);
                Assert.Equal(expected.Origin.Digest, actual.Origin.Digest); Assert.Equal(expected.Origin.Bytes.ToArray(), actual.Origin.Bytes.ToArray());
            }
            Assert.False(reader.IsComplete); Assert.Null(reader.ReadNext()); Assert.True(reader.IsComplete); Assert.Equal(info, reader.VerifiedInfo);
        }
        Assert.True(output.CanRead); Assert.True(output.CanWrite);
    }

    [Fact]
    public void Scope_roles_directory_versions_and_counts_are_enforced_without_optional_originals()
    {
        Assert.Throws<ArchiveWriteException>(() => new ArchiveOriginEvidenceScope(Guid.Empty, new(ArchiveWriteFormat.Tar), ArchiveOriginOperation.Create, 0));
        Assert.Throws<ArchiveWriteException>(() => Scope(ArchiveOriginOperation.Update, 0));
        Assert.Throws<ArchiveWriteException>(() => Scope(count: ArchiveWritePolicy.MaxMembers + 1));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveOriginEvidenceRecord(ArchiveOriginSourceKind.Directory, new("directory", 1, Hash([1])), Absent));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveOriginEvidenceRecord(ArchiveOriginSourceKind.Directory, new("directory", 0, Hash([1])), Absent));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveOriginEvidenceRecord(ArchiveOriginSourceKind.File,
            new("large", ArchiveWritePolicy.MaxPayloadBytes + 1, Hash([])), Absent));
        foreach (var scope in new[] { Scope(count: 1), Scope(ArchiveOriginOperation.Update, 1) })
        {
            using var output = new MemoryStream(); using var writer = new ArchiveOriginEvidenceWriter(output, scope, TestContext.Current.CancellationToken);
            var wrong = scope.Operation == ArchiveOriginOperation.Create
                ? new ArchiveOriginEvidenceRecord(ArchiveOriginSourceKind.OriginalArchive, new("original", 0, Hash([])), Absent) : FileRecord();
            Assert.Throws<ArchiveWriteException>(() => writer.Append(wrong)); Assert.Equal(48, output.Length);
            Assert.Throws<ArchiveWriteException>(() => writer.Append(FileRecord())); Assert.Throws<ArchiveWriteException>(() => writer.Complete());
        }
    }

    [Fact]
    public void Missing_or_extra_rows_poison_completion_and_partial_disposal_retains_the_owned_stream()
    {
        using var output = new MemoryStream();
        using (var writer = new ArchiveOriginEvidenceWriter(output, Scope(), TestContext.Current.CancellationToken))
        {
            writer.Append(FileRecord()); Assert.Throws<ArchiveWriteException>(() => writer.Complete());
            Assert.Throws<ArchiveWriteException>(() => writer.Append(DirectoryRecord));
        }
        Assert.True(output.CanWrite); Assert.True(output.Length > 48);
        using var extra = new MemoryStream(); using var one = new ArchiveOriginEvidenceWriter(extra, Scope(count: 1), TestContext.Current.CancellationToken);
        one.Append(FileRecord()); Assert.Throws<ArchiveWriteException>(() => one.Append(FileRecord())); Assert.Throws<ArchiveWriteException>(() => one.Complete());
    }

    [Fact]
    public void Wrong_scope_early_completion_trailing_data_and_unrepaired_checksum_never_verify()
    {
        var fixture = Fixture();
        foreach (var scope in new[] { new ArchiveOriginEvidenceScope(Guid.NewGuid(), fixture.Scope.Profile, fixture.Scope.Operation, 3),
                     Scope(ArchiveOriginOperation.Create, 3), Scope(ArchiveOriginOperation.Update, 2),
                     Scope(ArchiveOriginOperation.Update, 3, ArchiveWriteFormat.Gzip) })
        {
            using var input = new MemoryStream(fixture.Bytes); Assert.Throws<ArchiveWriteException>(() =>
                new ArchiveOriginEvidenceReader(input, fixture.Info, scope, TestContext.Current.CancellationToken));
        }
        using (var input = new MemoryStream(fixture.Bytes))
        using (var reader = new ArchiveOriginEvidenceReader(input, fixture.Info, fixture.Scope, TestContext.Current.CancellationToken))
        {
            reader.ReadNext(); Assert.Throws<ArchiveWriteException>(() => reader.Complete());
            Assert.Throws<ArchiveWriteException>(() => reader.ReadNext()); Assert.False(reader.IsComplete);
        }
        byte[] changed = fixture.Bytes.ToArray(); changed[^5] ^= 1;
        Refuse(changed, fixture.Info, fixture.Scope);
        byte[] trailing = [.. fixture.Bytes, 0]; Refuse(trailing, new(trailing.Length, Hash(trailing)), fixture.Scope);
    }

    [Fact]
    public void Every_truncation_is_refused_even_with_a_repaired_external_checksum()
    {
        var fixture = Fixture();
        for (int size = 0; size < fixture.Bytes.Length; size++)
        {
            byte[] truncated = fixture.Bytes[..size];
            if (size < 52) { Assert.Throws<ArchiveWriteException>(() => new ArchiveOriginEvidenceInfo(size, Hash(truncated))); continue; }
            Refuse(truncated, new(size, Hash(truncated)), fixture.Scope);
        }
    }

    [Fact]
    public void Repaired_hash_does_not_bypass_header_preallocation_record_role_or_presence_validation()
    {
        var fixture = Fixture();
        var changes = new List<Action<byte[]>>
        {
            b => b[0] ^= 1, b => b[8] = 2, b => b[11] = 1, b => b[12] ^= 1,
            b => Set(b,28,99), b => Set(b,32,2), b => Set(b,36,99), b => Set(b,40,0), b => Set(b,44,int.MaxValue),
            b => Set(b,48,int.MaxValue), b => Set(b,52,99), b => Set(b,56,int.MaxValue),
            b => BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(60),-1),
            b => b[100]=99, b => b[101]=99, b => b[102]=99, b => b[103]=1,
            b => Set(b,104,int.MaxValue), b => b[101]=(byte)ArchiveOriginState.Absent,
            b => b[108]=0xff, b => b[^1]^=1,
        };
        foreach (var change in changes)
        {
            byte[] bytes = fixture.Bytes.ToArray(); change(bytes); Refuse(bytes, new(bytes.Length, Hash(bytes)), fixture.Scope);
        }
    }

    [Fact]
    public void Cancellation_and_io_failure_poison_both_directions_without_closing_owner_streams()
    {
        using var canceled = new CancellationTokenSource(); using var output = new MemoryStream();
        using (var writer = new ArchiveOriginEvidenceWriter(output, Scope(), canceled.Token))
        {
            writer.Append(FileRecord()); canceled.Cancel(); Assert.Throws<OperationCanceledException>(() => writer.Append(DirectoryRecord));
            Assert.Throws<ArchiveWriteException>(() => writer.Complete());
        }
        Assert.True(output.CanWrite);
        var fixture = Fixture(); using var cancellation = new CancellationTokenSource(); using var input = new MemoryStream(fixture.Bytes);
        using (var reader = new ArchiveOriginEvidenceReader(input, fixture.Info, fixture.Scope, cancellation.Token))
        {
            reader.ReadNext(); cancellation.Cancel(); Assert.Throws<OperationCanceledException>(() => reader.ReadNext());
            Assert.Throws<ArchiveWriteException>(() => reader.ReadNext()); Assert.Null(reader.VerifiedInfo);
        }
        Assert.True(input.CanRead);
        using var broken = new FaultStream(); using var brokenWriter = new ArchiveOriginEvidenceWriter(broken, Scope(count: 1), TestContext.Current.CancellationToken);
        broken.Fail = true; Assert.Throws<IOException>(() => brokenWriter.Append(FileRecord()));
        broken.Fail = false; Assert.Throws<ArchiveWriteException>(() => brokenWriter.Append(FileRecord()));
        using var brokenRead = new FaultStream(fixture.Bytes); using var brokenReader = new ArchiveOriginEvidenceReader(brokenRead, fixture.Info, fixture.Scope, TestContext.Current.CancellationToken);
        brokenRead.Fail = true; Assert.Throws<IOException>(() => brokenReader.ReadNext()); brokenRead.Fail = false;
        Assert.Throws<ArchiveWriteException>(() => brokenReader.ReadNext());
    }

    [Fact]
    public void Logical_input_and_aggregate_metadata_limits_apply_while_streaming_without_retaining_the_set()
    {
        using (var output = new MemoryStream())
        using (var writer = new ArchiveOriginEvidenceWriter(output, Scope(count: 9), TestContext.Current.CancellationToken))
        {
            var record = new ArchiveOriginEvidenceRecord(ArchiveOriginSourceKind.File, new("large", ArchiveWritePolicy.MaxPayloadBytes, Hash([])), Absent);
            for (int i = 0; i < 8; i++) writer.Append(record);
            Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => writer.Append(record)).Reason);
        }
        using var discard = new DiscardStream(); using var bounded = new ArchiveOriginEvidenceWriter(discard, Scope(count: 3000), TestContext.Current.CancellationToken);
        var big = FileRecord(mark: ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, new byte[ArchiveOriginSnapshot.MaxBytes]));
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => { for (int i = 0; i < 3000; i++) bounded.Append(big); }).Reason);
        Assert.True(discard.Length <= ArchiveWritePolicy.MaxMetadataBytes); Assert.True(bounded.Records < 3000);
    }

    [Fact]
    public void Reader_does_not_keep_decoded_records_alive_after_the_caller_discards_them()
    {
        using var output = new MemoryStream(); var scope = Scope(count: 100);
        ArchiveOriginEvidenceInfo info;
        using (var writer = new ArchiveOriginEvidenceWriter(output, scope, TestContext.Current.CancellationToken))
        { for (int i = 0; i < 100; i++) writer.Append(FileRecord(i.ToString(), ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, new byte[4096]))); info = writer.Complete(); }
        output.Position = 0; using var reader = new ArchiveOriginEvidenceReader(output, info, scope, TestContext.Current.CancellationToken);
        var weak = Drain(reader); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.True(reader.IsComplete); Assert.All(weak, reference => Assert.False(reference.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> Drain(ArchiveOriginEvidenceReader reader)
    { var result = new List<WeakReference>(); while (reader.ReadNext() is { } row) result.Add(new(row)); return result; }
    private static void Set(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void Refuse(byte[] bytes, ArchiveOriginEvidenceInfo info, ArchiveOriginEvidenceScope scope)
    {
        using var stream = new MemoryStream(bytes);
        Assert.ThrowsAny<IOException>(() =>
        {
            using var reader = new ArchiveOriginEvidenceReader(stream, info, scope, TestContext.Current.CancellationToken);
            while (reader.ReadNext() is not null) { }
        });
    }

    private sealed class FaultStream : MemoryStream
    {
        public bool Fail { get; set; }
        public FaultStream() { }
        public FaultStream(byte[] bytes) : base(bytes) { }
        public override void Write(ReadOnlySpan<byte> bytes) { if (Fail) throw new IOException("Injected write failure."); base.Write(bytes); }
        public override int Read(Span<byte> bytes) { if (Fail) throw new IOException("Injected read failure."); return base.Read(bytes); }
    }
    private sealed class DiscardStream : Stream
    {
        private long length, position;
        public override bool CanRead => false; public override bool CanWrite => true; public override bool CanSeek => true;
        public override long Length => length; public override long Position { get => position; set => position = value; }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> bytes) { position += bytes.Length; length = Math.Max(length, position); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
