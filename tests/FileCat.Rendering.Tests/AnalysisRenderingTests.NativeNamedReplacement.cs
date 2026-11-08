using Avalonia.Headless.XUnit;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData("replace")]
    [InlineData("keep")]
    [InlineData("reader")]
    [InlineData("stale")]
    public async Task NativeNamedReplacementUsesCompletePreparedBytesAndPrivateCatalogOwnership(string mode)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        string device = PathUtil.GetDeviceKey(profile.Root); Guid id = Guid.NewGuid();
        AnalysisPrivateDirectory Open() => OperatingSystem.IsWindows()
            ? new WindowsAnalysisPrivateDirectory(profile.Root, "named-replacement") : new UnixAnalysisPrivateDirectory(profile.Root, "named-replacement");
        var owned = await OwnedAnalysisStore.OpenAsync(profile.Services.Io, device, () =>
        {
            var directory = Open(); using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
            new AnalysisPrivateProfileState(store).LoadOrCreate(token);
            new AnalysisSavedCatalog(store).SaveNew("owned.fcdsa", Fixture(), id, AnalysisSavedView.Default, token);
            new AnalysisSavedCatalog(store).SaveNew("other.fcdsa", Fixture(), Guid.NewGuid(), AnalysisSavedView.Default, token);
            return directory;
        }, token);
        try
        {
            string path = Path.Combine(profile.Root, "named-replacement", "owned.fcdsa");
            byte[] before = File.ReadAllBytes(path), other = File.ReadAllBytes(Path.Combine(profile.Root, "named-replacement", "other.fcdsa"));
            var review = await owned.PrepareReplacementAsync(id, Fixture(), AnalysisSavedView.Default, token);
            Assert.Equal(before, File.ReadAllBytes(path));
            if (mode == "keep")
            {
                await review.DisposeAsync(); Assert.Equal(before, File.ReadAllBytes(path));
            }
            else if (mode == "reader")
            {
                AnalysisPrivateFile? reader = null; AnalysisPrivateDirectory? directory = null;
                await AnalysisIo.Run(profile.Services.Io, device, _ => { directory = Open(); reader = directory.OpenRead("owned.fcdsa"); return true; }, token);
                try
                {
                    var failure = await Assert.ThrowsAsync<OwnedAnalysisReplacementException>(() => owned.ReplaceAsync(review, token));
                    Assert.Equal(OwnedAnalysisReplacementState.Kept, failure.Outcome.State); Assert.Equal(before, File.ReadAllBytes(path));
                }
                finally { await AnalysisIo.Run(profile.Services.Io, device, _ => { try { reader!.Dispose(); } finally { directory!.Dispose(); } return true; }, CancellationToken.None); }
            }
            else if (mode == "stale")
            {
                File.Move(path, path + ".original"); File.Copy(path + ".original", path);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                var failure = await Assert.ThrowsAsync<OwnedAnalysisReplacementException>(() => owned.ReplaceAsync(review, token));
                Assert.Equal(OwnedAnalysisReplacementState.Kept, failure.Outcome.State); Assert.Equal(before, File.ReadAllBytes(path));
            }
            else
            {
                // Closing the application owner must wait for this outstanding review, yet the
                // explicitly chosen transaction continues on its retained capability.
                var closing = owned.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
                var result = await owned.ReplaceAsync(review, token); await closing;
                Assert.Equal(OwnedAnalysisReplacementState.Replaced, result.State); Assert.Equal(review.Incoming.InventoryId, result.Receipt!.InventoryId);
                await using var reopened = await OwnedAnalysisStore.OpenAsync(profile.Services.Io, device, Open, token);
                using var catalog = await reopened.ReadCatalogAsync(token); Assert.Empty(catalog.PendingReplacements);
                Assert.DoesNotContain(catalog.Entries, entry => entry.InventoryId == id);
                Assert.Equal(review.Incoming.Sha256, (await reopened.InspectAsync(review.Incoming.InventoryId, token)).Sha256);
            }
            Assert.Equal(other, File.ReadAllBytes(Path.Combine(profile.Root, "named-replacement", "other.fcdsa")));
        }
        finally { await owned.DisposeAsync(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedNativeReplacementRetainsTheCapturedRevisionAcrossSourceClosureAndTheFinalDecision(bool replace)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(indexed: true);
        var token = TestContext.Current.CancellationToken; string device = PathUtil.GetDeviceKey(profile.Root); Guid previous = Guid.NewGuid();
        await using var owned = await OwnedAnalysisStore.OpenAsync(profile.Services.Io, device, () =>
        {
            AnalysisPrivateDirectory directory = OperatingSystem.IsWindows()
                ? new WindowsAnalysisPrivateDirectory(profile.Root, "indexed-replacement") : new UnixAnalysisPrivateDirectory(profile.Root, "indexed-replacement");
            using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
            new AnalysisPrivateProfileState(store).LoadOrCreate(token);
            new AnalysisSavedCatalog(store).SaveNew("owned.fcdsa", Fixture(), previous, AnalysisSavedView.Default, token);
            return directory;
        }, token);
        var descriptor = source.Lease!.Descriptor;
        var review = await owned.PrepareReplacementAsync(previous, source.Lease, AnalysisSavedView.Default, token);
        await source.Lease.DisposeAsync(); source.Lease = null;
        source.AssertPayloads();
        var result = replace ? await owned.ReplaceAsync(review, token) : await owned.KeepExistingAsync(review);
        Assert.Equal(replace ? OwnedAnalysisReplacementState.Replaced : OwnedAnalysisReplacementState.Kept, result.State);
        using var catalog = await owned.ReadCatalogAsync(token); Assert.Empty(catalog.PendingReplacements);
        Assert.Equal(replace ? review.Incoming.InventoryId : previous, Assert.Single(catalog.Entries).InventoryId);
        if (replace)
        {
            var details = await owned.InspectAsync(review.Incoming.InventoryId, token);
            Assert.Equal(review.Incoming.Sha256, details.Sha256); Assert.Equal(descriptor.Coverage, details.Entry.Coverage);
            Assert.Equal(AnalysisSavedCatalog.ViewForInventory(descriptor, AnalysisSavedView.Default), details.Entry.View);
        }
        source.AssertPayloads();
    }
}
