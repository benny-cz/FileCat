using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData("named-delete")] [InlineData("named-restore")] [InlineData("named-pending-delete")] [InlineData("named-kept")]
    [InlineData("rolling-opaque-indexed")] [InlineData("rolling-opaque-unindexed")] [InlineData("rolling-prune")] [InlineData("rolling-capture")]
    public async Task NativeHistoryAndNamedRootCloseFaultPreservesConfirmedFactsAndNeverReportsCleanSuccess(string mode)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        bool rolling = mode.StartsWith("rolling", StringComparison.Ordinal), unindexed = mode.EndsWith("unindexed", StringComparison.Ordinal);
        await using var fixture = await HistoryFaultFixture.CreateAsync(profile, rolling, unindexed, mode is "named-restore" or "named-pending-delete");
        var owner = fixture.Owner; Task? actualClose = null;
        void RequestClose()
        {
            if (actualClose is not null) return;
            fixture.Root.FailClose = true; actualClose = owner.DisposeAsync().AsTask();
            Assert.False(actualClose.IsCompleted);
        }
        var review = mode is "rolling-prune" or "rolling-capture" || mode.Contains("opaque", StringComparison.Ordinal) ? null : await owner.PrepareDeleteAsync(fixture.Id, token);
        var opaque = mode.Contains("opaque", StringComparison.Ordinal) ? await owner.PrepareOpaqueHistoryDeleteAsync(fixture.Name, token) : null;
        if (mode == "named-restore") fixture.Hooks.BeforeMove = (_, next) => { if (AnalysisPrivateNames.IsCatalogEntry(next)) RequestClose(); };
        else fixture.Hooks.BeforeRemove = name =>
        {
            if (name != fixture.Name) return;
            RequestClose(); if (mode == "named-kept") throw new IOException("Injected known pre-unlink refusal.");
        };
        try
        {
            if (mode is "rolling-prune" or "rolling-capture")
            {
                var error = await Assert.ThrowsAsync<AnalysisRollingCaptureException>(() => mode == "rolling-prune"
                    ? owner.PruneRollingAsync(new(true, 1), null, token)
                    : owner.CaptureRollingAsync(new(true, 2), Fixture(), AnalysisSavedView.Default, null, token));
                Assert.Equal(AnalysisRollingCaptureState.Uncertain, error.Outcome.State);
                Assert.Equal(fixture.Id, Assert.Single(error.Outcome.Pruned));
                Assert.Equal(mode == "rolling-capture", error.Outcome.Receipt is not null);
                Assert.Equal(mode == "rolling-capture", error.Outcome.Cataloged);
                Assert.True(AnalysisCloseUnconfirmedException.IsPresent(error));
            }
            else
            {
                var error = await Assert.ThrowsAsync<OwnedAnalysisDeletionException>(() => opaque is not null ? owner.DeleteOpaqueHistoryAsync(opaque, token)
                    : mode == "named-restore" ? owner.RestoreNamedAsync(review!, token) : owner.DeleteAsync(review!, token));
                Assert.Equal(OwnedAnalysisDeletionState.Uncertain, error.Outcome.State);
                Assert.Equal(mode is not ("named-restore" or "named-kept"), error.Outcome.PayloadAbsentConfirmed);
                Assert.True(AnalysisCloseUnconfirmedException.IsPresent(error));
            }
            Assert.NotNull(actualClose); await Assert.ThrowsAsync<AnalysisCloseUnconfirmedException>(() => actualClose);
            Assert.Equal(1, fixture.Root.Closes); fixture.AssertNeighbor();
            Assert.Equal(mode is "named-restore" or "named-kept", File.Exists(fixture.Path));
            if (File.Exists(fixture.Path)) Assert.Equal(fixture.Original, await File.ReadAllBytesAsync(fixture.Path, token));
        }
        finally
        {
            if (actualClose is not null) try { await actualClose; } catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { }
        }
    }

    [AvaloniaTheory]
    [InlineData(false, "before-move")] [InlineData(true, "before-move")]
    [InlineData(false, "after-move")] [InlineData(true, "after-move")]
    [InlineData(false, "before-unlink")] [InlineData(true, "before-unlink")]
    [InlineData(false, "rollback-fails")] [InlineData(true, "rollback-fails")]
    [InlineData(false, "after-unlink")] [InlineData(true, "after-unlink")]
    [InlineData(false, "metadata-unlink")] [InlineData(true, "metadata-unlink")]
    [InlineData(false, "cancel-after-move")] [InlineData(true, "cancel-after-move")]
    public async Task NativeRollingFailureBoundariesPreserveOtherCopiesAndRequireFreshRecovery(bool capture, string point)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        await using var fixture = await HistoryFaultFixture.CreateAsync(profile, true, false, false);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        fixture.Hooks.BeforeMove = (_, next) =>
        {
            if (point == "before-move" && AnalysisPrivateNames.IsPendingDeletion(next) || point == "rollback-fails" && AnalysisPrivateNames.IsCatalogEntry(next))
                throw new IOException("Injected metadata movement refusal.");
        };
        fixture.Hooks.AfterMove = (_, next) =>
        {
            if (!AnalysisPrivateNames.IsPendingDeletion(next)) return;
            if (point == "after-move") throw new IOException("Injected post-move failure.");
            if (point == "cancel-after-move") cancellation.Cancel();
        };
        fixture.Hooks.BeforeRemove = name =>
        {
            if (name == fixture.Name && point is "before-unlink" or "rollback-fails" || AnalysisPrivateNames.IsPendingDeletion(name) && point == "metadata-unlink")
                throw new IOException("Injected pre-unlink refusal.");
        };
        fixture.Hooks.AfterRemove = name => { if (name == fixture.Name && point == "after-unlink") throw new IOException("Injected post-unlink durability failure."); };
        var error = await Assert.ThrowsAsync<AnalysisRollingCaptureException>(() => capture
            ? fixture.Owner.CaptureRollingAsync(new(true, 2), Fixture(), AnalysisSavedView.Default, null, cancellation.Token)
            : fixture.Owner.PruneRollingAsync(new(true, 1), null, cancellation.Token));
        Assert.Equal(point == "after-unlink" ? AnalysisRollingCaptureState.Uncertain : AnalysisRollingCaptureState.RecoveryRequired, error.Outcome.State);
        Assert.Equal(point == "metadata-unlink" ? 1 : 0, error.Outcome.Pruned.Length);
        Assert.Equal(capture, error.Outcome.Receipt is not null); Assert.False(error.Outcome.Cataloged);
        bool payloadPresent = point is not ("after-unlink" or "metadata-unlink");
        Assert.Equal(payloadPresent, File.Exists(fixture.Path)); fixture.AssertNeighbor();
        if (payloadPresent) Assert.Equal(fixture.Original, await File.ReadAllBytesAsync(fixture.Path, token));
        fixture.ClearHooks();
        using (var current = await fixture.Owner.ReadCatalogAsync(token))
        {
            var pending = current.PendingDeletions.SingleOrDefault(item => item.Entry.InventoryId == fixture.Id);
            bool needsPending = point is "rollback-fails" or "after-unlink" or "metadata-unlink";
            Assert.Equal(needsPending, pending is not null);
            if (pending is not null)
            {
                var fresh = await fixture.Owner.PrepareDeleteAsync(fixture.Id, token);
                var recovered = payloadPresent ? await fixture.Owner.RestoreNamedAsync(fresh, token) : await fixture.Owner.DeleteAsync(fresh, token);
                Assert.Equal(payloadPresent ? OwnedAnalysisDeletionState.Restored : OwnedAnalysisDeletionState.Deleted, recovered.State);
            }
            if (capture)
            {
                Assert.Equal(error.Outcome.Receipt!.Name, Assert.Single(current.UnindexedNames));
                var inspected = await fixture.Owner.PrepareRollingRecoveryAsync(error.Outcome.Receipt.Name, token);
                Assert.Equal(error.Outcome.Receipt.InventoryId, inspected.InventoryId);
            }
        }
        using var after = await fixture.Owner.ReadCatalogAsync(token); Assert.Empty(after.PendingDeletions); fixture.AssertNeighbor();
    }

    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task NativeOpaqueHistoryFailureBoundaryDistinguishesKnownKeptBytesFromUncertainUnlink(bool indexed, bool afterUnlink)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        await using var fixture = await HistoryFaultFixture.CreateAsync(profile, true, !indexed, false);
        var review = await fixture.Owner.PrepareOpaqueHistoryDeleteAsync(fixture.Name, token);
        if (afterUnlink) fixture.Hooks.AfterRemove = name => { if (name == fixture.Name) throw new IOException("Injected post-unlink failure."); };
        else fixture.Hooks.BeforeRemove = name => { if (name == fixture.Name) throw new IOException("Injected known pre-unlink refusal."); };
        var error = await Assert.ThrowsAsync<OwnedAnalysisDeletionException>(() => fixture.Owner.DeleteOpaqueHistoryAsync(review, token));
        Assert.Equal(afterUnlink ? OwnedAnalysisDeletionState.Uncertain : OwnedAnalysisDeletionState.Kept, error.Outcome.State);
        Assert.False(error.Outcome.PayloadAbsentConfirmed); Assert.Equal(!afterUnlink, File.Exists(fixture.Path)); fixture.AssertNeighbor();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Owner.DeleteOpaqueHistoryAsync(review, token));
        fixture.ClearHooks();
        if (!afterUnlink)
            Assert.Equal(OwnedAnalysisDeletionState.Deleted, (await fixture.Owner.DeleteOpaqueHistoryAsync(await fixture.Owner.PrepareOpaqueHistoryDeleteAsync(fixture.Name, token), token)).State);
        else if (indexed)
            Assert.Equal(OwnedAnalysisDeletionState.Deleted, (await fixture.Owner.DeleteAsync(await fixture.Owner.PrepareDeleteAsync(fixture.Id, token), token)).State);
        fixture.AssertNeighbor();
    }

    private sealed class HistoryFaultFixture : IAsyncDisposable
    {
        internal required OwnedAnalysisStore Owner;
        internal required MaintenanceDirectory Hooks;
        internal required ClosingInventoryDirectory Root;
        internal Guid Id;
        internal string Name = "", Path = "", NeighborPath = "", NeighborHash = "";
        internal byte[] Original = [];
        internal void AssertNeighbor() => Assert.Equal(NeighborHash, RollingHash(NeighborPath));
        internal void ClearHooks() { Hooks.BeforeMove = Hooks.AfterMove = null; Hooks.BeforeRemove = Hooks.AfterRemove = null; }
        public async ValueTask DisposeAsync()
        {
            try { await Owner.DisposeAsync(); }
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { }
        }
        internal static async Task<HistoryFaultFixture> CreateAsync(PreparationFixture profile, bool rolling, bool unindexed, bool pending)
        {
            var token = TestContext.Current.CancellationToken; Guid id = Guid.NewGuid(), neighbor = Guid.NewGuid();
            string child = "history-fault", directoryPath = System.IO.Path.Combine(profile.Root, child);
            string name = rolling ? OwnedAnalysisStore.RollingName(id) : "owned.fcdsa", other = rolling ? OwnedAnalysisStore.RollingName(neighbor) : "other.fcdsa";
            MaintenanceDirectory? hooks = null; ClosingInventoryDirectory? close = null;
            AnalysisPrivateDirectory Prepare()
            {
                AnalysisPrivateDirectory native = OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(profile.Root, child) : new UnixAnalysisPrivateDirectory(profile.Root, child);
                close = new(native); hooks = new(close);
                try
                {
                    using var store = new AnalysisPrivateInventoryStore(hooks, leaveOpen: true); new AnalysisPrivateProfileState(store).LoadOrCreate(token);
                    var catalog = new AnalysisSavedCatalog(store);
                    if (unindexed) store.SaveNew(name, Fixture(), id, token); else catalog.SaveNew(name, Fixture(), id, AnalysisSavedView.Default, token);
                    catalog.SaveNew(other, Fixture(), neighbor, AnalysisSavedView.Default, token);
                    using var current = catalog.Read(token);
                    if (!unindexed)
                    {
                        string metadata = AnalysisPrivateNames.CatalogEntry(current.CatalogId!.Value, id);
                        string path = System.IO.Path.Combine(directoryPath, metadata); var node = JsonNode.Parse(File.ReadAllText(path))!;
                        node["saved"] = DateTimeOffset.UtcNow.AddDays(-5).ToString("O", CultureInfo.InvariantCulture); File.WriteAllText(path, node.ToJsonString());
                        if (pending)
                        {
                            using var selected = hooks.OpenMaintenance(metadata);
                            selected.MoveOwnedNew(AnalysisPrivateNames.PendingDeletion(current.CatalogId.Value, id, Guid.NewGuid()));
                        }
                    }
                    return hooks;
                }
                catch { hooks.Dispose(); throw; }
            }
            string device = PathUtil.GetDeviceKey(directoryPath);
            var owner = rolling ? await OwnedAnalysisStore.OpenRollingReviewAsync(profile.Services.Io, device, Prepare, token)
                : await OwnedAnalysisStore.OpenAsync(profile.Services.Io, device, Prepare, token);
            string payload = System.IO.Path.Combine(directoryPath, name), otherPath = System.IO.Path.Combine(directoryPath, other);
            return new() { Owner = owner, Hooks = hooks!, Root = close!, Id = id, Name = name, Path = payload, NeighborPath = otherPath,
                NeighborHash = RollingHash(otherPath), Original = await File.ReadAllBytesAsync(payload, token) };
        }
    }
}
