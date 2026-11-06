using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task ReopenedQualifiedInventoryKeepsUnknownCountsAndIncompleteStaleLocationsVisible()
    {
        using var styles = new ProductStyles(); var snapshot = Fixture() with { Freshness = AnalysisFreshness.PossiblyStale };
        ulong eligible = (ulong)snapshot.Nodes.Count(n => n.Kind == AnalysisKind.Directory && n.IncludedByPolicy && n.MetadataState == MeasurementState.Known && n.Cloud != true);
        snapshot = snapshot with { CoverageDetails = AnalysisCoverage.Create(snapshot) with
        { DenominatorFinal = false, Qualification = new(false, eligible, false, true) } };
        using var stream = new MemoryStream(); AnalysisInventoryFormat.Write(stream, snapshot, Guid.NewGuid(), TestContext.Current.CancellationToken); stream.Position = 0;
        using var inventory = AnalysisInventoryFormat.Read(stream, TestContext.Current.CancellationToken);
        using var retained = inventory.Retain(); using var model = new DiskSpaceViewModel(retained.Retain(), null, false);
        await model.RebuildAsync(true); var window = new DiskSpaceWindow();
        try
        {
            window.AddSession(model); window.Show(); Frame(window);
            foreach (string message in new[] { "Enumeration counts are unavailable", "Stale-location samples are incomplete", "cannot be assigned to a recorded location" })
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text?.Contains(message, StringComparison.Ordinal) == true);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
