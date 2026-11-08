using System.Security.Cryptography;

namespace FileCat.Core.Analysis;

// Exact private bytes, not a claim that these bytes encode a valid inventory.
internal sealed class AnalysisRollingOpaqueDeleteReview(OwnedAnalysisStore owner, Guid? catalog, string name, Guid id,
    string identity, long bytes, string sha256, OwnedAnalysisDeleteReview? indexed = null)
{
    internal OwnedAnalysisStore Owner { get; } = owner;
    internal Guid? CatalogId { get; } = catalog;
    internal string Name { get; } = name;
    internal Guid InventoryId { get; } = id;
    internal string Identity { get; } = identity;
    internal long Bytes { get; } = bytes;
    internal string Sha256 { get; } = sha256;
    internal OwnedAnalysisDeleteReview? Indexed { get; } = indexed;
    private int _used;
    internal void Consume(OwnedAnalysisStore actual)
    {
        if (!ReferenceEquals(actual, Owner) || Interlocked.Exchange(ref _used, 1) != 0)
            throw new InvalidOperationException("Obtain a fresh private history removal review.");
    }
}

public sealed partial class OwnedAnalysisStore
{
    private async Task<(long Bytes, string Sha256)> InspectOpaqueBytesAsync(Stream stream, Action verify, CancellationToken cancellation)
    {
        long reservation = 0;
        try
        {
            AnalysisInventoryFormat.Reserve(AnalysisLimits.BatchBytes + 4096, ref reservation);
            byte[] buffer = new byte[AnalysisLimits.BatchBytes];
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long length = await AnalysisIo.Run(_io, _device, _ =>
            {
                verify(); long size = stream.Length;
                if (size is < 0 or > AnalysisLimits.InventoryBytes || stream.Position != 0)
                    throw new InvalidDataException("Private history bytes exceed the bounded review or are not at their beginning.");
                return size;
            }, cancellation).ConfigureAwait(false);
            long consumed = 0;
            while (true)
            {
                int read = await AnalysisIo.Run(_io, _device, token =>
                {
                    token.ThrowIfCancellationRequested(); verify();
                    if (stream.Length != length) throw new InvalidDataException("The private history length changed during review.");
                    int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, length - consumed + 1));
                    verify(); return count;
                }, cancellation).ConfigureAwait(false);
                if (read == 0) break;
                consumed = checked(consumed + read);
                if (consumed > length) throw new InvalidDataException("The private history bytes grew during review.");
                digest.AppendData(buffer.AsSpan(0, read));
            }
            if (consumed != length) throw new InvalidDataException("The private history bytes ended early.");
            cancellation.ThrowIfCancellationRequested(); return (length, Convert.ToHexStringLower(digest.GetHashAndReset()));
        }
        finally { AnalysisInventoryFormat.Release(reservation); }
    }

    internal async Task<AnalysisRollingOpaqueDeleteReview> PrepareOpaqueHistoryDeleteAsync(string name, CancellationToken cancellation)
    {
        if (!TryRollingName(name, out Guid id)) throw new ArgumentException("A generated history filename is required.");
        var indexed = await _owned.RunAsync((data, token) =>
        {
            RequireProfile(data, token);
            using var catalog = new AnalysisSavedCatalog(data.Store!).Read(token);
            if (!data.Rolling) throw new InvalidOperationException("An isolated history owner is required.");
            ValidateRollingShape(data.Store!, catalog, null, token, reviewUnindexed: true);
            return catalog.Entries.Any(entry => entry.InventoryId == id) || catalog.PendingDeletions.Any(entry => entry.Entry.InventoryId == id);
        }, cancellation).ConfigureAwait(false);
        if (indexed)
        {
            var review = await PrepareDeleteCoreAsync(id, cancellation, opaque: true).ConfigureAwait(false);
            if (!review.PayloadPresent) throw new InvalidOperationException("The payload is already absent. Review completion of its pending metadata instead.");
            return new(this, review.CatalogId, name, id, review.PayloadIdentity!, review.OpaqueBytes!.Value, review.PayloadSha256!, review);
        }
        return await _owned.UseAsync(async data =>
        {
            var work = new OpaqueHistoryWork();
            try
            {
                var basis = await AnalysisIo.Run(_io, _device, token =>
                {
                    RequireProfile(data, token);
                    using var catalog = new AnalysisSavedCatalog(data.Store!).Read(token); RequireUnindexed(data, catalog, name, token);
                    using var directory = data.Store!.BorrowDirectory(); work.Payload = directory.OpenMaintenance(name);
                    return (Catalog: catalog.CatalogId, Identity: work.Payload.Identity);
                }, cancellation).ConfigureAwait(false);
                var inspected = await InspectOpaqueBytesAsync(work.Payload!.Stream, work.Payload.Verify, cancellation).ConfigureAwait(false);
                await AnalysisIo.Run(_io, _device, token =>
                {
                    RequireProfile(data, token); work.Payload!.Verify();
                    using var catalog = new AnalysisSavedCatalog(data.Store!).Read(token); RequireUnindexed(data, catalog, name, token);
                    if (catalog.CatalogId != basis.Catalog || work.Payload.Identity != basis.Identity) throw new InvalidDataException("History changed during review.");
                    return true;
                }, cancellation).ConfigureAwait(false);
                return new AnalysisRollingOpaqueDeleteReview(this, basis.Catalog, name, id, basis.Identity, inspected.Bytes, inspected.Sha256);
            }
            finally { await AnalysisIo.ReleaseScratchAsync(_io, _device, work).ConfigureAwait(false); }
        }).ConfigureAwait(false);
    }

    internal Task<OwnedAnalysisDeletionOutcome> DeleteOpaqueHistoryAsync(AnalysisRollingOpaqueDeleteReview review, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(review); review.Consume(this);
        if (review.Indexed is { } indexed) return DeleteAsync(indexed, cancellation);
        return _owned.UseAsync(async data =>
        {
            var work = new OpaqueHistoryWork(); Exception? failure = null; bool attempted = false, absent = false;
            try
            {
                await AnalysisIo.Run(_io, _device, token =>
                {
                    RequireProfile(data, token); work.Writer = data.Store!.OpenCatalogLock(token);
                    RequireCurrent(token);
                    using var directory = data.Store.BorrowDirectory(); work.Payload = directory.OpenMaintenance(review.Name);
                    if (work.Payload.Identity != review.Identity) throw new InvalidDataException("The reviewed private history object changed.");
                    return true;
                }, cancellation).ConfigureAwait(false);
                var inspected = await InspectOpaqueBytesAsync(work.Payload!.Stream, work.Payload.Verify, cancellation).ConfigureAwait(false);
                if (inspected.Bytes != review.Bytes || inspected.Sha256 != review.Sha256) throw new InvalidDataException("The reviewed private history bytes changed. Review them again.");
                await AnalysisIo.Run(_io, _device, token =>
                {
                    RequireCurrent(token); work.Writer!.Verify(); work.Payload.Verify(); token.ThrowIfCancellationRequested();
                    attempted = true; work.Payload.RemoveOwned();
                    var removed = work.Payload; work.Payload = null; AnalysisIo.CloseOnWorker(removed);
                    using var directory = data.Store!.BorrowDirectory(); RequireAbsent(directory, review.Name, token); absent = true;
                    return true;
                }, cancellation).ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                try { await AnalysisIo.ReleaseScratchAsync(_io, _device, work).ConfigureAwait(false); }
                catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            }
            if (failure is null) return new OwnedAnalysisDeletionOutcome(OwnedAnalysisDeletionState.Deleted, review.InventoryId, review.Name, true, null,
                "The exact reviewed unindexed history copy was removed. Other history, named analyses and source files were unchanged. This is logical deletion, not secure erasure.");
            bool uncertain = attempted && !absent || AnalysisCloseUnconfirmedException.IsPresent(failure);
            throw new OwnedAnalysisDeletionException(new(uncertain ? OwnedAnalysisDeletionState.Uncertain : OwnedAnalysisDeletionState.Kept,
                review.InventoryId, review.Name, absent, null, uncertain ? "Private history removal was not fully confirmed. Inspect the current store before retrying."
                    : "The unindexed history copy was kept. Obtain a fresh review. " + AnalysisNames.Display(failure.Message, 400)), failure);

            void RequireCurrent(CancellationToken token)
            {
                RequireProfile(data, token); using var catalog = new AnalysisSavedCatalog(data.Store!).Read(token);
                RequireUnindexed(data, catalog, review.Name, token);
                if (catalog.CatalogId != review.CatalogId) throw new InvalidDataException("History changed after removal review.");
            }
        });
    }
    private sealed class OpaqueHistoryWork : IAnalysisScratchOwned
    {
        internal AnalysisPrivateFile? Writer, Payload;
        public void Dispose()
        {
            Exception? failure = null;
            try { Interlocked.Exchange(ref Payload, null)?.Dispose(); } catch (Exception error) { failure = error; }
            try { Interlocked.Exchange(ref Writer, null)?.Dispose(); } catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
