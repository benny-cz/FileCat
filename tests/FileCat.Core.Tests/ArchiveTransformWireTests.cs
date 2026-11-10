using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveTransformWireTests
{
    private static readonly Guid Nonce = Guid.NewGuid();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static ArchiveTransformInvocation Creation() => ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(null,
        [new(37, 1, Hash([1]), DateTimeOffset.UnixEpoch)], [], [new("one", ArchiveMemberType.File, 37), new("two", ArchiveMemberType.File, 37)], TestContext.Current.CancellationToken));
    private static byte[] Write(ArchiveTransformRequest request)
    {
        using var stream = new MemoryStream(); ArchiveTransformWire.WriteRequest(new(new(Nonce, ArchiveWritePolicy.MaxWorkerWireBytes), stream, writing: true), request);
        return stream.ToArray();
    }
    private static ArchiveTransformRequest Read(byte[] data)
    { using var stream = new MemoryStream(data, writable: false); return ArchiveTransformWire.ReadRequest(new(new(Nonce, ArchiveWritePolicy.MaxWorkerWireBytes), stream, writing: false)); }

    [Fact]
    public void Every_reference_has_one_fixed_grant_but_only_the_parent_knows_the_captured_identity_and_expectation()
    {
        var invocation = Creation(); var decoded = Read(Write(invocation.Request));
        Assert.Equal(new[] { 1, 2 }, decoded.Inputs.Keys); Assert.Equal(new[] { 37, 37 }, invocation.CapturedInputIds.Values);
        Assert.Equal(new int?[] { 1, 2 }, decoded.Additions.Select(a => a.InputId)); Assert.Null(decoded.Source);
        Assert.Equal(invocation.Request.OutputLengthBound, decoded.OutputLengthBound);
        Assert.All(decoded.Inputs.Values, i => Assert.Equal(Hash([1]), i.Sha256));
        Assert.False(Write(invocation.Request).AsSpan().IndexOf(Convert.FromHexString(invocation.Expected.Digest)) >= 0);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("flag")]
    [InlineData("count")]
    [InlineData("create-edits")]
    [InlineData("bound")]
    [InlineData("trailing")]
    [InlineData("truncate")]
    [InlineData("format")]
    [InlineData("unknown-format")]
    [InlineData("version")]
    [InlineData("level")]
    [InlineData("dictionary")]
    public void Bad_header_counts_and_shape_refuse_before_any_following_record_is_read(string field)
    {
        byte[] valid = Write(Creation().Request); byte[] header;
        using (var source = new MemoryStream(valid)) header = new ArchiveWorkerRecords(new(Nonce, ArchiveWritePolicy.MaxWorkerWireBytes), source, writing: false).Read(ArchiveControlRecord.TransformRequest);
        switch (field)
        {
            case "schema": header[0] = 1; break;
            case "format": BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(2), (int)ArchiveWriteFormat.Zip); break; // ZIP has no transform worker.
            case "unknown-format": BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(2), -1); break;
            case "version": BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(6), 2); break;
            case "level": BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10), 3); break;
            case "dictionary": BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(14), 1 << 20); break;
            case "flag": header[18] = 2; break;
            case "count": BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(19), int.MaxValue); break;
            case "create-edits": BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(23), 1); break;
            case "bound": BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(31), long.MaxValue); break;
            case "trailing": header = [.. header, 0]; break;
            case "truncate": header = header[..^1]; break;
        }
        using var bytes = new MemoryStream(); var records = new ArchiveWorkerRecords(new(Nonce, ArchiveWritePolicy.MaxWorkerWireBytes), bytes, writing: true);
        records.Write(ArchiveControlRecord.TransformRequest, header);
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, Assert.Throws<ArchiveWriteException>(() => Read(bytes.ToArray())).Reason);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("length")]
    [InlineData("time")]
    [InlineData("nanos-negative")]
    [InlineData("nanos-overflow")]
    [InlineData("schema")]
    [InlineData("mode")]
    [InlineData("trailing")]
    public void Bad_input_descriptors_refuse_without_exposing_input_or_stage_authority(string field)
    {
        using var valid = new MemoryStream(Write(Creation().Request)); var read = new ArchiveWorkerRecords(new(Nonce, ArchiveWritePolicy.MaxWorkerWireBytes), valid, writing: false);
        byte[] header = read.Read(ArchiveControlRecord.TransformRequest), input = read.Read(ArchiveControlRecord.TransformInput);
        switch (field)
        {
            case "id": BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(2), 37); break;
            case "length": BinaryPrimitives.WriteInt64LittleEndian(input.AsSpan(6), -1); break;
            case "time": BinaryPrimitives.WriteInt64LittleEndian(input.AsSpan(46), long.MaxValue); break;
            case "nanos-negative": BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(54), -1); break;
            case "nanos-overflow": BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(54), 1_000_000_000); break;
            case "schema": input[0] = 1; break;
            case "mode": BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(58), -1); break;
            case "trailing": input = [.. input, 0]; break;
        }
        using var bytes = new MemoryStream(); var records = new ArchiveWorkerRecords(new(Nonce, ArchiveWritePolicy.MaxWorkerWireBytes), bytes, writing: true);
        records.Write(ArchiveControlRecord.TransformRequest, header); records.Write(ArchiveControlRecord.TransformInput, input);
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, Assert.Throws<ArchiveWriteException>(() => Read(bytes.ToArray())).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reused_or_unknown_wire_grants_cannot_change_the_fixed_reference_set(bool unknown)
    {
        var original = Creation().Request;
        var changed = new ArchiveTransformRequest(null, original.Inputs.ToDictionary(), [],
            [original.Additions[0], new("two", ArchiveMemberType.File, unknown ? 3 : 1)], original.OutputLengthBound);
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, Assert.Throws<ArchiveWriteException>(() => Read(Write(changed))).Reason);
    }

    [Fact]
    public void Receipts_require_exact_schema_lengths_digests_and_EOF()
    {
        var expected = Creation().Expected; var receipt = new ArchiveTransformReceipt(1024, expected.Digest, expected.ContentDigest, expected.MetadataDigest);
        byte[] data = ArchiveTransformWire.Receipt(receipt); Assert.Equal(receipt, ArchiveTransformWire.ReadReceipt(data));
        foreach (byte[] invalid in new[] { data[..^1], [.. data, 0] })
            Assert.Throws<ArchiveWriteException>(() => ArchiveTransformWire.ReadReceipt(invalid));
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(2), -1); Assert.Throws<ArchiveWriteException>(() => ArchiveTransformWire.ReadReceipt(data));
    }

    [Fact]
    public void Contradictory_source_and_output_profiles_cannot_be_serialized_as_one_profile()
    {
        var request = new ArchiveTransformRequest(new(new(ArchiveWriteFormat.Tar), 1024, Hash(new byte[1024])), [], [], [], 2048, new(ArchiveWriteFormat.TarGzip));
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, Assert.Throws<ArchiveWriteException>(() => Write(request)).Reason);
    }
}
