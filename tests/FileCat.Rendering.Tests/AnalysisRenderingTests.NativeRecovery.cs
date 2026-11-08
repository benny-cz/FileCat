using Avalonia.Headless.XUnit;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData("marker", true)]
    [InlineData("staged", true)]
    [InlineData("backed", true)]
    [InlineData("switched", true)]
    [InlineData("committed", true)]
    [InlineData("missing", true)]
    [InlineData("staged", false)]
    [InlineData("backed", false)]
    [InlineData("switched", false)]
    [InlineData("committed", false)]
    [InlineData("cleaning", false)]
    [InlineData("missing", false)]
    public async Task NativeReplacementRecoveryRestoresOrFinishesOnlyFreshlyReviewedCompletePrivateFiles(string state, bool restore)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        var fixture = await CreateNativeRecoveryAsync(profile, state, token); await using var owned = fixture.Store;
        var before = Directory.GetFiles(fixture.Root).Order(StringComparer.Ordinal).ToArray();
        var review = await owned.PrepareReplacementRecoveryAsync(fixture.Operation, token);
        Assert.Equal(before, Directory.GetFiles(fixture.Root).Order(StringComparer.Ordinal));
        var outcome = await owned.RecoverReplacementAsync(review, restore ? OwnedAnalysisRecoveryAction.RestorePrevious : OwnedAnalysisRecoveryAction.FinishIncoming, cancellation: token);
        Assert.Equal(restore ? OwnedAnalysisRecoveryState.Restored : OwnedAnalysisRecoveryState.FinishedIncoming, outcome.State);
        Assert.Equal(restore ? fixture.OldBytes : fixture.NewBytes, File.ReadAllBytes(Path.Combine(fixture.Root, "owned.fcdsa")));
        Assert.Equal(fixture.OtherBytes, File.ReadAllBytes(Path.Combine(fixture.Root, "other.fcdsa")));
        using var catalog = await owned.ReadCatalogAsync(token); Assert.Empty(catalog.PendingReplacements); Assert.Equal(2, catalog.Entries.Length);
        Assert.Equal(outcome.Receipt!.InventoryId, catalog.Entries.Single(e => e.Name == "owned.fcdsa").InventoryId);
        Assert.DoesNotContain(Directory.EnumerateFiles(fixture.Root).Select(Path.GetFileName), name => name is not null && AnalysisPrivateNames.IsReplacementPayload(name));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeRecoveryPreservesUnknownOrCorruptOriginalAndCreatesOnlyAnExplicitNewRecoveryName(bool corrupt)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        var fixture = await CreateNativeRecoveryAsync(profile, "switched", token); await using var owned = fixture.Store;
        string original = Path.Combine(fixture.Root, "owned.fcdsa");
        byte[] foreign;
        if (corrupt) foreign = [1, 2, 3];
        else { using var data = new MemoryStream(); AnalysisInventoryFormat.Write(data, Fixture(), Guid.NewGuid(), token); foreign = data.ToArray(); }
        File.WriteAllBytes(original, foreign);
        var review = await owned.PrepareReplacementRecoveryAsync(fixture.Operation, token);
        Assert.False(review.CanRestorePrevious); Assert.False(review.CanFinishIncoming); Assert.NotNull(review.CopySource);
        var outcome = await owned.RecoverReplacementAsync(review, OwnedAnalysisRecoveryAction.CopyPrevious, "recovered.fcdsa", token);
        Assert.Equal(OwnedAnalysisRecoveryState.CopiedPrevious, outcome.State);
        Assert.Equal(foreign, File.ReadAllBytes(original)); Assert.Equal(fixture.OldBytes, File.ReadAllBytes(Path.Combine(fixture.Root, "recovered.fcdsa")));
        using var catalog = await owned.ReadCatalogAsync(token); Assert.Single(catalog.PendingReplacements); Assert.Contains("recovered.fcdsa", catalog.UnindexedNames);
    }

    private sealed record NativeRecoveryFixture(OwnedAnalysisStore Store, string Root, Guid Operation, byte[] OldBytes, byte[] NewBytes, byte[] OtherBytes);
    private static async Task<NativeRecoveryFixture> CreateNativeRecoveryAsync(PreparationFixture profile, string state, CancellationToken cancellation,
        bool applicationStore = false, Func<AnalysisPrivateDirectory, AnalysisPrivateDirectory>? wrap = null)
    {
        Guid operation = Guid.NewGuid(); byte[]? old = null, fresh = null, other = null;
        if (applicationStore) { await using var prepared = await FileCat.App.Services.AnalysisIndexedAcquisition.PrepareAsync(profile.Services, cancellation, indexedStorage: false); }
        string root = applicationStore ? profile.Services.Paths.AnalysisSnapshotsDirectory : Path.Combine(profile.Root, "native-recovery");
        string parent = Path.GetDirectoryName(root)!, child = Path.GetFileName(root);
        var owned = await OwnedAnalysisStore.OpenAsync(profile.Services.Io, PathUtil.GetDeviceKey(profile.Root), () =>
        {
            AnalysisPrivateDirectory directory = OperatingSystem.IsWindows()
                ? new WindowsAnalysisPrivateDirectory(parent, child) : new UnixAnalysisPrivateDirectory(parent, child);
            if (wrap is not null) directory = wrap(directory);
            try
            {
                using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); new AnalysisPrivateProfileState(store).LoadOrCreate(cancellation);
                var catalog = new AnalysisSavedCatalog(store); Guid previous = Guid.NewGuid(), incoming = Guid.NewGuid();
                catalog.SaveNew("owned.fcdsa", Fixture(), previous, AnalysisSavedView.Default, cancellation);
                catalog.SaveNew("incoming.fcdsa", Fixture(), incoming, AnalysisSavedView.Default, cancellation);
                catalog.SaveNew("other.fcdsa", Fixture(), Guid.NewGuid(), AnalysisSavedView.Default, cancellation);
                Guid catalogId; using (var index = catalog.Read(cancellation)) catalogId = index.CatalogId!.Value;
                old = Read("owned.fcdsa"); fresh = Read("incoming.fcdsa"); other = Read("other.fcdsa");
                string currentMetadata = AnalysisPrivateNames.CatalogEntry(catalogId, incoming);
                var metadata = JsonNode.Parse(Read(currentMetadata))!.AsObject(); metadata["name"] = "owned.fcdsa";
                using (var file = directory.OpenMaintenance(AnalysisPrivateNames.CatalogEntry(catalogId, previous)))
                    file.MoveOwnedNew(AnalysisPrivateNames.PendingReplacement(catalogId, previous, incoming, operation));
                using (var file = directory.OpenMaintenance(currentMetadata)) file.RemoveOwned();
                using (var file = directory.OpenMaintenance("incoming.fcdsa")) file.RemoveOwned();
                string incomingName = AnalysisPrivateNames.ReplacementIncoming(incoming, operation);
                if (state != "marker") Write(incomingName, fresh);
                if (state is "backed" or "switched" or "committed" or "missing") Write(AnalysisPrivateNames.ReplacementPrevious(previous, operation), old);
                if (state is "switched" or "committed" or "cleaning")
                {
                    using var target = directory.OpenReplacementFile("owned.fcdsa"); using var source = directory.OpenReplacementFile(incomingName); source.ReplaceOwned(target);
                }
                if (state == "missing") { using var file = directory.OpenMaintenance("owned.fcdsa"); file.RemoveOwned(); }
                if (state is "committed" or "cleaning") store.WriteMetadataNew(currentMetadata, Encoding.UTF8.GetBytes(metadata.ToJsonString() + "\n"), cancellation);
                return directory;

                byte[] Read(string name) { using var file = directory.OpenRead(name); using var bytes = new MemoryStream(); file.Stream.CopyTo(bytes); return bytes.ToArray(); }
                void Write(string name, byte[] bytes) { using var file = directory.CreateStaging(); file.Stream.Write(bytes); file.Flush(); file.PublishNew(name); }
            }
            catch { directory.Dispose(); throw; }
        }, cancellation);
        return new(owned, root, operation, old!, fresh!, other!);
    }
}
