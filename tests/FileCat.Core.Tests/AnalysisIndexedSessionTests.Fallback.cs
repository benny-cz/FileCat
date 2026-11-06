using System.Collections.Concurrent;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisIndexedSessionTests
{
    [Fact]
    public async Task NativeUnixSourcesExcludePrivateRootsWhenSecureScratchIsUnavailable()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows native backend runs in its platform test assembly.");
        await AnalysisNativeMemoryFallbackCases.Run(path => new UnixAnalysisBackend(path));
    }

    [Fact]
    public async Task UnavailableInitialScratchUsesFullBoundedMemoryAndRetainsPrivateExclusion()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = new Backend(20) { IncludePrivate = true };
        var protection = new Backend(0, "private"); int opens = 0; var progress = new CapturedProgress();
        var storage = new AnalysisStorageOptions("scratch", () => { opens++; throw new IOException("Controlled scratch refusal."); },
            [new("private", () => protection)], PreviewPaths: 4) { AllowMemoryFallback = true };
        using var session = new AnalysisCoordinator(io).Start([source], progress, storage: storage);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(20), Cancellation);
        Assert.True(session.UsesMemoryFallback); Assert.Null(session.DatasetDescriptor); Assert.Equal(1, opens);
        var snapshot = session.Snapshot(); Assert.Equal(22, snapshot.Nodes.Length); AnalysisValidation.Validate(snapshot, Cancellation);
        Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, snapshot.Coverage); Assert.True(source.Closed); Assert.True(protection.Closed);
        Assert.False(source.PrivateEnumerated); var hidden = Assert.Single(snapshot.Nodes, n => n.Name == "private");
        Assert.False(hidden.IncludedByPolicy); Assert.Null(hidden.ObservedIdentity); Assert.Equal(MeasurementState.Excluded, hidden.MetadataState);
        Assert.DoesNotContain(snapshot.Nodes, n => n.Name == "must-not-enumerate");
        var final = progress.Items.Last(); Assert.True(final.MemoryFallback); Assert.False(final.PreviewLimited); Assert.Equal(22, final.ObservedPaths);
    }

    [Fact]
    public async Task InitialPayloadFailureClosesUnpublishedScratchBeforeMemoryTraversal()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory
        { BeforeNextWrite = () => throw new IOException("Controlled initial payload refusal.") };
        var source = new Backend(20) { IncludePrivate = true, BeforeEnumerate = () => Assert.True(directory.Closed) };
        using var session = new AnalysisCoordinator(io).Start([source], storage: Storage(directory, new Backend(0, "private")) with { AllowMemoryFallback = true });
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(20), Cancellation);
        Assert.True(session.UsesMemoryFallback); Assert.True(directory.Closed); Assert.Equal(1, directory.Writes);
        Assert.Equal(22, session.Snapshot().Nodes.Length); Assert.False(source.PrivateEnumerated);
        AnalysisValidation.Validate(session.Snapshot(), Cancellation);
    }

    [Fact]
    public async Task UnverifiedPrivateExclusionCannotBecomeMemoryFallback()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = new Backend(20); var protection = new Backend(0, "private") { Verified = false };
        int opens = 0;
        var storage = new AnalysisStorageOptions("scratch", () => { opens++; throw new IOException("Must not open scratch."); }, [new("private", () => protection)])
        { AllowMemoryFallback = true };
        using var session = new AnalysisCoordinator(io).Start([source], storage: storage);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(20), Cancellation);
        Assert.False(session.UsesMemoryFallback); Assert.False(source.Enumerated); Assert.Equal(0, opens);
        Assert.Empty(session.Snapshot().Nodes); Assert.Equal(NamespaceCoverage.Partial, session.Snapshot().Coverage);
        Assert.True(source.Closed); Assert.True(protection.Closed);
    }

    [Fact]
    public async Task LaterScratchFailureKeepsItsPrefixInsteadOfRestartingIntoMemory()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        var source = new Backend(20) { BeforeEnumerate = () => directory.BeforeNextWrite = () => throw new IOException("Controlled later write refusal.") };
        using var session = new AnalysisCoordinator(io).Start([source], storage: Storage(directory, new Backend(0, "private")) with { AllowMemoryFallback = true });
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(20), Cancellation);
        Assert.False(session.UsesMemoryFallback); Assert.True(source.Enumerated); Assert.True(source.Closed);
        Assert.Equal(NamespaceCoverage.Partial, session.Snapshot().Coverage);
        Assert.Contains(session.Snapshot().Nodes, n => n.Name == "source");
    }

    [Fact]
    public async Task StopDuringInitialScratchFailureCannotResumeTheCanceledScanInMemory()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var directory = new MemoryDirectory { BeforeNextWrite = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); throw new IOException("Controlled late return."); } };
        var source = new Backend(20);
        using var session = new AnalysisCoordinator(io).Start([source], storage: Storage(directory, new Backend(0, "private")) with { AllowMemoryFallback = true });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Cancellation)); session.Stop();
            Assert.Equal(AnalysisPhase.Stopped, session.Snapshot().Phase); Assert.False(session.Completion.IsCompleted);
        }
        finally { release.Set(); }
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(20), Cancellation);
        Assert.False(session.UsesMemoryFallback); Assert.False(source.Enumerated); Assert.True(source.Closed, "The actual source must be closed after session completion.");
        Assert.Equal(AnalysisPhase.Stopped, session.Snapshot().Phase);
        Console.WriteLine($"Stopped fallback fixture: source closed={source.Closed}, directory closed={directory.Closed}, retained dataset={session.DatasetDescriptor is not null}.");
        session.Dispose(); await session.DatasetCloseCompletion.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        Assert.True(directory.Closed, "The directory must close after the stopped dataset's actual owner closes.");
    }
    private sealed class CapturedProgress : IProgress<AnalysisProgress>
    {
        internal readonly ConcurrentQueue<AnalysisProgress> Items = new();
        public void Report(AnalysisProgress value) => Items.Enqueue(value);
    }
}
