using System.Collections.Concurrent;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisScanningTests
{
    [Fact]
    public async Task RescanEpochKeepsTheOldSnapshotAndAdvancesOneRuntimeLineageWithTaggedProgress()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var coordinator = new AnalysisCoordinator(io, "epoch-profile");
        var initial = new FakeBackend(3); using var first = coordinator.Start([initial]); await Done(first);
        using var retained = first.AcquireSnapshot(); var old = retained.Snapshot;
        var reports = new ConcurrentQueue<AnalysisProgress>();
        using var second = coordinator.Rescan(first, [new FakeBackend(7) { Root = initial.Root }], new CallbackProgress(reports.Enqueue));
        await Done(second); var next = second.Snapshot();
        Assert.Equal(old.Identity.SessionId, next.Identity.SessionId); Assert.Equal(old.Identity.Epoch + 1, next.Identity.Epoch);
        Assert.True(next.Identity.Revision > old.Identity.Revision); Assert.Equal(old.Identity.TimeZoneId, next.Identity.TimeZoneId);
        Assert.Equal(4, old.Nodes.Length); Assert.Equal(8, next.Nodes.Length); Assert.Equal(old, first.Snapshot());
        Assert.NotEmpty(reports); Assert.All(reports, report => { Assert.Equal(next.Identity.SessionId, report.SessionId); Assert.Equal(2, report.Epoch); });
        first.Dispose(); Assert.Equal(old, retained.Snapshot);
        using var third = coordinator.Rescan(second, [new FakeBackend(1) { Root = initial.Root }]); await Done(third);
        Assert.Equal(3, third.Snapshot().Identity.Epoch); Assert.True(third.Snapshot().Identity.Revision > next.Identity.Revision);
        AnalysisValidation.Validate(next, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RescanEpochWaitsForActualEnumerationAndActualSourceClose(bool duringClose)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initial = new FakeBackend(1); Action block = () => { entered.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); };
        if (duringClose) initial.BeforeClose = block; else initial.BeforeEnumerate = block;
        var coordinator = new AnalysisCoordinator(io, "epoch-profile"); using var first = coordinator.Start([initial]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var fresh = new FakeBackend(2) { Root = initial.Root };
        try
        {
            Assert.False(first.Completion.IsCompleted); Assert.Throws<InvalidOperationException>(() => coordinator.Rescan(first, [fresh]));
            Assert.False(fresh.Disposed);
        }
        finally { release.Set(); }
        await Done(first); using var second = coordinator.Rescan(first, [fresh]); await Done(second);
        Assert.True(initial.Disposed && fresh.Disposed);
    }

    [Theory]
    [InlineData("scheduler")]
    [InlineData("profile")]
    [InlineData("closed-session")]
    [InlineData("same-source")]
    [InlineData("successor")]
    [InlineData("failed-close")]
    public async Task RescanEpochRefusalsNeverTakeOwnershipOfFreshCallerSources(string reason)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var otherIo = new DeviceIoScheduler();
        var coordinator = new AnalysisCoordinator(io, "epoch-profile"); var initial = new FakeBackend(1);
        if (reason == "failed-close") initial.BeforeClose = () => throw new IOException("Injected close refusal.");
        using var first = coordinator.Start([initial]); await Done(first); using var fresh = new FakeBackend(2) { Root = initial.Root };
        AnalysisSession? successor = null;
        try
        {
            if (reason == "successor") { successor = coordinator.Rescan(first, [new FakeBackend(1) { Root = initial.Root }]); await Done(successor); }
            if (reason == "closed-session") first.Dispose();
            var caller = reason == "scheduler" ? new AnalysisCoordinator(otherIo, "epoch-profile") :
                reason == "profile" ? new AnalysisCoordinator(io, "foreign-profile") : coordinator;
            if (reason == "same-source") Assert.Throws<InvalidOperationException>(() => caller.Rescan(first, [initial]));
            else if (reason == "closed-session") Assert.Throws<ObjectDisposedException>(() => caller.Rescan(first, [fresh]));
            else Assert.Throws<InvalidOperationException>(() => caller.Rescan(first, [fresh]));
            Assert.False(fresh.Disposed);
        }
        finally { successor?.Dispose(); }
    }

    [Theory]
    [InlineData("locator")]
    [InlineData("scheme")]
    [InlineData("source")]
    [InlineData("connection")]
    [InlineData("boundary")]
    [InlineData("name-rules")]
    [InlineData("root-identity")]
    [InlineData("root-kind")]
    [InlineData("root-count")]
    public async Task RescanEpochRefusesAChangedSelectedRootWithoutReservingTheNextEpoch(string change)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var coordinator = new AnalysisCoordinator(io, "epoch-profile");
        var initial = new FakeBackend(1);
        if (change == "connection") initial = new FakeBackend(1) { Root = initial.Root with { Scheme = "provider", ConnectionIdentityReference = "old" } };
        using var first = coordinator.Start([initial]); await Done(first);
        var root = change switch
        {
            "locator" => initial.Root with { CanonicalLocator = "different" }, "scheme" => initial.Root with { Scheme = "foreign" },
            "source" => initial.Root with { SourceIdentity = "different" }, "connection" => initial.Root with { ConnectionIdentityReference = "different" },
            "boundary" => initial.Root with { TraversalBoundary = "different" }, "name-rules" => initial.Root with { NameRules = AnalysisNameRules.Unix },
            _ => initial.Root
        };
        var entry = change == "root-identity" ? initial.RootEntry with { Identity = "replacement" } :
            change == "root-kind" ? initial.RootEntry with { Kind = AnalysisKind.File } : initial.RootEntry;
        using var invalid = new EpochBackend(new FakeBackend(1), root, entry);
        Assert.Throws<InvalidOperationException>(() => coordinator.Rescan(first, change == "root-count" ? [invalid, invalid] : [invalid]));
        Assert.False(invalid.Inner.Disposed);
        using var second = coordinator.Rescan(first, [new FakeBackend(2) { Root = initial.Root }]); await Done(second);
        Assert.Equal(2, second.Snapshot().Identity.Epoch);
    }

    [Fact]
    public async Task RescanEpochRollsBackItsReservationWhenTheNewSessionConstructorFails()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var coordinator = new AnalysisCoordinator(io, "epoch-profile");
        var initial = new FakeBackend(1); using var first = coordinator.Start([initial]); await Done(first);
        using var invalid = new EpochBackend(new FakeBackend(1), initial.Root, initial.RootEntry) { ThrowOnRootRead = 2 };
        Assert.Throws<IOException>(() => coordinator.Rescan(first, [invalid])); Assert.False(invalid.Inner.Disposed);
        using var second = coordinator.Rescan(first, [new FakeBackend(2) { Root = initial.Root }]); await Done(second);
        Assert.Equal(2, second.Snapshot().Identity.Epoch);
    }

    private static Task Done(AnalysisSession session) => session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    [Fact]
    public async Task RescanEpochUsesAFreshLocalRuntimeBindingAfterThePhysicalRootHasBeenRevalidated()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var coordinator = new AnalysisCoordinator(io, "epoch-profile");
        var initial = new FakeBackend(1); initial = new FakeBackend(1) { Root = initial.Root with { ConnectionIdentityReference = "old-runtime-binding" } };
        using var first = coordinator.Start([initial]); await Done(first);
        using var second = coordinator.Rescan(first, [new FakeBackend(2) { Root = initial.Root with { ConnectionIdentityReference = "new-runtime-binding" } }]);
        await Done(second); Assert.Equal("new-runtime-binding", Assert.Single(second.Snapshot().Roots).ConnectionIdentityReference);
        Assert.Equal("old-runtime-binding", Assert.Single(first.Snapshot().Roots).ConnectionIdentityReference);
    }

    [Fact]
    public async Task RescanEpochCanPauseResumeAndStopWithoutLosingItsRefreshPhaseOrRetainedPredecessor()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var coordinator = new AnalysisCoordinator(io, "epoch-profile");
        var initial = new FakeBackend(1); using var first = coordinator.Start([initial]); await Done(first); var old = first.Snapshot();
        using var firstRead = new ManualResetEventSlim(); using var resumedRead = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool resuming = false;
        var fresh = new FakeBackend(1024)
        {
            Root = initial.Root,
            BeforeEnumerate = () => { entered.TrySetResult(); Assert.True(firstRead.Wait(TimeSpan.FromSeconds(10))); },
            Transform = (_, entry) => { if (resuming) { resumed.TrySetResult(); Assert.True(resumedRead.Wait(TimeSpan.FromSeconds(10))); } return entry; }
        };
        using var second = coordinator.Rescan(first, [fresh], new CallbackProgress(p => { if (p.Phase == AnalysisPhase.Paused) paused.TrySetResult(); }));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(AnalysisPhase.Refreshing, second.Snapshot().Phase); second.Pause(); firstRead.Set();
            await paused.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(AnalysisPhase.Paused, second.Snapshot().Phase); resuming = true; second.Resume();
            await resumed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(AnalysisPhase.Refreshing, second.Snapshot().Phase); second.Stop();
        }
        finally { firstRead.Set(); resumedRead.Set(); }
        await Done(second); Assert.Equal(AnalysisPhase.Stopped, second.Snapshot().Phase); Assert.Equal(old, first.Snapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeUnixRescanEpochAcceptsNewChildrenButRefusesAReplacedSelectedRoot(bool replaceRoot)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native source fixture."); Ordinary();
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, "root"); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "first"), "one");
        using var io = new DeviceIoScheduler(); var coordinator = new AnalysisCoordinator(io, "epoch-profile");
        using var first = coordinator.Start([new UnixAnalysisBackend(path)]); await Done(first); var old = first.Snapshot();
        if (replaceRoot) { Directory.Move(path, Path.Combine(temp.Path, "original")); Directory.CreateDirectory(path); }
        File.WriteAllText(Path.Combine(path, "second"), "two");
        using var source = new UnixAnalysisBackend(path);
        if (replaceRoot) Assert.Throws<InvalidOperationException>(() => coordinator.Rescan(first, [source]));
        else
        {
            using var second = coordinator.Rescan(first, [source]); await Done(second); var next = second.Snapshot();
            Assert.Equal(3, next.Nodes.Length); Assert.Equal(old.Identity.SessionId, next.Identity.SessionId); Assert.Equal(2, next.Identity.Epoch);
            AnalysisValidation.Validate(next, TestContext.Current.CancellationToken);
        }
        Assert.Equal(old, first.Snapshot());
    }

    private sealed class EpochBackend(FakeBackend inner, RootBinding root, AnalysisEntry entry) : IAnalysisBackend
    {
        internal readonly FakeBackend Inner = inner;
        internal int ThrowOnRootRead;
        private int _rootReads;
        public RootBinding Root => ++_rootReads == ThrowOnRootRead ? throw new IOException("Injected constructor metadata failure.") : root;
        public string Basis => Inner.Basis;
        public string IoDeviceKey => Inner.IoDeviceKey;
        public AnalysisEntry RootEntry => entry;
        public IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation) => Inner.Children(directory, cancellation);
        public void ValidateReadScope(AnalysisEntry directory) => Inner.ValidateReadScope(directory);
        public void Dispose() => Inner.Dispose();
    }
}
