using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [Fact]
    public async Task FastNtfsDiscoveryProjectsOnlyRootedNonPrivateScopeWithTaint()
    {
        OrdinaryPreparation(); using var profile = new NtfsFixture();
        var token = TestContext.Current.CancellationToken;
        string device = PathUtil.GetDeviceKey(profile.Root);
        var root = new NtfsProjectionRoot(profile.Root, "held-volume");
        var privateRoot = new NtfsProjectionRoot(profile.Root, "held-private");
        var scan = new NtfsProjectionScan(root);
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
            await using var indexed = await session.AcquireDatasetAsync(token);
            Assert.NotNull(indexed);
            Assert.Equal(preview.Taint, indexed.Descriptor.Taint);
            Assert.False(indexed.Descriptor.CoverageDetails.EnumerationKnown);
            Assert.Equal(preview.Nodes.Select(n => n.Name), (await indexed.ReadPathsAsync(0, cancellation: token)).Rows.Select(n => n.Name));
            Assert.Equal(MeasurementState.Unsupported, Assert.Single((await indexed.ReadFactsAsync(0, cancellation: token)).Rows).Measurement.State);
            Assert.Equal(2ul, (await indexed.ReadPathAsync(4, token)).ParentId);
            Assert.Throws<InvalidOperationException>(() => coordinator.Rescan(session, [new NtfsProjectionRoot(profile.Root, "held-volume")]));
            Assert.Equal(1, scan.Captures);
            Assert.Equal(1, root.Disposals);
            Assert.Equal(1, privateRoot.Disposals);
        }
        finally { session.Dispose(); await session.DatasetCloseCompletion; }
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

    private sealed class NtfsProjectionScan(NtfsProjectionRoot root, bool unverifiedBoundary = false) : IAnalysisNtfsScan
    {
        public IAnalysisBackend Source => root;
        public UInt128 RootId => 1;
        internal int Captures { get; private set; }
        public void ValidateVolume(CancellationToken cancellation) => cancellation.ThrowIfCancellationRequested();
        public UInt128? GetPrivateBoundaryId(IAnalysisBackend protection) => unverifiedBoundary ? throw new AnalysisSourceChangedException() : (UInt128)3;
        public async Task<AnalysisNtfsOutcome> CaptureAsync(AnalysisNtfsDiscovery discovery, CancellationToken cancellation)
        {
            Captures++;
            ImmutableArray<AnalysisNtfsObservation> rows = [Row(4, 2, "file"), Row(5, 3, "private-child"), Row(6, 7, "target"),
                Row(7, 1, "link", FileAttributes.Directory | FileAttributes.ReparsePoint), Row(3, 1, "private-name", FileAttributes.Directory),
                Row(2, 1, "folder", FileAttributes.Directory), Row(8, 1, "dup"), Row(9, 1, "dup"), Row(10, 99, "orphan")];
            await discovery.AppendAsync(rows, cancellation);
            await discovery.SealAsync((ulong)rows.Length, cancellation);
            return AnalysisNtfsOutcome.Finished;
        }
        private static AnalysisNtfsObservation Row(uint id, uint parent, string name, FileAttributes attributes = FileAttributes.Normal) =>
            new(id, parent, (uint)attributes, [.. AnalysisRawNames.Encode("utf16le", name)]);
    }
}
