using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

[Collection("Analysis bounded work capacity")]
public sealed partial class AnalysisDatasetSummaryTests
{
    private const string Device = "summary-fixture";
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task StreamedTotalsMatchTheGoldenAccountingIncludingUnverifiedAliasesAndDirectorySelf(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with
        {
            Objects = [snapshot.Objects[0] with { VerifiedIdentity = null }, snapshot.Objects[1]],
            Facts = snapshot.Facts.Add(new("directory", null, 2, "directory-self", "fixture-v1", AnalysisMeasurement.Known(32), "fixture", DateTimeOffset.UnixEpoch, []))
        };
        await using var dataset = await Build(snapshot, io); await using var reader = await dataset.AcquireAsync(Cancellation);
        var actual = await reader.SummarizeAsync(metric, Cancellation); var expected = AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation).Totals;
        Equal(expected, actual.Totals); Assert.Equal(dataset.Descriptor.Identity, actual.Identity); Assert.Equal(metric, actual.Metric);
        Assert.True(actual.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
    }

    [Theory]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    public async Task DistinctObjectIdsWithOneVerifiedIdentityAreChargedOnce(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with
        {
            Nodes = snapshot.Nodes.SetItem(4, snapshot.Nodes[4] with { ObjectId = "X2" }),
            Objects = snapshot.Objects.Add(snapshot.Objects[0] with { Id = "X2" }),
            Facts = snapshot.Facts.Add(snapshot.Facts[0] with { Id = "duplicate", ObjectId = "X2" })
        };
        await using var dataset = await Build(snapshot, io); await using var reader = await dataset.AcquireAsync(Cancellation);
        Equal(AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation).Totals, (await reader.SummarizeAsync(metric, Cancellation)).Totals);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    public async Task MissingMeasurementsRemainIndependentOfKnownZeroAndFileCounts(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler();
        foreach (var state in Enum.GetValues<MeasurementState>().Where(s => s != MeasurementState.Known))
        {
            var snapshot = AnalysisAccountingTests.Golden();
            snapshot = snapshot with
            {
                Objects = [snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Missing(state) }, snapshot.Objects[1] with { LogicalLength = AnalysisMeasurement.Known(0) }],
                Facts = [snapshot.Facts[0] with { Measurement = AnalysisMeasurement.Missing(state) }, snapshot.Facts[1] with { Measurement = AnalysisMeasurement.Known(0) }]
            };
            await using var dataset = await Build(snapshot, io); await using var reader = await dataset.AcquireAsync(Cancellation);
            var actual = await reader.SummarizeAsync(metric, Cancellation);
            Equal(AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation).Totals, actual.Totals);
            Assert.Equal(3UL, actual.Totals.MatchingPaths); Assert.Equal((UInt128)0, actual.Totals.Known);
        }
    }

    [Fact]
    public async Task DifferentRootMeasurementBasesSuppressAnAdditiveOnDiskTotal()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with
        {
            Roots = snapshot.Roots.Add(snapshot.Roots[0] with { Id = "s", CanonicalLocator = "Other", DisplayLocator = "Other", Label = "Other" }),
            Nodes = snapshot.Nodes.Add(new(7, "s", 0, "Other", AnalysisKind.Directory)).Add(new(8, "s", 7, "q", AnalysisKind.File, "Q")),
            Objects = snapshot.Objects.Add(new("Q", "other:Q", AnalysisMeasurement.Known(1), AnalysisMeasurement.Known(1))),
            Facts = snapshot.Facts.Add(new("other-fact", "Q", null, "primary", "different-basis", AnalysisMeasurement.Known(512), "fixture", DateTimeOffset.UnixEpoch, []))
        };
        await using var dataset = await Build(snapshot, io); await using var reader = await dataset.AcquireAsync(Cancellation);
        var actual = (await reader.SummarizeAsync(AnalysisMetric.OnDisk, Cancellation)).Totals;
        Equal(AnalysisAggregate.Project(snapshot, AnalysisMetric.OnDisk, cancellation: Cancellation).Totals, actual);
        Assert.False(actual.CompatibleBasis); Assert.Equal((UInt128)0, actual.Known);
    }

    [Fact]
    public async Task ALeadingUnknownVersionCannotHideTwoConflictingKnownVersions()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden(); var obj = snapshot.Objects[0]; var fact = snapshot.Facts[0];
        snapshot = snapshot with
        {
            Nodes = snapshot.Nodes.SetItem(4, snapshot.Nodes[4] with { ObjectId = "X2" }).SetItem(5, snapshot.Nodes[5] with { ObjectId = "X3" }),
            Objects = [obj, obj with { Id = "X2", VersionEvidence = "v1" }, obj with { Id = "X3", VersionEvidence = "v2" }],
            Facts = [fact, fact with { Id = "f2", ObjectId = "X2" }, fact with { Id = "f3", ObjectId = "X3" }]
        };
        await using var dataset = await Build(snapshot, io); await using var reader = await dataset.AcquireAsync(Cancellation);
        foreach (var metric in new[] { AnalysisMetric.UniqueFileSize, AnalysisMetric.OnDisk })
        {
            var actual = (await reader.SummarizeAsync(metric, Cancellation)).Totals;
            Equal(AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation).Totals, actual);
            Assert.Equal(1UL, actual.Missing[MeasurementState.Changed]);
        }
    }

    [Fact]
    public async Task OverflowFailsWithoutDamagingTheImmutableDataset()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = [snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(UInt128.MaxValue) }, snapshot.Objects[1]] };
        await using var dataset = await Build(snapshot, io); await using var reader = await dataset.AcquireAsync(Cancellation);
        await Assert.ThrowsAsync<OverflowException>(() => reader.SummarizeAsync(AnalysisMetric.FileSize, Cancellation));
        Assert.Equal((UInt128)3, (await reader.SummarizeAsync(AnalysisMetric.Files, Cancellation)).Totals.Known);
    }

    [Fact]
    public async Task ClosingAReaderWaitsForItsActualBlockedSummaryRead()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory); var reader = await dataset.AcquireAsync(Cancellation);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        directory.BeforeNextRead = () => { entered.Set(); release.Wait(Cancellation); };
        var summary = reader.SummarizeAsync(AnalysisMetric.OnDisk, Cancellation); Task? closing = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation)); closing = reader.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
        }
        finally { release.Set(); }
        Assert.Equal((UInt128)80, (await summary).Totals.Known); await closing!;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => reader.SummarizeAsync(AnalysisMetric.Files, Cancellation));
        await dataset.DisposeAsync(); Assert.True(directory.Closed);
    }

    private static void Equal(AnalysisTotals expected, AnalysisTotals actual)
    {
        Assert.Equal(expected.Known, actual.Known); Assert.Equal(expected.IdentityUnverified, actual.IdentityUnverified);
        Assert.Equal(expected.MatchingPaths, actual.MatchingPaths); Assert.Equal(expected.CompatibleBasis, actual.CompatibleBasis);
        Assert.Equal(expected.Missing.OrderBy(p => p.Key), actual.Missing.OrderBy(p => p.Key));
    }
    [Fact]
    public async Task ThousandsOfAliasesYieldToInteractiveWorkWithoutChargingEachVerifiedAlias()
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1); var directory = new MemoryDirectory();
        var snapshot = AnalysisAccountingTests.Golden(); var paths = snapshot.Nodes.Take(3).ToImmutableArray().ToBuilder();
        for (ulong id = 4; id < 3004; id++) paths.Add(new(id, "r", id % 2 == 0 ? 2UL : 3UL, "alias-" + id, AnalysisKind.File, "X"));
        snapshot = snapshot with { Nodes = paths.ToImmutable(), Objects = [snapshot.Objects[0]], Facts = [snapshot.Facts[0]] };
        await using var dataset = await Build(snapshot, io, directory); await using var reader = await dataset.AcquireAsync(Cancellation);
        bool completed = false; Task<bool>? interactive = null;
        directory.BeforeNextRead = () => interactive = io.Run(Device, IoPriority.Interactive, _ => completed);
        var actual = (await reader.SummarizeAsync(AnalysisMetric.OnDisk, Cancellation)).Totals; completed = true;
        Assert.NotNull(interactive); Assert.False(await interactive); Assert.Equal(3000UL, actual.MatchingPaths); Assert.Equal((UInt128)80, actual.Known);
        Assert.Equal((UInt128)100, (await reader.SummarizeAsync(AnalysisMetric.UniqueFileSize, Cancellation)).Totals.Known);
    }

    [Fact]
    public async Task GloballyQueuedSummaryCancellationDoesNotReadItsDataset()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var first = new MemoryDirectory(); var second = new MemoryDirectory(); var third = new MemoryDirectory();
        string a = Device + "-a", b = Device + "-b", c = Device + "-c";
        await using var ownerA = await Build(AnalysisAccountingTests.Golden(), io, first, a);
        await using var ownerB = await Build(AnalysisAccountingTests.Golden(), io, second, b);
        await using var ownerC = await Build(AnalysisAccountingTests.Golden(), io, third, c);
        await using var readerA = await ownerA.AcquireAsync(Cancellation); await using var readerB = await ownerB.AcquireAsync(Cancellation); await using var readerC = await ownerC.AcquireAsync(Cancellation);
        using var enteredA = new ManualResetEventSlim(); using var enteredB = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        first.BeforeNextRead = () => { enteredA.Set(); release.Wait(Cancellation); }; second.BeforeNextRead = () => { enteredB.Set(); release.Wait(Cancellation); };
        var runningA = readerA.SummarizeAsync(AnalysisMetric.OnDisk, Cancellation); var runningB = readerB.SummarizeAsync(AnalysisMetric.OnDisk, Cancellation);
        bool read = false; third.BeforeNextRead = () => read = true;
        try
        {
            Assert.True(enteredA.Wait(TimeSpan.FromSeconds(4), Cancellation)); Assert.True(enteredB.Wait(TimeSpan.FromSeconds(4), Cancellation));
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            var queued = readerC.SummarizeAsync(AnalysisMetric.OnDisk, stop.Token); Assert.False(queued.IsCompleted); Assert.False(read);
            stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued); Assert.False(read);
        }
        finally { release.Set(); }
        await runningA; await runningB;
        Assert.Equal((UInt128)80, (await readerC.SummarizeAsync(AnalysisMetric.OnDisk, Cancellation)).Totals.Known); Assert.True(read);
    }

    private static async Task<AnalysisDataset> Build(AnalysisSnapshot snapshot, DeviceIoScheduler io, MemoryDirectory? directory = null, AnalysisIoDevices? device = null, AnalysisEnumerationProgress? enumeration = null,
        long sessionLimit = AnalysisLimits.ScratchBytes, Action<AnalysisScratchWorkspace>? afterSeal = null, AnalysisProfileQuota? quota = null)
    {
        device ??= Device;
        directory ??= new(); using var workspace = new AnalysisScratchWorkspace(directory, sessionLimit, AnalysisScratchBudget.Shared, quota);
        var header = new AnalysisIndexedGraphHeader(snapshot.Identity, snapshot.Roots, snapshot.Policy, snapshot.Phase, snapshot.Coverage, Origin: AnalysisOrigin.ImportedInventory) { Enumeration = enumeration };
        using var graph = new AnalysisIndexedGraph(workspace.CreateArena(), header);
        foreach (var node in snapshot.Nodes) graph.AppendPath(node, Cancellation);
        foreach (var obj in snapshot.Objects) graph.AppendObject(obj with { ObservedAliases = [] }, Cancellation);
        foreach (var fact in snapshot.Facts) graph.AppendFact(fact, Cancellation);
        foreach (var issue in snapshot.Issues) graph.AppendIssue(issue, Cancellation);
        var revision = await graph.SealAsync(workspace, io, device, snapshot.Identity, snapshot.Phase, snapshot.Coverage, Cancellation);
        try { afterSeal?.Invoke(workspace); return new(revision, io, device); }
        catch { await AnalysisIo.ReleaseScratchAsync(io, device, revision); throw; }
    }
    private sealed class MemoryDirectory : AnalysisPrivateDirectory
    {
        internal bool Closed; internal Action? BeforeNextRead; internal long WrittenBytes;
        internal override void Verify() => ObjectDisposedException.ThrowIf(Closed, this);
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1 << 34, (UInt128)1 << 34);
        internal override AnalysisPrivateScratchFile CreateScratch() => new MemoryFile(this);
        internal override AnalysisPrivateFile CreateStaging() => throw new NotSupportedException();
        internal override AnalysisPrivateFile OpenRead(string name) => throw new NotSupportedException();
        public override void Dispose() => Closed = true;
    }
    private sealed class MemoryFile(MemoryDirectory directory) : AnalysisPrivateScratchFile
    {
        private readonly MemoryStream _bytes = new();
        public override long Length => _bytes.Length;
        public override void Verify() { }
        public override int ReadAt(Span<byte> buffer, long offset) { var before = directory.BeforeNextRead; directory.BeforeNextRead = null; before?.Invoke(); _bytes.Position = offset; return _bytes.Read(buffer); }
        public override void WriteAt(ReadOnlySpan<byte> buffer, long offset) { directory.WrittenBytes += buffer.Length; _bytes.Position = offset; _bytes.Write(buffer); }
        public override void Dispose() => _bytes.Dispose();
    }
}
