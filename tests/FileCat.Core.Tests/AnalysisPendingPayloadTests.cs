using System.Collections.Immutable;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

[Collection("Analysis bounded work capacity")]
public sealed class AnalysisPendingPayloadTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    [Fact]
    public void AllProducerFamiliesShareEightMiBAndReleaseExactlyTheirOwnCharges()
    {
        Assert.Equal(0, AnalysisPendingPayload.ReservedBytes);
        using var first = AnalysisPendingPayload.Working.Acquire(AnalysisPendingPayload.WorkingPerScan);
        using var second = AnalysisPendingPayload.Working.Acquire(AnalysisPendingPayload.WorkingPerScan);
        using var frontier = AnalysisPendingPayload.Frontier.Acquire(AnalysisPendingPayload.Frontier.Limit);
        using var writes = AnalysisPendingPayload.Writes.Acquire(AnalysisPendingPayload.Writes.Limit);
        Assert.Equal(AnalysisLimits.PendingBytes, AnalysisPendingPayload.ReservedBytes);
        Assert.False(AnalysisPendingPayload.Working.TryAcquire(1, out _)); Assert.False(AnalysisPendingPayload.Frontier.TryAcquire(1, out _));
        Assert.False(AnalysisPendingPayload.Writes.TryAcquire(1, out _));
        frontier.Dispose(); frontier.Dispose(); Assert.Equal(AnalysisLimits.PendingBytes - AnalysisPendingPayload.Frontier.Limit, AnalysisPendingPayload.ReservedBytes);
        using var reused = AnalysisPendingPayload.Frontier.Acquire(AnalysisPendingPayload.Frontier.Limit);
        Assert.Equal(AnalysisLimits.PendingBytes, AnalysisPendingPayload.ReservedBytes);
    }
    [Fact]
    public async Task SaturatedTraversalAndWritesCannotStarveProducerWorkAndRejectedDirectoriesCountAsFailed()
    {
        Ordinary(); using var otherProducer = AnalysisPendingPayload.Working.Acquire(AnalysisPendingPayload.WorkingPerScan);
        using var frontier = AnalysisPendingPayload.Frontier.Acquire(AnalysisPendingPayload.Frontier.Limit);
        using var writes = AnalysisPendingPayload.Writes.Acquire(AnalysisPendingPayload.Writes.Limit);
        using var io = new DeviceIoScheduler(); var backend = new Source("coverage", [DirectoryEntry("one"), DirectoryEntry("two"), DirectoryEntry("three")]);
        using var session = new AnalysisCoordinator(io).Start([backend]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), Cancellation);
        var snapshot = session.Snapshot(); Assert.Equal(4, snapshot.Nodes.Length); Assert.Equal(NamespaceCoverage.Partial, snapshot.Coverage);
        Assert.Equal((ulong)AnalysisPendingPayload.WorkingPerScan, snapshot.Policy.Limits["producerWorkingBytesPerScan"]);
        Assert.Equal((ulong)AnalysisPendingPayload.Frontier.Limit, snapshot.Policy.Limits["pendingTraversalBytes"]);
        Assert.Equal((ulong)AnalysisPendingPayload.Writes.Limit, snapshot.Policy.Limits["pendingCaptureBytes"]);
        Assert.Equal((ulong)AnalysisPendingPayload.WitnessBytes, snapshot.Policy.Limits["sourceWitnessBytes"]);
        Assert.Equal(4UL, snapshot.CoverageDetails!.EnumerationRequested); Assert.Equal(1UL, snapshot.CoverageDetails.EnumerationCompleted);
        Assert.Equal(3UL, snapshot.CoverageDetails.EnumerationFailed); Assert.Equal(3, snapshot.Issues.Count(i => i.Code == "pending-payload-limit"));
        Assert.Equal(1, backend.Enumerations); Assert.Equal(1, backend.Closes);
        Assert.Equal(AnalysisPendingPayload.WorkingPerScan, AnalysisPendingPayload.Working.ReservedBytes);
    }
    [Fact]
    public async Task BothActualProducersKeepWorkingSpaceThroughStopAndAThirdWaitsWithoutCharging()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var entered = new CountdownEvent(2); using var release = new ManualResetEventSlim();
        var a = new Source("a", [], () => { entered.Signal(); release.Wait(Cancellation); });
        var b = new Source("b", [], () => { entered.Signal(); release.Wait(Cancellation); }); var c = new Source("c", []);
        using var first = new AnalysisCoordinator(io).Start([a]); using var second = new AnalysisCoordinator(io).Start([b]);
        AnalysisSession? third = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation)); third = new AnalysisCoordinator(io).Start([c]);
            Assert.Equal(AnalysisPendingPayload.Working.Limit, AnalysisPendingPayload.Working.ReservedBytes);
            first.Stop(); second.Stop(); Assert.False(first.Completion.IsCompleted); Assert.False(second.Completion.IsCompleted);
            Assert.Equal(AnalysisPendingPayload.Working.Limit, AnalysisPendingPayload.Working.ReservedBytes); Assert.Equal(0, c.Enumerations);
        }
        finally { release.Set(); }
        try
        {
            await Task.WhenAll(first.Completion, second.Completion, third!.Completion).WaitAsync(TimeSpan.FromSeconds(15), Cancellation);
            Assert.Equal(0, AnalysisPendingPayload.ReservedBytes); Assert.Equal(1, c.Enumerations); Assert.Equal(1, a.Closes); Assert.Equal(1, b.Closes);
        }
        finally { third?.Dispose(); }
    }
    [Theory]
    [InlineData("name")]
    [InlineData("identity")]
    [InlineData("version")]
    [InlineData("encoding")]
    [InlineData("issue")]
    public async Task OversizedDisplayOrMetadataStringsCannotBypassTheProducerBatchBound(string field)
    {
        Ordinary(); string large = new('x', AnalysisLimits.BatchBytes); var entry = FileEntry("small");
        entry = field switch { "name" => entry with { Name = large }, "identity" => entry with { Identity = large },
            "version" => entry with { Version = large }, "encoding" => entry with { NameEncoding = large }, _ => entry with { IssueCode = large } };
        using var io = new DeviceIoScheduler(); using var session = new AnalysisCoordinator(io).Start([new Source(field, [entry])]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), Cancellation); var snapshot = session.Snapshot();
        Assert.Single(snapshot.Nodes); Assert.Equal(NamespaceCoverage.Partial, snapshot.Coverage);
        Assert.Contains(snapshot.Issues, i => i.Code == "producer-record-limit"); Assert.Equal(0, AnalysisPendingPayload.ReservedBytes);
    }
    [Fact]
    public async Task AnOversizedRuntimeReadWitnessStopsBeforeStartingAnotherEnumeration()
    {
        Ordinary(); var root = DirectoryEntry("root") with { RelativeName = [], Ancestry = new(Guid.NewGuid(),
            ImmutableArray.Create(new byte[checked((int)AnalysisPendingPayload.WitnessBytes)]), "identity", "v", null) };
        var source = new Source("witness", [], root: root); using var io = new DeviceIoScheduler(); using var session = new AnalysisCoordinator(io).Start([source]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), Cancellation); var snapshot = session.Snapshot();
        Assert.Equal(0, source.Enumerations); Assert.Equal(1UL, snapshot.CoverageDetails!.EnumerationFailed);
        Assert.Contains(snapshot.Issues, i => i.Code == "frontier-read-witness-limit"); Assert.Equal(0, AnalysisPendingPayload.ReservedBytes);
    }
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private static AnalysisEntry DirectoryEntry(string name) => FileEntry(name) with { Kind = AnalysisKind.Directory, Identity = null };
    private static AnalysisEntry FileEntry(string name) => new(Encoding.UTF8.GetBytes(name).ToImmutableArray(), name,
        Encoding.UTF8.GetBytes(name).ToImmutableArray(), AnalysisKind.File, null, AnalysisMeasurement.Known(1), AnalysisMeasurement.Known(1),
        AnalysisMeasurement.Known(1), null, "v");
    private sealed class Source(string device, ImmutableArray<AnalysisEntry> children, Action? before = null, AnalysisEntry? root = null) : IAnalysisBackend
    {
        internal int Enumerations, Closes;
        public RootBinding Root { get; } = new("r", "file", "/fixture-" + device, "/fixture-" + device, device, IdentityAssurance.Unknown, "root");
        public AnalysisEntry RootEntry { get; } = root ?? DirectoryEntry("root") with { RelativeName = [] };
        public string Basis => "fixture";
        public string IoDeviceKey => "pending-payload-" + device;
        public void ValidateReadScope(AnalysisEntry directory) => Assert.False(Thread.CurrentThread.IsThreadPoolThread);
        public IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation)
        {
            Interlocked.Increment(ref Enumerations); before?.Invoke();
            if (!directory.RelativeName.IsEmpty) yield break;
            foreach (var child in children) { cancellation.ThrowIfCancellationRequested(); yield return child; }
        }
        public void Dispose() => Interlocked.Increment(ref Closes);
    }
}
