using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;
using FileCat.Core.Selection;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PanelDisplayFilterRetainsFullInventoryAndResolvedViewAcrossNativeReaders(bool indexed)
    {
        OrdinaryPreparation();
        using var fixture = new PreparationFixture();
        var token = TestContext.Current.CancellationToken;
        string input = Directory.CreateDirectory(Path.Combine(fixture.Root, "input")).FullName;
        File.WriteAllText(Path.Combine(input, "keep.txt"), "12345");
        File.WriteAllText(Path.Combine(input, "skip.txt"), "12345678");
        File.WriteAllText(Path.Combine(input, "other.bin"), "1234");

        string rules = "*.txt|skip.txt";
        Assert.True(Mask.TryParse("@candidate", name => name == "candidate" ? rules : null, out var mask, out _));
        string? captured = AnalysisPanelFilter.Capture(mask);
        Assert.NotNull(captured);
        rules = "*.bin";
        using var model = new DiskSpaceViewModel(fixture.Services, indexedStorage: indexed)
        { OfferedPanelFilter = captured, Metric = AnalysisMetric.FileSize };
        try
        {
            await model.StartAsync([input], usePanelFilter: true);
            await Until(() => model.Phase == AnalysisPhase.Finished, timeout: TimeSpan.FromSeconds(25));
            await model.RebuildAsync(true, token);
            Assert.True(model.PanelFilterApplied);
            var snapshot = model.Tree!.Snapshot;
            Assert.True(snapshot.Policy.Exclusions.All(rule => !rule.Contains("candidate", StringComparison.Ordinal)));
            Assert.Equal((UInt128)5, model.Tree.Projection.Totals.Known);
            Assert.Equal((UInt128)17, AnalysisAggregate.Project(snapshot, AnalysisMetric.FileSize).Totals.Known);
            Assert.Contains(snapshot.Nodes, node => node.Name == "skip.txt" && node.IncludedByPolicy);
            Assert.Contains(snapshot.Nodes, node => node.Name == "other.bin" && node.IncludedByPolicy);

            if (indexed)
            {
                await using var dataset = await model.AcquireRecordedDatasetAsync(token);
                await using var view = await AnalysisDatasetExplorer.CreateAsync(dataset, AnalysisMetric.FileSize,
                    new AnalysisFilterRequest("") { PanelFilter = model.PublishedReportView.PanelFilter }, token);
                Assert.Equal(1UL, view.Filter!.DefinitePaths);
                Assert.Contains((await dataset.ReadPathsAsync(0, cancellation: token)).Rows, row => row.Name == "other.bin");
            }
            else
            {
                var receipt = await model.SaveNewAsync("panel-display.fcdsa", token);
                using var catalog = await model.ReadSavedCatalogAsync(token);
                var entry = Assert.Single(catalog.Entries);
                Assert.Equal(receipt.InventoryId, entry.InventoryId);
                Assert.Equal(captured, entry.View.PanelFilter);
                using var offline = await model.OpenSavedAsync(entry.Name, entry, token);
                try
                {
                    await offline.RebuildAsync(true, token);
                    Assert.Equal((UInt128)5, offline.Tree!.Projection.Totals.Known);
                    offline.PanelFilter = null;
                    await offline.RebuildAsync(true, token);
                    Assert.Equal((UInt128)17, offline.Tree.Projection.Totals.Known);
                }
                finally { offline.Dispose(); await offline.CloseCompletion; }
            }
        }
        finally { model.Dispose(); await model.CloseCompletion; }
    }

    [AvaloniaFact]
    public void PanelDisplayFilterRejectsRegularExpressionsInCapturedViewData()
    {
        Assert.True(Mask.TryParse("/test/", out var mask, out _));
        Assert.Null(AnalysisPanelFilter.Capture(mask));
        var injected = mask.CaptureSnapshot()!.ToJsonString();
        Assert.Throws<FormatException>(() => AnalysisPanelFilter.Restore(injected));
    }
}
