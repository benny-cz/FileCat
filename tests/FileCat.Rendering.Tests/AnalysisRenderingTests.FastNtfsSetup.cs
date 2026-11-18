using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [Fact]
    public void FastNtfsSetupOnlyAcceptsExplicitWholeVolumeSpelling()
    {
        const string volume = @"\\?\Volume{23bd77fc-bb6e-48c7-bd48-ef3ad9444443}\";
        Assert.Equal(@"C:\", NtfsMetadataVolume.NormalizeSelection("c:/"));
        Assert.Equal(volume, NtfsMetadataVolume.NormalizeSelection(volume.ToUpperInvariant()));
        foreach (string refused in new[] { "C:", @"C:\data", @"C:\data\..\", @"\\server\share\", @"\\.\PhysicalDrive0",
            volume + "folder", "C:\\ ", "/" })
            Assert.Null(NtfsMetadataVolume.NormalizeSelection(refused));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FastNtfsSetupReviewsExactScopeWithPreservingDefault(bool proceed)
    {
        using var styles = new ProductStyles(); using var ui = new HistoryDialogFixture(480, 620, 20);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var scope = new FastNtfsScopeReview(@"F:\", @"\\?\Volume{23bd77fc-bb6e-48c7-bd48-ef3ad9444443}\", AnalysisComponentPreset.DetailedAllocation);
        var pending = DiskSpaceWindow.ConfirmFastNtfsAsync(ui.Dialogs, scope, cancel.Token);
        await Until(() => ui.Dialogs.IsOpen); Frame(ui.Window);
        var keep = ui.Find("Cancel");
        Assert.True(keep.IsDefault); Assert.True(keep.IsCancel); Assert.Same(keep, ui.Window.FocusManager?.GetFocusedElement());
        Assert.False(ui.Find("Continue to authorization").IsDefault);
        string visible = string.Join('\n', ui.Window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
        Assert.Contains("whole local NTFS volume " + scope.Selection, visible);
        Assert.Contains(scope.Volume, visible);
        Assert.Contains("Detailed allocation", visible);
        Assert.Contains("normally cannot see", visible);
        Assert.Contains("cleanup rights", visible);
        ui.Click(proceed ? "Continue to authorization" : "Cancel");
        Assert.Equal(proceed, await pending);
        pending = DiskSpaceWindow.ConfirmFastNtfsAsync(ui.Dialogs, scope, cancel.Token);
        await Until(() => ui.Dialogs.IsOpen);
        cancel.Cancel();
        Assert.False(await pending.WaitAsync(TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task FastNtfsSetupKeepsStandardAvailableInUnqualifiedView()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        int prepared = 0, reviews = 0;
        using var model = new DiskSpaceViewModel(fixture.Services, _ => { prepared++; throw new InvalidOperationException(); });
        var window = new DiskSpaceWindow { Width = 480, Height = 620 };
        try
        {
            window.AddSession(model, [@"C:\"]); window.Show(); Frame(window);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(),
                b => b.IsEffectivelyVisible && b.IsEnabled && b.Content is "Start analysis");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
                b => b.IsEffectivelyVisible && b.Content is "Fast NTFS scan…");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsEffectivelyVisible && AutomationProperties.GetAutomationId(t) == "FastNtfsAvailability" &&
                    t.Text?.Contains("Use standard analysis", StringComparison.Ordinal) == true);
            await model.StartFastNtfsAsync([@"C:\"], 0, (_, _) => { reviews++; return Task.FromResult(true); });
            Assert.Equal(AnalysisPhase.Ready, model.Phase);
            Assert.Equal(0, prepared);
            Assert.Equal(0, reviews);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
