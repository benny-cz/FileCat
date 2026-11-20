using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    public async Task OtherSubsetNativeCaptureRetainsGeometryMembershipThroughEveryOrderAndAllProducerClosures(AnalysisMetric metric)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync(sharedAlias: true);
        var token = TestContext.Current.CancellationToken; string path = profile.Services.Paths.AnalysisSnapshotsDirectory;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            await prepared.SaveInventoryAsync(source.Lease!, "captured.fcdsa", AnalysisSavedView.Default, token);
        using var inventory = await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(path), cancellation =>
        {
            using AnalysisPrivateDirectory directory = OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path)) : new UnixAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path));
            using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); return store.Load("captured.fcdsa", cancellation);
        }, token);
        var snapshot = inventory.Snapshot; ulong root = snapshot.Nodes.Single(node => node.ParentId == 0).Id;
        var tree = AnalysisTree.Build(snapshot, AnalysisAggregate.Project(snapshot, metric, cancellation: token), false, token);
        var hierarchy = await source.Lease!.BuildHierarchyAsync(metric, token); var subset = await hierarchy.CreateSubsetAsync(root, 130, token);
        await hierarchy.DisposeAsync(); await source.ReleaseInitialLeaseAsync();
        try
        {
            string key = AnalysisTree.OtherKey(AnalysisTree.Key(root), 130);
            var expected = tree.ChildrenOf(key).Where(item => item.NodeId.HasValue).Select(item => item.NodeId!.Value).ToHashSet();
            foreach (var sort in Enum.GetValues<AnalysisHierarchySort>()) foreach (bool descending in new[] { false, true })
            {
                ulong offset = 0; var rows = new HashSet<ulong>();
                while (offset < subset.Rows)
                {
                    var page = await subset.ReadChildrenAsync(sort, offset, 53, token, descending); Assert.NotEmpty(page.Rows);
                    foreach (var row in page.Rows) Assert.True(rows.Add(row.Path.Id)); offset = page.NextOffset;
                }
                Assert.True(expected.SetEquals(rows));
            }
            Assert.Equal(tree.ScopeValue(key), AnalysisGroupedChart.Area(subset.Totals, metric));
            Assert.Equal(tree.CaptureMatchingFiles(key, token).NodeIds, (await subset.CaptureMatchingFilesAsync(cancellation: token)).NodeIds);
            Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
        }
        finally { await subset.DisposeAsync(); await hierarchy.DisposeAsync(); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(source.PrivatePath));
    }
}
