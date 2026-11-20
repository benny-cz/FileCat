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
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeAnalysisChangeHintsFlagExternalWritesWithoutRefreshingTheCapturedResult(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        string root = Path.Combine(fixture.Root, "watched");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "keep.txt"), "original");
        var pool = new AnalysisSessionAdmission();
        var window = new DiskSpaceWindow(null, null, pool);
        int startingHandles = AnalysisNativeHandles.Count;
        try
        {
            window.Show();
            var original = await window.AddOwnedAcquisitionAsync(fixture.Services, async token =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexedStorage: indexed);
                try { await StartAcquiredAsync(owner, fixture, root); return owner; }
                catch { await owner.DisposeAsync(); throw; }
            });
            await Until(() => ComparisonText(window).Contains("Direct changes watched at 1 of 1", StringComparison.Ordinal),
                () => ComparisonText(window), TimeSpan.FromSeconds(20));
            File.WriteAllText(Path.Combine(root, "external.txt"), "new observations require explicit refresh");
            await Until(() => ComparisonText(window).Contains("Possibly stale: watched.", StringComparison.Ordinal),
                () => ComparisonText(window), TimeSpan.FromSeconds(20));
            Assert.Equal(1, window.SessionCount);
            if (original.Indexed is { } explorer) Assert.Equal(1UL, explorer.LogicalTotals.Files);
            else Assert.DoesNotContain(original.Memory!.Tree!.Snapshot.Nodes, node => node.Name == "external.txt");
            Assert.Equal("original", File.ReadAllText(Path.Combine(root, "keep.txt")));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        await AssertAllRuntimePlacesAvailableAsync(pool);
        Assert.Equal(startingHandles, AnalysisNativeHandles.Count);
    }

    [AvaloniaFact]
    public async Task NativeAnalysisChangeHintsRefuseToWatchAReplacementAtTheSamePath()
    {
        OrdinaryPreparation();
        using var fixture = new PreparationFixture();
        string root = Path.Combine(fixture.Root, "watched");
        Directory.CreateDirectory(root);
        await using var owner = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services);
        await StartAcquiredAsync(owner, fixture, root);
        Directory.Move(root, Path.Combine(fixture.Root, "original"));
        Directory.CreateDirectory(root);

        await Until(() => owner.ReadRuntimeChangeHints()?.Contains("source identity changed", StringComparison.Ordinal) == true,
            () => owner.ReadRuntimeChangeHints() ?? "", TimeSpan.FromSeconds(20));
        Assert.Contains("Live change hints are unavailable", owner.ReadRuntimeChangeHints(), StringComparison.Ordinal);
    }
}
