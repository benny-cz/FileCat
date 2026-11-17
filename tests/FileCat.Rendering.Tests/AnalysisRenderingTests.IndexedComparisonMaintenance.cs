using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task NativeIndexedComparisonMaintenanceKeepsDefaultsAndInvalidatesOnlyRemovedPrivateCopy(bool delete, bool full)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await IndexedFixture.CreateAsync(); var token = TestContext.Current.CancellationToken;
        const string name = "managed-comparison.fcdsc";
        await SaveIndexedMaintenanceFixtureAsync(profile.Services, source.Lease!, name, token);
        string path = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, name); byte[] bytes = File.ReadAllBytes(path);
        var pool = new AnalysisSessionAdmission(); var other = full ? Enumerable.Range(0, 7).Select(_ => pool.Acquire()).ToArray() : [];
        var window = new DiskSpaceWindow(null, null, pool);
        try
        {
            await window.AddSavedIndexedComparisonSessionAsync(profile.Services, name, token); window.Show(); Frame(window);
            await OpenIndexedMaintenanceReviewAsync(window, name, full);
            var keep = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Keep comparison"));
            var remove = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Delete comparison"));
            Assert.True(keep.IsDefault); Assert.False(remove.IsDefault); Assert.Contains("danger", remove.Classes);
            await UntilComparisonPersistence(() => keep.IsFocused || keep.IsKeyboardFocusWithin);
            Click(window, delete ? "Delete comparison" : "Keep comparison");
            if (delete)
            {
                await UntilComparisonPersistence(() => window.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible && Equals(button.Content, "OK")), () => ComparisonText(window));
                Click(window, "OK");
            }
            var rows = ComparisonControl<ListBox>(window, "Captured comparison rows")!;
            await UntilComparisonPersistence(() => rows.IsEnabled);
            Assert.NotEmpty(rows.Items); Assert.Equal(!delete, File.Exists(path));
            if (!delete) Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
            Click(window, "Close analysis");
            if (delete)
            {
                await UntilComparisonPersistence(() => ComparisonText(window).Contains("Discard these temporary comparison observations", StringComparison.Ordinal));
                Assert.True(window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Keep open")).IsDefault);
                Click(window, "Keep open"); Assert.Equal(1, window.SessionCount);
            }
            else await UntilComparisonPersistence(() => window.SessionCount == 0, () => ComparisonText(window));
        }
        finally
        {
            window.ForceClose(); await window.WaitForIndexedCloseAsync();
            foreach (var slot in other) await slot.CloseAfterAsync(Task.CompletedTask);
        }
        var recovered = Enumerable.Range(0, AnalysisLimits.Sessions).Select(_ => pool.Acquire()).ToArray();
        foreach (var slot in recovered) await slot.CloseAfterAsync(Task.CompletedTask);
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }

    [AvaloniaFact]
    public async Task NativeIndexedComparisonMaintenanceWindowCloseCancelsReviewAndActuallyReleasesPrivateOwner()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await IndexedFixture.CreateAsync(); var token = TestContext.Current.CancellationToken;
        const string name = "close-review.fcdsc";
        await SaveIndexedMaintenanceFixtureAsync(profile.Services, source.Lease!, name, token);
        string path = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, name); byte[] bytes = File.ReadAllBytes(path);
        var window = new DiskSpaceWindow();
        try
        {
            await window.AddSavedIndexedComparisonSessionAsync(profile.Services, name, token); window.Show(); Frame(window);
            await OpenIndexedMaintenanceReviewAsync(window, name, false);
            window.ForceClose(); await window.WaitForIndexedCloseAsync().WaitAsync(TimeSpan.FromSeconds(30), token);
            Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    private static async Task SaveIndexedMaintenanceFixtureAsync(AppServices services, AnalysisDatasetLease source, string name, CancellationToken token)
    {
        await using var result = await source.CompareIndexedAsync(source, AnalysisMetric.FileSize, cancellation: token);
        await using var captured = await result.CaptureBundleAsync(new(), cancellation: token);
        await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, token, indexedStorage: false);
        await prepared.SaveComparisonAsync(captured, name, token);
    }
    private static async Task OpenIndexedMaintenanceReviewAsync(DiskSpaceWindow window, string name, bool full)
    {
        Click(window, "Open comparison…"); await UntilComparisonPersistence(() => ComparisonControl<TextBox>(window, "Saved comparison filename") is not null);
        Click(window, "Browse saved comparisons"); await UntilComparisonPersistence(() => ComparisonControl<ComboBox>(window, "Saved comparisons")?.ItemCount == 1);
        ComparisonControl<ComboBox>(window, "Saved comparisons")!.SelectedIndex = 0;
        var remove = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Delete selected comparison…"));
        ComparisonControl<TextBox>(window, "Saved comparison filename")!.Text = "different-name.fcdsc";
        await UntilComparisonPersistence(() => !remove.IsEffectivelyEnabled);
        ComparisonControl<TextBox>(window, "Saved comparison filename")!.Text = name;
        await UntilComparisonPersistence(() => remove.IsEffectivelyEnabled);
        if (full) Assert.False(window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Open comparison observations")).IsEffectivelyEnabled);
        Click(window, "Delete selected comparison…");
        await UntilComparisonPersistence(() => window.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible && Equals(button.Content, "Keep comparison")), () => ComparisonText(window));
    }
}
