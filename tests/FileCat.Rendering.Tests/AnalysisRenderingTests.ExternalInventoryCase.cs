using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task NativeExternalInventorySuffixCaseOpensExactChosenFileAndKeepsInputs(bool comparison, bool mixed)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        string suffix = comparison ? mixed ? ".FcDsC" : ".FCDSC" : mixed ? ".FcDsA" : ".FCDSA";
        string path = Path.Combine(profile.Root, "Chosen-Input" + suffix);
        string lower = Path.Combine(profile.Root, "Chosen-Input" + (comparison ? ".fcdsc" : ".fcdsa"));
        Guid id = Guid.NewGuid(); byte[] bytes;
        if (comparison)
        {
            using var output = new MemoryStream(); var snapshot = Fixture();
            AnalysisComparisonFormat.Write(output, id, snapshot, snapshot, new(), null, "Captured observations", token);
            bytes = output.ToArray();
        }
        else bytes = ExternalInventoryBytes(Fixture());
        File.WriteAllBytes(path, bytes);
        // On a case-sensitive filesystem an incorrect normalization would open
        // this different, invalid file. Never replace the selected file on an
        // insensitive filesystem merely to construct the negative control.
        byte[] decoy = [0, 1, 2, 3]; bool distinct = !File.Exists(lower);
        if (distinct) File.WriteAllBytes(lower, decoy);
        int handles = AnalysisNativeHandles.Count;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token))
        {
            if (comparison)
            {
                await using var loaded = await prepared.LoadExternalComparisonAsync(path, token);
                Assert.Equal(id, loaded.ComparisonId); Assert.True(loaded.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
            }
            else
            {
                await using var loaded = await prepared.LoadExternalInventoryAsync(path, token);
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), loaded.SourceSha256);
                Assert.Equal(AnalysisOrigin.ImportedInventory, loaded.Descriptor.Origin);
            }
        }
        Assert.Equal(handles, AnalysisNativeHandles.Count); Assert.Equal(bytes, File.ReadAllBytes(path));
        if (distinct) Assert.Equal(decoy, File.ReadAllBytes(lower));
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }
}
