using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisScanningTests
{
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary test process: " + AnalysisAdmission.Refusal());

    [Fact]
    public async Task SnapshotLeaseRetainsAnImmutableRevisionAfterOwnerCloses()
    {
        Ordinary();
        using var io = new DeviceIoScheduler();
        var session = new AnalysisCoordinator(io).Start([new FakeBackend(2)]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        using var lease = session.AcquireSnapshot();
        session.Dispose();
        Assert.Equal(3, lease.Snapshot.Nodes.Length);
        Assert.Throws<ObjectDisposedException>(() => session.Snapshot());
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => lease.Snapshot);
    }

    [Fact]
    public async Task PauseSettlesTheAdmittedBatchAndResumeContinuesTheSameSession()
    {
        Ordinary();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var io = new DeviceIoScheduler();
        var source = new FakeBackend(1000) { BeforeEnumerate = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(15)); } };
        using var session = new AnalysisCoordinator(io).Start([source]);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            session.Pause(); release.Set();
            for (int i = 0; i < 200 && session.Snapshot().Phase != AnalysisPhase.Paused; i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            var paused = session.Snapshot();
            Assert.Equal(AnalysisPhase.Paused, paused.Phase);
            Assert.False(session.Completion.IsCompleted);
            session.Resume();
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var finished = session.Snapshot();
            Assert.Equal(1001, finished.Nodes.Length);
            Assert.Equal(paused.Identity.SessionId, finished.Identity.SessionId);
            Assert.True(finished.Identity.Revision > paused.Identity.Revision);
        }
        finally { release.Set(); }
    }

    [Fact]
    public void NativeHandleBudgetCannotBeBypassedByAnotherSessionAndCanBeReused()
    {
        var leases = new List<IDisposable>();
        try
        {
            for (int i = 0; i < AnalysisLimits.NativeHandles; i++) leases.Add(AnalysisNativeHandles.Acquire());
            Assert.Throws<IOException>(() => AnalysisNativeHandles.Acquire());
            leases[0].Dispose(); leases[0].Dispose();
            using var replacement = AnalysisNativeHandles.Acquire();
            Assert.Throws<IOException>(() => AnalysisNativeHandles.Acquire());
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    [Fact]
    public async Task WideDirectoryCrossesBatchBoundariesWithoutLosingEntries()
    {
        Ordinary();
        using var io = new DeviceIoScheduler();
        var source = new FakeBackend(1100);
        using var session = new AnalysisCoordinator(io).Start([source]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        var snapshot = session.Snapshot();
        AnalysisValidation.Validate(snapshot, TestContext.Current.CancellationToken);
        Assert.Equal(1101, snapshot.Nodes.Length);
        Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, snapshot.Coverage);
        Assert.True(source.Disposed);
        Assert.Equal((UInt128)1100, AnalysisAggregate.Project(snapshot, AnalysisMetric.Files, cancellation: TestContext.Current.CancellationToken).Totals.Known);
    }

    [Fact]
    public async Task StopFreezesResultsAndRetainsNativeSourceUntilBlockedRequestReturns()
    {
        Ordinary();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var io = new DeviceIoScheduler();
        var source = new FakeBackend(2) { BeforeEnumerate = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(15)); } };
        using var session = new AnalysisCoordinator(io).Start([source]);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            session.Stop();
            var frozen = session.Snapshot();
            Assert.Equal(AnalysisPhase.Stopped, frozen.Phase);
            Assert.Equal(NamespaceCoverage.Partial, frozen.Coverage);
            Assert.False(source.Disposed);
            Assert.False(session.Completion.IsCompleted);
            release.Set();
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(frozen, session.Snapshot());
            Assert.True(source.Disposed);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task PartialEnumerationRetainsAlreadyObservedRecords()
    {
        Ordinary();
        using var io = new DeviceIoScheduler();
        using var session = new AnalysisCoordinator(io).Start([new FakeBackend(5) { FailAfter = 2 }]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var snapshot = session.Snapshot();
        Assert.Equal(3, snapshot.Nodes.Length);
        Assert.Equal(NamespaceCoverage.Partial, snapshot.Coverage);
        Assert.Contains(snapshot.Issues, i => i.Code == "source-changed-or-boundary");
    }

    [Fact]
    public async Task UnixNativeScannerCountsHardLinksOnceAndDoesNotTraverseSymlink()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native fixture."); Ordinary();
        using var temp = new TempDir();
        string root = Path.Combine(temp.Path, "root"); Directory.CreateDirectory(root);
        string a = Path.Combine(root, "a"); Directory.CreateDirectory(a);
        string b = Path.Combine(root, "b"); Directory.CreateDirectory(b);
        string file = Path.Combine(a, "data.bin"); File.WriteAllBytes(file, new byte[10000]);
        Assert.Equal(0, Link(file, Path.Combine(b, "alias.bin")));
        string outside = Path.Combine(temp.Path, "outside"); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "must-not-appear"), "outside");
        Directory.CreateSymbolicLink(Path.Combine(root, "link-out"), outside);
        using (var direct = new UnixAnalysisBackend(root))
        {
            var rootChildren = direct.Children(direct.RootEntry, TestContext.Current.CancellationToken).ToArray();
            Assert.Contains(rootChildren, c => c.Name == "link-out" && c.Kind == AnalysisKind.Link);
            foreach (var child in rootChildren.Where(c => c.Kind == AnalysisKind.Directory))
                Assert.Single(direct.Children(child, TestContext.Current.CancellationToken));
        }
        using var io = new DeviceIoScheduler();
        using var session = new AnalysisCoordinator(io).Start([new UnixAnalysisBackend(root)]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        var s = session.Snapshot(); AnalysisValidation.Validate(s, TestContext.Current.CancellationToken);
        Assert.True(s.Coverage == NamespaceCoverage.CompleteUnderPolicy, string.Join(",", s.Issues.Select(i => i.Code + ":" + i.NodeId)));
        Assert.DoesNotContain(s.Nodes, n => n.Name == "must-not-appear");
        Assert.Contains(s.Nodes, n => n.Name == "link-out" && n.Kind == AnalysisKind.Link);
        Assert.Equal((UInt128)20000, AnalysisAggregate.Project(s, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        Assert.Equal((UInt128)10000, AnalysisAggregate.Project(s, AnalysisMetric.UniqueFileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        var disk = AnalysisAggregate.Project(s, AnalysisMetric.OnDisk, cancellation: TestContext.Current.CancellationToken);
        Assert.True(disk.Totals.Known > 0);
        Assert.Single(disk.Charges);
        Assert.True(disk.Charges[0].Shared);
    }

    private sealed class FakeBackend(int count) : IAnalysisBackend
    {
        public bool Disposed;
        public Action? BeforeEnumerate;
        public int FailAfter = int.MaxValue;
        public RootBinding Root { get; } = new("1", "file", "fake", "fake-" + Guid.NewGuid(), "fake-" + Guid.NewGuid(), IdentityAssurance.Verified, "fake");
        public string Basis => "fixture-v1";
        public AnalysisEntry RootEntry => Entry("root", AnalysisKind.Directory);
        public IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation)
        {
            BeforeEnumerate?.Invoke();
            for (int i = 0; i < count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (i == FailAfter) throw new AnalysisSourceChangedException();
                yield return Entry(i + new string('x', 200), AnalysisKind.File);
            }
        }
        private static AnalysisEntry Entry(string name, AnalysisKind kind) => new([], name, Encoding.UTF8.GetBytes(name).ToImmutableArray(), kind,
            name, AnalysisMeasurement.Known(1), AnalysisMeasurement.Known(1), AnalysisMeasurement.Known(1), DateTimeOffset.UnixEpoch, "v1");
        public void Dispose() => Disposed = true;
    }
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8)] private static partial int Link(string oldName, string newName);
}
