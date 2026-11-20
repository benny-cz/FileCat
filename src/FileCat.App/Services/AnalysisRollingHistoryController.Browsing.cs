using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.App.Services;

internal enum AnalysisHistoryRowState { Saved, PendingPresent, PendingAbsent, Unindexed }
internal sealed record AnalysisHistoryRow(Guid? CatalogId, Guid InventoryId, string Name, long? Bytes, DateTimeOffset? SavedUtc,
    NamespaceCoverage? Coverage, AnalysisTaint? Taint, AnalysisHistoryRowState State)
{
    public override string ToString() => State switch
    {
        AnalysisHistoryRowState.Unindexed => "Unindexed copy · " + AnalysisNames.Display(Name, 80),
        AnalysisHistoryRowState.PendingPresent => SavedUtc!.Value.ToLocalTime().ToString("g") + " · pending removal · file kept",
        AnalysisHistoryRowState.PendingAbsent => SavedUtc!.Value.ToLocalTime().ToString("g") + " · pending removal · file absent",
        _ => SavedUtc!.Value.ToLocalTime().ToString("g") + " · " + ViewModels.DiskSpaceViewModel.Bytes((UInt128)Bytes!.Value) +
            (Coverage == NamespaceCoverage.CompleteUnderPolicy ? " · complete under policy" : " · partial observations")
    };
}
internal sealed record AnalysisHistoryPage(int Offset, int Total, ImmutableArray<AnalysisHistoryRow> Rows)
{ internal bool HasMore => Offset + Rows.Length < Total; }

