using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    // Stable test identifiers must not replace the spoken text of a live region.
    private static string? AutomationLabel(Control control) => AutomationProperties.GetName(control) is { Length: > 0 } name
        ? name : AutomationProperties.GetAutomationId(control);

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
