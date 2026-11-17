using System.Buffers.Binary;
using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisObservationCaptureTests
{
    private static AnalysisCapturedFile SortedFile(ulong path, string? identity) => new(path,
        new(AnalysisObservationCapture.Id('p', path), identity, AnalysisMeasurement.Known(17), AnalysisMeasurement.Known(2)),
        new("f" + path, AnalysisObservationCapture.Id('p', path), null, "data", "basis", AnalysisMeasurement.Known(4096), "native", Start, []));

    [Theory]
    [InlineData(null)]
    [InlineData("identity")]
    [InlineData("identity😀")]
    public void SortedCapturePayloadKeepsExactMetadataAndLowestPathBeforePayloadBytes(string? identity)
    {
        var first = SortedFile(7, identity); var second = SortedFile(257, identity);
        byte[] a = AnalysisCapturedFileCodec.SortKey(AnalysisCapturedFileCodec.Encode(first));
        byte[] b = AnalysisCapturedFileCodec.SortKey(AnalysisCapturedFileCodec.Encode(second));
        Assert.Equal(first, AnalysisCapturedFileCodec.DecodeSortKey(a, out var group));
        Assert.Equal(AnalysisCapturedFileCodec.IdentityKey(first), group);
        Assert.True(a.AsSpan().SequenceCompareTo(b) < 0);
        if (identity is not null)
        { AnalysisCapturedFileCodec.DecodeSortKey(b, out var other); Assert.Equal(group, other); }
    }

    [Fact]
    public void MaximumAcceptedCapturedValueFieldsFitTheExistingSortKeyLimit()
    {
        var file = SortedFile(7, new string('i', 4096));
        file = file with
        {
            Object = file.Object with { VersionEvidence = new string('v', 4096), LogicalLength = AnalysisMeasurement.Known(UInt128.MaxValue) },
            Fact = file.Fact with { Id = new string('f', 512), ComponentId = new string('c', 256), BasisId = new string('b', 256), Method = new string('m', 256),
                Includes = Enumerable.Range(0, AnalysisLimits.Components).Select(n => n.ToString("D4") + new string('x', 252)).ToImmutableArray() }
        };
        byte[] key = AnalysisCapturedFileCodec.SortKey(AnalysisCapturedFileCodec.Encode(file));
        Assert.InRange(key.Length, 70_000, AnalysisExternalSort.MaximumKeyBytes);
        var decoded = AnalysisCapturedFileCodec.DecodeSortKey(key, out _);
        Assert.Equal(file.Object, decoded.Object); Assert.Equal(file.Fact.Includes, decoded.Fact.Includes);
    }

    [Fact]
    public void SortedCapturePayloadRejectsTruncationTrailingDataAndContradictoryGroupingOrPath()
    {
        byte[] key = AnalysisCapturedFileCodec.SortKey(AnalysisCapturedFileCodec.Encode(SortedFile(7, "identity")));
        for (int count = 0; count < key.Length; count++)
        { int length = count; Assert.Throws<InvalidDataException>(() => AnalysisCapturedFileCodec.DecodeSortKey(key.AsSpan(0, length), out _)); }
        Assert.Throws<InvalidDataException>(() => AnalysisCapturedFileCodec.DecodeSortKey([.. key, 1], out _));
        byte[] bad = key.ToArray(); BinaryPrimitives.WriteInt32BigEndian(bad, int.MaxValue);
        Assert.Throws<InvalidDataException>(() => AnalysisCapturedFileCodec.DecodeSortKey(bad, out _));
        bad = key.ToArray(); bad[5] ^= 1;
        Assert.Throws<InvalidDataException>(() => AnalysisCapturedFileCodec.DecodeSortKey(bad, out _));
        bad = key.ToArray(); int identityLength = BinaryPrimitives.ReadInt32BigEndian(bad);
        BinaryPrimitives.WriteUInt64BigEndian(bad.AsSpan(4 + identityLength), 8);
        Assert.Throws<InvalidDataException>(() => AnalysisCapturedFileCodec.DecodeSortKey(bad, out _));
    }

    [Fact]
    public async Task PermutedNativeIdentitiesProduceOrderedCanonicalObjectsWithExactAliasesAndBoundedReads()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory);
        using var io = new DeviceIoScheduler(); var header = Header;
        using var capture = new AnalysisObservationCapture(workspace.CreateArena(), header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        const int files = 5003;
        for (int n = 0; n < files; n++)
            capture.Append(Entry("f" + n, identity: "inode-" + ((n * 7919) % files).ToString("D8")), 1, "r", "basis", cancellation: Cancellation);
        capture.Append(Entry("alias", identity: "inode-00000000"), 1, "r", "basis", cancellation: Cancellation);
        capture.Publish(Cancellation); long before = directory.ReadBytes;
        using var revision = await Complete(capture, workspace, io, header); using var reader = revision.Acquire();
        long coalescingBytes = directory.ReadBytes - before;
        Console.WriteLine("Authenticated bytes read while coalescing 5,003 permuted identities: " + coalescingBytes);
        Assert.True(coalescingBytes < 128L * 1024 * 1024, "Coalescing amplified a small captured tape into excessive authenticated block reads.");
        Assert.Equal((ulong)files, reader.Count(AnalysisIndexedGraph.Objects));
        for (ulong row = 1; row <= (ulong)files; row++)
            Assert.Equal(AnalysisObservationCapture.Id('o', row + 1), reader.ReadObject(row, Cancellation).Id);
        string? first = reader.ReadPath(2, Cancellation).ObjectId;
        Assert.Equal(first, reader.ReadPath((ulong)files + 2, Cancellation).ObjectId); Assert.Equal(2UL, reader.Aliases(first!, Cancellation).Count);
        // Includes final graph validation and readback for this small, cache-resident fixture.
        // Cache-pressure performance is measured separately with real million-file trees.
        Assert.True(directory.ReadBytes - before < 128L * 1024 * 1024, "Coalescing amplified a small captured tape into excessive authenticated block reads.");
    }
}