internal sealed partial class AnalysisRollingHistoryController
{
    private readonly object _protectionGate = new();
    private readonly Dictionary<Guid, int> _protectedHistory = [];
    private int _protectionCount;
    private IReadOnlySet<Guid> ProtectedHistory() { lock (_protectionGate) return new SortedSet<Guid>(_protectedHistory.Keys); }
    private IDisposable Protect(Guid id)
    {
        lock (_protectionGate)
        {
            if (_protectionCount >= AnalysisLimits.Sessions) throw new InvalidOperationException("History has eight protected views or reviews. Close one before opening another.");
            _protectionCount++; _protectedHistory[id] = _protectedHistory.GetValueOrDefault(id) + 1;
        }
        return new HistoryProtection(this, id);
    }
    private sealed class HistoryProtection(AnalysisRollingHistoryController owner, Guid id) : IDisposable
    {
        private AnalysisRollingHistoryController? _owner = owner;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is not { } current) return;
            lock (current._protectionGate)
            {
                current._protectionCount--;
                if (--current._protectedHistory[id] == 0) current._protectedHistory.Remove(id);
            }
            current.WakeMaintenance();
        }
    }
    internal void RecordHistoryFailure(Exception error)
    { if (AnalysisCloseUnconfirmedException.IsPresent(error)) Interlocked.CompareExchange(ref _unconfirmedClose, error, null); }
    private async Task<T> HistoryWorkAsync<T>(Func<Task<T>> operation, CancellationToken cancellation)
    {
        if (Interlocked.Increment(ref _pending) > AnalysisLimits.Sessions)
        { Interlocked.Decrement(ref _pending); throw new InvalidOperationException("History work is busy. Wait for an existing operation."); }
        bool entered = false;
        try
        {
            await _writer.WaitAsync(cancellation).ConfigureAwait(false); entered = true;
            if (UnconfirmedClose is { } close) throw new AnalysisCloseUnconfirmedException(close);
            return await operation().ConfigureAwait(false);
        }
        catch (Exception error) { RecordHistoryFailure(error); throw; }
        finally { if (entered) _writer.Release(); Interlocked.Decrement(ref _pending); }
    }
    internal Task<AnalysisHistoryPage> ReadHistoryPageAsync(int offset, CancellationToken cancellation) => HistoryWorkAsync(async () =>
    {
        if (offset is < 0 or > AnalysisSavedCatalog.EntryLimit * 2) throw new ArgumentOutOfRangeException(nameof(offset));
        await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, cancellation, false).ConfigureAwait(false);
        await using var store = await AnalysisRollingHistoryAccess.OpenReviewAsync(services, cancellation).ConfigureAwait(false);
        using var catalog = await store.ReadCatalogAsync(cancellation).ConfigureAwait(false);
        var rows = catalog.Entries.Select(entry => Row(catalog.CatalogId, entry, AnalysisHistoryRowState.Saved))
            .Concat(catalog.PendingDeletions.Select(pending => Row(catalog.CatalogId, pending.Entry, pending.PayloadPresent ? AnalysisHistoryRowState.PendingPresent : AnalysisHistoryRowState.PendingAbsent)))
            .Concat(catalog.UnindexedNames.Select(name =>
            {
                if (!OwnedAnalysisStore.TryRollingName(name, out Guid id)) throw new InvalidDataException("Unexpected history name.");
                return new AnalysisHistoryRow(catalog.CatalogId, id, name, null, null, null, null, AnalysisHistoryRowState.Unindexed);
            })).OrderByDescending(row => row.SavedUtc).ThenBy(row => row.InventoryId).ToArray();
        return new AnalysisHistoryPage(Math.Min(offset, rows.Length), rows.Length, rows.Skip(offset).Take(128).ToImmutableArray());
    }, cancellation);

    internal Task<OwnedAnalysisInventoryDetails> InspectHistoryAsync(AnalysisHistoryRow row, CancellationToken cancellation) => HistoryWorkAsync(async () =>
    {
        await using var store = await AnalysisRollingHistoryAccess.OpenReviewAsync(services, cancellation).ConfigureAwait(false);
        using (var catalog = await store.ReadCatalogAsync(cancellation).ConfigureAwait(false)) _ = SelectRow(catalog, row);
        return await store.InspectAsync(row.InventoryId, cancellation).ConfigureAwait(false);
    }, cancellation);

    internal Task<AnalysisHistoryOpenSource> OpenHistoryAsync(AnalysisHistoryRow row, CancellationToken cancellation) => HistoryWorkAsync(async () =>
    {
        IDisposable? protection = null; AnalysisIndexedInventory? inventory = null;
        try
        {
            if (row.State != AnalysisHistoryRowState.Saved) throw new InvalidOperationException("Recover this history entry before opening it.");
            AnalysisSavedEntry entry;
            {
                await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, cancellation).ConfigureAwait(false);
                await using (var store = await AnalysisRollingHistoryAccess.OpenReviewAsync(services, cancellation).ConfigureAwait(false))
                using (var catalog = await store.ReadCatalogAsync(cancellation).ConfigureAwait(false)) entry = SelectRow(catalog, row);
                protection = Protect(row.InventoryId);
                inventory = await prepared.LoadHistoryInventoryAsync(entry, cancellation).ConfigureAwait(false);
            }
            var result = new AnalysisHistoryOpenSource(this, entry, inventory, protection); inventory = null; protection = null; return result;
        }
        finally
        {
            try { if (inventory is not null) await inventory.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { RecordHistoryFailure(error); throw; }
            finally { if (UnconfirmedClose is null) protection?.Dispose(); }
        }
    }, cancellation);

    internal Task<AnalysisHistoryMaintenanceCapture> PrepareHistoryMaintenanceAsync(AnalysisHistoryRow row, CancellationToken cancellation) => HistoryWorkAsync(async () =>
    {
        OwnedAnalysisStore? store = null; IDisposable? protection = null;
        try
        {
            store = await AnalysisRollingHistoryAccess.OpenReviewAsync(services, cancellation).ConfigureAwait(false);
            using (var catalog = await store.ReadCatalogAsync(cancellation).ConfigureAwait(false)) _ = SelectRow(catalog, row);
            protection = Protect(row.InventoryId);
            var review = await store.PrepareDeleteAsync(row.InventoryId, cancellation).ConfigureAwait(false);
            var result = new AnalysisHistoryMaintenanceCapture(this, store, review, protection); store = null; protection = null; return result;
        }
        finally
        {
            try { if (store is not null) await store.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { RecordHistoryFailure(error); throw; }
            finally { if (UnconfirmedClose is null) protection?.Dispose(); }
        }
    }, cancellation);

    internal Task<OwnedAnalysisDeletionOutcome> ChangeHistoryAsync(AnalysisHistoryMaintenanceCapture capture, bool restore, CancellationToken cancellation) => HistoryWorkAsync(async () =>
    {
        if (!ReferenceEquals(capture.Controller, this)) throw new InvalidOperationException("The history review belongs to another profile.");
        capture.Choose();
        lock (_protectionGate)
            if (_protectedHistory.GetValueOrDefault(capture.Review.Entry.InventoryId) != 1)
                throw new InvalidOperationException("This history copy is used by an open view or another review. Close that work and obtain a fresh removal review.");
        return restore ? await capture.Store.RestoreNamedAsync(capture.Review, cancellation).ConfigureAwait(false)
            : await capture.Store.DeleteAsync(capture.Review, cancellation).ConfigureAwait(false);
    }, cancellation);

    internal Task<AnalysisHistoryRecoveryCapture> PrepareHistoryRecoveryAsync(AnalysisHistoryRow row, CancellationToken cancellation) => HistoryWorkAsync(async () =>
    {
        OwnedAnalysisStore? store = null; IDisposable? protection = null; AnalysisIndexedInventory? validated = null;
        try
        {
            if (row.State != AnalysisHistoryRowState.Unindexed) throw new InvalidOperationException("Select an unindexed history copy.");
            store = await AnalysisRollingHistoryAccess.OpenReviewAsync(services, cancellation).ConfigureAwait(false);
            using (var catalog = await store.ReadCatalogAsync(cancellation).ConfigureAwait(false))
                if (catalog.CatalogId != row.CatalogId || !catalog.UnindexedNames.Contains(row.Name, StringComparer.Ordinal)) throw new InvalidDataException("History changed. Refresh before recovery.");
            protection = Protect(row.InventoryId);
            var review = await store.PrepareRollingRecoveryAsync(row.Name, cancellation).ConfigureAwait(false);
            var entry = new AnalysisSavedEntry(review.InventoryId, review.Name, review.Bytes, DateTimeOffset.UtcNow,
                review.Inspection.Coverage, review.Inspection.Taint, AnalysisSavedView.Default);
            {
                await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, cancellation).ConfigureAwait(false);
                validated = await prepared.LoadHistoryInventoryAsync(entry, cancellation).ConfigureAwait(false);
            }
            if (validated.SourceSha256 != review.Sha256) throw new InvalidDataException("The history bytes changed during graph validation. Inspect them again.");
            var result = new AnalysisHistoryRecoveryCapture(this, store, review, validated, protection); store = null; validated = null; protection = null; return result;
        }
        finally
        {
            Exception? failure = null;
            try { if (validated is not null) await validated.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure = error; }
            try { if (store is not null) await store.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            if (failure is not null) { RecordHistoryFailure(failure); throw failure; }
            if (UnconfirmedClose is null) protection?.Dispose();
        }
    }, cancellation);
    internal Task<AnalysisRollingCaptureOutcome> RecoverHistoryAsync(AnalysisHistoryRecoveryCapture capture, CancellationToken cancellation) => HistoryWorkAsync(() =>
    {
        if (!ReferenceEquals(capture.Controller, this)) throw new InvalidOperationException("This recovery review belongs to another profile.");
        capture.Choose();
        return capture.Store.RecoverRollingAsync(capture.Review, capture.Inventory, cancellation);
    }, cancellation);

    private static AnalysisHistoryRow Row(Guid? catalog, AnalysisSavedEntry entry, AnalysisHistoryRowState state) =>
        new(catalog, entry.InventoryId, entry.Name, entry.Bytes, entry.SavedUtc, entry.Coverage, entry.Taint, state);
    private static AnalysisSavedEntry SelectRow(AnalysisCatalogSnapshot catalog, AnalysisHistoryRow row)
    {
        var entry = row.State == AnalysisHistoryRowState.Saved ? catalog.Entries.SingleOrDefault(entry => entry.InventoryId == row.InventoryId)
            : catalog.PendingDeletions.SingleOrDefault(pending => pending.Entry.InventoryId == row.InventoryId)?.Entry;
        var pending = catalog.PendingDeletions.SingleOrDefault(item => item.Entry.InventoryId == row.InventoryId);
        var state = pending is null ? AnalysisHistoryRowState.Saved : pending.PayloadPresent ? AnalysisHistoryRowState.PendingPresent : AnalysisHistoryRowState.PendingAbsent;
        if (entry is null || Row(catalog.CatalogId, entry, state) != row) throw new InvalidDataException("The selected history entry changed. Refresh and inspect it again.");
        return entry;
    }
}

