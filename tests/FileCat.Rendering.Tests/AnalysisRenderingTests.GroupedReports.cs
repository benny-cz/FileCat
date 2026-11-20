using System.Collections.Immutable;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task NativeGroupedReportsMatchMemoryAndIndexedScopesAfterAllOriginalOwnersCloseAndSourceMovesOffline(int variant)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync(sharedAlias: true, extraExtensions: 4);
        var token = TestContext.Current.CancellationToken; var lease = source.Lease!; string folder = Path.Combine(source.Root, "reports"); Directory.CreateDirectory(folder);
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
            await prepared.ExportInventoryAsync(lease, folder, "source.fcdsa", token);
        using var input = File.OpenRead(Path.Combine(folder, "source.fcdsa")); using var inventory = AnalysisInventoryFormat.Read(input, token); using var owner = inventory.Retain();
        var snapshot = owner.Snapshot; var projection = AnalysisAggregate.Project(snapshot, AnalysisMetric.UniqueFileSize, cancellation: token);
        var tree = variant == 6 ? AnalysisTree.Build(snapshot, projection, false, token) : AnalysisTree.BuildGrouped(snapshot, projection, AnalysisGroupingDimension.Extension, false, token);
        AnalysisDatasetGroupExplorer? groups = null; AnalysisDatasetExplorer? folders = null;
        AnalysisCapturedReport? indexed = null; AnalysisCapturedMemoryReport? memory = null;
        try
        {
            string parent; string? group = null; ulong directory = 0;
            if (variant == 6)
            {
                var root = (await lease.ReadPathsAsync(0, cancellation: token)).Rows.Single(path => path.ParentId == 0); directory = root.Id; parent = AnalysisTree.Key(directory);
                var initial = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.UniqueFileSize, cancellation: token);
                try { folders = await initial.CreateScopeAsync(directory, 1, token); } finally { await initial.DisposeAsync(); }
                indexed = await lease.CaptureViewReportAsync(folders, directory, AnalysisReportFormat.Csv, cancellation: token);
            }
            else
            {
                var initial = await AnalysisDatasetGroupExplorer.CreateAsync(lease, AnalysisMetric.UniqueFileSize, AnalysisGroupingDimension.Extension, cancellation: token);
                try
                {
                    var overview = await initial.ReadGroupsAsync(cancellation: token);
                    if (variant is 1 or 3) group = overview.Rows.Single(value => value.Kind == AnalysisGroupKind.Entries && value.Definition.Value.Contains("bin", StringComparison.Ordinal)).Definition.Key;
                    if (variant is 4 or 5) group = overview.Rows.Single(value => value.Kind == AnalysisGroupKind.SharedAcrossGroups).Definition.Key;
                    groups = await initial.CreateScopeAsync(group, variant is 2 or 3 or 5 ? 1 : 0, token);
                }
                finally { await initial.DisposeAsync(); }
                parent = group ?? AnalysisTree.RootKey; indexed = await lease.CaptureViewReportAsync(groups, group, AnalysisReportFormat.Csv, cancellation: token);
            }
            string scope = variant is 2 or 3 or 5 or 6 ? AnalysisTree.OtherKey(parent, 1) : parent;
            memory = await AnalysisCapturedMemoryReport.CaptureAsync(owner, tree, profile.Services.Io, AnalysisReportFormat.Csv, AnalysisReportScope.FilteredView, scope, cancellation: token);
            Assert.Equal(indexed.Header.ViewKind, memory.Header.ViewKind); Assert.Equal(indexed.Header.Rows, memory.Header.Rows);
            Assert.Equal(indexed.Header.Totals.Known, memory.Header.Totals.Known); Assert.Equal(indexed.Header.Totals.IdentityUnverified, memory.Header.Totals.IdentityUnverified);
            Assert.Equal(indexed.Header.Logical.KnownBytes, memory.Header.Logical.KnownBytes); Assert.Equal(indexed.Header.Logical.Files, memory.Header.Logical.Files);
            if (variant is 4 or 5) { Assert.True(indexed.Header.IncludesAccountingReferences); Assert.Equal((UInt128)6, indexed.Header.Logical.KnownBytes); }
            if (variant == 5) Assert.Equal((UInt128)0, indexed.Header.Totals.Known);
            if (groups is not null) { await groups.DisposeAsync(); groups = null; }
            if (folders is not null) { await folders.DisposeAsync(); folders = null; }
            owner.Dispose(); inventory.Dispose(); input.Dispose(); await source.ReleaseInitialLeaseAsync();
            string live = Path.Combine(source.Root, "source"), offline = Path.Combine(source.Root, "source-offline"), prefix = Path.GetFullPath(source.Root) + Path.DirectorySeparatorChar;
            Assert.StartsWith(prefix, Path.GetFullPath(live)); Assert.StartsWith(prefix, Path.GetFullPath(offline)); Directory.Move(live, offline);
            try
            {
                foreach (var pair in new[] { (Name: "indexed.csv", Report: indexed), (Name: "memory.csv", Report: (AnalysisCapturedReport)memory) })
                {
                    await using var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token);
                    await prepared.ExportReportAsync(pair.Report, folder, pair.Name, false, token);
                }
                var left = CapturedCsv(File.ReadAllText(Path.Combine(folder, "indexed.csv"))); var right = CapturedCsv(File.ReadAllText(Path.Combine(folder, "memory.csv")));
                Assert.Equal((int)indexed.Header.Rows + 2, left.Count); Assert.Equal(left.Count, right.Count);
                string[] fields = ["item", "display_location", "entry_kind", "active_known", "identity_unverified", "active_missing", "file_size_known_bytes", "file_size_missing", "file_paths", "namespace_incomplete", "charges_elsewhere", "filter_context"];
                int[] indexes = fields.Select(name => Array.IndexOf(left[0], name)).ToArray();
                string[] Values(List<string[]> rows) => rows.Skip(2).Select(row => string.Join('\u001f', indexes.Select(index => row[index]))).Order(StringComparer.Ordinal).ToArray();
                Assert.Equal(Values(left), Values(right)); Assert.False(Directory.Exists(live));
            }
            finally { Directory.Move(offline, live); }
            await indexed.DisposeAsync(); indexed = null; await memory.DisposeAsync(); memory = null;
            Assert.Empty(Directory.GetFiles(source.PrivatePath)); Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
        }
        finally
        {
            try { if (indexed is not null) await indexed.DisposeAsync(); } finally
            { try { if (memory is not null) await memory.DisposeAsync(); } finally { try { if (groups is not null) await groups.DisposeAsync(); } finally { if (folders is not null) await folders.DisposeAsync(); } } }
        }
    }

    [AvaloniaFact]
    public async Task NativeGroupedReportRetainsProviderBasesAndRejectsAnotherSourceRevision()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync(sharedAlias: true);
        await using var other = await NativeNamesFixture.CreateAsync(true); var token = TestContext.Current.CancellationToken;
        await using var groups = await AnalysisDatasetGroupExplorer.CreateAsync(source.Lease!, AnalysisMetric.OnDisk, AnalysisGroupingDimension.Extension, cancellation: token);
        int handles = AnalysisNativeHandles.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => other.Lease!.CaptureViewReportAsync(groups, null, AnalysisReportFormat.Csv, cancellation: token));
        Assert.Equal(handles, AnalysisNativeHandles.Count);
        await using var report = await source.Lease!.CaptureViewReportAsync(groups, null, AnalysisReportFormat.Csv, cancellation: token);
        Assert.NotNull(report.Header.MeasurementBases); Assert.False(report.Header.MeasurementBases.DetailLimited);
        var facts = await source.Lease.ReadFactsAsync(0, 512, token);
        Assert.Equal(facts.Rows.Select(fact => fact.BasisId).Distinct().Order(StringComparer.Ordinal), report.Header.MeasurementBases.Labels);
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath)); other.AssertPayloads();
    }

    [AvaloniaFact]
    public async Task NativeGroupedReportCloseWaitsForActualOutputAndThenReleasesItsIndependentScope()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync(sharedAlias: true);
        var token = TestContext.Current.CancellationToken; var groups = await AnalysisDatasetGroupExplorer.CreateAsync(source.Lease!, AnalysisMetric.UniqueFileSize, AnalysisGroupingDimension.Extension, cancellation: token);
        var report = await source.Lease!.CaptureViewReportAsync(groups, null, AnalysisReportFormat.Csv, cancellation: token);
        await groups.DisposeAsync(); await source.ReleaseInitialLeaseAsync(); string folder = Path.Combine(source.Root, "reports"); Directory.CreateDirectory(folder);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); using var release = new ManualResetEventSlim();
        var output = report.ExportNewAsync("canceled.csv", PathUtil.GetDeviceKey(folder), () => new ExportFaultDestination(
            OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
                : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "report-staging")), "cancel", entered, release), true, token);
        Task? close = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token); close = report.DisposeAsync().AsTask(); Assert.False(close.IsCompleted); Assert.False(output.IsCompleted);
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => output); await close;
            Assert.Empty(Directory.GetFiles(folder)); Assert.Empty(Directory.GetFiles(source.PrivatePath)); Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
        }
        finally
        {
            release.Set(); try { await output; } catch (Exception error) when (error is IOException or OperationCanceledException) { }
            await (close ?? report.DisposeAsync().AsTask()); await report.DisposeAsync();
        }
    }
}
