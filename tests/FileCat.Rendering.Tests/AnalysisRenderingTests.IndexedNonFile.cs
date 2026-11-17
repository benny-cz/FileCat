using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task IndexedNamespaceRowsExplainNontraversalAndKeepEveryColumnInsideThePane()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync(namespaceEntry: true);
        var window = new DiskSpaceWindow { Width = 1180, Height = 900, FontSize = 20 };
        var explorer = await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.OnDisk, cancellation: TestContext.Current.CancellationToken);
        window.Show(); Frame(window);
        try
        {
            var list = IndexedList(window); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>());
            Click(window, "Explore selected folder"); await Until(() => IndexedReady(window, explorer, 1)); Frame(window);
            while (!list.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Name == "namespace-link"))
            {
                ulong previous = explorer.Page!.Children.Offset; Assert.True(explorer.Page.Children.HasMore);
                Click(window, "Next entries"); await Until(() => explorer.Page!.Children.Offset > previous && IndexedReady(window, explorer, 1)); Frame(window);
            }
            var link = list.Items.OfType<AnalysisHierarchyRow>().Single(row => row.Path.Name == "namespace-link");
            list.SelectedItem = link; list.ScrollIntoView(link);
            await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(text => AutomationLabel(text) == "Captured metadata inspector" && text.Text?.Contains("target was not followed or measured", StringComparison.Ordinal) == true));
            foreach (int width in new[] { 1180, 380 })
            {
                window.Width = width; Frame(window); list.ScrollIntoView(link); Frame(window);
                var grid = window.GetVisualDescendants().OfType<Grid>().Single(control => AutomationLabel(control)?.StartsWith("namespace-link;", StringComparison.Ordinal) == true);
                string name = AutomationLabel(grid)!; Assert.Contains("Not counted", name); Assert.Contains("Link not followed", name); Assert.DoesNotContain("; 0;", name);
                Assert.True(grid.Bounds.Height >= 32); Assert.True(grid.Bounds.Width <= list.Bounds.Width, $"Row {grid.Bounds.Width}, pane {list.Bounds.Width}.");
                foreach (var cell in grid.Children.OfType<TextBlock>()) Assert.True(cell.Bounds.Right <= grid.Bounds.Width + 0.5);
                var filePaths = Assert.Single(grid.Children.OfType<TextBlock>(), cell => cell.Text?.Contains("Not counted", StringComparison.Ordinal) == true);
                Assert.Equal(2, Grid.GetRow(filePaths)); Assert.StartsWith("File paths: ", filePaths.Text);
                Assert.True(filePaths.Bounds.Width >= 100, $"File-path evidence is squeezed into {filePaths.Bounds.Width} DIP at {width} DIP/20-DIP text.");
                Assert.Contains("target was not followed or measured", Assert.IsType<RichTip>(ToolTip.GetTip(grid)).Text);
            }
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
