using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Tests;

internal static class AnalysisSourceFrontierFixture
{
    internal static async Task VerifyWideSource(string parent, Func<string, IAnalysisBackend> prepare, Func<string, AnalysisPrivateDirectory> openPrivate, CancellationToken cancellation)
    {
        const int directories = 3000;
        string sourcePath = Path.Combine(parent, "wide-source"), privatePath = Path.Combine(sourcePath, "private"); Directory.CreateDirectory(sourcePath);
        using var io = new DeviceIoScheduler(); string device = FileCat.Core.FileSystem.PathUtil.GetDeviceKey(sourcePath);
        AnalysisPrivateDirectory? storage = await AnalysisIo.Run(io, device, _ => openPrivate(sourcePath), cancellation);
        string canary = Path.Combine(privatePath, "canary.txt"); File.WriteAllText(canary, "private frontier canary");
        for (int n = 0; n < directories; n++)
        {
            string directory = Path.Combine(sourcePath, "directory-" + n.ToString("D4")); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "file.bin"), [(byte)(n % 251)]);
        }
        AnalysisSession? session = null; IAnalysisBackend? source = null; AnalysisDatasetLease? reader = null;
        try
        {
            source = await AnalysisIo.Run(io, device, _ => prepare(sourcePath), cancellation);
            session = new AnalysisCoordinator(io, "native-frontier").Start([source], storage: new(device,
                () => Interlocked.Exchange(ref storage, null) ?? throw new InvalidOperationException("Private ownership transferred."), [new(device, () => prepare(privatePath))], PreviewPaths: 2)); source = null;
            await session.Completion.WaitAsync(TimeSpan.FromMinutes(3), cancellation);
            Assert.Equal(0UL, session.SourceCloseFailures); Assert.Equal(AnalysisPhase.Finished, session.Snapshot().Phase);
            Assert.Equal((ulong)(directories * 2 + 2), session.DatasetDescriptor!.Paths); Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, session.DatasetDescriptor.Coverage);
            var coverage = session.DatasetDescriptor.CoverageDetails; Assert.True(coverage.EnumerationKnown); Assert.True(coverage.Observations.DenominatorFinal);
            Assert.Equal((ulong)directories + 1, coverage.Observations.EnumerationRequested); Assert.Equal((ulong)directories + 1, coverage.Observations.EnumerationCompleted);
            Assert.Equal(0UL, coverage.Observations.EnumerationFailed); Assert.Equal(0UL, coverage.UnscheduledDirectories); Assert.Equal((ulong)directories, coverage.Observations.IdentityVerifiedPaths);
            reader = await session.AcquireDatasetAsync(cancellation); Assert.NotNull(reader);
            session.Dispose(); await session.DatasetCloseCompletion;
            Assert.Equal((UInt128)directories, (await reader.SummarizeAsync(AnalysisMetric.Files, cancellation)).Totals.Known);
            Assert.Equal((UInt128)directories, (await reader.SummarizeAsync(AnalysisMetric.FileSize, cancellation)).Totals.Known);
            Assert.Equal((UInt128)directories, (await reader.SummarizeAsync(AnalysisMetric.UniqueFileSize, cancellation)).Totals.Known);
            await using (var hierarchy = await reader.BuildLogicalHierarchyAsync(cancellation))
            {
                Assert.Equal((UInt128)directories, hierarchy.Totals.KnownBytes); Assert.Equal((ulong)directories, hierarchy.Totals.Files);
                var root = await hierarchy.ReadAsync(1, cancellation); Assert.Equal((ulong)directories, root.Totals.Files);
                ulong count = 0;
                for (ulong offset = 0;;)
                {
                    var page = await hierarchy.ReadChildrenAsync(1, AnalysisLogicalSort.FileSize, offset, cancellation: cancellation);
                    count += (ulong)page.Rows.Length;
                    foreach (var row in page.Rows.Where(r => r.Path.IncludedByPolicy)) { Assert.Equal((UInt128)1, row.Totals.KnownBytes); Assert.Equal(1UL, row.Totals.Files); }
                    if (!page.HasMore) break; offset = page.NextOffset;
                }
                Assert.Equal((ulong)directories + 1, count);
            }
            await using (var charges = await reader.BuildObjectChargesAsync(AnalysisMetric.UniqueFileSize, cancellation))
            {
                Assert.Equal((UInt128)directories, charges.Totals.Known); Assert.Equal((ulong)directories, charges.Totals.MatchingPaths);
                Assert.Equal((UInt128)0, charges.Totals.IdentityUnverified); Assert.Empty(charges.Totals.Missing);
                ulong count = 0;
                for (ulong offset = 0;;)
                {
                    var page = await charges.ReadAllChargesAsync(offset, cancellation: cancellation); count += (ulong)page.Rows.Length;
                    foreach (var charge in page.Rows) { Assert.False(charge.Shared); Assert.Equal(1UL, charge.ObservedAliases); Assert.Equal((UInt128)1, charge.Measurement.Value); }
                    if (!page.HasMore) break; offset = page.NextOffset;
                }
                Assert.Equal((ulong)directories, count);
            }
            ulong files = 0, folders = 0;
            for (ulong offset = 0; offset < reader.Descriptor.Paths;)
            {
                var page = await reader.ReadPathsAsync(offset, cancellation: cancellation);
                foreach (var path in page.Rows)
                {
                    if (path.Kind == AnalysisKind.File) { Assert.Equal("file.bin", path.Name); files++; }
                    if (path.Kind == AnalysisKind.Directory) folders++;
                    Assert.NotEqual("canary.txt", path.Name);
                }
                offset = page.NextOffset;
            }
            Assert.Equal((ulong)directories, files); Assert.Equal((ulong)directories + 2, folders);
            await reader.DisposeAsync(); reader = null;
            Assert.Equal(new[] { canary }, Directory.GetFiles(privatePath)); Assert.Equal("private frontier canary", File.ReadAllText(canary));
            for (int n = 0; n < directories; n++)
                Assert.Equal(new byte[] { (byte)(n % 251) }, File.ReadAllBytes(Path.Combine(sourcePath, "directory-" + n.ToString("D4"), "file.bin")));
        }
        finally
        {
            if (reader is not null) await reader.DisposeAsync();
            if (session is not null) { session.Dispose(); await session.Completion; await session.DatasetCloseCompletion; }
            if (source is not null) await AnalysisIo.ReleaseAsync(io, source);
            if (storage is not null) await AnalysisIo.Run(io, device, _ => { storage.Dispose(); return true; });
        }
    }

    internal static async Task VerifyReplacementAfterQueueing(string parent, Func<string, IAnalysisBackend> prepare, Func<string, AnalysisPrivateDirectory> openPrivate, CancellationToken cancellation)
    {
        string sourcePath = Path.Combine(parent, "racing-source"), childPath = Path.Combine(sourcePath, "queued"), retired = Path.Combine(parent, "retired");
        Directory.CreateDirectory(childPath); File.WriteAllText(Path.Combine(childPath, "original.txt"), "original remains preserved");
        using var io = new DeviceIoScheduler(); string device = FileCat.Core.FileSystem.PathUtil.GetDeviceKey(sourcePath);
        AnalysisPrivateDirectory? storage = await AnalysisIo.Run(io, device, _ => openPrivate(sourcePath), cancellation);
        IAnalysisBackend? source = null; AnalysisSession? session = null; AnalysisDatasetLease? reader = null; bool replaced = false;
        try
        {
            source = await AnalysisIo.Run(io, device, _ => new RacingBackend(prepare(sourcePath), () =>
            {
                Directory.Move(childPath, retired); Directory.CreateDirectory(childPath);
                File.WriteAllText(Path.Combine(childPath, "outside-canary.txt"), "replacement must never be traversed"); replaced = true;
            }), cancellation);
            session = new AnalysisCoordinator(io).Start([source], storage: new(device, () => Interlocked.Exchange(ref storage, null) ?? throw new InvalidOperationException("Private ownership transferred."),
                [new(device, () => new RacingBackend(prepare(Path.Combine(sourcePath, "private")), () => { }))], PreviewPaths: 2)); source = null;
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(30), cancellation); Assert.True(replaced); Assert.Equal(0UL, session.SourceCloseFailures);
            reader = await session.AcquireDatasetAsync(cancellation); Assert.NotNull(reader); Assert.Equal(NamespaceCoverage.Partial, reader.Descriptor.Coverage);
            var coverage = reader.Descriptor.CoverageDetails; Assert.True(coverage.EnumerationKnown); Assert.Equal(2UL, coverage.Observations.EnumerationRequested);
            Assert.Equal(1UL, coverage.Observations.EnumerationCompleted); Assert.Equal(1UL, coverage.Observations.EnumerationFailed);
            Assert.False(coverage.Observations.DenominatorFinal); Assert.Contains("1", coverage.Observations.StaleRootIds);
            for (ulong offset = 0; offset < reader.Descriptor.Paths;)
            {
                var page = await reader.ReadPathsAsync(offset, cancellation: cancellation);
                Assert.DoesNotContain(page.Rows, path => path.Name is "outside-canary.txt" or "original.txt"); offset = page.NextOffset;
            }
            var issues = await reader.ReadIssuesAsync(0, cancellation: cancellation); Assert.Contains(issues.Rows, issue => issue.Code == "source-changed-or-boundary");
            Assert.Equal("original remains preserved", File.ReadAllText(Path.Combine(retired, "original.txt")));
            Assert.Equal("replacement must never be traversed", File.ReadAllText(Path.Combine(childPath, "outside-canary.txt")));
        }
        finally
        {
            if (reader is not null) await reader.DisposeAsync();
            if (session is not null) { session.Dispose(); await session.Completion; await session.DatasetCloseCompletion; }
            if (source is not null) await AnalysisIo.ReleaseAsync(io, source);
            if (storage is not null) await AnalysisIo.Run(io, device, _ => { storage.Dispose(); return true; });
        }
        Assert.Empty(Directory.GetFiles(Path.Combine(sourcePath, "private")));
    }
    private sealed class RacingBackend(IAnalysisBackend inner, Action replace) : IAnalysisBackend
    {
        private int _rootChecks;
        public RootBinding Root => inner.Root;
        public AnalysisEntry RootEntry => inner.RootEntry;
        public string Basis => inner.Basis;
        public string IoDeviceKey => inner.IoDeviceKey;
        public IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation) => inner.Children(directory, cancellation);
        public void ValidateReadScope(AnalysisEntry directory)
        {
            inner.ValidateReadScope(directory);
            // The root's last batch has passed its native post-check. Change the pending child in
            // the gap before the coordinator stores and later rehydrates its old read witness.
            if (directory.RelativeName.IsEmpty && ++_rootChecks == 2) replace();
        }
        public AnalysisRootContainment ContainsRoot(IAnalysisBackend other) => inner.ContainsRoot(other is RacingBackend racing ? racing.Inner : other);
        private IAnalysisBackend Inner => inner;
        public void Dispose() => inner.Dispose();
    }

    internal static async Task VerifyPrivateFileRoot(string parent, Func<string, IAnalysisBackend> prepare, Func<string, AnalysisPrivateDirectory> openPrivate, CancellationToken cancellation)
    {
        using var io = new DeviceIoScheduler(); string device = FileCat.Core.FileSystem.PathUtil.GetDeviceKey(parent);
        AnalysisPrivateDirectory? storage = await AnalysisIo.Run(io, device, _ => openPrivate(parent), cancellation);
        string privatePath = Path.Combine(parent, "private"), canary = Path.Combine(privatePath, "private-inventory.jsonl");
        const string contents = "private inventory canary"; File.WriteAllText(canary, contents);
        IAnalysisBackend? source = null; AnalysisSession? session = null; AnalysisDatasetLease? reader = null;
        try
        {
            source = await AnalysisIo.Run(io, device, _ => prepare(canary), cancellation);
            session = new AnalysisCoordinator(io).Start([source], storage: new(device,
                () => Interlocked.Exchange(ref storage, null) ?? throw new InvalidOperationException("Private ownership transferred."), [new(device, () => prepare(privatePath))])); source = null;
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(30), cancellation);
            reader = await session.AcquireDatasetAsync(cancellation); Assert.NotNull(reader);
            var root = Assert.Single((await reader.ReadPathsAsync(0, cancellation: cancellation)).Rows);
            Assert.Equal(MeasurementState.Excluded, root.MetadataState); Assert.False(root.IncludedByPolicy); Assert.Null(root.ObservedIdentity);
            Assert.Null(root.Times!.ModifiedUtc); Assert.Null(reader.Descriptor.Roots[0].ObservedObjectIdentity);
            var obj = Assert.Single((await reader.ReadObjectsAsync(0, cancellation: cancellation)).Rows);
            Assert.Null(obj.VerifiedIdentity); Assert.Null(obj.ModifiedUtc); Assert.Equal(AnalysisMeasurement.Missing(MeasurementState.Excluded), obj.LogicalLength);
            foreach (var metric in Enum.GetValues<AnalysisMetric>())
            { var total = (await reader.SummarizeAsync(metric, cancellation)).Totals; Assert.Equal((UInt128)0, total.Known); Assert.Equal(0UL, total.MatchingPaths); }
            Assert.Contains((await reader.ReadIssuesAsync(0, cancellation: cancellation)).Rows, issue => issue.Code == "private-analysis-state-excluded");
        }
        finally
        {
            if (reader is not null) await reader.DisposeAsync();
            if (session is not null) { session.Dispose(); await session.Completion; await session.DatasetCloseCompletion; }
            if (source is not null) await AnalysisIo.ReleaseAsync(io, source);
            if (storage is not null) await AnalysisIo.Run(io, device, _ => { storage.Dispose(); return true; });
        }
        Assert.Equal(contents, File.ReadAllText(canary)); Assert.Equal(new[] { canary }, Directory.GetFiles(privatePath));
    }
}
