using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisObservationCaptureTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-06T00:00:00Z");
    private const string Device = "captured-observations";
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private static AnalysisIndexedGraphHeader Header => new(new(Guid.NewGuid(), 2, 1, "fixture", Start, Start) { TimeZoneId = "UTC" },
        [new("r", "file", "/source", "/source", null, IdentityAssurance.Unknown, "source") { NameRules = AnalysisNameRules.Unix }], new(), AnalysisPhase.Scanning, NamespaceCoverage.Partial);
    private static AnalysisEntry Entry(string name, AnalysisKind kind = AnalysisKind.File, string? identity = "native-file", ulong length = 10, ulong allocation = 4096, string version = "v", ulong links = 2) =>
        new([], name, [], kind, identity, AnalysisMeasurement.Known(length), AnalysisMeasurement.Known(allocation), AnalysisMeasurement.Known(links), Start, version, ObservedUtc: Start);
    private static Task<AnalysisIndexedGraphRevision> Complete(AnalysisObservationCapture capture, AnalysisScratchWorkspace workspace, DeviceIoScheduler io, AnalysisIndexedGraphHeader header) =>
        capture.CompleteAsync(workspace, io, Device, header.Identity with { Revision = 2, ObservedUtc = Start.AddSeconds(10) }, AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy, Cancellation);

    [Fact]
    public async Task StaleEvidencePastTheIssueSampleCapRetainsExactRootAndIncompleteScopeEvidence()
    {
        Ordinary(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var io = new DeviceIoScheduler(); var header = Header;
        using var capture = new AnalysisObservationCapture(workspace.CreateArena(), header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        for (int n = 0; n < AnalysisLimits.Issues; n++) capture.Issue("special-entry-not-traversed", 1, MeasurementState.Excluded, cancellation: Cancellation);
        capture.Issue("source-changed-or-boundary", 1, MeasurementState.Failed, cancellation: Cancellation);
        using var revision = await Complete(capture, workspace, io, header); var coverage = revision.Header.CoverageDetails!;
        Assert.Equal(AnalysisFreshness.PossiblyStale, revision.Header.Freshness); Assert.Equal(1UL, revision.Header.IssueTotals[MeasurementState.Failed]);
        Assert.Equal(new[] { "r" }, coverage.Observations.StaleRootIds); Assert.False(coverage.StaleScopesComplete); Assert.False(coverage.HasUnlocatedStaleScopes);
        Assert.Empty(coverage.Observations.StaleScopeNodes);
    }

    [Fact]
    public async Task EnumerationPublicationRejectsBackwardCountsWithoutChangingTheAcceptedResult()
    {
        Ordinary(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var io = new DeviceIoScheduler(); var header = Header;
        using var capture = new AnalysisObservationCapture(workspace.CreateArena(), header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        capture.Enumeration(new(1, 0, 0), Cancellation); capture.Publish(Cancellation); capture.Enumeration(new(1, 1, 0), Cancellation); capture.Publish(Cancellation);
        Assert.Throws<InvalidDataException>(() => capture.Enumeration(new(1, 0, 0), Cancellation));
        Assert.Throws<InvalidDataException>(() => capture.Enumeration(new(2, 1, 0), Cancellation));
        using var revision = await Complete(capture, workspace, io, header);
        Assert.Equal(1UL, revision.Header.CoverageDetails!.Observations.EnumerationCompleted); Assert.True(revision.Header.CoverageDetails.Observations.DenominatorFinal);
    }

    [Fact]
    public async Task ExactIdentityAliasesCoalesceWhileUnverifiedFilesStayDistinctAndEveryRawPathSurvives()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler(); var header = Header;
        using var capture = new AnalysisObservationCapture(workspace.CreateArena(), header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", true, Cancellation);
        capture.Append(Entry("first"), 1, "r", "basis", cancellation: Cancellation); capture.Publish(Cancellation);
        capture.Append(Entry("second"), 1, "r", "basis", cancellation: Cancellation);
        capture.Append(Entry("unknown-a", identity: null), 1, "r", "basis", cancellation: Cancellation);
        capture.Append(Entry("unknown-b", identity: null), 1, "r", "basis", cancellation: Cancellation);
        // Full keys differing only at the end must not be treated as an identity hash collision/equality.
        capture.Append(Entry("near", identity: "native-filf"), 1, "r", "basis", cancellation: Cancellation);
        using var revision = await Complete(capture, workspace, io, header); using var reader = revision.Acquire();
        Assert.Equal(6UL, reader.Count(AnalysisIndexedGraph.Paths)); Assert.Equal(4UL, reader.Count(AnalysisIndexedGraph.Objects)); Assert.Equal(5UL, reader.Count(AnalysisIndexedGraph.Facts));
        Assert.Equal(AnalysisObservationCapture.Id('o', 2), reader.ReadPath(2, Cancellation).ObjectId); Assert.Equal(reader.ReadPath(2, Cancellation).ObjectId, reader.ReadPath(3, Cancellation).ObjectId);
        Assert.NotEqual(reader.ReadPath(4, Cancellation).ObjectId, reader.ReadPath(5, Cancellation).ObjectId);
        Assert.Equal(2UL, reader.Aliases(reader.ReadPath(2, Cancellation).ObjectId!, Cancellation).Count);
        Assert.Equal(1UL, reader.Aliases(reader.ReadPath(6, Cancellation).ObjectId!, Cancellation).Count);
        capture.Dispose(); revision.Dispose(); workspace.Dispose(); Assert.False(directory.Closed);
        using var retained = reader.Retain(); reader.Dispose(); Assert.Equal("second", retained.ReadPath(3, Cancellation).Name);
        retained.Dispose(); Assert.True(directory.Closed); Assert.Equal(0, workspace.ReservedBytes); Assert.Equal(0, directory.LiveFiles);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("length")]
    [InlineData("allocation")]
    [InlineData("links")]
    public async Task ConflictingAliasObservationsBecomeChangedWithoutLosingAnyAliasOrInventingAllocation(string difference)
    {
        Ordinary(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var io = new DeviceIoScheduler(); var header = Header;
        using var capture = new AnalysisObservationCapture(workspace.CreateArena(), header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        capture.Append(Entry("first"), 1, "r", "basis", cancellation: Cancellation);
        capture.Append(Entry("second", length: difference == "length" ? 11UL : 10UL, allocation: difference == "allocation" ? 8192UL : 4096UL,
            version: difference == "version" ? "later" : "v", links: difference == "links" ? 3UL : 2UL), 1, "r", "basis", cancellation: Cancellation);
        using var revision = await Complete(capture, workspace, io, header); using var reader = revision.Acquire();
        Assert.Equal(1UL, reader.Count(AnalysisIndexedGraph.Objects)); Assert.Equal(2UL, reader.Aliases(reader.ReadPath(2, Cancellation).ObjectId!, Cancellation).Count);
        var obj = reader.ReadObject(1, Cancellation); var fact = reader.ReadFact(1, Cancellation);
        Assert.Equal(difference is "version" or "length" ? MeasurementState.Changed : MeasurementState.Known, obj.LogicalLength.State);
        Assert.Equal(difference == "links" ? MeasurementState.Changed : MeasurementState.Known, obj.LinkCount.State);
        Assert.Equal(difference == "links" ? MeasurementState.Known : MeasurementState.Changed, fact.Measurement.State);
        Assert.Equal(AnalysisFreshness.PossiblyStale, reader.Header.Freshness); Assert.Equal(1UL, reader.Header.IssueTotals[MeasurementState.Changed]);
        Assert.Equal(1UL, reader.Count(AnalysisIndexedGraph.Issues));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("directory")]
    [InlineData("issue")]
    public async Task AlreadyChangedObservationsRetainTheirFreshnessWithoutAnotherAlias(string kind)
    {
        Ordinary(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var io = new DeviceIoScheduler(); var header = Header;
        using var capture = new AnalysisObservationCapture(workspace.CreateArena(), header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        if (kind == "file") capture.Append(Entry("changed", identity: null) with { MetadataState = MeasurementState.Changed, LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Changed),
            Allocation = AnalysisMeasurement.Missing(MeasurementState.Changed), Links = AnalysisMeasurement.Missing(MeasurementState.Changed), Version = "" }, 1, "r", "basis", cancellation: Cancellation);
        else if (kind == "directory") capture.Append(Entry("directory", AnalysisKind.Directory, null) with { Allocation = AnalysisMeasurement.Missing(MeasurementState.Changed) }, 1, "r", "basis", true, Cancellation);
        else capture.Issue("source-changed-or-boundary", 1, MeasurementState.Changed, "r", Cancellation);
        using var revision = await Complete(capture, workspace, io, header); using var reader = revision.Acquire();
        Assert.Equal(AnalysisFreshness.PossiblyStale, reader.Header.Freshness);
        if (kind == "file") { Assert.Equal(MeasurementState.Changed, reader.ReadObject(1, Cancellation).LogicalLength.State); Assert.Equal(MeasurementState.Changed, reader.ReadPath(2, Cancellation).MetadataState); }
    }

    [Fact]
    public async Task DetailedIssueSamplesStopAtTheirCapWhileExactCountersAndBoundariesContinue()
    {
        Ordinary(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var io = new DeviceIoScheduler(); var header = Header;
        using var capture = new AnalysisObservationCapture(workspace.CreateArena(), header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        for (int n = 0; n < AnalysisLimits.Issues + 7; n++) capture.Issue("navigation-link-not-followed", 1, MeasurementState.Excluded, "r", Cancellation);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => capture.Issue("navigation-link-not-followed", 1, MeasurementState.Excluded, "r", stop.Token));
        using var revision = await Complete(capture, workspace, io, header); using var reader = revision.Acquire();
        Assert.Equal((ulong)AnalysisLimits.Issues, reader.Count(AnalysisIndexedGraph.Issues));
        Assert.Equal((ulong)AnalysisLimits.Issues + 7, reader.Header.IssueTotals[MeasurementState.Excluded]);
        Assert.Equal((ulong)AnalysisLimits.Issues + 7, reader.Header.BoundaryTotals[AnalysisBoundaryKind.NavigationLink]);
    }

    [Fact]
    public async Task AFailedLaterRawFlushKeepsThePreviousCoherentPrefixAvailableAsPartial()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler(); var header = Header;
        using var capture = new AnalysisObservationCapture(workspace.CreateArena(), header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        capture.Append(Entry("first"), 1, "r", "basis", cancellation: Cancellation); capture.Publish(Cancellation);
        capture.Append(Entry("not-published"), 1, "r", "basis", cancellation: Cancellation); directory.FailNextWrite = true;
        Assert.Throws<IOException>(() => capture.Publish(Cancellation)); Assert.True(capture.Failed);
        using var revision = await Complete(capture, workspace, io, header); using var reader = revision.Acquire();
        Assert.Equal(2UL, reader.Count(AnalysisIndexedGraph.Paths)); Assert.Equal(1UL, reader.Count(AnalysisIndexedGraph.Objects));
        Assert.Equal(NamespaceCoverage.Partial, reader.Header.Coverage); Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadPath(3, Cancellation));
    }

    [Fact]
    public async Task TemporaryIndexFailurePreservesCapturedMetadataForAnIdenticalRetryAndCancellationReleasesOutputs()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler(); var header = Header;
        using var capture = new AnalysisObservationCapture(workspace.CreateArena(), header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        capture.Append(Entry("file"), 1, "r", "basis", cancellation: Cancellation); capture.Publish(Cancellation);
        directory.FailAtCreate = directory.Creates + 2; // final graph arena, then refused identity spool.
        await Assert.ThrowsAsync<IOException>(() => Complete(capture, workspace, io, header)); Assert.Equal(1, directory.LiveFiles);
        using var revision = await Complete(capture, workspace, io, header); using var reader = revision.Acquire(); Assert.Equal("file", reader.ReadPath(2, Cancellation).Name);
        Assert.Throws<InvalidOperationException>(() => capture.Append(Entry("late"), 1, "r", "basis", cancellation: Cancellation));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture.CompleteAsync(workspace, io, Device, header.Identity with { Revision = 2, ObservedUtc = Start.AddSeconds(10) }, AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy, stop.Token));
    }

    [Fact]
    public void CapturedFileCodecRejectsEveryTruncationAndTrailingBytesWithoutEncodingLiveReadWitnesses()
    {
        var file = new AnalysisCapturedFile(7, new(AnalysisObservationCapture.Id('p', 7), "native", AnalysisMeasurement.Known(UInt128.MaxValue), AnalysisMeasurement.Known(2)),
            new("f7", AnalysisObservationCapture.Id('p', 7), null, "data", "basis", AnalysisMeasurement.Missing(MeasurementState.Unsupported), "native", Start, []));
        byte[] bytes = AnalysisCapturedFileCodec.Encode(file); Assert.Equal(file, AnalysisCapturedFileCodec.Decode(bytes));
        for (int n = 0; n < bytes.Length; n++) { int count = n; Assert.Throws<InvalidDataException>(() => AnalysisCapturedFileCodec.Decode(bytes.AsSpan(0, count))); }
        Assert.Throws<InvalidDataException>(() => AnalysisCapturedFileCodec.Decode([.. bytes, 1]));
        Assert.DoesNotContain("Ancestry", typeof(AnalysisCapturedFile).GetProperties().Select(p => p.Name));
    }

    private sealed class MemoryDirectory : AnalysisPrivateDirectory
    {
        internal bool Closed, FailNextWrite; internal int LiveFiles, Creates, FailAtCreate = -1;
        internal long ReadBytes;
        internal override void Verify() => ObjectDisposedException.ThrowIf(Closed, this);
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1024 * 1024 * 1024, (UInt128)1024 * 1024 * 1024);
        internal override AnalysisPrivateScratchFile CreateScratch()
        { if (++Creates == FailAtCreate) throw new IOException("Injected arena create failure."); LiveFiles++; return new MemoryFile(this); }
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
        { _bytes.Position = offset; int read = _bytes.Read(buffer); directory.ReadBytes += read; return read; }
        public override void WriteAt(ReadOnlySpan<byte> buffer, long offset)
        {
            _bytes.Position = offset;
            if (directory.FailNextWrite) { directory.FailNextWrite = false; _bytes.Write(buffer[..Math.Min(buffer.Length, 3)]); throw new IOException("Injected partial capture write."); }
            _bytes.Write(buffer);
        }
        public override void Dispose() { if (_closed) return; _closed = true; _bytes.Dispose(); directory.LiveFiles--; }
    }
}
