namespace FileCat.Core.Analysis;

/// <summary>One fresh runtime review over an existing complete unpublished-in-catalog history object.</summary>
internal sealed class AnalysisRollingRecoveryReview
{
    internal readonly OwnedAnalysisStore Owner;
    internal readonly Guid? CatalogId;
    internal readonly string NativeIdentity;
    internal readonly AnalysisInventoryFormat.ContainerInspection Inspection;
    private int _used;
    internal string Name { get; }
    internal Guid InventoryId => Inspection.Id;
    internal long Bytes => Inspection.Bytes;
    internal string Sha256 => Inspection.Sha256;
    internal AnalysisRollingRecoveryReview(OwnedAnalysisStore owner, Guid? catalog, string name, string identity,
        AnalysisInventoryFormat.ContainerInspection inspection)
    { Owner = owner; CatalogId = catalog; Name = name; NativeIdentity = identity; Inspection = inspection; }
    internal void Consume(OwnedAnalysisStore owner)
    {
        if (!ReferenceEquals(Owner, owner) || Interlocked.Exchange(ref _used, 1) != 0)
            throw new InvalidOperationException("Obtain a fresh review of this history copy.");
    }
}

public sealed partial class OwnedAnalysisStore
{
    internal Task<AnalysisRollingRecoveryReview> PrepareRollingRecoveryAsync(string name, CancellationToken cancellation) =>
        _owned.UseAsync(async data =>
        {
            if (!data.Rolling || !TryRollingName(name, out Guid id)) throw new InvalidOperationException("A generated history copy is required.");
            AnalysisPrivateInventoryStore.DatasetRead? input = null;
            try
            {
                var catalogId = await AnalysisIo.Run(_io, _device, token =>
                {
                    RequireProfile(data, token);
                    using var catalog = new AnalysisSavedCatalog(data.Store!).Read(token);
                    RequireUnindexed(data, catalog, name, token);
                    input = AnalysisPrivateInventoryStore.BeginDatasetRead(data.Store!.BorrowDirectory(), name, token);
                    return catalog.CatalogId;
                }, cancellation).ConfigureAwait(false);
                var inspection = await AnalysisInventoryFormat.InspectContainerAsync(input!, _io, _device, cancellation).ConfigureAwait(false);
                if (inspection.Id != id) throw new InvalidDataException("The history filename and complete inventory identity disagree. Existing data was kept.");
                return await AnalysisIo.Run(_io, _device, token =>
                {
                    input!.Verify(); RequireProfile(data, token);
                    using var catalog = new AnalysisSavedCatalog(data.Store!).Read(token);
                    RequireUnindexed(data, catalog, name, token);
                    if (catalog.CatalogId != catalogId) throw new InvalidDataException("History changed during inspection. Inspect it again.");
                    return new AnalysisRollingRecoveryReview(this, catalogId, name, input.Identity, inspection);
                }, cancellation).ConfigureAwait(false);
            }
            finally { if (input is not null) await AnalysisIo.ReleaseScratchAsync(_io, _device, input).ConfigureAwait(false); }
        });

    internal async Task<AnalysisRollingCaptureOutcome> RecoverRollingAsync(AnalysisRollingRecoveryReview review, AnalysisIndexedInventory validated, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(review); ArgumentNullException.ThrowIfNull(validated);
        if (validated.InventoryId != review.InventoryId || validated.SourceBytes != review.Bytes || validated.SourceSha256 != review.Sha256)
            throw new InvalidDataException("Recovery requires graph validation of the exact reviewed complete bytes.");
        var retained = await validated.AcquireAsync(cancellation).ConfigureAwait(false);
        return await FinishRollingAsync(() =>
        {
            review.Consume(this);
            return _owned.UseAsync(async data =>
        {
            var work = new RollingWork(); bool cataloged = false; Exception? failure = null;
            var receipt = new AnalysisInventoryReceipt(review.InventoryId, review.Name, review.Bytes);
            try
            {
                await AnalysisIo.Run(_io, _device, token =>
                {
                    RequireProfile(data, token); work.Writer = data.Store!.OpenCatalogLock(token);
                    using var catalog = new AnalysisSavedCatalog(data.Store).Read(token);
                    RequireUnindexed(data, catalog, review.Name, token);
                    if (catalog.CatalogId != review.CatalogId) throw new InvalidDataException("History changed after recovery review.");
                    UInt128 bytes = catalog.Entries.Concat(catalog.PendingDeletions.Where(pending => pending.PayloadPresent).Select(pending => pending.Entry))
                        .Aggregate((UInt128)review.Bytes, (sum, entry) => sum + (UInt128)entry.Bytes);
                    if (bytes > (UInt128)AnalysisRollingHistoryPolicy.ByteHardLimit) throw new InvalidDataException("Recovering this entry would exceed the absolute history byte bound. Keep it and save an independent named copy before reviewing removal.");
                    work.Incoming = AnalysisPrivateInventoryStore.BeginDatasetRead(data.Store.BorrowDirectory(), review.Name, token);
                    if (work.Incoming.Identity != review.NativeIdentity) throw new InvalidDataException("The reviewed history object changed.");
                    return true;
                }, cancellation).ConfigureAwait(false);
                var inspected = await AnalysisInventoryFormat.InspectContainerAsync(work.Incoming!, _io, _device, cancellation).ConfigureAwait(false);
                if (inspected != review.Inspection) throw new InvalidDataException("The complete history bytes changed after review. Inspect them again.");
                await AnalysisIo.Run(_io, _device, token =>
                {
                    RequireProfile(data, token); work.Writer!.Verify(); work.Incoming!.Verify();
                    using (var current = new AnalysisSavedCatalog(data.Store!).Read(token))
                    {
                        RequireUnindexed(data, current, review.Name, token);
                        if (current.CatalogId != review.CatalogId) throw new InvalidDataException("History changed during recovery.");
                    }
                    using var writing = new AnalysisSavedCatalog(data.Store!).BeginPublishedWriteUnderLock(work.Writer, receipt,
                        inspected.Coverage, inspected.Taint, AnalysisSavedView.Default, token);
                    writing.Complete(receipt, token); cataloged = true; return true;
                }, cancellation).ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                try { await AnalysisIo.ReleaseScratchAsync(_io, _device, work).ConfigureAwait(false); }
                catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            }
            var outcome = new AnalysisRollingCaptureOutcome(failure is null ? AnalysisRollingCaptureState.Saved : AnalysisCloseUnconfirmedException.IsPresent(failure)
                ? AnalysisRollingCaptureState.Uncertain : AnalysisRollingCaptureState.RecoveryRequired,
                review.Name, receipt, cataloged, [], failure is null
                    ? "The complete copy was added to history with default display settings. Retention uses its new catalog time; no other history or source file was removed."
                    : "History recovery was not fully confirmed. Keep remaining files and inspect the current catalog state before retrying.");
            if (failure is not null) throw new AnalysisRollingCaptureException(outcome, failure);
            return outcome;
            });
        }, retained).ConfigureAwait(false);
    }

    private static void RequireUnindexed(Contents data, AnalysisCatalogSnapshot catalog, string name, CancellationToken cancellation)
    {
        if (!data.Rolling) throw new InvalidOperationException("An isolated history owner is required.");
        ValidateRollingShape(data.Store!, catalog, null, cancellation, reviewUnindexed: true);
        if (!catalog.UnindexedNames.Contains(name, StringComparer.Ordinal)) throw new InvalidDataException("That copy is no longer unindexed. Refresh history before reviewing it.");
    }
}
