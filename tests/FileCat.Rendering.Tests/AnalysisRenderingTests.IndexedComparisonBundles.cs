using FileCat.App.Services;
using FileCat.Core.Analysis;
using Avalonia.Headless.XUnit;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeIndexedComparisonBundlesSaveOpenExportAndImportBothEntireOfflineInputs(bool structural)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync(sharedAlias: true);
        var token = TestContext.Current.CancellationToken;
        ulong expectedPaths = source.Lease!.Descriptor.Paths;
        var roots = structural ? source.Lease!.Descriptor.Roots.ToDictionary(root => root.Id, root => root.Id, StringComparer.Ordinal) : null;
        await using var result = await source.Lease!.CompareIndexedAsync(source.Lease, AnalysisMetric.UniqueFileSize, roots, token);
        var view = new AnalysisComparisonView { Metric = AnalysisMetric.UniqueFileSize, Query = "name:match-target", Order = AnalysisComparisonOrder.Path };
        await using var captured = await result.CaptureBundleAsync(view, "Previous captured inventory", cancellation: token);
        await source.ReleaseInitialLeaseAsync(); var closing = result.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
        string name = "Comparison.fcdsc", path = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, name);
        string original = Path.Combine(source.Root, "source"), offline = Path.Combine(source.Root, "source-offline"); Directory.Move(original, offline);
        try
        {
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            {
                var receipt = await prepared.SaveComparisonAsync(captured, name, token); Assert.Equal(name, receipt.Name);
            }
            byte[] saved = File.ReadAllBytes(path);
            RetainIndexedComparisonOutput(path, $"bundle-named-{structural}.fcdsc");
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
                await Assert.ThrowsAsync<AnalysisExportTargetExistsException>(() => prepared.SaveComparisonAsync(captured, name, token));
            Assert.Equal(saved, File.ReadAllBytes(path));
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
                Assert.Contains(name, await prepared.ReadComparisonNamesAsync(token));
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token))
            await using (var loaded = await prepared.LoadSavedComparisonAsync(name, token))
            {
                Assert.Equal(view, loaded.View); Assert.Equal(structural, loaded.StructuralRoots is not null);
                Assert.True(loaded.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
                Assert.Equal(expectedPaths, loaded.Previous.Descriptor.Paths);
                Assert.Equal(loaded.Previous.Descriptor.Paths, loaded.Current.Descriptor.Paths);
                Assert.Equal(AnalysisOrigin.ImportedInventory, loaded.Previous.Descriptor.Origin);
            }
            string folder = Path.Combine(source.Root, "comparison-export"); Directory.CreateDirectory(folder);
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
                await prepared.ExportComparisonAsync(captured, folder, name, token);
            string external = Path.Combine(folder, name); byte[] exported = File.ReadAllBytes(external);
            RetainIndexedComparisonOutput(external, $"bundle-exported-{structural}.fcdsc");
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token))
            await using (var loaded = await prepared.LoadExternalComparisonAsync(external, token))
            {
                Assert.Equal(view, loaded.View); Assert.Equal(structural, loaded.StructuralRoots is not null);
                Assert.Equal(expectedPaths, loaded.Current.Descriptor.Paths);
            }
            Assert.Equal(exported, File.ReadAllBytes(external)); Assert.Equal(saved, File.ReadAllBytes(path)); Assert.False(Directory.Exists(original));
        }
        finally { Directory.Move(offline, original); }
        await captured.DisposeAsync(); await closing;
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath)); Assert.Empty(Directory.GetFiles(source.PrivatePath));
    }
}
