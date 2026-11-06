using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task NavigationLinksAndSpecialEntriesDoNotMasqueradeAsMeasuredZeroFiles()
    {
        using var styles = new ProductStyles(); var original = Fixture();
        var snapshot = original with
        {
            Nodes = original.Nodes.Add(new(7, "r", 1, "link-outside", AnalysisKind.Link))
                .Add(new(8, "r", 1, "named-pipe", AnalysisKind.Special)).Add(new(9, "r", 1, "zero.bin", AnalysisKind.File, "zero")),
            Objects = original.Objects.Add(new("zero", "device:zero", AnalysisMeasurement.Known(0), AnalysisMeasurement.Known(1))),
            Facts = original.Facts.Add(new("zero", "zero", null, "primary", "fixture", AnalysisMeasurement.Known(0), "fixture", DateTimeOffset.UnixEpoch, []))
        };
        foreach (var metric in Enum.GetValues<AnalysisMetric>())
        {
            using var baseline = new DiskSpaceViewModel(original) { Metric = metric }; await baseline.RebuildAsync(true);
            using var model = new DiskSpaceViewModel(snapshot) { Metric = metric }; await model.RebuildAsync(true);
            Assert.Equal(baseline.Tree!.ScopeValue(AnalysisTree.RootKey) + (metric == AnalysisMetric.Files ? (UInt128)1 : 0), model.Tree!.ScopeValue(AnalysisTree.RootKey));
            model.Focus("n:1", open: true); await Until(() => model.Selection.Scope == "n:1" && model.Rows.Length == 5);
            var link = model.Tree.Items["n:7"]; var special = model.Tree.Items["n:8"]; var zero = model.Tree.Items["n:9"];
            Assert.Equal("Link not followed", DiskSpaceViewModel.StatusText(link)); Assert.Equal("Special entry not traversed", DiskSpaceViewModel.StatusText(special));
            Assert.Equal("Not measured", DiskSpaceViewModel.LogicalText(link, true)); Assert.Equal("Not measured", DiskSpaceViewModel.LogicalText(special));
            Assert.Equal(metric == AnalysisMetric.Files ? "Not counted" : "Not measured", DiskSpaceViewModel.ValueText(link, metric, true));
            Assert.Equal("Observed", DiskSpaceViewModel.StatusText(zero)); Assert.Equal("0 bytes", DiskSpaceViewModel.LogicalText(zero, true));
            Assert.Equal(metric == AnalysisMetric.Files ? "1" : "0 bytes", DiskSpaceViewModel.ValueText(zero, metric, true));
            // Namespace boundaries do not turn a complete metric into Unknown or change any charge.
            Assert.False(link.Unknown); Assert.False(special.Unknown);
            foreach (var sort in new[] { AnalysisTableSort.ActiveMetric, AnalysisTableSort.FileSize, AnalysisTableSort.FilePaths })
            foreach (bool descending in new[] { false, true })
            {
                using var order = AnalysisTableOrder.Apply(model.Rows, sort, descending);
                Assert.All(order.Rows.Take(3), row => Assert.False(row.IsNonFileLeaf));
                Assert.All(order.Rows.Skip(3), row => Assert.True(row.IsNonFileLeaf));
            }
            // Synthetic accounting groups use Special as their storage kind and keep their real measure.
            var group = special with { Synthetic = true, Value = 42 };
            Assert.DoesNotContain("Not measured", DiskSpaceViewModel.ValueText(group, AnalysisMetric.OnDisk, true));

            var window = new DiskSpaceWindow { Height = 960 }; window.AddSession(model); window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var row = window.GetVisualDescendants().OfType<Grid>().Single(c => AutomationProperties.GetName(c)?.StartsWith("link-outside;", StringComparison.Ordinal) == true);
                Assert.Contains("Not measured", AutomationProperties.GetName(row)); Assert.Contains("Link not followed", AutomationProperties.GetName(row));
                var tip = Assert.IsType<RichTip>(ToolTip.GetTip(row)); Assert.Contains("target was not followed or measured", tip.Text); Assert.DoesNotContain("Known active value", tip.Text);
                model.Focus("n:7"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), c => c.Text?.Contains("target was not followed or measured", StringComparison.Ordinal) == true);
            }
            finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        }
    }
}
