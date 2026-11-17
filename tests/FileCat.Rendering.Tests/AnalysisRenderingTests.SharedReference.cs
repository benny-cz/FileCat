using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisMetric.UniqueFileSize, "Treemap", false)]
    [InlineData(AnalysisMetric.UniqueFileSize, "Sunburst", false)]
    [InlineData(AnalysisMetric.OnDisk, "Treemap", false)]
    [InlineData(AnalysisMetric.OnDisk, "Sunburst", false)]
    [InlineData(AnalysisMetric.UniqueFileSize, "Treemap", true)]
    [InlineData(AnalysisMetric.UniqueFileSize, "Sunburst", true)]
    [InlineData(AnalysisMetric.OnDisk, "Treemap", true)]
    [InlineData(AnalysisMetric.OnDisk, "Sunburst", true)]
    public async Task FolderSharedReferenceNativeWindowShowsMatchingAliasesWithoutAddingTheirStatisticsTwice(AnalysisMetric metric, string mode, bool filtered)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var source = await IndexedFixture.CreateAsync(sharedAlias: true);
        var token = TestContext.Current.CancellationToken; var window = new DiskSpaceWindow { Width = 1180, Height = 960 };
        var explorer = await window.AddIndexedSessionAsync(source.Lease!, metric, filtered ? new("name:match-target.bin") : null, token); await source.ReleaseInitialLeaseAsync();
        window.Show(); Frame(window);
        try
        {
            var list = IndexedList(window); var root = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>()); list.SelectedItem = root;
            Click(window, "Explore selected folder"); await Until(() => explorer.Page?.Scope?.Path.Id == root.Path.Id && IndexedReady(window, explorer, root.Path.Id));
            IndexedPersistenceControl<ComboBox>(window, "Indexed chart mode").SelectedItem = mode; Frame(window);
            var bucket = Assert.Single(explorer.Page!.Buckets, item => item.Kind == AnalysisHierarchyBucketKind.SharedFiles);
            Assert.NotNull(bucket.ReferenceLogical); ulong files = filtered ? 1UL : 2UL;
            Assert.Equal(files, bucket.ReferenceLogical.Files); Assert.Equal((UInt128)source.Payload.Length * files, bucket.ReferenceLogical.KnownBytes); Assert.Equal(0UL, bucket.ReferenceLogical.UnknownFiles);
            var projection = AnalysisIndexedChart.Bucket(bucket, metric); Assert.Equal(0UL, projection.Files); Assert.Equal((UInt128)0, projection.LogicalSize);
            Assert.Equal(files + " references", DiskSpaceViewModel.FilePathsText(projection));
            var inspect = window.GetVisualDescendants().OfType<Button>().Single(button => AutomationLabel(button) == "Inspect Shared hard-linked files");
            Click(window, inspect.Content!.ToString()!); Assert.Contains("Matching reference paths: " + files + " references", IndexedDetails(window).Text);
            Assert.Contains(bucket.ReferenceLogical.KnownBytes + " bytes (references)", IndexedDetails(window).Text);
            var chart = explorer.Chart!; var actualChildren = chart.ChildrenOf(chart.Scope);
            Assert.Equal(chart.Items[chart.Scope].Value, actualChildren.Aggregate((UInt128)0, (sum, item) => checked(sum + item.Value)));
            // A remainder carries omitted logical/count statistics. A chart with
            // only the shared charge can omit every zero-area namespace row.
            if (chart.Remainders.Count != 0)
            {
                Assert.Equal(chart.Items[chart.Scope].LogicalSize, actualChildren.Aggregate((UInt128)0, (sum, item) => checked(sum + item.LogicalSize)));
                Assert.Equal(chart.Items[chart.Scope].Files, actualChildren.Aggregate(0UL, (sum, item) => checked(sum + item.Files)));
            }
            Assert.Single(window.GetVisualDescendants().OfType<AnalysisChartControl>(), control => control.IsEffectivelyVisible);
            Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(source.PrivatePath));
    }
}
