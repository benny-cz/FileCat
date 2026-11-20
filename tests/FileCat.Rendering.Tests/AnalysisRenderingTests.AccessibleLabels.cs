using Avalonia.Automation;
using Avalonia.Automation.Peers;
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
    // Stable test identifiers must not replace the spoken text of a live region.
    private static string? AutomationLabel(Control control) => AutomationProperties.GetName(control) is { Length: > 0 } name
        ? name : AutomationProperties.GetAutomationId(control);

    [AvaloniaFact]
    public async Task ChartScopeLabelsNameTheirMetricUnitsAndUnknownAmounts()
    {
        using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture());
        await model.RebuildAsync(true);
        var tree = model.Tree!;
        Assert.Equal(DiskSpaceViewModel.Bytes(32768), AnalysisChartControl.ScopeAmount(tree, "n:3", exact: false));
        Assert.Equal("Unknown", AnalysisChartControl.ScopeAmount(tree, "n:6", exact: false));
        Assert.True(tree.Items[AnalysisTree.RootKey].Unknown);
        Assert.EndsWith(" known", AnalysisChartControl.ScopeAmount(tree, AnalysisTree.RootKey, exact: true));
        Assert.Equal(DiskSpaceViewModel.Bytes(tree.ScopeValue(AnalysisTree.RootKey)),
            AnalysisChartControl.ScopeAmount(tree, AnalysisTree.RootKey, exact: false));
        foreach (var chart in new AnalysisChartControl[] { new TreemapControl(), new SunburstControl() })
        {
            chart.SetView(tree, "n:3", null, []);
            string label = AutomationProperties.GetName(chart)!;
            Assert.Contains("On disk — reported:", label);
            Assert.Contains(" bytes", label);
            chart.SetView(tree, "n:6", null, []);
            Assert.Contains("On disk — reported: Unknown", AutomationProperties.GetName(chart));
        }
        model.Metric = AnalysisMetric.Files;
        await model.RebuildAsync(true);
        Assert.Equal("1 file path", AnalysisChartControl.ScopeAmount(model.Tree!, "n:3", exact: false));
    }

    [AvaloniaFact]
    public async Task LiveAnalysisStatusSpeaksItsUpdatedTextRatherThanItsStableTestIdentifier()
    {
        using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture());
        await model.RebuildAsync(true); var window = new DiskSpaceWindow();
        try
        {
            window.AddSession(model); window.Show(); Frame(window);
            var status = window.GetVisualDescendants().OfType<TextBlock>().Single(text => AutomationProperties.GetAutomationId(text) == "Analysis status");
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetName(status)));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            var peer = new TextBlockAutomationPeer(status);
            model.SetStatus("Scan stopped. The recorded partial result is kept."); Frame(window);
            Assert.Equal(status.Text, peer.GetName());
            Assert.Contains("recorded partial result", peer.GetName());
            model.SetStatus("Scan finished. Open the recorded result to inspect its entries."); Frame(window);
            Assert.Equal(status.Text, peer.GetName()); Assert.Contains("Scan finished", peer.GetName());
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); model.Dispose(); await model.CloseCompletion; }
    }
}
