using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisMetric.OnDisk, "Treemap", false)]
    [InlineData(AnalysisMetric.OnDisk, "Sunburst", false)]
    [InlineData(AnalysisMetric.UniqueFileSize, "Treemap", false)]
    [InlineData(AnalysisMetric.UniqueFileSize, "Sunburst", false)]
    [InlineData(AnalysisMetric.OnDisk, "Treemap", true)]
    [InlineData(AnalysisMetric.OnDisk, "Sunburst", true)]
    [InlineData(AnalysisMetric.UniqueFileSize, "Treemap", true)]
    [InlineData(AnalysisMetric.UniqueFileSize, "Sunburst", true)]
    public async Task UnmeasuredOtherNativeSavedWindowKeepsSharedAliasesAndExcludesMeasuredZero(AnalysisMetric metric, string mode, bool filtered)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        var saved = AnalysisSavedView.Default with { Metric = metric, Scope = AnalysisTree.OtherKey("n:1", 1), Chart = mode,
            Query = filtered ? "name:source.cs OR name:zero.bin" : "" };
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false)) { }
        string path = profile.Services.Paths.AnalysisSnapshotsDirectory, absent = Path.Combine(profile.Root, "source-must-not-be-created");
        var entry = await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(path), cancellation =>
        {
            var snapshot = Fixture(); snapshot = snapshot with
            {
                Nodes = [snapshot.Nodes[0], snapshot.Nodes[3] with { ParentId = 1 }, snapshot.Nodes[4] with { ParentId = 1, ObjectId = "a" }, snapshot.Nodes[5] with { ParentId = 1, Name = "zero.bin" }],
                Objects = [snapshot.Objects[0] with { LogicalLength = metric == AnalysisMetric.UniqueFileSize ? AnalysisMeasurement.Missing(MeasurementState.Unsupported) : AnalysisMeasurement.Known(100), LinkCount = AnalysisMeasurement.Known(2) },
                    snapshot.Objects[2] with { VerifiedIdentity = "device:c", LogicalLength = AnalysisMeasurement.Known(0) }],
                Facts = [snapshot.Facts[0] with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Unsupported) }, snapshot.Facts[2] with { Measurement = AnalysisMeasurement.Known(0) }],
                Roots = [snapshot.Roots[0] with { CanonicalLocator = absent }]
            };
            using AnalysisPrivateDirectory directory = OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path)) : new UnixAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path));
            using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); var catalog = new AnalysisSavedCatalog(store);
            catalog.SaveNew("unmeasured.fcdsa", snapshot, Guid.NewGuid(), saved, cancellation); using var entries = catalog.Read(cancellation); return Assert.Single(entries.Entries);
        }, token);
        byte[] before = File.ReadAllBytes(Path.Combine(path, entry.Name)); var window = new DiskSpaceWindow();
        try
        {
            var explorer = await window.AddSavedIndexedSessionAsync(profile.Services, entry.Name, entry, token); window.Show(); Frame(window);
            Assert.True(ExactOtherReady(window)); Assert.Equal(1, explorer.GeometrySkipped);
            Assert.Equal(new ulong[] { 4, 5 }, explorer.Page!.Children.Rows.Select(row => row.Path.Id).Order());
            Assert.All(explorer.Page.Children.Rows, row =>
            {
                var item = AnalysisIndexedChart.Row(row, metric);
                Assert.True(item.Unknown);
                Assert.DoesNotContain("coverage partial", DiskSpaceViewModel.FilePathsText(item));
                var partial = AnalysisIndexedChart.Row(row with { NamespaceIncomplete = true }, metric);
                Assert.Equal(DiskSpaceViewModel.FilePathsText(item) + " observed (coverage partial)", DiskSpaceViewModel.FilePathsText(partial));
            });
            var bucket = Assert.Single(explorer.Page.Buckets); Assert.Equal(AnalysisHierarchyBucketKind.SharedFiles, bucket.Kind); Assert.NotEmpty(bucket.Totals.Missing);
            Assert.NotNull(bucket.ReferenceLogical); Assert.Equal(filtered ? 1UL : 2UL, bucket.ReferenceLogical.Files);
            Assert.Equal(metric == AnalysisMetric.UniqueFileSize ? bucket.ReferenceLogical.Files : 0UL, bucket.ReferenceLogical.UnknownFiles);
            Click(window, "Shared hard-linked files · Unknown");
            Assert.Contains("Matching reference paths: " + (filtered ? "1" : "2") + " references", IndexedDetails(window).Text);
            Assert.Contains("(references)", IndexedDetails(window).Text);
            Assert.Equal((UInt128)0, explorer.Chart!.ScopeValue(explorer.Chart.Scope)); Assert.True(explorer.Chart.Items[explorer.Chart.Scope].Unknown);
            Assert.True(IndexedSelectionCount(window, 0, 0)); Click(window, "Mark matching files in this scope");
            await Until(() => IndexedSelectionCount(window, filtered ? 1 : 2, 0));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(path, entry.Name))); Assert.False(Directory.Exists(absent));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }
}
