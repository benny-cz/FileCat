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
    [InlineData(false)] [InlineData(true)]
    public async Task NativeIndexedComparisonCapacityExplainsRefusalBeforeLoadingAndRecoversOnlyAfterActualClose(bool retainReader)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await IndexedFixture.CreateAsync(); var token = TestContext.Current.CancellationToken;
        const string name = "capacity-comparison.fcdsc";
        await SaveIndexedMaintenanceFixtureAsync(profile.Services, source.Lease!, name, token);
        string saved = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, name); byte[] bytes = File.ReadAllBytes(saved);
        var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool);
        var otherPool = new AnalysisSessionAdmission(); var other = new DiskSpaceWindow(null, null, otherPool);
        AnalysisIndexedComparison.ReadLease? reader = null;
        try
        {
            Assert.Null(AnalysisIndexedComparison.IndexCapacityRefusal);
            await window.AddIndexedComparisonSessionAsync(source.Lease!, source.Lease!, AnalysisMetric.FileSize,
                cancellation: token, services: profile.Services, compare: async (old, now, metric, roots, stop) =>
                {
                    var result = await now.CompareIndexedAsync(old, metric, roots, stop);
                    if (retainReader) reader = result.Retain();
                    return result;
                });
            window.Show(); Frame(window);
            Click(window, "Open comparison…");
            await UntilComparisonPersistence(() => ComparisonControl<TextBox>(window, "Saved comparison filename") is not null);
            ComparisonControl<TextBox>(window, "Saved comparison filename")!.Text = name;
            var open = window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && Equals(b.Content, "Open comparison observations"));
            await UntilComparisonPersistence(() => !open.IsEffectivelyEnabled);
            Assert.Contains("One indexed comparison can be open at a time", ComparisonText(window));
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && Equals(b.Content, "Browse saved comparisons")).IsEffectivelyEnabled);
            Click(window, "Cancel");
            await UntilComparisonPersistence(() => ComparisonControl<TextBox>(window, "Saved comparison filename") is null);
            bool loaded = false, built = false;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => other.AddPreparedIndexedComparisonSessionAsync(profile.Services, _ =>
            { loaded = true; return Task.FromException<AnalysisSavedIndexedComparison>(new IOException("Must not load")); }, token));
            Assert.Contains("One indexed comparison", refusal.Message); Assert.False(loaded); Assert.True(otherPool.HasCapacity);
            await Assert.ThrowsAsync<InvalidOperationException>(() => other.AddIndexedComparisonSessionAsync(source.Lease!, source.Lease!, AnalysisMetric.Files,
                cancellation: token, compare: (_, _, _, _, _) => { built = true; return Task.FromException<AnalysisIndexedComparison>(new IOException("Must not build")); }));
            Assert.False(built); Assert.Equal(0, other.SessionCount);
            window.ForceClose(); var close = window.WaitForIndexedCloseAsync();
            if (retainReader)
            {
                Assert.False(close.IsCompleted); Assert.NotNull(AnalysisIndexedComparison.IndexCapacityRefusal);
                await reader!.DisposeAsync(); reader = null;
            }
            await close; Assert.Null(AnalysisIndexedComparison.IndexCapacityRefusal);
            await other.AddSavedIndexedComparisonSessionAsync(profile.Services, name, token);
            Assert.Equal(1, other.SessionCount); Assert.Equal(bytes, File.ReadAllBytes(saved));
        }
        finally
        {
            if (reader is not null) await reader.DisposeAsync();
            window.ForceClose(); other.ForceClose(); await window.WaitForIndexedCloseAsync(); await other.WaitForIndexedCloseAsync();
        }
        Assert.Null(AnalysisIndexedComparison.IndexCapacityRefusal); Assert.True(pool.HasCapacity); Assert.True(otherPool.HasCapacity);
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath)); Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }
}
