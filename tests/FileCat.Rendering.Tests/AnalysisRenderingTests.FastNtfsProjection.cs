using System.Collections.Immutable;
using System.Buffers.Binary;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FastNtfsDiscoveryProjectsOnlyRootedNonPrivateScopeWithTaint(bool supplement)
    {
        OrdinaryPreparation(); using var profile = new NtfsFixture();
        var token = TestContext.Current.CancellationToken;
        string device = PathUtil.GetDeviceKey(profile.Root);
        var root = new NtfsProjectionRoot(profile.Root, "held-volume");
        var privateRoot = new NtfsProjectionRoot(profile.Root, "held-private");
        var scan = new NtfsProjectionScan(root, supplement: supplement);
        var storage = new AnalysisStorageOptions(device, () => OpenNtfsFixtureDirectory(profile),
            [new(device, () => privateRoot)]);
        var coordinator = new AnalysisCoordinator(profile.Io);
        using var session = coordinator.StartNtfs(scan, storage);
        try
        {
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(30), token);
            var preview = session.Snapshot();
            AnalysisValidation.Validate(preview, token);
            Assert.Equal(AnalysisPhase.Finished, preview.Phase);
            Assert.Equal(AnalysisPrivacy.Join(AnalysisTaint.PrivilegedDerived), preview.Taint);
            Assert.Equal(NamespaceCoverage.Partial, preview.Coverage);
            Assert.False(preview.CoverageDetails!.Qualification!.EnumerationKnown);
            Assert.Equal(new[] { "held-volume", "folder", "link", "file" }, preview.Nodes.Select(n => n.Name));
            Assert.Contains(preview.Issues, i => i.Code == "private-analysis-state-excluded" && i.NodeId == 1);
            Assert.Contains(preview.Issues, i => i.Code == "ntfs-discovery-ambiguous-name");
            Assert.Contains(preview.Issues, i => i.Code == "ntfs-discovery-unreachable-or-excluded");
            Assert.Contains(preview.Issues, i => i.Code == "ntfs-discovery-alias-coverage-incomplete");
            Assert.Contains(preview.Issues, i => i.Code == "ntfs-discovery-ordinary-measurement-unavailable");
            await using var indexed = await session.AcquireDatasetAsync(token);
            Assert.NotNull(indexed);
            Assert.Equal(preview.Taint, indexed.Descriptor.Taint);
            Assert.False(indexed.Descriptor.CoverageDetails.EnumerationKnown);
            Assert.Equal(preview.Nodes.Select(n => n.Name), (await indexed.ReadPathsAsync(0, cancellation: token)).Rows.Select(n => n.Name));
            var allocation = Assert.Single((await indexed.ReadFactsAsync(0, cancellation: token)).Rows).Measurement;
            Assert.Equal(supplement ? AnalysisMeasurement.Known(4096) : AnalysisMeasurement.Missing(MeasurementState.Unsupported), allocation);
            Assert.Equal(2ul, (await indexed.ReadPathAsync(4, token)).ParentId);
            Assert.Throws<InvalidOperationException>(() => coordinator.Rescan(session, [new NtfsProjectionRoot(profile.Root, "held-volume")]));
            Assert.Equal(1, scan.Captures);
            Assert.Equal(1, root.Disposals);
            Assert.Equal(1, privateRoot.Disposals);
            Assert.Equal(supplement ? new UInt128[] { 2, 7, 4 } : [2, 7], scan.LookupIds);
            Assert.Equal(supplement ? new UInt128[] { 2 } : [], scan.LookupAncestors);
        }
        finally { session.Dispose(); await session.DatasetCloseCompletion; }
    }

    [Fact]
    public void FastNtfsDiscoveryOrdinaryMeasurementsRequireNativeIdentityAndExactSpelling()
    {
        OrdinaryPreparation();
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new NtfsFixture();
        var token = TestContext.Current.CancellationToken;
        string folder = Directory.CreateDirectory(Path.Combine(fixture.Root, "MiXeD")).FullName;
        string file = Path.Combine(folder, "Primary.bin"), alias = Path.Combine(folder, "OtherName.bin");
        string stream = file + ":note";
        byte[] primary = new byte[24577], extra = new byte[16391];
        primary[15] = 3; extra[9000] = 7;
        File.WriteAllBytes(file, primary);
        File.WriteAllBytes(stream, extra);
        CreateGroupedNativeAlias(file, alias);
        string alternate = Directory.CreateDirectory(Path.Combine(fixture.Root, "Another")).FullName;
        string redirect = Path.Combine(fixture.Root, "Redirect");
        Junction.Create(redirect, alternate);
        try
        {
            int before = AnalysisNativeHandles.Count;
            using (var source = new WindowsAnalysisBackend(fixture.Root))
            {
                var branch = NativeObservation(folder, FileAttributes.Directory);
                var leaf = NativeObservation(file);
                var sameFile = leaf with { RawName = Raw(Path.GetFileName(alias)) };
                var normal = source.MeasureDiscoveryChildren([branch], [leaf, sameFile], detailed: false, token);
                Assert.All(normal, row =>
                {
                    Assert.NotNull(row);
                    Assert.Equal((UInt128)primary.Length, row.LogicalLength.Value);
                    Assert.True(row.Allocation.IsKnown);
                    Assert.Equal((UInt128)2, row.Links.Value);
                    Assert.Null(row.Ancestry);
                    Assert.Empty(row.RelativeName);
                    Assert.Null(row.ExtraAllocation);
                });
                Assert.Equal(normal[0]!.Identity, normal[1]!.Identity);
                Assert.Equal("OtherName.bin", normal[1]!.Name);

                var detailed = Assert.Single(source.MeasureDiscoveryChildren([branch], [leaf], detailed: true, token));
                Assert.NotNull(detailed);
                Assert.Equal(AnalysisComponents.NamedStreamBasis, detailed.ExtraAllocation?.Basis);
                Assert.True(detailed.ExtraAllocation?.Measurement.Value >= (UInt128)extra.Length);

                var wrongCase = leaf with { RawName = Raw("primary.bin") };
                var wrongId = leaf with { ObjectId = leaf.ObjectId ^ 1 };
                var missing = leaf with { RawName = Raw("Missing.bin") };
                var changed = source.MeasureDiscoveryChildren([branch], [wrongCase, wrongId, missing], detailed: true, token);
                Assert.All(changed, row => Assert.True(row is { Identity: null, MetadataState: MeasurementState.Changed } && !row.Allocation.IsKnown));
                var replacedParent = branch with { ObjectId = branch.ObjectId ^ 1 };
                Assert.Equal(MeasurementState.Changed, Assert.Single(source.MeasureDiscoveryChildren([replacedParent], [leaf], false, token))!.MetadataState);

                var reparse = branch with { RawName = Raw("Redirect") };
                var blocked = Assert.Single(source.MeasureDiscoveryChildren([reparse], [leaf], false, token));
                Assert.True(blocked is null || blocked.Identity is null && !blocked.LogicalLength.IsKnown);
            }
            Assert.Equal(before, AnalysisNativeHandles.Count);
            Assert.Equal(primary, File.ReadAllBytes(file));
            Assert.Equal(extra, File.ReadAllBytes(stream));
            Assert.Empty(Directory.EnumerateFileSystemEntries(alternate));
        }
        finally { Directory.Delete(redirect); }

        static AnalysisNtfsObservation NativeObservation(string path, FileAttributes attributes = FileAttributes.Normal)
        {
            using var entry = new WindowsAnalysisBackend(path);
            string windowsId = Assert.IsType<string>(entry.RootEntry.Identity);
            var id = BinaryPrimitives.ReadUInt128LittleEndian(Convert.FromHexString(windowsId[(windowsId.LastIndexOf(':') + 1)..]));
            return new(id, 1, (uint)attributes, Raw(Path.GetFileName(path)));
        }
        static ImmutableArray<byte> Raw(string name) => [.. AnalysisRawNames.Encode("utf16le", name)];
    }

    [Fact]
    public async Task FastNtfsDiscoveryRequiresPrivateStorageAndResolvedExclusionsBeforeAuthorization()
    {
        OrdinaryPreparation(); using var profile = new NtfsFixture();
        var token = TestContext.Current.CancellationToken;
        string device = PathUtil.GetDeviceKey(profile.Root);
        var coordinator = new AnalysisCoordinator(profile.Io);
        var root = new NtfsProjectionRoot(profile.Root, "held-volume");
        var privateRoot = new NtfsProjectionRoot(profile.Root, "held-private");
        var scan = new NtfsProjectionScan(root, unverifiedBoundary: true);
        var storage = new AnalysisStorageOptions(device, () => OpenNtfsFixtureDirectory(profile), [new(device, () => privateRoot)])
            { AllowMemoryFallback = true };
        Assert.Throws<InvalidOperationException>(() => coordinator.StartNtfs(scan, storage with { MemoryOnly = true }));
        using var session = coordinator.StartNtfs(scan, storage);
        try
        {
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(30), token);
            Assert.Equal(0, scan.Captures);
            Assert.Equal(AnalysisPhase.Failed, session.Snapshot().Phase);
            Assert.Empty(session.Snapshot().Nodes);
            Assert.Contains(session.Snapshot().Issues, i => i.Code == "ntfs-discovery-unavailable");
            Assert.Equal(1, root.Disposals);
            Assert.Equal(1, privateRoot.Disposals);
        }
        finally { session.Dispose(); await session.DatasetCloseCompletion; }
    }

    private static AnalysisPrivateDirectory OpenNtfsFixtureDirectory(NtfsFixture profile) => OperatingSystem.IsWindows()
        ? new WindowsAnalysisPrivateDirectory(profile.Root, "ntfs-projection") : new UnixAnalysisPrivateDirectory(profile.Root, "ntfs-projection");

    [Fact]
    public async Task FastNtfsDiscoverySessionPublishesPausedOnlyAfterBrokerAcknowledgment()
    {
        OrdinaryPreparation(); using var profile = new NtfsFixture();
        var token = TestContext.Current.CancellationToken;
        string device = PathUtil.GetDeviceKey(profile.Root);
        var accepted = new TaskCompletionSource<AnalysisNtfsControl>(TaskCreationOptions.RunContinuationsAsynchronously);
        var root = new NtfsProjectionRoot(profile.Root, "held-volume");
        var scan = new NtfsProjectionScan(root, broker: async (control, cancellation) =>
        {
            accepted.TrySetResult(control);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        var storage = new AnalysisStorageOptions(device, () => OpenNtfsFixtureDirectory(profile),
            [new(device, () => new NtfsProjectionRoot(profile.Root, "held-private"))]);
        using var session = new AnalysisCoordinator(profile.Io).StartNtfs(scan, storage);
        try
        {
            var control = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.True(session.CanPause);
            session.Pause();
            Assert.True(control.PauseRequested());
            Assert.Equal(AnalysisPhase.Pausing, session.Snapshot().Phase);
            control.ReportPaused(true);
            Assert.Equal(AnalysisPhase.Paused, session.Snapshot().Phase);
            session.Resume();
            Assert.False(control.PauseRequested());
            Assert.Equal(AnalysisPhase.Paused, session.Snapshot().Phase);
            control.ReportPaused(false);
            Assert.Equal(AnalysisPhase.Scanning, session.Snapshot().Phase);
            session.Stop();
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Equal(AnalysisPhase.Stopped, session.Snapshot().Phase);
            Assert.Single(session.Snapshot().Nodes);
            Assert.Equal(1, root.Disposals);
        }
        finally { session.Stop(); await session.Completion.WaitAsync(token); session.Dispose(); await session.DatasetCloseCompletion; }
    }

    private sealed class NtfsFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Environment.CurrentDirectory, ".tmp", "analysis-ntfs-" + Guid.NewGuid().ToString("N"));
        internal DeviceIoScheduler Io { get; }

        internal NtfsFixture()
        {
            Directory.CreateDirectory(Root);
            Io = new(threadsPerDevice: 1);
        }

        public void Dispose()
        {
            Io.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class NtfsProjectionRoot(string path, string identity) : IAnalysisBackend
    {
        public RootBinding Root { get; } = new("1", "file", path, path, identity, IdentityAssurance.Verified, identity)
            { NameRules = AnalysisNameRules.Windows };
        public string Basis => AnalysisComponents.WindowsPrimaryAllocationBasis;
        public AnalysisEntry RootEntry { get; } = new([], identity, [], AnalysisKind.Directory, identity,
            AnalysisMeasurement.Missing(MeasurementState.Unsupported), AnalysisMeasurement.Missing(MeasurementState.Unsupported),
            AnalysisMeasurement.Missing(MeasurementState.Unsupported), null, "held", ObservedUtc: DateTimeOffset.UtcNow);
        internal int Disposals { get; private set; }
        public IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation) => throw new InvalidOperationException("Unexpected standard enumeration.");
        public void ValidateReadScope(AnalysisEntry directory) => throw new InvalidOperationException("Unexpected standard enumeration.");
        public AnalysisRootContainment ContainsRoot(IAnalysisBackend other) => AnalysisRootContainment.Disjoint;
        public void Dispose() => Disposals++;
    }

    private sealed class NtfsProjectionScan(NtfsProjectionRoot root, bool unverifiedBoundary = false, bool supplement = false,
        Func<AnalysisNtfsControl, CancellationToken, Task>? broker = null) : IAnalysisNtfsScan
    {
        public IAnalysisBackend Source => root;
        public UInt128 RootId => 1;
        internal int Captures { get; private set; }
        internal List<UInt128> LookupIds { get; } = [];
        internal List<UInt128> LookupAncestors { get; } = [];
        public void ValidateVolume(CancellationToken cancellation) => cancellation.ThrowIfCancellationRequested();
        public UInt128? GetPrivateBoundaryId(IAnalysisBackend protection) => unverifiedBoundary ? throw new AnalysisSourceChangedException() : (UInt128)3;
        public AnalysisEntry?[] Measure(IReadOnlyList<AnalysisNtfsObservation> ancestors, IReadOnlyList<AnalysisNtfsObservation> children,
            bool detailed, CancellationToken cancellation)
        {
            LookupIds.AddRange(children.Select(row => row.ObjectId));
            LookupAncestors.AddRange(ancestors.Select(row => row.ObjectId));
            var measured = new AnalysisEntry?[children.Count];
            if (!supplement) return measured;
            for (int i = 0; i < children.Count; i++)
            {
                var row = children[i];
                var now = DateTimeOffset.UtcNow;
                if (row.ObjectId == 2) measured[i] = row.ToEntry(now) with { Identity = "ordinary-folder", Version = "held-folder" };
                if (row.ObjectId == 4) measured[i] = row.ToEntry(now) with { Identity = "ordinary-file", Version = "held-file",
                    LogicalLength = AnalysisMeasurement.Known(512), Allocation = AnalysisMeasurement.Known(4096), Links = AnalysisMeasurement.Known(2) };
            }
            return measured;
        }
        public async Task<AnalysisNtfsOutcome> CaptureAsync(AnalysisNtfsDiscovery discovery, AnalysisNtfsControl control, CancellationToken cancellation)
        {
            Captures++;
            control.ReportPaused(false);
            if (broker is not null) await broker(control, cancellation);
            ImmutableArray<AnalysisNtfsObservation> rows = [Row(4, 2, "file"), Row(5, 3, "private-child"), Row(6, 7, "target"),
                Row(7, 1, "link", FileAttributes.Directory | FileAttributes.ReparsePoint), Row(3, 1, "private-name", FileAttributes.Directory),
                Row(2, 1, "folder", FileAttributes.Directory), Row(8, 1, "dup"), Row(9, 1, "dup"), Row(10, 99, "orphan")];
            await discovery.AppendAsync(rows, cancellation);
            control.ReportProgress((uint)rows.Length);
            await discovery.SealAsync((ulong)rows.Length, cancellation);
            return AnalysisNtfsOutcome.Finished;
        }
        private static AnalysisNtfsObservation Row(uint id, uint parent, string name, FileAttributes attributes = FileAttributes.Normal) =>
            new(id, parent, (uint)attributes, [.. AnalysisRawNames.Encode("utf16le", name)]);
    }
}
