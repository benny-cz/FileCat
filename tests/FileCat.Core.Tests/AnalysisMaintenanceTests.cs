using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

[Collection("Analysis bounded work capacity")]
public sealed partial class AnalysisMaintenanceTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private static Guid Save(Directory directory, string name = "owned.fcdsa")
    {
        using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
        new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation);
        Guid id = Guid.NewGuid(); new AnalysisSavedCatalog(store).SaveNew(name, AnalysisAccountingTests.Golden(), id, AnalysisSavedView.Default, Cancellation); return id;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteUsesOnlyReviewedObjectsWithoutAllocatingAJournalEvenBelowSaveReserve(bool lowSpace)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler();
        Guid id = Save(directory); Save(directory, "other.fcdsa");
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "maintenance", () => directory, Cancellation);
        var review = await owned.PrepareDeleteAsync(id, Cancellation);
        var untouched = directory.Files.Where(p => p.Key != review.Entry.Name && !p.Key.Contains(id.ToString("N"), StringComparison.Ordinal)).ToArray();
        int publications = directory.Publications; directory.LowSpace = lowSpace;
        var outcome = await owned.DeleteAsync(review, Cancellation);
        Assert.Equal(OwnedAnalysisDeletionState.Deleted, outcome.State); Assert.True(outcome.PayloadAbsentConfirmed);
        Assert.Equal(publications, directory.Publications); Assert.False(directory.Files.ContainsKey(review.Entry.Name));
        foreach (var entry in untouched) Assert.Same(entry.Value, directory.Files[entry.Key]);
        using var catalog = await owned.ReadCatalogAsync(Cancellation); Assert.Single(catalog.Entries); Assert.Empty(catalog.PendingDeletions);
        Assert.Throws<InvalidOperationException>(() => { _ = owned.DeleteAsync(review, Cancellation); }); Assert.Equal(0, directory.Leases);
        directory.LowSpace = false; Assert.NotEqual(id, Save(directory));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ActivePayloadAndMetadataReadersRefusePreparationOrExecutionWithoutMutation(bool metadata, bool execution)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "reader-maintenance", () => directory, Cancellation);
        var review = execution ? await owned.PrepareDeleteAsync(id, Cancellation) : null;
        string name = metadata ? directory.Files.Keys.Single(AnalysisPrivateNames.IsCatalogEntry) : "owned.fcdsa";
        var before = directory.Files.ToArray(); using var reader = directory.OpenRead(name);
        if (execution)
        {
            var failure = await Assert.ThrowsAsync<OwnedAnalysisDeletionException>(() => owned.DeleteAsync(review!, Cancellation));
            Assert.Equal(OwnedAnalysisDeletionState.Kept, failure.Outcome.State);
        }
        else await Assert.ThrowsAsync<IOException>(() => owned.PrepareDeleteAsync(id, Cancellation));
        Assert.Equal(before, directory.Files.ToArray()); reader.Dispose();
        Assert.Equal(OwnedAnalysisDeletionState.Deleted, (await owned.DeleteAsync(await owned.PrepareDeleteAsync(id, Cancellation), Cancellation)).State);
    }

    [Theory]
    [InlineData("payload-object")]
    [InlineData("metadata-object")]
    [InlineData("payload-bytes")]
    [InlineData("profile")]
    [InlineData("catalog")]
    public async Task AReviewRefusesSubstitutionOrChangedBytesAndCannotBeReplayed(string change)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "stale-maintenance", () => directory, Cancellation);
        var review = await owned.PrepareDeleteAsync(id, Cancellation);
        string target = change switch { "metadata-object" => directory.Files.Keys.Single(AnalysisPrivateNames.IsCatalogEntry),
            "profile" => AnalysisPrivateNames.PrivateState, "catalog" => AnalysisPrivateNames.Catalog, _ => review.Entry.Name };
        var original = directory.Files[target];
        if (change.EndsWith("object", StringComparison.Ordinal)) directory.Files[target] = new(original.Bytes.ToArray());
        else { original.Bytes = original.Bytes.ToArray(); original.Bytes[original.Bytes.Length / 2] ^= 1; }
        var failure = await Assert.ThrowsAsync<OwnedAnalysisDeletionException>(() => owned.DeleteAsync(review, Cancellation));
        Assert.Equal(OwnedAnalysisDeletionState.Kept, failure.Outcome.State); Assert.True(directory.Files.ContainsKey(review.Entry.Name));
        Assert.DoesNotContain(directory.Files.Keys, AnalysisPrivateNames.IsPendingDeletion);
        Assert.Throws<InvalidOperationException>(() => { _ = owned.DeleteAsync(review, Cancellation); }); Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureOrCancellationBeforePayloadRemovalRestoresTheExactNamedEntry(bool cancel)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "rollback-maintenance", () => directory, Cancellation);
        var review = await owned.PrepareDeleteAsync(id, Cancellation); var original = directory.Files.ToArray();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        if (cancel) directory.AfterMove = (_, name) => { if (AnalysisPrivateNames.IsPendingDeletion(name)) stop.Cancel(); };
        else directory.BeforeRemove = _ => throw new IOException("Refused before unlink");
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owned.DeleteAsync(review, stop.Token));
        else Assert.Equal(OwnedAnalysisDeletionState.Kept, (await Assert.ThrowsAsync<OwnedAnalysisDeletionException>(() => owned.DeleteAsync(review, stop.Token))).Outcome.State);
        Assert.Equal(original.OrderBy(p => p.Key), directory.Files.OrderBy(p => p.Key)); Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnInterruptedPendingEntryRequiresFreshExplicitRecoveryAndNeverReplaysOnRead(bool restore)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "pending-maintenance", () => directory, Cancellation);
        var review = await owned.PrepareDeleteAsync(id, Cancellation);
        directory.BeforeRemove = _ => throw new IOException("Refused data removal");
        directory.BeforeMove = (_, destination) => { if (AnalysisPrivateNames.IsCatalogEntry(destination)) throw new IOException("Refused rollback"); };
        var failed = await Assert.ThrowsAsync<OwnedAnalysisDeletionException>(() => owned.DeleteAsync(review, Cancellation));
        Assert.Equal(OwnedAnalysisDeletionState.RecoveryRequired, failed.Outcome.State);
        using (var catalog = await owned.ReadCatalogAsync(Cancellation)) { Assert.Empty(catalog.Entries); Assert.True(Assert.Single(catalog.PendingDeletions).PayloadPresent); }
        Assert.True(directory.Files.ContainsKey("owned.fcdsa")); Assert.Throws<IOException>(() => Save(directory));
        Save(directory, "other.fcdsa"); directory.BeforeRemove = null; directory.BeforeMove = null;
        var fresh = await owned.PrepareDeleteAsync(id, Cancellation); Assert.True(fresh.IsRecovery);
        var result = restore ? await owned.RestoreNamedAsync(fresh, Cancellation) : await owned.DeleteAsync(fresh, Cancellation);
        Assert.Equal(restore ? OwnedAnalysisDeletionState.Restored : OwnedAnalysisDeletionState.Deleted, result.State);
        using var after = await owned.ReadCatalogAsync(Cancellation); Assert.Empty(after.PendingDeletions);
        Assert.Equal(restore, directory.Files.ContainsKey("owned.fcdsa")); Assert.Equal(0, directory.Leases);
    }

    [Fact]
    public async Task FailureAfterPayloadUnlinkLeavesAMetadataOnlyRecoveryThatCannotRestoreMissingData()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "unlinked-maintenance", () => directory, Cancellation);
        directory.AfterRemove = name => { if (name.EndsWith(".fcdsa", StringComparison.Ordinal)) throw new IOException("Unconfirmed unlink flush"); };
        var failure = await Assert.ThrowsAsync<OwnedAnalysisDeletionException>(() => Delete());
        Assert.Equal(OwnedAnalysisDeletionState.Uncertain, failure.Outcome.State);
        using (var pending = await owned.ReadCatalogAsync(Cancellation)) Assert.False(Assert.Single(pending.PendingDeletions).PayloadPresent);
        var fresh = await owned.PrepareDeleteAsync(id, Cancellation); Assert.True(fresh.IsRecovery); Assert.False(fresh.PayloadPresent);
        Assert.Throws<InvalidOperationException>(() => { _ = owned.RestoreNamedAsync(fresh, Cancellation); });
        directory.AfterRemove = null; Assert.Equal(OwnedAnalysisDeletionState.Deleted, (await owned.DeleteAsync(fresh, Cancellation)).State);
        using var after = await owned.ReadCatalogAsync(Cancellation); Assert.Empty(after.Entries); Assert.Empty(after.PendingDeletions); Assert.Equal(0, directory.Leases);
        async Task Delete() => await owned.DeleteAsync(await owned.PrepareDeleteAsync(id, Cancellation), Cancellation);
    }

    [Fact]
    public async Task APostMoveFailureWithRefusedRollbackReportsPendingAndPostMetadataUnlinkReportsUncertainty()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "move-maintenance", () => directory, Cancellation);
        directory.AfterMove = (_, destination) => { if (AnalysisPrivateNames.IsPendingDeletion(destination)) throw new IOException("Unconfirmed rename flush"); };
        directory.BeforeMove = (_, destination) => { if (AnalysisPrivateNames.IsCatalogEntry(destination)) throw new IOException("Rollback unavailable"); };
        var failure = await Assert.ThrowsAsync<OwnedAnalysisDeletionException>(() => Delete());
        Assert.Equal(OwnedAnalysisDeletionState.RecoveryRequired, failure.Outcome.State);
        using (var pending = await owned.ReadCatalogAsync(Cancellation)) Assert.True(Assert.Single(pending.PendingDeletions).PayloadPresent);
        directory.AfterMove = null; directory.BeforeMove = null;
        directory.AfterRemove = name => { if (AnalysisPrivateNames.IsPendingDeletion(name)) throw new IOException("Unconfirmed metadata unlink flush"); };
        failure = await Assert.ThrowsAsync<OwnedAnalysisDeletionException>(() => Delete());
        Assert.Equal(OwnedAnalysisDeletionState.Uncertain, failure.Outcome.State); Assert.True(failure.Outcome.PayloadAbsentConfirmed);
        using var after = await owned.ReadCatalogAsync(Cancellation); Assert.Empty(after.Entries); Assert.Empty(after.PendingDeletions); Assert.Equal(0, directory.Leases);
        async Task Delete() => await owned.DeleteAsync(await owned.PrepareDeleteAsync(id, Cancellation), Cancellation);
    }

    [Fact]
    public async Task AReviewCannotBeReboundToAnotherStoreAndWrongOwnerDoesNotConsumeIt()
    {
        Ordinary(); using var directory = new Directory(); using var other = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory); Save(other);
        await using var first = await OwnedAnalysisStore.OpenAsync(io, "first-maintenance", () => directory, Cancellation);
        await using var second = await OwnedAnalysisStore.OpenAsync(io, "second-maintenance", () => other, Cancellation);
        var review = await first.PrepareDeleteAsync(id, Cancellation);
        Assert.Throws<ArgumentException>(() => { _ = second.DeleteAsync(review, Cancellation); });
        Assert.Equal(OwnedAnalysisDeletionState.Deleted, (await first.DeleteAsync(review, Cancellation)).State);
        Assert.Single(other.Files.Keys, n => n.EndsWith(".fcdsa", StringComparison.Ordinal));
    }
}
