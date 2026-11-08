using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeferredReplacementRootCloseFailureRetainsTheConfirmedReceiptAndActualOwner(bool replace)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        ClosingInventoryDirectory? directory = null; Guid old = Guid.NewGuid(); string device = PathUtil.GetDeviceKey(profile.Root);
        var owned = await OwnedAnalysisStore.OpenAsync(profile.Services.Io, device, () =>
        {
            AnalysisPrivateDirectory native = OperatingSystem.IsWindows()
                ? new WindowsAnalysisPrivateDirectory(profile.Root, "replacement-close") : new UnixAnalysisPrivateDirectory(profile.Root, "replacement-close");
            directory = new(native);
            try
            {
                using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
                new AnalysisPrivateProfileState(store).LoadOrCreate(token);
                new AnalysisSavedCatalog(store).SaveNew("owned.fcdsa", Fixture(), old, AnalysisSavedView.Default, token); return directory;
            }
            catch { directory.Dispose(); throw; }
        }, token);
        try
        {
            var review = await owned.PrepareReplacementAsync(old, Fixture(), AnalysisSavedView.Default, token);
            directory!.FailClose = true; var closing = owned.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
            var error = await Assert.ThrowsAsync<OwnedAnalysisReplacementException>(() => replace ? owned.ReplaceAsync(review, token) : owned.KeepExistingAsync(review));
            Assert.Equal(OwnedAnalysisReplacementState.Uncertain, error.Outcome.State);
            Assert.Equal(replace, error.Outcome.Receipt is not null); Assert.True(AnalysisCloseUnconfirmedException.IsPresent(error));
            if (replace)
            {
                Assert.Equal(review.Incoming.InventoryId, error.Outcome.Receipt!.InventoryId);
                Assert.StartsWith("The new named analysis was saved, but private cleanup", error.Outcome.Detail);
                using var file = File.OpenRead(Path.Combine(profile.Root, "replacement-close", "owned.fcdsa"));
                Assert.Equal(review.Incoming.Sha256, Convert.ToHexStringLower(SHA256.HashData(file)));
            }
            await Assert.ThrowsAsync<AnalysisCloseUnconfirmedException>(() => closing);
            Assert.Equal(1, directory.Closes);
        }
        finally
        {
            try { await owned.DisposeAsync(); }
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { }
        }
    }
    private sealed class ClosingInventoryDirectory(AnalysisPrivateDirectory inner) : AnalysisPrivateDirectory
    {
        internal bool FailClose;
        internal int Closes;
        internal override void Verify() => inner.Verify();
        internal override (UInt128 Capacity, UInt128 Available) Space() => inner.Space();
        internal override string[] Entries(CancellationToken cancellation) => inner.Entries(cancellation);
        internal override AnalysisPrivateFile CreateStaging() => inner.CreateStaging();
        internal override AnalysisPrivateFile OpenRead(string name) => inner.OpenRead(name);
        internal override AnalysisPrivateFile OpenMaintenance(string name) => inner.OpenMaintenance(name);
        internal override AnalysisPrivateFile OpenReplacementFile(string name) => inner.OpenReplacementFile(name);
        public override void Dispose() { Closes++; inner.Dispose(); if (FailClose) throw new IOException("Injected unconfirmed root close."); }
    }
}
