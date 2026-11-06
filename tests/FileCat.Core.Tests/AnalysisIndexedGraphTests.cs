using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class AnalysisIndexedGraphTests
{
    private const string Device = "indexed-graph-fixture";
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-06T00:00:00Z");
    private static AnalysisIndexedGraphHeader Header => new(new(Guid.NewGuid(), 3, 0, "fixture", Start, Start) { TimeZoneId = "UTC" },
        [new("root", "file", "/source", "/source", null, IdentityAssurance.Unknown, "source") { NameRules = AnalysisNameRules.Unix }],
        new ScanPolicy(), AnalysisPhase.Scanning, NamespaceCoverage.Partial, Origin: AnalysisOrigin.ImportedInventory,
        Taint: AnalysisTaint.OrdinarySensitive | AnalysisTaint.PrivilegedDerived);
    private static AnalysisObject Object(string id = "object", string? identity = "native-object") =>
        new(id, identity, AnalysisMeasurement.Known(0), AnalysisMeasurement.Known(2), Start, "version");
    private static StorageFact Fact(string id = "fact", string? owner = "object", ulong? directory = null) =>
        new(id, owner, directory, "reported-data", "fixture", AnalysisMeasurement.Known(4096), "native-fixture", Start, []);
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private static async Task<AnalysisIndexedGraph> Create(AnalysisScratchWorkspace workspace, DeviceIoScheduler io, AnalysisIndexedGraphHeader header) =>
        await AnalysisIo.Run(io, Device, _ => new AnalysisIndexedGraph(workspace.CreateArena(), header), Cancellation);
    private static Task<AnalysisIndexedGraphRevision> Seal(AnalysisIndexedGraph graph, AnalysisScratchWorkspace workspace, DeviceIoScheduler io,
        AnalysisIndexedGraphHeader header, CancellationToken cancellation) =>
        graph.SealAsync(workspace, io, Device, header.Identity with { Revision = 1, ObservedUtc = Start.AddSeconds(10) }, AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy, cancellation);

    [Fact]
    public async Task ExactRelationsRetainTheSameInertGraphAfterEveryWriterOwnerCloses()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler(); var header = Header;
        using var graph = await Create(workspace, io, header);
        await AnalysisIo.Run(io, Device, token =>
        {
            graph.AppendPath(new(1, "root", 0, "source", AnalysisKind.Directory), token);
            graph.AppendPath(new(2, "root", 1, "first", AnalysisKind.File, "object") { ObservedIdentity = "native-object", Times = new(Start, Start, 1) }, token);
            graph.AppendPath(new(3, "root", 1, "second", AnalysisKind.File, "object") { ObservedIdentity = "native-object" }, token);
            graph.AppendObject(Object(), token); graph.AppendFact(Fact(), token); graph.AppendFact(Fact("directory", null, 1), token);
            graph.AppendIssue(new("changed", 2, MeasurementState.Changed) { RootId = "root" }, token);
            graph.AppendIssue(new("root-issue") { RootId = "root" }, token); graph.AppendIssue(new("global-issue"), token);
            return true;
        }, Cancellation);
        using var revision = await Seal(graph, workspace, io, header, Cancellation); using var reader = revision.Acquire();
        Assert.Equal(header.Identity.SessionId, reader.Header.Identity.SessionId); Assert.Equal(3, reader.Header.Identity.Epoch); Assert.Equal(1, reader.Header.Identity.Revision);
        Assert.Equal(AnalysisOrigin.ImportedInventory, reader.Header.Origin);
        Assert.Equal(AnalysisPrivacy.Imported(header.Taint), reader.Header.Taint);
        Assert.Equal(3UL, reader.Count(AnalysisIndexedGraph.Paths)); Assert.Equal(new AnalysisIndexRange(2, 2), reader.Children(0, 1, Cancellation));
        var aliases = reader.Aliases("object", Cancellation); Assert.Equal(2UL, aliases.Count);
        Assert.Equal(new ulong[] { 2, 3 }, Enumerable.Range(0, 2).Select(n => reader.ReadIndex(AnalysisIndexedGraphRevision.Aliases, aliases.First + (ulong)n, Cancellation).Value));
        var facts = reader.Find(AnalysisIndexedGraphRevision.FactOwners, AnalysisGraphKeys.FactOwner("object", null), cancellation: Cancellation);
        Assert.Equal(1UL, facts.Count); Assert.Equal((UInt128)4096, reader.ReadFact(reader.ReadIndex(AnalysisIndexedGraphRevision.FactOwners, facts.First, Cancellation).Value, Cancellation).Measurement.Value);
        var issues = reader.Find(AnalysisIndexedGraphRevision.IssueOwners, AnalysisGraphKeys.IssueOwner(2, null, header.Roots), cancellation: Cancellation);
        Assert.Equal(1UL, issues.Count); Assert.Equal("changed", reader.ReadIssue(reader.ReadIndex(AnalysisIndexedGraphRevision.IssueOwners, issues.First, Cancellation).Value, Cancellation).Code);
        Assert.Throws<InvalidOperationException>(() => graph.AppendIssue(new("late"), Cancellation));
        graph.Dispose(); revision.Dispose(); workspace.Dispose(); Assert.False(directory.Closed);
        using var retained = reader.Retain(); reader.Dispose(); Assert.Equal("second", retained.ReadPath(3, Cancellation).Name);
        Assert.Equal(AnalysisMeasurement.Known(0), retained.ReadObject(1, Cancellation).LogicalLength);
        retained.Dispose(); Assert.True(directory.Closed); Assert.Equal(0, directory.LiveFiles); Assert.Equal(0, workspace.ReservedBytes);
    }

    [Theory]
    [InlineData("duplicate-object")]
    [InlineData("duplicate-child")]
    [InlineData("missing-object")]
    [InlineData("identity-mismatch")]
    [InlineData("missing-fact-owner")]
    [InlineData("fact-file-owner")]
    [InlineData("duplicate-fact")]
    [InlineData("missing-issue-node")]
    public async Task InvalidCrossTableRelationshipsNeverPublishAUsableGraphOrLeakTemporaryIndexes(string fault)
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler(); var header = Header;
        using var graph = await Create(workspace, io, header);
        graph.AppendPath(new(1, "root", 0, "source", AnalysisKind.Directory), Cancellation);
        graph.AppendPath(new(2, "root", 1, "first", AnalysisKind.File, fault == "missing-object" ? "missing" : "object")
            { ObservedIdentity = fault == "identity-mismatch" ? "wrong-object" : "native-object" }, Cancellation);
        graph.AppendObject(Object(), Cancellation);
        switch (fault)
        {
            case "duplicate-object": graph.AppendObject(Object(), Cancellation); break;
            case "duplicate-child": graph.AppendPath(new(3, "root", 1, "first", AnalysisKind.File, "object"), Cancellation); break;
            case "missing-fact-owner": graph.AppendFact(Fact(owner: "missing"), Cancellation); break;
            case "fact-file-owner": graph.AppendFact(Fact(owner: null, directory: 2), Cancellation); break;
            case "duplicate-fact": graph.AppendFact(Fact(), Cancellation); graph.AppendFact(Fact(), Cancellation); break;
            case "missing-issue-node": graph.AppendIssue(new("missing", 9), Cancellation); break;
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => Seal(graph, workspace, io, header, Cancellation));
        Assert.Equal(1, directory.LiveFiles); // only the caller-owned raw tables survive a refused index build.
        graph.Dispose(); Assert.Equal(0, workspace.ReservedBytes); Assert.Equal(0, directory.LiveFiles);
    }

    [Fact]
    public async Task EarlierParentValidationDoesNotEvictTheSequentialPathCacheForEveryObservation()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler(); var header = Header;
        using var graph = await Create(workspace, io, header);
        for (ulong id = 1; id <= 5000; id++)
            graph.AppendPath(new(id, "root", id / 2, "folder-" + id, AnalysisKind.Directory), Cancellation);
        using var revision = await Seal(graph, workspace, io, header, Cancellation);
        Assert.True(directory.ReadBytes < 16L * 1024 * 1024, $"Excessive parent-validation read volume: {directory.ReadBytes} bytes.");
        using var reader = revision.Acquire(); Assert.Equal(5000UL, reader.Count(AnalysisIndexedGraph.Paths));
        Assert.Equal(2500UL, reader.ReadPath(5000, Cancellation).ParentId);
    }

    [Fact]
    public async Task WholeGraphConsumersStayBoundedAndRetainAfterOwnerClosureWithoutSpendingOneLeasePerTable()
    {
        Ordinary(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var io = new DeviceIoScheduler(); var header = Header;
        using var graph = await Create(workspace, io, header); graph.AppendPath(new(1, "root", 0, "source", AnalysisKind.Directory), Cancellation);
        using var revision = await Seal(graph, workspace, io, header, Cancellation);
        var readers = Enumerable.Range(0, AnalysisLimits.SnapshotConsumers).Select(_ => revision.Acquire()).ToArray();
        try
        {
            Assert.Throws<IOException>(() => revision.Acquire()); Assert.Throws<IOException>(() => readers[0].Retain());
            revision.Dispose(); graph.Dispose(); workspace.Dispose(); Assert.Throws<IOException>(() => readers[0].Retain());
            readers[^1].Dispose(); using var extra = readers[0].Retain(); Assert.Equal("source", extra.ReadPath(1, Cancellation).Name);
        }
        finally { foreach (var reader in readers) reader.Dispose(); }
        Assert.Equal(0, workspace.ReservedBytes);
    }

    [Fact]
    public async Task FailedRawPublicationStopsTheWriterWithoutPublishingAnIndexedGraph()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler(); var header = Header;
        using var graph = await Create(workspace, io, header); graph.AppendPath(new(1, "root", 0, "source", AnalysisKind.Directory), Cancellation);
        directory.FailNextWrite = true;
        // Raw table flush fails before any index can publish. This graph is then explicitly failed.
        await Assert.ThrowsAsync<IOException>(() => Seal(graph, workspace, io, header, Cancellation));
        Assert.Throws<InvalidOperationException>(() => graph.AppendObject(Object(), Cancellation));
        await Assert.ThrowsAsync<IOException>(() => Seal(graph, workspace, io, header, Cancellation));
        graph.Dispose(); Assert.Equal(0, workspace.ReservedBytes);
    }

    [Fact]
    public async Task FailedTemporaryIndexCanRetryExactlyTheSameFrozenGraphWithoutLosingItsInput()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler(); var header = Header;
        using var graph = await Create(workspace, io, header); graph.AppendPath(new(1, "root", 0, "source", AnalysisKind.Directory), Cancellation);
        directory.FailAtCreate = 3; // raw arena, first sorted spool, then refused index output.
        await Assert.ThrowsAsync<IOException>(() => Seal(graph, workspace, io, header, Cancellation));
        Assert.Equal(1, directory.LiveFiles); Assert.True(workspace.ReservedBytes > 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => graph.SealAsync(workspace, io, Device,
            header.Identity with { Revision = 1, ObservedUtc = Start.AddSeconds(11) }, AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy, Cancellation));
        using var revision = await Seal(graph, workspace, io, header, Cancellation); using var reader = revision.Acquire();
        Assert.Equal("source", reader.ReadPath(1, Cancellation).Name); Assert.Equal(1UL, reader.Count(AnalysisIndexedGraph.Paths));
    }

    private sealed class MemoryDirectory : AnalysisPrivateDirectory
    {
        internal bool Closed, FailNextWrite; internal int LiveFiles, Creates, FailAtCreate = -1; internal long ReadBytes;
        internal override void Verify() => ObjectDisposedException.ThrowIf(Closed, this);
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1024 * 1024 * 1024, (UInt128)1024 * 1024 * 1024);
        internal override AnalysisPrivateScratchFile CreateScratch()
        { if (++Creates == FailAtCreate) throw new IOException("Injected index arena failure."); LiveFiles++; return new MemoryFile(this); }
        internal override AnalysisPrivateFile CreateStaging() => throw new NotSupportedException();
        internal override AnalysisPrivateFile OpenRead(string name) => throw new NotSupportedException();
        public override void Dispose() { Closed = true; }
    }
    private sealed class MemoryFile(MemoryDirectory directory) : AnalysisPrivateScratchFile
    {
        private readonly MemoryStream _bytes = new(); private bool _closed;
        public override long Length => _bytes.Length;
        public override void Verify() { }
        public override int ReadAt(Span<byte> buffer, long offset)
        { _bytes.Position = offset; int read = _bytes.Read(buffer); Interlocked.Add(ref directory.ReadBytes, read); return read; }
        public override void WriteAt(ReadOnlySpan<byte> buffer, long offset)
        {
            _bytes.Position = offset;
            if (directory.FailNextWrite) { directory.FailNextWrite = false; _bytes.Write(buffer[..Math.Min(buffer.Length, 3)]); throw new IOException("Injected partial graph write."); }
            _bytes.Write(buffer);
        }
        public override void Dispose() { if (_closed) return; _closed = true; _bytes.Dispose(); directory.LiveFiles--; }
    }
}
