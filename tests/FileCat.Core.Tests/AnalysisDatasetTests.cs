using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

[Collection("Analysis bounded work capacity")]
public sealed partial class AnalysisDatasetTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-06T00:00:00Z");
    private const string Device = "paged-dataset-fixture";

    [Fact]
    public async Task PagesAndRelatedQueriesStayExactAfterEveryProducerOwnerCloses()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler();
        using var capture = Capture(workspace, out var header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        capture.Append(Entry("first"), 1, "r", "basis", cancellation: Cancellation); capture.Append(Entry("second"), 1, "r", "basis", cancellation: Cancellation);
        capture.Issue("sample", 2, MeasurementState.Unsupported, "r", Cancellation);
        var revision = await Complete(capture, workspace, io, header); await using var dataset = new AnalysisDataset(revision, io, Device);
        long before = directory.Reads; Assert.Equal(3UL, dataset.Descriptor.Paths); Assert.Equal(1UL, dataset.Descriptor.Objects); Assert.Equal(before, directory.Reads);
        await using var lease = await dataset.AcquireAsync(Cancellation); Assert.Equal(before, directory.Reads); // metadata/ownership only.
        capture.Dispose(); workspace.Dispose(); await dataset.DisposeAsync(); Assert.False(directory.Closed);
        var first = await lease.ReadPathsAsync(0, 2, Cancellation); Assert.Equal(new ulong[] { 1, 2 }, first.Rows.Select(n => n.Id)); Assert.True(first.HasMore);
        var second = await lease.ReadPathsAsync(first.NextOffset, 2, Cancellation); Assert.Equal(new ulong[] { 3 }, second.Rows.Select(n => n.Id)); Assert.False(second.HasMore);
        var children = await lease.ReadChildrenAsync(0, 1, 0, cancellation: Cancellation); Assert.Equal(new[] { "first", "second" }, children.Rows.Select(n => n.Name));
        string id = children.Rows[0].ObjectId!;
        var aliases = await lease.ReadAliasesAsync(id, 0, cancellation: Cancellation); Assert.Equal(new ulong[] { 2, 3 }, aliases.Rows.Select(n => n.Id));
        var objects = await lease.ReadObjectsAsync(0, cancellation: Cancellation); Assert.Single(objects.Rows); Assert.Equal(id, objects.Rows[0].Id);
        Assert.Equal(objects.Rows[0], await lease.ReadObjectAsync(id, Cancellation)); Assert.Null(await lease.ReadObjectAsync("missing", Cancellation));
        Assert.Empty((await lease.ReadDirectoryFactsAsync(1, 0, cancellation: Cancellation)).Rows);
        var facts = await lease.ReadObjectFactsAsync(id, 0, cancellation: Cancellation); Assert.Single(facts.Rows); Assert.Equal(AnalysisMeasurement.Known(4096), facts.Rows[0].Measurement);
        var issues = await lease.ReadPathIssuesAsync(2, 0, cancellation: Cancellation); Assert.Equal("sample", Assert.Single(issues.Rows).Code);
        Assert.Equal(1UL, lease.Descriptor.Issues);
        await using var retained = await lease.RetainAsync(Cancellation); await lease.DisposeAsync();
        Assert.Equal(3UL, (await retained.ReadPathsAsync(0, cancellation: Cancellation)).Total);
        await retained.DisposeAsync(); Assert.True(directory.Closed); Assert.Equal(0, workspace.ReservedBytes);
    }

    [Fact]
    public async Task LongMetadataRowsRespectTheByteBoundAndAdvanceWithoutTruncatingNames()
    {
        Ordinary(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var io = new DeviceIoScheduler(); using var capture = Capture(workspace, out var header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        string a = new('x', AnalysisLimits.RawNameBytes - 1); a += "a"; string b = a[..^1] + "b";
        capture.Append(Entry(a, identity: null), 1, "r", "basis", cancellation: Cancellation); capture.Append(Entry(b, identity: null), 1, "r", "basis", cancellation: Cancellation);
        await using var dataset = new AnalysisDataset(await Complete(capture, workspace, io, header), io, Device); await using var lease = await dataset.AcquireAsync(Cancellation);
        var page = await lease.ReadPathsAsync(1, AnalysisLimits.BatchRecords, Cancellation); Assert.Equal(a, Assert.Single(page.Rows).Name); Assert.True(page.HasMore);
        var next = await lease.ReadPathsAsync(page.NextOffset, AnalysisLimits.BatchRecords, Cancellation); Assert.Equal(b, Assert.Single(next.Rows).Name); Assert.False(next.HasMore);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => lease.ReadPathsAsync(4, cancellation: Cancellation));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => lease.ReadPathsAsync(0, AnalysisLimits.BatchRecords + 1, Cancellation));
    }

    [Fact]
    public async Task DisposalWaitsForTheActualQueuedReadAndBoundsPendingRequests()
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1); var resource = new FakeOwner();
        var owned = new AnalysisOwnedDatasetIo<FakeOwner>(resource, io, Device);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        Task blocker = io.Run(Device, IoPriority.Interactive, _ => { entered.Set(); release.Wait(Cancellation); }, Cancellation);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(3), Cancellation));
        var requests = Enumerable.Range(0, AnalysisLimits.IoRequests).Select(_ => owned.RunAsync((value, _) => { Assert.False(value.Closed); return 1; }, Cancellation)).ToArray();
        Assert.Throws<InvalidOperationException>(() => { _ = owned.RunAsync((_, _) => 1, Cancellation); });
        var close = owned.DisposeAsync().AsTask(); Assert.False(close.IsCompleted); Assert.False(resource.Closed);
        try { release.Set(); await blocker; Assert.All(await Task.WhenAll(requests), n => Assert.Equal(1, n)); await close; }
        finally { release.Set(); }
        Assert.True(resource.Closed); Assert.False(resource.DisposedOnPool); Assert.Equal(1, resource.Closes);
        Assert.Throws<ObjectDisposedException>(() => { _ = owned.RunAsync((_, _) => 1, Cancellation); });
    }

    [Fact]
    public async Task PublicWriterReturnsTheCanonicalDatasetAndRejectsAnOversizedBatchBeforePublication()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var io = new DeviceIoScheduler();
        var header = Header(); await using var writer = await AnalysisObservationWriter.CreateAsync(io, Device, () => directory, header.Identity, header.Roots, header.Policy, Cancellation);
        var root = await writer.AppendBatchAsync([new(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis")], Cancellation); Assert.Equal(1UL, root[0]);
        var files = await writer.AppendBatchAsync([new(Entry("one"), 1, "r", "basis"), new(Entry("two"), 1, "r", "basis")], Cancellation); Assert.Equal(new ulong[] { 2, 3 }, files);
        await Assert.ThrowsAsync<ArgumentException>(() => writer.AppendBatchAsync([new(Entry(new string('x', AnalysisLimits.BatchBytes)), 1, "r", "basis")], Cancellation));
        await using var dataset = await writer.CompleteAsync(header.Identity with { Revision = 2, ObservedUtc = Start.AddSeconds(5) }, AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy, Cancellation);
        await using var reader = await dataset.AcquireAsync(Cancellation); Assert.Equal(3UL, reader.Descriptor.Paths); Assert.Equal(1UL, reader.Descriptor.Objects);
        Assert.Equal(2UL, (await reader.ReadAliasesAsync(AnalysisObservationCapture.Id('o', 2), 0, cancellation: Cancellation)).Total);
    }

    [Fact]
    public async Task PublicWriterNeverPublishesTheFirstHalfOfARejectedBatch()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var header = Header();
        await using var writer = await AnalysisObservationWriter.CreateAsync(io, Device, () => new MemoryDirectory(), header.Identity, header.Roots, header.Policy, Cancellation);
        await writer.AppendBatchAsync([new(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis")], Cancellation);
        await Assert.ThrowsAsync<InvalidDataException>(() => writer.AppendBatchAsync([new(Entry("not-published"), 1, "r", "basis"), new(Entry("bad-root"), 1, "missing", "basis")], Cancellation));
        await using var dataset = await writer.CompleteAsync(header.Identity with { Revision = 2, ObservedUtc = Start.AddSeconds(5) }, AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy, Cancellation);
        Assert.Equal(1UL, dataset.Descriptor.Paths); Assert.Equal(0UL, dataset.Descriptor.Objects); Assert.Equal(NamespaceCoverage.Partial, dataset.Descriptor.Coverage);
    }

    [Fact]
    public async Task SchedulerShutdownStillReleasesExistingOwnershipWithoutNewSourceWork()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var resource = new FakeOwner(); var owned = new AnalysisOwnedDatasetIo<FakeOwner>(resource, io, Device);
        io.Dispose(); await owned.DisposeAsync(); Assert.True(resource.Closed); Assert.False(resource.DisposedOnPool); Assert.Equal(1, resource.Closes);
    }

    private static AnalysisObservationCapture Capture(AnalysisScratchWorkspace workspace, out AnalysisIndexedGraphHeader header)
    {
        header = Header(); return new(workspace.CreateArena(), header);
    }
    private static AnalysisIndexedGraphHeader Header() => new(new(Guid.NewGuid(), 1, 1, "fixture", Start, Start) { TimeZoneId = "UTC" },
            [new("r", "file", "/source", "/source", null, IdentityAssurance.Unknown, "source") { NameRules = AnalysisNameRules.Unix }],
            new(), AnalysisPhase.Scanning, NamespaceCoverage.Partial);
    private static AnalysisEntry Entry(string name, AnalysisKind kind = AnalysisKind.File, string? identity = "object") =>
        new([], name, [], kind, identity, AnalysisMeasurement.Known(0), AnalysisMeasurement.Known(4096), AnalysisMeasurement.Known(2), null, "v", ObservedUtc: Start);
    private static Task<AnalysisIndexedGraphRevision> Complete(AnalysisObservationCapture capture, AnalysisScratchWorkspace workspace, DeviceIoScheduler io, AnalysisIndexedGraphHeader header) =>
        capture.CompleteAsync(workspace, io, Device, header.Identity with { Revision = 2, ObservedUtc = Start.AddSeconds(5) }, AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy, Cancellation);
    private sealed class FakeOwner : IAnalysisScratchOwned
    {
        internal bool Closed, DisposedOnPool; internal int Closes;
        public void Dispose() { Closed = true; DisposedOnPool = Thread.CurrentThread.IsThreadPoolThread; Closes++; }
    }
    private sealed class MemoryDirectory : AnalysisPrivateDirectory
    {
        internal bool Closed; internal long Reads;
        internal override void Verify() => ObjectDisposedException.ThrowIf(Closed, this);
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1024 * 1024 * 1024, (UInt128)1024 * 1024 * 1024);
        internal override AnalysisPrivateScratchFile CreateScratch() => new MemoryFile(this);
        internal override AnalysisPrivateFile CreateStaging() => throw new NotSupportedException();
        internal override AnalysisPrivateFile OpenRead(string name) => throw new NotSupportedException();
        public override void Dispose() { Closed = true; }
    }
    private sealed class MemoryFile(MemoryDirectory directory) : AnalysisPrivateScratchFile
    {
        private readonly MemoryStream _bytes = new();
        public override long Length => _bytes.Length;
        public override void Verify() { }
        public override int ReadAt(Span<byte> buffer, long offset) { Interlocked.Increment(ref directory.Reads); _bytes.Position = offset; return _bytes.Read(buffer); }
        public override void WriteAt(ReadOnlySpan<byte> buffer, long offset) { _bytes.Position = offset; _bytes.Write(buffer); }
        public override void Dispose() => _bytes.Dispose();
    }
}