internal sealed class AnalysisHistoryOpenSource(AnalysisRollingHistoryController controller, AnalysisSavedEntry entry,
    AnalysisIndexedInventory inventory, IDisposable protection) : IAsyncDisposable
{
    internal AnalysisSavedEntry Entry { get; } = entry;
    internal Task<AnalysisDatasetLease> AcquireAsync(CancellationToken cancellation) => inventory.AcquireAsync(cancellation);
    private Task? _close;
    public ValueTask DisposeAsync() { lock (this) return new(_close ??= CloseAsync()); }
    private async Task CloseAsync()
    {
        try { await inventory.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { controller.RecordHistoryFailure(error); throw; }
        if (controller.UnconfirmedClose is null) protection.Dispose();
    }
}

internal sealed class AnalysisHistoryMaintenanceCapture(AnalysisRollingHistoryController controller, OwnedAnalysisStore store,
    OwnedAnalysisDeleteReview review, IDisposable protection) : IAsyncDisposable
{
    internal AnalysisRollingHistoryController Controller => controller;
    internal OwnedAnalysisStore Store => store;
    internal OwnedAnalysisDeleteReview Review => review;
    private int _chosen;
    internal void Choose() { if (Interlocked.Exchange(ref _chosen, 1) != 0) throw new InvalidOperationException("Obtain a fresh history removal review."); }
    private Task? _close;
    public ValueTask DisposeAsync() { lock (this) return new(_close ??= CloseAsync()); }
    private async Task CloseAsync()
    {
        try { await store.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { controller.RecordHistoryFailure(error); throw; }
        if (controller.UnconfirmedClose is null) protection.Dispose();
    }
}

internal sealed class AnalysisHistoryRecoveryCapture(AnalysisRollingHistoryController controller, OwnedAnalysisStore store,
    AnalysisRollingRecoveryReview review, AnalysisIndexedInventory inventory, IDisposable protection) : IAsyncDisposable
{
    internal AnalysisRollingHistoryController Controller => controller;
    internal OwnedAnalysisStore Store => store;
    internal AnalysisRollingRecoveryReview Review => review;
    internal AnalysisIndexedInventory Inventory => inventory;
    private int _chosen;
    internal void Choose() { if (Interlocked.Exchange(ref _chosen, 1) != 0) throw new InvalidOperationException("Obtain a fresh history recovery review."); }
    private Task? _close;
    public ValueTask DisposeAsync() { lock (this) return new(_close ??= CloseAsync()); }
    private async Task CloseAsync()
    {
        Exception? failure = null;
        try { await inventory.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure = error; }
        try { await store.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        if (failure is not null) { controller.RecordHistoryFailure(failure); throw failure; }
        if (controller.UnconfirmedClose is null) protection.Dispose();
    }
}
