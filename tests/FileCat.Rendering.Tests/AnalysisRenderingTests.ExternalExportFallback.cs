using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task MemoryFallbackCanExportCapturedObservationsWhileNamedSaveRemainsUnavailable()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); using var inventory = ImportedFixture();
        using var model = new DiskSpaceViewModel(inventory.Retain(), profile.Services, memoryFallback: true); await model.RebuildAsync(true);
        Assert.False(model.CanSave); Assert.True(model.CanExport); var window = new DiskSpaceWindow();
        try
        {
            window.AddSession(model); window.Show(); Frame(window);
            var export = FindAnalysisButton(window, "Export entire inventory…")!;
            Assert.True(export.IsEnabled); Assert.False(window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Content as string == "Save…").IsEnabled);
            Assert.True(FindAnalysisButton(window, "Export report…")!.IsEnabled);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeExternalExportWithCorruptProfileKeepsItsBytesAndUsesIndependentPrivateOutput(bool indexed)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(indexed);
        await CorruptProfileForMemoryAsync(profile); byte[] state = File.ReadAllBytes(profile.Services.Paths.AnalysisPrivateStateFile);
        string folder = Path.Combine(source.Root, "fallback-output"); Directory.CreateDirectory(folder); var token = TestContext.Current.CancellationToken;
        int handles = AnalysisNativeHandles.Count;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
        {
            Assert.NotNull(prepared.MemoryPreparationNotice);
            var receipt = indexed ? await prepared.ExportInventoryAsync(source.Lease!, folder, "fallback.fcdsa", token)
                : await prepared.ExportInventoryAsync(source.Memory!, folder, "fallback.fcdsa", token);
            Assert.True(receipt.Bytes > 0);
        }
        using (var stream = File.OpenRead(Path.Combine(folder, "fallback.fcdsa")))
        using (var imported = AnalysisInventoryFormat.Read(stream, token))
        { Assert.Equal(source.Files.Count + 1, imported.Snapshot.Nodes.Length); Assert.Equal(AnalysisOrigin.ImportedInventory, imported.Snapshot.Origin); }
        Assert.Equal(state, File.ReadAllBytes(profile.Services.Paths.AnalysisPrivateStateFile)); Assert.Equal(handles, AnalysisNativeHandles.Count); source.AssertPayloads();
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisSnapshotsDirectory, "*.fcdsa"));
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisHistoryDirectory, "*.fcdsa"));
        if (Directory.Exists(profile.Services.Paths.AnalysisScratchDirectory)) Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory, "*", SearchOption.AllDirectories));
    }
}
