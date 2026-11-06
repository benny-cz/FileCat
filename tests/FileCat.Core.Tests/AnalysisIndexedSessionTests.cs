using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class AnalysisIndexedSessionTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private static AnalysisStorageOptions Storage(MemoryDirectory directory, Backend protection, int preview = 4) => new("session-scratch", () => directory,
        [new("protected-source", () => protection)], preview);

    [Fact]
    public async Task RealCoordinatorContinuesPastItsPreviewAndPreservesCanonicalVisibleIdentities()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var io = new DeviceIoScheduler(); var source = new Backend(250); var protection = new Backend(0, "private");
        var session = new AnalysisCoordinator(io).Start([source], storage: Storage(directory, protection));
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(20), Cancellation);
        var preview = session.Snapshot(); Assert.Equal(4, preview.Nodes.Length); Assert.Equal(NamespaceCoverage.Partial, preview.Coverage); AnalysisValidation.Validate(preview, Cancellation);
        Assert.True(source.Closed); Assert.True(protection.Closed); Assert.Equal(251UL, session.DatasetDescriptor!.Paths);
        var coverage = session.DatasetDescriptor.CoverageDetails; Assert.True(coverage.EnumerationKnown); Assert.True(coverage.Observations.DenominatorFinal);
        Assert.Equal(1UL, coverage.Observations.EnumerationRequested); Assert.Equal(1UL, coverage.Observations.EnumerationCompleted); Assert.Equal(0UL, coverage.UnscheduledDirectories);
        Assert.Equal(250UL, coverage.Observations.IdentityVerifiedPaths); Assert.Equal(250UL, coverage.Observations.MeasurementStates[MeasurementState.Known]);
        await using var dataset = await session.AcquireDatasetAsync(Cancellation); Assert.NotNull(dataset);
        var all = await dataset.ReadPathsAsync(0, AnalysisLimits.BatchRecords, Cancellation); Assert.Equal(251, all.Rows.Length); Assert.False(all.HasMore);
        foreach (var path in preview.Nodes) Assert.Equal(path.ObjectId, all.Rows.Single(n => n.Id == path.Id).ObjectId);
        session.Dispose(); await session.DatasetCloseCompletion; Assert.False(directory.Closed);
        Assert.Equal(251UL, (await dataset.ReadPathsAsync(0, cancellation: Cancellation)).Total);
        await dataset.DisposeAsync(); Assert.True(directory.Closed);
    }

    [Fact]
    public async Task PreviewPollingDuringIndexingCannotOvertakeTheDatasetRevision()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var io = new DeviceIoScheduler(); var source = new Backend(5);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        source.BeforeClose = () => directory.BeforeNextWrite = () => { entered.Set(); release.Wait(Cancellation); };
        using var session = new AnalysisCoordinator(io).Start([source], storage: Storage(directory, new Backend(0, "private")));
        AnalysisSnapshot? preview = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation)); preview = session.Snapshot();
            Assert.Equal(AnalysisPhase.Finished, preview.Phase);
            for (int n = 0; n < 20; n++) Assert.Equal(preview.Identity, session.Snapshot().Identity);
        }
        finally { release.Set(); }
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(20), Cancellation);
        Assert.NotNull(preview); Assert.True(session.DatasetDescriptor!.Identity.Revision > preview.Identity.Revision);
        Assert.Equal(preview.Identity, session.Snapshot().Identity);
    }

    [Fact]
    public async Task PrivateDirectoryIdentityIsRecordedAsExcludedAndNeverEnumerated()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var io = new DeviceIoScheduler(); var source = new Backend(5) { IncludePrivate = true }; var protection = new Backend(0, "private");
        using var session = new AnalysisCoordinator(io).Start([source], storage: Storage(directory, protection, 20));
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(20), Cancellation);
        await using var dataset = await session.AcquireDatasetAsync(Cancellation); Assert.NotNull(dataset);
        var paths = await dataset.ReadPathsAsync(0, cancellation: Cancellation);
        var hidden = Assert.Single(paths.Rows, n => n.Name == "private"); Assert.False(hidden.IncludedByPolicy); Assert.Equal(MeasurementState.Excluded, hidden.MetadataState);
        Assert.DoesNotContain(paths.Rows, n => n.Name == "must-not-enumerate"); Assert.False(source.PrivateEnumerated);
        Assert.Contains("filecat-private-analysis-state", dataset.Descriptor.Policy.Exclusions);
        Assert.Equal(1UL, dataset.Descriptor.IssueTotals[MeasurementState.Excluded]);
    }

    [Fact]
    public async Task UnverifiedProtectedIdentityRefusesTraversalAndKeepsAnExplicitPartialResult()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = new Backend(4); var protection = new Backend(0, "private") { Verified = false };
        using var session = new AnalysisCoordinator(io).Start([source], storage: Storage(new MemoryDirectory(), protection));
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(20), Cancellation); Assert.False(source.Enumerated); Assert.True(source.Closed); Assert.True(protection.Closed);
        Assert.Equal(NamespaceCoverage.Partial, session.Snapshot().Coverage);
    }

    [Fact]
    public async Task StopDuringBlockedPrivateFlushReturnsImmediatelyAndRejectsItsLaterPublication()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var io = new DeviceIoScheduler(); var header = new AnalysisIndexedGraphHeader(
            new(Guid.NewGuid(), 1, 1, "fixture", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            [new("r", "file", "/source", "/source", null, IdentityAssurance.Unknown, "source")], new(), AnalysisPhase.Scanning, NamespaceCoverage.Partial);
        using var workspace = new AnalysisScratchWorkspace(directory); using var capture = new AnalysisObservationCapture(workspace.CreateArena(), header);
        capture.Append(Backend.Entry("root", AnalysisKind.Directory), 0, "r", "basis", cancellation: Cancellation);
        capture.Enumeration(new(1, 0, 0), Cancellation); capture.Publish(Cancellation);
        capture.Append(Backend.Entry("late"), 1, "r", "basis", cancellation: Cancellation);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); directory.BeforeNextWrite = () => { entered.Set(); release.Wait(Cancellation); };
        var flush = Task.Run(() => capture.Publish(Cancellation), Cancellation);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3), Cancellation));
            await Task.Run(() => capture.RequestStop("device-not-responding"), Cancellation).WaitAsync(TimeSpan.FromSeconds(2), Cancellation);
        }
        finally { release.Set(); }
        await flush;
        Assert.Throws<OperationCanceledException>(() => capture.Enumeration(new(1, 1, 0), Cancellation));
        using var revision = await capture.CompleteAsync(workspace, io, "stop-prefix", header.Identity with { Revision = 2, ObservedUtc = DateTimeOffset.UtcNow }, AnalysisPhase.Stopped, NamespaceCoverage.Partial, Cancellation, AnalysisFreshness.SourceUnavailable);
        using var reader = revision.Acquire(); Assert.Equal(1UL, reader.Count(AnalysisIndexedGraph.Paths));
        Assert.Equal(AnalysisPhase.Stopped, reader.Header.Phase); Assert.Equal(0UL, reader.Count(AnalysisIndexedGraph.Objects));
        Assert.Equal(1UL, reader.Header.IssueTotals[MeasurementState.Failed]); Assert.Equal(0UL, reader.Count(AnalysisIndexedGraph.Issues));
        Assert.Equal(1UL, reader.Header.CoverageDetails!.Observations.EnumerationPending); Assert.True(reader.Header.CoverageDetails.HasUnlocatedStaleScopes);
    }

    private sealed class Backend(int count, string identity = "source") : IAnalysisBackend
    {
        internal bool Closed, Enumerated, IncludePrivate, PrivateEnumerated; internal bool Verified = true;
        internal Action? BeforeClose;
        public RootBinding Root => new("r", "file", "/" + identity, "/" + identity, "fixture", Verified ? IdentityAssurance.Verified : IdentityAssurance.Unknown, identity) { NameRules = AnalysisNameRules.Unix };
        public AnalysisEntry RootEntry => Entry(identity, AnalysisKind.Directory);
        public string Basis => "basis";
        public string IoDeviceKey => "source-" + identity;
        public IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation)
        {
            Enumerated = true;
            if (directory.Identity == "private") { PrivateEnumerated = true; yield return Entry("must-not-enumerate"); yield break; }
            if (IncludePrivate) yield return Entry("private", AnalysisKind.Directory);
            for (int n = 0; n < count; n++) { cancellation.ThrowIfCancellationRequested(); yield return Entry("file-" + n); }
        }
        internal static AnalysisEntry Entry(string name, AnalysisKind kind = AnalysisKind.File) => new([], name, [], kind, name,
            AnalysisMeasurement.Known(1), AnalysisMeasurement.Known(4096), AnalysisMeasurement.Known(1), DateTimeOffset.UnixEpoch, "v", ObservedUtc: DateTimeOffset.UtcNow);
        public void ValidateReadScope(AnalysisEntry directory) { ObjectDisposedException.ThrowIf(Closed, this); }
        public AnalysisRootContainment ContainsRoot(IAnalysisBackend other) => AnalysisRootContainment.Disjoint;
        public void Dispose() { Closed = true; BeforeClose?.Invoke(); }
    }
    private sealed class MemoryDirectory : AnalysisPrivateDirectory
    {
        internal bool Closed; internal Action? BeforeNextWrite;
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
        public override int ReadAt(Span<byte> buffer, long offset) { _bytes.Position = offset; return _bytes.Read(buffer); }
        public override void WriteAt(ReadOnlySpan<byte> buffer, long offset) { var before = directory.BeforeNextWrite; directory.BeforeNextWrite = null; before?.Invoke(); _bytes.Position = offset; _bytes.Write(buffer); }
        public override void Dispose() => _bytes.Dispose();
    }
}
