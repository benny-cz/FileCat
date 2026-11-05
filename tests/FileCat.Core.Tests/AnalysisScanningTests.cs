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
    public async Task UnexpectedBackendFailureCannotPublishACompleteFinishedInventory()
    {
        Ordinary(); using var io = new DeviceIoScheduler();
        var source = new FakeBackend(1) { BeforeEnumerate = () => throw new NotSupportedException("injected producer failure") };
        using var session = new AnalysisCoordinator(io).Start([source]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var snapshot = session.Snapshot(); Assert.True(source.Disposed);
        Assert.Equal(AnalysisPhase.Failed, snapshot.Phase); Assert.Equal(NamespaceCoverage.Partial, snapshot.Coverage);
        Assert.Contains(snapshot.Issues, i => i.Code == "analysis-producer-failed");
        AnalysisValidation.Validate(snapshot, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FailedBackendCloseStillClosesRemainingSourcesAndReleasesScanAdmission()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); int closes = 0;
        var first = new FakeBackend(1) { BeforeClose = () => { Interlocked.Increment(ref closes); throw new IOException("injected close failure"); } };
        var second = new FakeBackend(1);
        using var session = new AnalysisCoordinator(io).Start([first, second]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(first.Disposed); Assert.True(second.Disposed); Assert.Equal(1, closes);
        var snapshot = session.Snapshot();
        Assert.Equal(NamespaceCoverage.Partial, snapshot.Coverage);
        Assert.Contains(snapshot.Issues, i => i.Code == "analysis-source-close-failed");
        Assert.Equal((UInt128)2, AnalysisAggregate.Project(snapshot, AnalysisMetric.Files, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        using var next = new AnalysisCoordinator(io).Start([new FakeBackend(1)]);
        await next.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, next.Snapshot().Coverage);
    }

    [Fact]
    public async Task SchedulerShutdownPreservesPartialStoppedResultsAndActualSourceOwnership()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var gate = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakeBackend(1100) { BeforeEnumerate = () => { entered.TrySetResult(); gate.Wait(TimeSpan.FromSeconds(15)); } };
        using var session = new AnalysisCoordinator(io).Start([source]);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            io.Dispose(); Assert.False(source.Disposed); Assert.False(session.Completion.IsCompleted);
        }
        finally { gate.Set(); }
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var snapshot = session.Snapshot();
        Assert.True(source.Disposed); Assert.Equal(AnalysisPhase.Stopped, snapshot.Phase);
        Assert.Equal(NamespaceCoverage.Partial, snapshot.Coverage); Assert.Equal(AnalysisFreshness.SourceUnavailable, snapshot.Freshness);
        Assert.Contains(snapshot.Issues, i => i.Code == "analysis-io-stopped");
        Assert.InRange(snapshot.Nodes.Length, 1, 513);
        AnalysisValidation.Validate(snapshot, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UnixUnreadableDirectoryDoesNotLoseReadableSiblingFiles()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native fixture."); Ordinary();
        using var temp = new TempDir(); string root = Path.Combine(temp.Path, "root"), denied = Path.Combine(root, "denied");
        Directory.CreateDirectory(denied); File.WriteAllBytes(Path.Combine(denied, "unseen.bin"), new byte[99]);
        File.WriteAllBytes(Path.Combine(root, "readable.bin"), new byte[11]);
        var original = File.GetUnixFileMode(denied);
        try
        {
            File.SetUnixFileMode(denied, UnixFileMode.None);
            using var io = new DeviceIoScheduler(); using var session = new AnalysisCoordinator(io).Start([new UnixAnalysisBackend(root)]);
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            var s = session.Snapshot(); AnalysisValidation.Validate(s, TestContext.Current.CancellationToken);
            Assert.Equal(NamespaceCoverage.Partial, s.Coverage); Assert.Contains(s.Nodes, n => n.Name == "readable.bin");
            Assert.DoesNotContain(s.Nodes, n => n.Name == "unseen.bin");
            Assert.Equal((UInt128)11, AnalysisAggregate.Project(s, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
            Assert.Equal(1ul, s.CoverageDetails!.EnumerationCompleted);
            var deniedNode = s.Nodes.Single(n => n.Name == "denied");
            if (deniedNode.MetadataState == MeasurementState.Known)
            {
                Assert.Equal(2ul, s.CoverageDetails.EnumerationRequested); Assert.Equal(1ul, s.CoverageDetails.EnumerationFailed);
                Assert.Contains(s.Issues, i => i.NodeId == deniedNode.Id && i.Code == "enumeration-unavailable");
            }
            else
            {
                Assert.Equal(MeasurementState.Failed, deniedNode.MetadataState);
                Assert.Equal(1ul, s.CoverageDetails.EnumerationRequested); Assert.Equal(0ul, s.CoverageDetails.EnumerationFailed);
                Assert.Contains(s.Issues, i => i.NodeId == deniedNode.Id && i.Code == "entry-metadata-unavailable");
            }
        }
        finally { File.SetUnixFileMode(denied, original); }
    }

    [Fact]
    public async Task UnavailableEntriesPreserveSiblingsAndExcludePolicyBoundariesFromEveryMetric()
    {
        Ordinary(); using var io = new DeviceIoScheduler();
        var backend = new FakeBackend(7)
        {
            Transform = (i, entry) => i is 0 or 6 ? entry : AnalysisEntry.Unavailable(entry.RelativeName, entry.RawName, "utf8",
                i == 3 ? AnalysisKind.Directory : i == 4 ? AnalysisKind.Unknown : AnalysisKind.File,
                i == 2 ? MeasurementState.Excluded : i == 5 ? MeasurementState.Changed : MeasurementState.Failed,
                i == 2 ? "source-mount-boundary" : i == 5 ? "entry-changed-before-observation" : "entry-metadata-unavailable")
        };
        using var session = new AnalysisCoordinator(io).Start([backend]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var s = session.Snapshot(); AnalysisValidation.Validate(s, TestContext.Current.CancellationToken);
        Assert.Equal(8, s.Nodes.Length); Assert.Equal(NamespaceCoverage.Partial, s.Coverage);
        Assert.Equal(AnalysisFreshness.PossiblyStale, s.Freshness);
        Assert.Equal(1ul, s.CoverageDetails!.EnumerationRequested); Assert.Equal(1ul, s.CoverageDetails.EnumerationCompleted);
        Assert.Equal(0ul, s.CoverageDetails.EnumerationFailed);
        Assert.Equal((UInt128)4, AnalysisAggregate.Project(s, AnalysisMetric.Files, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        foreach (var metric in new[] { AnalysisMetric.FileSize, AnalysisMetric.UniqueFileSize, AnalysisMetric.OnDisk })
        {
            var p = AnalysisAggregate.Project(s, metric, cancellation: TestContext.Current.CancellationToken);
            Assert.Equal((UInt128)2, p.Totals.Known);
            Assert.Equal(1ul, p.Totals.Missing[MeasurementState.Failed]); Assert.Equal(1ul, p.Totals.Missing[MeasurementState.Changed]);
            Assert.DoesNotContain(4ul, p.MatchingNodes);
        }
    }

    [Fact]
    public async Task CapturedSelectionRetainsExactlyThePublishedRevisionAfterTheSessionCloses()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var session = new AnalysisCoordinator(io).Start([new FakeBackend(2)]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        using var published = session.AcquireSnapshot();
        using var selection = AnalysisSelectionLease.Capture(published, [2, 3, 2], AnalysisSelectionKind.VisibleMatches, "visible fixture", "name:x");
        Assert.Equal(published.Snapshot.Identity.Revision, selection.Selection.Revision); Assert.Equal(2ul, selection.Selection.ExactMemberCount);
        Assert.Equal(new ulong[] { 2, 3 }, selection.Selection.NodeIds); Assert.Equal(2, selection.Selection.ObjectIds.Length);
        session.Dispose(); published.Dispose(); Assert.Equal(3, selection.Snapshot.Nodes.Length);
        selection.Dispose(); Assert.Throws<ObjectDisposedException>(() => selection.Snapshot);
        Assert.Throws<ObjectDisposedException>(() => published.Retain());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnixNestedRootsAreCoveredOnceInEitherSelectionOrder(bool childFirst)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native fixture."); Ordinary();
        using var temp = new TempDir(); string root = Path.Combine(temp.Path, "root"), inner = Path.Combine(root, "inner");
        Directory.CreateDirectory(inner); File.WriteAllBytes(Path.Combine(inner, "child.bin"), new byte[100]);
        string direct = Path.Combine(root, "direct.bin"); File.WriteAllBytes(direct, new byte[20]);
        var parent = new UnixAnalysisBackend(root); var child = new UnixAnalysisBackend(inner);
        using var io = new DeviceIoScheduler();
        using var session = new AnalysisCoordinator(io).Start(childFirst ? [child, parent, new UnixAnalysisBackend(direct), new UnixAnalysisBackend(root)]
            : [parent, child, new UnixAnalysisBackend(direct), new UnixAnalysisBackend(root)]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        var s = session.Snapshot(); AnalysisValidation.Validate(s, TestContext.Current.CancellationToken);
        Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, s.Coverage);
        Assert.Equal((UInt128)120, AnalysisAggregate.Project(s, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        Assert.Equal(3, s.Roots.Count(r => r.Disposition == AnalysisRootDisposition.Covered));
        string active = Assert.Single(s.Roots, r => r.Disposition == AnalysisRootDisposition.Active).Id;
        Assert.All(s.Roots.Where(r => r.Disposition == AnalysisRootDisposition.Covered), r => Assert.Equal(active, r.CoveredByRootId));
        var covered = s.Roots.Single(r => r.Label == "inner");
        var scopes = new AnalysisRootScopeIndex(s, TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisTruth.True, scopes.IsMember(covered.Id, s.Nodes.Single(n => n.Name == "child.bin")));
        Assert.Equal(AnalysisTruth.False, scopes.IsMember(covered.Id, s.Nodes.Single(n => n.Name == "direct.bin")));
        Assert.Equal(2ul, s.CoverageDetails!.EnumerationCompleted); Assert.Equal(0ul, s.CoverageDetails.EnumerationPending);
        Assert.All(s.Nodes, n => Assert.NotNull(n.Times));
    }

    [Fact]
    public async Task UnverifiableOverlapIsExcludedWithAnIssueInsteadOfDoubleCounting()
    {
        Ordinary();
        string root = Path.Combine(Path.GetTempPath(), "analysis-unverified-" + Guid.NewGuid().ToString("N"));
        var parent = new FakeBackend(1) { Root = new("1", "file", root, root, "parent", IdentityAssurance.Verified, "parent") };
        var child = new FakeBackend(1) { Root = new("1", "file", root + Path.DirectorySeparatorChar + "child", root + Path.DirectorySeparatorChar + "child", "child", IdentityAssurance.Verified, "child") };
        using var io = new DeviceIoScheduler(); using var session = new AnalysisCoordinator(io).Start([child, parent]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var s = session.Snapshot();
        Assert.Equal(NamespaceCoverage.Partial, s.Coverage);
        Assert.Equal(AnalysisRootDisposition.ExcludedUnverifiedOverlap, s.Roots[0].Disposition);
        Assert.Equal((UInt128)1, AnalysisAggregate.Project(s, AnalysisMetric.Files, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        Assert.Contains(s.Issues, i => i.Code == "root-overlap-unverified" && i.RootId == "1");
    }

    [Fact]
    public async Task CloudDirectoryIsAnExplicitPolicyBoundaryWithoutEnumeration()
    {
        Ordinary(); bool enumerated = false;
        var source = new FakeBackend(1) { CloudRoot = true, BeforeEnumerate = () => enumerated = true };
        using var io = new DeviceIoScheduler();
        using var session = new AnalysisCoordinator(io).Start([source]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var s = session.Snapshot();
        Assert.False(enumerated); Assert.Single(s.Nodes);
        Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, s.Coverage);
        Assert.Equal(MeasurementState.Excluded, Assert.Single(s.Issues).State);
        Assert.Equal("cloud-directory-not-enumerated", s.Issues[0].Code);
    }

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
    public async Task DeviceTimeoutFreezesResultsWithoutClosingTheInFlightSource()
    {
        Ordinary();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var io = new DeviceIoScheduler(TimeSpan.FromMilliseconds(40));
        var source = new FakeBackend(2) { BeforeEnumerate = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(15)); } };
        using var session = new AnalysisCoordinator(io).Start([source]);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            for (int i = 0; i < 200 && session.Snapshot().Phase != AnalysisPhase.Stopped; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            var frozen = session.Snapshot();
            Assert.Equal(AnalysisPhase.Stopped, frozen.Phase);
            Assert.Equal(AnalysisFreshness.SourceUnavailable, frozen.Freshness);
            Assert.Contains(frozen.Issues, i => i.Code == "device-not-responding");
            Assert.False(source.Disposed);
            using var other = new AnalysisCoordinator(io).Start([new FakeBackend(1)]);
            await other.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, other.Snapshot().Coverage);
            release.Set(); await session.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(frozen, session.Snapshot());
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task ScopeMustRemainBoundUntilTheBatchIsPublished()
    {
        Ordinary();
        using var io = new DeviceIoScheduler();
        bool changed = false;
        var source = new FakeBackend(2) { BeforeEnumerate = () => changed = true, Validate = () => { if (changed) throw new AnalysisSourceChangedException(); } };
        using var session = new AnalysisCoordinator(io).Start([source]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var snapshot = session.Snapshot();
        Assert.Single(snapshot.Nodes); // The unbound batch cannot enter charts or an inventory.
        Assert.Equal(NamespaceCoverage.Partial, snapshot.Coverage);
        Assert.Equal(AnalysisFreshness.PossiblyStale, snapshot.Freshness);
    }

    [Fact]
    public void UnixQueuedDirectoryRetainsItsAncestorBinding()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native fixture."); Ordinary();
        using var temp = new TempDir();
        string root = Path.Combine(temp.Path, "root");
        string parent = Path.Combine(root, "parent"); string child = Path.Combine(parent, "child");
        Directory.CreateDirectory(child); File.WriteAllText(Path.Combine(child, "file"), "owned fixture");
        using var backend = new UnixAnalysisBackend(root);
        var p = Assert.Single(backend.Children(backend.RootEntry, TestContext.Current.CancellationToken));
        var c = Assert.Single(backend.Children(p, TestContext.Current.CancellationToken));
        backend.ValidateReadScope(c);
        Thread.Sleep(20);
        Directory.Move(parent, parent + "-moved"); Directory.Move(parent + "-moved", parent);
        Assert.Single(Directory.EnumerateFileSystemEntries(child)); // A path-only control would miss this movement.
        Assert.Throws<AnalysisSourceChangedException>(() => backend.ValidateReadScope(c));
    }

    [Fact]
    public async Task UnixExplicitHardLinkedFileRootsAreTwoPathsAndOneObject()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native fixture."); Ordinary();
        using var temp = new TempDir();
        string a = Path.Combine(temp.Path, "first.bin"), b = Path.Combine(temp.Path, "second.bin");
        File.WriteAllBytes(a, new byte[10000]); Assert.Equal(0, Link(a, b));
        using var io = new DeviceIoScheduler();
        using var session = new AnalysisCoordinator(io).Start([new UnixAnalysisBackend(a), new UnixAnalysisBackend(b)]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var s = session.Snapshot(); AnalysisValidation.Validate(s, TestContext.Current.CancellationToken);
        Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, s.Coverage); Assert.Equal(2, s.Nodes.Length);
        Assert.Equal((UInt128)20000, AnalysisAggregate.Project(s, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        Assert.Equal((UInt128)10000, AnalysisAggregate.Project(s, AnalysisMetric.UniqueFileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        Assert.Equal(0ul, Assert.Single(AnalysisAggregate.Project(s, AnalysisMetric.OnDisk, cancellation: TestContext.Current.CancellationToken).Charges).ParentId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnixNativeScannerCountsHardLinksOnceAndDoesNotTraverseSymlink(bool detailed)
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
        using var session = new AnalysisCoordinator(io).Start([new UnixAnalysisBackend(root)],
            components: detailed ? AnalysisComponentPreset.DetailedAllocation : AnalysisComponentPreset.Standard);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        var s = session.Snapshot(); AnalysisValidation.Validate(s, TestContext.Current.CancellationToken);
        Assert.True(s.Coverage == NamespaceCoverage.CompleteUnderPolicy, string.Join(",", s.Issues.Select(i => i.Code + ":" + i.NodeId)));
        Assert.DoesNotContain(s.Nodes, n => n.Name == "must-not-appear");
        Assert.Contains(s.Nodes, n => n.Name == "link-out" && n.Kind == AnalysisKind.Link);
        Assert.Equal((UInt128)20000, AnalysisAggregate.Project(s, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        Assert.Equal((UInt128)10000, AnalysisAggregate.Project(s, AnalysisMetric.UniqueFileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        var disk = AnalysisAggregate.Project(s, AnalysisMetric.OnDisk, cancellation: TestContext.Current.CancellationToken);
        Assert.True(disk.Totals.Known > 0);
        Assert.True(Assert.Single(disk.Charges, c => !c.DirectorySelf).Shared);
        Assert.Equal(detailed ? 3 : 0, s.Facts.Count(f => f.DirectoryId.HasValue));
        Assert.Equal(detailed ? 3 : 0, disk.Charges.Count(c => c.DirectorySelf));
        Assert.Equal(detailed ? "detailed" : "standard", s.Policy.ComponentPreset);
        Assert.All(s.Facts.Where(f => f.DirectoryId.HasValue), f => Assert.True(f.Measurement.IsKnown));
        Assert.Equal(disk.Totals.Known, disk.Charges.Aggregate((UInt128)0, (sum, c) => checked(sum + c.Value)));
    }

    private sealed class FakeBackend(int count) : IAnalysisBackend
    {
        public bool Disposed;
        public Action? BeforeEnumerate;
        public Action? BeforeClose;
        public Action? Validate;
        public bool CloudRoot;
        public int FailAfter = int.MaxValue;
        public Func<int, AnalysisEntry, AnalysisEntry>? Transform;
        public RootBinding Root { get; init; } = new("1", "file", "fake", "fake-" + Guid.NewGuid(), "fake-" + Guid.NewGuid(), IdentityAssurance.Verified, "fake");
        public string Basis => "fixture-v1";
        public string IoDeviceKey { get; init; } = "fixture:" + Guid.NewGuid().ToString("N");
        public AnalysisEntry RootEntry => Entry("root", AnalysisKind.Directory) with { Cloud = CloudRoot };
        public void ValidateReadScope(AnalysisEntry directory) => Validate?.Invoke();
        public IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation)
        {
            BeforeEnumerate?.Invoke();
            for (int i = 0; i < count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (i == FailAfter) throw new AnalysisSourceChangedException();
                var entry = Entry(i + new string('x', 200), AnalysisKind.File);
                yield return Transform?.Invoke(i, entry) ?? entry;
            }
        }
        private static AnalysisEntry Entry(string name, AnalysisKind kind) => new([], name, Encoding.UTF8.GetBytes(name).ToImmutableArray(), kind,
            name, AnalysisMeasurement.Known(1), AnalysisMeasurement.Known(1), AnalysisMeasurement.Known(1), DateTimeOffset.UnixEpoch, "v1");
        public void Dispose() { Disposed = true; BeforeClose?.Invoke(); }
    }
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8)] private static partial int Link(string oldName, string newName);
}
