using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisMaintenanceTests
{
    [Fact]
    public async Task PreparedReplacementKeepsOriginalBytesAndRetainsStoreUntilTheDecisionAndActualCleanup()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        var original = directory.Files.ToArray(); var owned = await OwnedAnalysisStore.OpenAsync(io, "replacement-keep", () => directory, Cancellation);
        var review = await owned.PrepareReplacementAsync(id, AnalysisAccountingTests.Golden(), AnalysisSavedView.Default, Cancellation);
        Assert.Equal(original, directory.Files.ToArray()); Assert.True(directory.Leases > 0);
        Assert.Equal(id, review.Previous.Entry.InventoryId); Assert.NotEqual(id, review.Incoming.InventoryId);
        Assert.NotEqual(default, review.Previous.StartedUtc); Assert.NotEmpty(review.Incoming.Sha256);
        var close = owned.DisposeAsync().AsTask(); Assert.False(close.IsCompleted);
        await review.DisposeAsync(); await close;
        Assert.Equal(original, directory.Files.ToArray()); Assert.Equal(0, directory.Leases);
        Assert.Throws<InvalidOperationException>(() => { _ = owned.ReplaceAsync(review, Cancellation); });
    }

    [Fact]
    public async Task ReplacementCommitsNewIdentityAtTheOriginalNameAndRetiresOnlyItsOwnPreviousVersion()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory); Save(directory, "other.fcdsa");
        var other = directory.Files["other.fcdsa"];
        var owned = await OwnedAnalysisStore.OpenAsync(io, "replacement-success", () => directory, Cancellation);
        var review = await owned.PrepareReplacementAsync(id, AnalysisAccountingTests.Golden(), AnalysisSavedView.Default, Cancellation);
        var close = owned.DisposeAsync().AsTask(); Assert.False(close.IsCompleted);
        var result = await owned.ReplaceAsync(review, Cancellation); await close;
        Assert.Equal(OwnedAnalysisReplacementState.Replaced, result.State); Assert.Equal(review.Incoming.InventoryId, result.Receipt!.InventoryId);
        Assert.Equal("owned.fcdsa", result.Receipt.Name); Assert.Same(other, directory.Files["other.fcdsa"]);
        using var raw = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); using var catalog = new AnalysisSavedCatalog(raw).Read(Cancellation);
        Assert.Equal(2, catalog.Entries.Length); Assert.Empty(catalog.PendingReplacements); Assert.DoesNotContain(catalog.Entries, e => e.InventoryId == id);
        using var loaded = raw.Load("owned.fcdsa", Cancellation); Assert.Equal(review.Incoming.InventoryId, loaded.InventoryId);
        Assert.DoesNotContain(directory.Files.Keys, AnalysisPrivateNames.IsReplacementPayload); Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData("payload-object")]
    [InlineData("payload-bytes")]
    [InlineData("metadata-object")]
    [InlineData("profile")]
    public async Task PreparedReplacementRevalidatesOldObjectsAndBytesAtExecutionWithoutChangingUnknownData(string change)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "replacement-stale", () => directory, Cancellation);
        var review = await owned.PrepareReplacementAsync(id, AnalysisAccountingTests.Golden(), AnalysisSavedView.Default, Cancellation);
        string name = change switch { "metadata-object" => directory.Files.Keys.Single(AnalysisPrivateNames.IsCatalogEntry), "profile" => AnalysisPrivateNames.PrivateState, _ => "owned.fcdsa" };
        if (change.EndsWith("object", StringComparison.Ordinal)) directory.Files[name] = new(directory.Files[name].Bytes.ToArray());
        else { var bytes = directory.Files[name].Bytes.ToArray(); bytes[bytes.Length / 2] ^= 1; directory.Files[name].Bytes = bytes; }
        var before = directory.Files.ToArray();
        var failure = await Assert.ThrowsAsync<OwnedAnalysisReplacementException>(() => owned.ReplaceAsync(review, Cancellation));
        Assert.Equal(OwnedAnalysisReplacementState.Kept, failure.Outcome.State); Assert.Equal(before, directory.Files.ToArray());
        Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementRefusesActiveReadersAfterReviewAndDoesNotWeakenTheirLease(bool metadata)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "replacement-reader", () => directory, Cancellation);
        var review = await owned.PrepareReplacementAsync(id, AnalysisAccountingTests.Golden(), AnalysisSavedView.Default, Cancellation);
        var before = directory.Files.ToArray(); string name = metadata ? directory.Files.Keys.Single(AnalysisPrivateNames.IsCatalogEntry) : "owned.fcdsa";
        using (directory.OpenRead(name))
        {
            var failure = await Assert.ThrowsAsync<OwnedAnalysisReplacementException>(() => owned.ReplaceAsync(review, Cancellation));
            Assert.Equal(OwnedAnalysisReplacementState.Kept, failure.Outcome.State); Assert.Equal(before, directory.Files.ToArray());
        }
        Assert.Equal(0, directory.Leases);
    }

    [Fact]
    public async Task LowBackupSpacePreservesTheOldFileAndRestoresMetadataWithoutWeakerReplacement()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "replacement-space", () => directory, Cancellation);
        var review = await owned.PrepareReplacementAsync(id, AnalysisAccountingTests.Golden(), AnalysisSavedView.Default, Cancellation);
        var before = directory.Files.ToArray(); directory.LowSpace = true;
        var failure = await Assert.ThrowsAsync<OwnedAnalysisReplacementException>(() => owned.ReplaceAsync(review, Cancellation));
        Assert.Equal(OwnedAnalysisReplacementState.Kept, failure.Outcome.State);
        Assert.Equal(before.OrderBy(p => p.Key), directory.Files.OrderBy(p => p.Key)); Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeCatalogCommitRestoresTheCompletePreviousBytes(bool afterSwitch)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "replacement-cancel", () => directory, Cancellation);
        var review = await owned.PrepareReplacementAsync(id, AnalysisAccountingTests.Golden(), AnalysisSavedView.Default, Cancellation);
        byte[] before = directory.Files["owned.fcdsa"].Bytes.ToArray();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        if (afterSwitch) directory.AfterReplace = (source, _) => { if (source.StartsWith("incoming-", StringComparison.Ordinal)) stop.Cancel(); };
        else directory.AfterMove = (_, target) => { if (AnalysisPrivateNames.IsPendingReplacement(target)) stop.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owned.ReplaceAsync(review, stop.Token));
        Assert.Equal(before, directory.Files["owned.fcdsa"].Bytes); Assert.DoesNotContain(directory.Files.Keys, AnalysisPrivateNames.IsReplacementPayload);
        using var catalog = await owned.ReadCatalogAsync(Cancellation); Assert.Equal(id, Assert.Single(catalog.Entries).InventoryId); Assert.Empty(catalog.PendingReplacements);
        Assert.Equal(0, directory.Leases);
    }

    [Fact]
    public async Task FailedRollbackRetainsBothCompleteVersionsAndAnInertPendingRecord()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "replacement-rollback", () => directory, Cancellation);
        var review = await owned.PrepareReplacementAsync(id, AnalysisAccountingTests.Golden(), AnalysisSavedView.Default, Cancellation);
        byte[] old = directory.Files["owned.fcdsa"].Bytes.ToArray();
        directory.AfterReplace = (source, _) => { if (source.StartsWith("incoming-", StringComparison.Ordinal)) throw new IOException("After switch"); };
        directory.BeforeReplace = (source, _) => { if (source.StartsWith("previous-", StringComparison.Ordinal)) throw new IOException("Rollback unavailable"); };
        var failure = await Assert.ThrowsAsync<OwnedAnalysisReplacementException>(() => owned.ReplaceAsync(review, Cancellation));
        Assert.Equal(OwnedAnalysisReplacementState.RecoveryRequired, failure.Outcome.State);
        var beforeRead = directory.Files.ToArray(); using var catalog = await owned.ReadCatalogAsync(Cancellation);
        var pending = Assert.Single(catalog.PendingReplacements); Assert.True(pending.OriginalPresent); Assert.True(pending.PreviousPresent); Assert.Null(pending.Current);
        Assert.Equal(old, directory.Files[AnalysisPrivateNames.ReplacementPrevious(id, pending.OperationId)].Bytes);
        Assert.Equal(beforeRead, directory.Files.ToArray()); Assert.Throws<IOException>(() => Save(directory)); Assert.Equal(0, directory.Leases);
    }

    [Fact]
    public async Task NewCatalogPublicationFailurePreservesPendingCopiesInsteadOfClaimingTheOriginalWasRestored()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "replacement-catalog", () => directory, Cancellation);
        var review = await owned.PrepareReplacementAsync(id, AnalysisAccountingTests.Golden(), AnalysisSavedView.Default, Cancellation);
        directory.BeforePublish = name => { if (AnalysisPrivateNames.IsCatalogEntry(name)) throw new IOException("Catalog unavailable"); };
        var failure = await Assert.ThrowsAsync<OwnedAnalysisReplacementException>(() => owned.ReplaceAsync(review, Cancellation));
        Assert.Equal(OwnedAnalysisReplacementState.RecoveryRequired, failure.Outcome.State); Assert.Null(failure.Outcome.Receipt);
        using var catalog = await owned.ReadCatalogAsync(Cancellation); Assert.True(Assert.Single(catalog.PendingReplacements).PreviousPresent);
        Assert.Equal(0, directory.Leases);
    }

    [Fact]
    public async Task CleanupRefusalAfterLogicalCommitReportsNewSaveAndPreviousCleanupSeparately()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "replacement-cleanup", () => directory, Cancellation);
        var review = await owned.PrepareReplacementAsync(id, AnalysisAccountingTests.Golden(), AnalysisSavedView.Default, Cancellation);
        directory.BeforeRemove = name => { if (name.StartsWith("previous-", StringComparison.Ordinal)) throw new IOException("Cleanup unavailable"); };
        var result = await owned.ReplaceAsync(review, Cancellation);
        Assert.Equal(OwnedAnalysisReplacementState.ReplacedPreviousKept, result.State); Assert.Equal(review.Incoming.InventoryId, result.Receipt!.InventoryId);
        using var catalog = await owned.ReadCatalogAsync(Cancellation); var pending = Assert.Single(catalog.PendingReplacements);
        Assert.Equal(review.Incoming.InventoryId, pending.Current!.InventoryId); Assert.True(pending.PreviousPresent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => owned.PrepareDeleteAsync(id, Cancellation)); Assert.Equal(0, directory.Leases);
    }

    [Fact]
    public async Task APreparedDecisionCannotBeReboundAndAForeignAttemptDoesNotConsumeIt()
    {
        Ordinary(); using var directory = new Directory(); using var other = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory); Save(other);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "replacement-owner", () => directory, Cancellation);
        await using var foreign = await OwnedAnalysisStore.OpenAsync(io, "replacement-foreign", () => other, Cancellation);
        var review = await owned.PrepareReplacementAsync(id, AnalysisAccountingTests.Golden(), AnalysisSavedView.Default, Cancellation);
        Assert.Throws<ArgumentException>(() => { _ = foreign.ReplaceAsync(review, Cancellation); });
        Assert.Equal(OwnedAnalysisReplacementState.Kept, (await owned.KeepExistingAsync(review)).State);
        Assert.Throws<InvalidOperationException>(() => { _ = owned.KeepExistingAsync(review); }); Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementPublicationReopenCannotGrantCleanupAuthorityOverASubstitutedName(bool previous)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Guid id = Save(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "replacement-publication-substitution", () => directory, Cancellation);
        var review = await owned.PrepareReplacementAsync(id, AnalysisAccountingTests.Golden(), AnalysisSavedView.Default, Cancellation);
        var original = directory.Files["owned.fcdsa"]; Node? foreign = null; string? substituted = null;
        directory.BeforeReplacementOpen = name =>
        {
            if (!name.StartsWith(previous ? "previous-" : "incoming-", StringComparison.Ordinal)) return;
            substituted = name; foreign = new(directory.Files[name].Bytes.ToArray()); directory.Files[name] = foreign;
        };
        var failure = await Assert.ThrowsAsync<OwnedAnalysisReplacementException>(() => owned.ReplaceAsync(review, Cancellation));
        Assert.Equal(OwnedAnalysisReplacementState.RecoveryRequired, failure.Outcome.State);
        Assert.Same(original, directory.Files["owned.fcdsa"]); Assert.Same(foreign, directory.Files[substituted!]);
        using var catalog = await owned.ReadCatalogAsync(Cancellation); Assert.Single(catalog.PendingReplacements); Assert.Equal(0, directory.Leases);
    }
}
