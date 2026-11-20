using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisMaintenanceTests
{
    private static (Guid Catalog, Guid Previous, Guid Incoming, Guid Operation, byte[] OldBytes, byte[] NewBytes) RecoveryFixture(Directory directory, string state)
    {
        Guid old = Save(directory), incoming = Save(directory, "incoming.fcdsa"), operation = Guid.NewGuid();
        Save(directory, "other.fcdsa");
        byte[] oldBytes = directory.Files["owned.fcdsa"].Bytes.ToArray(), newBytes = directory.Files["incoming.fcdsa"].Bytes.ToArray();
        Guid catalog;
        using (var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true))
        using (var before = new AnalysisSavedCatalog(store).Read(Cancellation)) catalog = before.CatalogId!.Value;
        string oldMetadata = AnalysisPrivateNames.CatalogEntry(catalog, old), freshMetadata = AnalysisPrivateNames.CatalogEntry(catalog, incoming);
        directory.Files[AnalysisPrivateNames.PendingReplacement(catalog, old, incoming, operation)] = directory.Files[oldMetadata]; directory.Files.Remove(oldMetadata);
        if (state is "committed" or "cleaning" or "missing-committed") Change(directory.Files[freshMetadata], "name", "owned.fcdsa");
        else directory.Files.Remove(freshMetadata);
        if (state is "switched" or "committed" or "cleaning") directory.Files["owned.fcdsa"] = new(newBytes);
        if (state is "missing" or "missing-committed") directory.Files.Remove("owned.fcdsa");
        if (state is "staged" or "backed" or "missing" or "missing-committed") directory.Files[AnalysisPrivateNames.ReplacementIncoming(incoming, operation)] = new(newBytes);
        if (state is "backed" or "switched" or "committed" or "missing" or "missing-committed") directory.Files[AnalysisPrivateNames.ReplacementPrevious(old, operation)] = new(oldBytes);
        directory.Files.Remove("incoming.fcdsa");
        return (catalog, old, incoming, operation, oldBytes, newBytes);
    }

    [Theory]
    [InlineData("marker", true)]
    [InlineData("staged", true)]
    [InlineData("backed", true)]
    [InlineData("switched", true)]
    [InlineData("committed", true)]
    [InlineData("missing", true)]
    [InlineData("missing-committed", true)]
    [InlineData("staged", false)]
    [InlineData("backed", false)]
    [InlineData("switched", false)]
    [InlineData("committed", false)]
    [InlineData("cleaning", false)]
    [InlineData("missing", false)]
    [InlineData("missing-committed", false)]
    public async Task FreshReplacementRecoveryUsesActualCompleteFilesAcrossInterruptedPublicationBoundaries(string state, bool restore)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); var fixture = RecoveryFixture(directory, state);
        var untouched = directory.Files["other.fcdsa"]; var before = directory.Files.ToArray();
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "fresh-recovery", () => directory, Cancellation);
        var review = await owned.PrepareReplacementRecoveryAsync(fixture.Operation, Cancellation);
        Assert.Equal(before, directory.Files.ToArray()); Assert.True(restore ? review.CanRestorePrevious : review.CanFinishIncoming);
        var result = await owned.RecoverReplacementAsync(review, restore ? OwnedAnalysisRecoveryAction.RestorePrevious : OwnedAnalysisRecoveryAction.FinishIncoming, cancellation: Cancellation);
        Assert.Equal(restore ? OwnedAnalysisRecoveryState.Restored : OwnedAnalysisRecoveryState.FinishedIncoming, result.State);
        Assert.Equal(restore ? fixture.Previous : fixture.Incoming, result.Receipt!.InventoryId);
        Assert.Equal(restore ? fixture.OldBytes : fixture.NewBytes, directory.Files["owned.fcdsa"].Bytes);
        Assert.Same(untouched, directory.Files["other.fcdsa"]); Assert.DoesNotContain(directory.Files.Keys, AnalysisPrivateNames.IsReplacementPayload);
        using var catalog = await owned.ReadCatalogAsync(Cancellation); Assert.Empty(catalog.PendingReplacements); Assert.Equal(2, catalog.Entries.Length);
        Assert.Contains(catalog.Entries, e => e.InventoryId == result.Receipt.InventoryId && e.Name == "owned.fcdsa"); Assert.Equal(0, directory.Leases);
        Assert.Throws<InvalidOperationException>(() => { _ = owned.RecoverReplacementAsync(review, restore ? OwnedAnalysisRecoveryAction.RestorePrevious : OwnedAnalysisRecoveryAction.FinishIncoming, cancellation: Cancellation); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownOrCorruptOriginalIsPreservedAndAVerifiedPreviousCopyCanUseANewName(bool corrupt)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); var fixture = RecoveryFixture(directory, "switched");
        byte[] foreign;
        if (corrupt) foreign = [1, 2, 3];
        else { using var bytes = new MemoryStream(); AnalysisInventoryFormat.Write(bytes, AnalysisAccountingTests.Golden(), Guid.NewGuid(), Cancellation); foreign = bytes.ToArray(); }
        directory.Files["owned.fcdsa"] = new(foreign); var before = directory.Files.ToArray();
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "preserving-recovery", () => directory, Cancellation);
        var review = await owned.PrepareReplacementRecoveryAsync(fixture.Operation, Cancellation);
        Assert.False(review.CanRestorePrevious); Assert.False(review.CanFinishIncoming); Assert.NotNull(review.CopySource); Assert.NotNull(review.BlockedReason);
        Assert.Throws<InvalidOperationException>(() => { _ = owned.RecoverReplacementAsync(review, OwnedAnalysisRecoveryAction.RestorePrevious, cancellation: Cancellation); });
        var result = await owned.RecoverReplacementAsync(review, OwnedAnalysisRecoveryAction.CopyPrevious, "recovered.fcdsa", Cancellation);
        Assert.Equal(OwnedAnalysisRecoveryState.CopiedPrevious, result.State); Assert.Equal(fixture.OldBytes, directory.Files["recovered.fcdsa"].Bytes);
        foreach (var pair in before) Assert.Same(pair.Value, directory.Files[pair.Key]);
        using var catalog = await owned.ReadCatalogAsync(Cancellation); Assert.Single(catalog.PendingReplacements); Assert.Contains("recovered.fcdsa", catalog.UnindexedNames);
        Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData("original")]
    [InlineData("marker")]
    [InlineData("previous-bytes")]
    [InlineData("profile")]
    public async Task RecoveryRevalidatesRuntimeObjectsAndFullBytesBeforeAnyMutation(string change)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); var fixture = RecoveryFixture(directory, "committed");
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "stale-recovery", () => directory, Cancellation);
        var review = await owned.PrepareReplacementRecoveryAsync(fixture.Operation, Cancellation);
        string target = change switch { "marker" => RecoveryMarkerName(fixture), "profile" => AnalysisPrivateNames.PrivateState,
            "previous-bytes" => AnalysisPrivateNames.ReplacementPrevious(fixture.Previous, fixture.Operation), _ => "owned.fcdsa" };
        if (change is "original" or "marker") directory.Files[target] = new(directory.Files[target].Bytes.ToArray());
        else { var bytes = directory.Files[target].Bytes.ToArray(); bytes[bytes.Length / 2] ^= 1; directory.Files[target].Bytes = bytes; }
        var before = directory.Files.ToArray();
        var error = await Assert.ThrowsAsync<OwnedAnalysisRecoveryException>(() => owned.RecoverReplacementAsync(review, OwnedAnalysisRecoveryAction.RestorePrevious, cancellation: Cancellation));
        Assert.Equal(OwnedAnalysisRecoveryState.Kept, error.Outcome.State); Assert.Equal(before, directory.Files.ToArray()); Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData("original")]
    [InlineData("marker")]
    [InlineData("previous")]
    public async Task RecoveryRefusesActiveReadersWithoutWeakeningTheirProtection(string target)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); var fixture = RecoveryFixture(directory, "committed");
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "reader-recovery", () => directory, Cancellation);
        var review = await owned.PrepareReplacementRecoveryAsync(fixture.Operation, Cancellation); var before = directory.Files.ToArray();
        using (directory.OpenRead(target == "original" ? "owned.fcdsa" : target == "marker" ? RecoveryMarkerName(fixture) : review.Previous.Name))
        {
            var error = await Assert.ThrowsAsync<OwnedAnalysisRecoveryException>(() => owned.RecoverReplacementAsync(review, OwnedAnalysisRecoveryAction.RestorePrevious, cancellation: Cancellation));
            Assert.Equal(OwnedAnalysisRecoveryState.Kept, error.Outcome.State); Assert.Equal(before, directory.Files.ToArray());
        }
        Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecoveryFinishingAndRestoringExistingCompleteCopiesNeedNoNewSpace(bool restore)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); var fixture = RecoveryFixture(directory, "committed");
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "reserve-recovery", () => directory, Cancellation);
        var review = await owned.PrepareReplacementRecoveryAsync(fixture.Operation, Cancellation); directory.LowSpace = true; int publications = directory.Publications;
        var result = await owned.RecoverReplacementAsync(review, restore ? OwnedAnalysisRecoveryAction.RestorePrevious : OwnedAnalysisRecoveryAction.FinishIncoming, cancellation: Cancellation);
        Assert.Equal(restore ? OwnedAnalysisRecoveryState.Restored : OwnedAnalysisRecoveryState.FinishedIncoming, result.State);
        Assert.Equal(publications, directory.Publications); Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData("missing", true)]
    [InlineData("staged", false)]
    public async Task RecoveryThatNeedsANewCompleteCopyPreservesTheReserveAndAllExistingData(string state, bool restore)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); var fixture = RecoveryFixture(directory, state);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "copy-reserve-recovery", () => directory, Cancellation);
        var review = await owned.PrepareReplacementRecoveryAsync(fixture.Operation, Cancellation); var before = directory.Files.ToArray(); directory.LowSpace = true;
        var error = await Assert.ThrowsAsync<OwnedAnalysisRecoveryException>(() => owned.RecoverReplacementAsync(review, restore ? OwnedAnalysisRecoveryAction.RestorePrevious : OwnedAnalysisRecoveryAction.FinishIncoming, cancellation: Cancellation));
        Assert.Equal(OwnedAnalysisRecoveryState.RecoveryRequired, error.Outcome.State); Assert.Equal(before, directory.Files.ToArray()); Assert.Equal(0, directory.Leases);
    }

    [Fact]
    public async Task RecoveryCleanupRefusalRetainsConfirmedIncomingReceiptAndCanBeFreshlyFinished()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); var fixture = RecoveryFixture(directory, "committed");
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "cleanup-recovery", () => directory, Cancellation);
        var review = await owned.PrepareReplacementRecoveryAsync(fixture.Operation, Cancellation);
        directory.BeforeRemove = name => { if (name == review.Previous.Name) throw new IOException("Cleanup unavailable"); };
        var result = await owned.RecoverReplacementAsync(review, OwnedAnalysisRecoveryAction.FinishIncoming, cancellation: Cancellation);
        Assert.Equal(OwnedAnalysisRecoveryState.IncomingSavedCleanupPending, result.State); Assert.Equal(fixture.Incoming, result.Receipt!.InventoryId);
        directory.BeforeRemove = null;
        var finished = await owned.RecoverReplacementAsync(await owned.PrepareReplacementRecoveryAsync(fixture.Operation, Cancellation), OwnedAnalysisRecoveryAction.FinishIncoming, cancellation: Cancellation);
        Assert.Equal(OwnedAnalysisRecoveryState.FinishedIncoming, finished.State); Assert.Equal(0, directory.Leases);
    }

    [Fact]
    public async Task CancellationAfterRetiringCurrentMetadataPreservesVersionsForAFreshDecision()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); var fixture = RecoveryFixture(directory, "committed");
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "cancel-recovery", () => directory, Cancellation);
        var review = await owned.PrepareReplacementRecoveryAsync(fixture.Operation, Cancellation);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        directory.AfterRemove = name => { if (name == AnalysisPrivateNames.CatalogEntry(fixture.Catalog, fixture.Incoming)) stop.Cancel(); };
        var error = await Assert.ThrowsAsync<OwnedAnalysisRecoveryException>(() => owned.RecoverReplacementAsync(review, OwnedAnalysisRecoveryAction.RestorePrevious, cancellation: stop.Token));
        Assert.Equal(OwnedAnalysisRecoveryState.RecoveryRequired, error.Outcome.State);
        Assert.Equal(fixture.NewBytes, directory.Files["owned.fcdsa"].Bytes); Assert.Equal(fixture.OldBytes, directory.Files[review.Previous.Name].Bytes);
        directory.AfterRemove = null; var fresh = await owned.PrepareReplacementRecoveryAsync(fixture.Operation, Cancellation);
        Assert.True(fresh.CanFinishIncoming); Assert.True(fresh.CanRestorePrevious);
        Assert.Equal(OwnedAnalysisRecoveryState.Restored, (await owned.RecoverReplacementAsync(fresh, OwnedAnalysisRecoveryAction.RestorePrevious, cancellation: Cancellation)).State);
        Assert.Equal(0, directory.Leases);
    }

    [Fact]
    public async Task RecoveryCopyRefusesACollisionAndWrongOwnerWithoutConsumingTheRightOwnersReview()
    {
        Ordinary(); using var directory = new Directory(); using var other = new Directory(); using var io = new DeviceIoScheduler(); var fixture = RecoveryFixture(directory, "committed"); Save(other);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "owner-recovery", () => directory, Cancellation);
        await using var foreign = await OwnedAnalysisStore.OpenAsync(io, "foreign-recovery", () => other, Cancellation);
        var review = await owned.PrepareReplacementRecoveryAsync(fixture.Operation, Cancellation);
        Assert.Throws<ArgumentException>(() => { _ = foreign.RecoverReplacementAsync(review, OwnedAnalysisRecoveryAction.RestorePrevious, cancellation: Cancellation); });
        var before = directory.Files.ToArray();
        var error = await Assert.ThrowsAsync<OwnedAnalysisRecoveryException>(() => owned.RecoverReplacementAsync(review, OwnedAnalysisRecoveryAction.CopyPrevious, "OTHER.fcdsa", Cancellation));
        Assert.Equal(OwnedAnalysisRecoveryState.Kept, error.Outcome.State); Assert.Equal(before, directory.Files.ToArray()); Assert.Equal(0, directory.Leases);
    }

    private static string RecoveryMarkerName((Guid Catalog, Guid Previous, Guid Incoming, Guid Operation, byte[] OldBytes, byte[] NewBytes) fixture) =>
        AnalysisPrivateNames.PendingReplacement(fixture.Catalog, fixture.Previous, fixture.Incoming, fixture.Operation);

    [Fact]
    public async Task RecoveryMetadataCleanupUncertaintyDoesNotLoseTheConfirmedIncomingSave()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); var fixture = RecoveryFixture(directory, "committed");
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "uncertain-cleanup-recovery", () => directory, Cancellation);
        var review = await owned.PrepareReplacementRecoveryAsync(fixture.Operation, Cancellation);
        directory.AfterRemove = name => { if (AnalysisPrivateNames.IsPendingReplacement(name)) throw new IOException("Unconfirmed cleanup durability"); };
        var error = await Assert.ThrowsAsync<OwnedAnalysisRecoveryException>(() => owned.RecoverReplacementAsync(review, OwnedAnalysisRecoveryAction.FinishIncoming, cancellation: Cancellation));
        Assert.Equal(OwnedAnalysisRecoveryState.Uncertain, error.Outcome.State); Assert.Equal(fixture.Incoming, error.Outcome.Receipt!.InventoryId);
        Assert.StartsWith("The incoming named analysis was saved, but cleanup", error.Outcome.Detail);
        using var catalog = await owned.ReadCatalogAsync(Cancellation); Assert.Empty(catalog.PendingReplacements); Assert.Contains(catalog.Entries, e => e.InventoryId == fixture.Incoming);
        Assert.Equal(0, directory.Leases);
    }
}
