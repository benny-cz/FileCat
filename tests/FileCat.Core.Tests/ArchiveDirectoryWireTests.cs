using System.Buffers.Binary;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveDirectoryWireTests
{
    [Theory]
    [InlineData("old-header")]
    [InlineData("old-addition")]
    [InlineData("missing-metadata")]
    [InlineData("file-kind")]
    [InlineData("file-grant")]
    [InlineData("mode")]
    [InlineData("nanos")]
    [InlineData("seconds")]
    [InlineData("trailing")]
    [InlineData("truncated")]
    public void Directory_records_reject_incompatible_schema_kind_grants_and_metadata(string change)
    {
        var ct = TestContext.Current.CancellationToken; var nonce = Guid.NewGuid();
        var plan = ArchiveTarTransformPlan.Create(null, [], [], [new("dir/", new ArchiveTransformDirectoryMetadata(new(-1, 999999999), 0x1c0))], ct);
        using var original = new MemoryStream();
        ArchiveTransformWire.WriteRequest(new(new(nonce, ArchiveWritePolicy.MaxWorkerWireBytes), original, true), ArchiveTransformWire.Prepare(plan, ct).Request, ct);
        original.Position = 0; var read = new ArchiveWorkerRecords(new(nonce, ArchiveWritePolicy.MaxWorkerWireBytes), original, false);
        byte[] header = read.Read(ArchiveControlRecord.TransformRequest), addition = read.Read(ArchiveControlRecord.TransformAddition);
        // schema + length-prefixed UTF-8 name + kind + grant + metadata flag, then seconds/nanos/mode.
        int kind = 6 + BinaryPrimitives.ReadInt32LittleEndian(addition.AsSpan(2)); int metadata = kind + 9;
        switch (change)
        {
            case "old-header": header[0] = 2; break;
            case "old-addition": addition[0] = 1; break;
            case "missing-metadata": addition[kind + 8] = 0; break;
            case "file-kind": BinaryPrimitives.WriteInt32LittleEndian(addition.AsSpan(kind), (int)ArchiveMemberType.File); break;
            case "file-grant": BinaryPrimitives.WriteInt32LittleEndian(addition.AsSpan(kind + 4), 1); break;
            case "mode": BinaryPrimitives.WriteInt32LittleEndian(addition.AsSpan(metadata + 12), 0x400); break;
            case "nanos": BinaryPrimitives.WriteInt32LittleEndian(addition.AsSpan(metadata + 8), 1_000_000_000); break;
            case "seconds": BinaryPrimitives.WriteInt64LittleEndian(addition.AsSpan(metadata), long.MaxValue); break;
            case "trailing": addition = [.. addition, 0]; break;
            case "truncated": addition = addition[..^1]; break;
        }
        using var bad = new MemoryStream(); var write = new ArchiveWorkerRecords(new(nonce, ArchiveWritePolicy.MaxWorkerWireBytes), bad, true);
        write.Write(ArchiveControlRecord.TransformRequest, header); write.Write(ArchiveControlRecord.TransformAddition, addition); bad.Position = 0;
        var failure = Assert.Throws<ArchiveWriteException>(() => ArchiveTransformWire.ReadRequest(new(new(nonce, ArchiveWritePolicy.MaxWorkerWireBytes), bad, false), ct));
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, failure.Reason);
    }
}
