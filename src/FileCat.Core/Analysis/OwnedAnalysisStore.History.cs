using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal enum AnalysisRollingCaptureState { Saved, Disabled, TooLarge, Deferred, RecoveryRequired, Uncertain, Maintained }
internal sealed record AnalysisRollingCaptureOutcome(AnalysisRollingCaptureState State, string? IncomingName,
    AnalysisInventoryReceipt? Receipt, bool Cataloged, ImmutableArray<Guid> Pruned, string Detail);
internal sealed class AnalysisRollingCaptureException(AnalysisRollingCaptureOutcome outcome, Exception inner) : IOException(outcome.Detail, inner)
{
    internal AnalysisRollingCaptureOutcome Outcome { get; } = outcome;
}

public sealed partial class OwnedAnalysisStore
{
    internal static string RollingName(Guid inventory) => inventory != Guid.Empty ? $"history-{inventory:N}.fcdsa" : throw new ArgumentException("A generated history identity is required.");
    internal static bool TryRollingName(string name, out Guid inventory)
    {
        inventory = Guid.Empty;
        return name.Length == 46 && name.StartsWith("history-", StringComparison.Ordinal) && name.EndsWith(".fcdsa", StringComparison.Ordinal) &&
            Guid.TryParseExact(name.AsSpan(8, 32), "N", out inventory) && inventory != Guid.Empty && name == RollingName(inventory);
    }

    // The application must supply a separately prepared history-root factory, never a catalog
    // locator or a named-store owner. This internal role is process-owned and not serialized.
    internal Task<AnalysisRollingCaptureOutcome> CaptureRollingAsync(AnalysisRollingHistoryPolicy policy, AnalysisSnapshot snapshot,
        AnalysisSavedView view, IReadOnlySet<Guid>? protectedInventories, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var saved = AnalysisSavedCatalog.ViewForInventory(snapshot, view);
        return CaptureRollingCoreAsync(policy, snapshot.Coverage, snapshot.Taint, saved, _device,
            (factory, name, id, limit, token) => AnalysisInventoryFormat.PrepareSnapshotNewAsync(snapshot, _io, _device, factory, name, id, limit, token),
            protectedInventories, cancellation);
    }

    internal async Task<AnalysisRollingCaptureOutcome> CaptureRollingAsync(AnalysisRollingHistoryPolicy policy, AnalysisDatasetLease source,
        AnalysisSavedView view, IReadOnlySet<Guid>? protectedInventories, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(policy); policy.Validate();
        var descriptor = source.Descriptor; var saved = AnalysisSavedCatalog.ViewForInventory(descriptor, view);
        var devices = source.ReplacementDevices(_device.Primary);
        var retained = await source.RetainAsync(cancellation).ConfigureAwait(false);
        return await FinishRollingAsync(() => CaptureRollingCoreAsync(policy, descriptor.Coverage, descriptor.Taint, saved, devices,
            (factory, name, id, limit, token) => retained.PreparePrivateNewAsync(_io, devices, factory, name, id, limit, token),
            protectedInventories, cancellation), retained, observeRoot: false).ConfigureAwait(false);
    }

    private Task<AnalysisRollingCaptureOutcome> CaptureRollingCoreAsync(AnalysisRollingHistoryPolicy policy,
        NamespaceCoverage coverage, AnalysisTaint taint, AnalysisSavedView view, AnalysisIoDevices devices,
        Func<Func<AnalysisPrivateDirectory>, string, Guid, long, CancellationToken, Task<AnalysisPrivateInventoryStore.DatasetWrite>> prepare,
        IReadOnlySet<Guid>? protectedInventories, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(policy); policy.Validate();
        return FinishRollingAsync(() => _owned.UseAsync<AnalysisRollingCaptureOutcome>(async data =>
        {
            if (!data.Rolling) throw new InvalidOperationException("Automatic retention requires an application-prepared isolated history store.");
            if (!policy.Enabled) return new(AnalysisRollingCaptureState.Disabled, null, null, false, [], "Rolling history is off. Existing history was kept.");
            Guid incoming = Guid.NewGuid(); string name = RollingName(incoming);
            var work = new RollingWork(); var pruned = ImmutableArray.CreateBuilder<Guid>();
            AnalysisInventoryReceipt? receipt = null; bool cataloged = false, pruningStarted = false;
            string? publishedIdentity = null; Exception? failure = null;
            AnalysisRollingHistoryPlan? plan = null;
            AnalysisRollingCaptureOutcome? deferred = null;
            try
            {
                var entries = await AnalysisIo.Run(_io, devices, token =>
                {
                    RequireProfile(data, token);
                    using (var initial = new AnalysisSavedCatalog(data.Store!).Read(token)) ValidateRollingShape(data.Store!, initial, null, token);
                    work.Writer = data.Store!.OpenCatalogLock(token);
                    return RollingEntries(data, null, token);
                }, cancellation).ConfigureAwait(false);
                work.Output = await prepare(() => data.Store!.BorrowDirectory(), name, incoming, policy.MaximumBytes, cancellation).ConfigureAwait(false);
                var prepared = await AnalysisInventoryFormat.InspectContainerAsync(work.Output.PreparedStream, work.Output.Verify, _io, devices, cancellation).ConfigureAwait(false);
                if (prepared.Id != incoming || prepared.Bytes != work.Output.Bytes || prepared.Coverage != coverage || prepared.Taint != taint)
                    throw new InvalidDataException("The complete prepared history inventory does not match its captured revision.");
                var now = DateTimeOffset.UtcNow;
                plan = AnalysisRollingHistoryPlanner.Plan(policy, entries.Select(e => new AnalysisRollingHistoryEntry(e.InventoryId, e.Bytes, e.SavedUtc)).ToArray(),
                    now, new(incoming, prepared.Bytes, now), protectedInventories, cancellation);
                if (!plan.AdmitIncoming)
                    deferred = new(AnalysisRollingCaptureState.Deferred, null, null, false, [], "History capture was deferred because the current retention limits, clock or protected entries could not be confirmed. Existing history was kept.");
                else
                {

                // Prepare exact old-object reviews before publication. The incoming file is then
                // published/closed and verified in full before any old payload can be removed.
                var reviews = new List<OwnedAnalysisDeleteReview>(plan.ProposedEvictions.Length);
                foreach (var candidate in plan.ProposedEvictions)
                    reviews.Add(await PrepareDeleteAsync(candidate.InventoryId, cancellation).ConfigureAwait(false));
                receipt = await AnalysisIo.Run(_io, devices, token =>
                {
                    RequireProfile(data, token); work.Writer!.Verify();
                    var current = RollingEntries(data, null, token, checkDirectory: false);
                    if (!current.SequenceEqual(entries)) throw new InvalidDataException("History changed while the incoming inventory was prepared.");
                    var saved = work.Output.PublishPrepared(incoming, token); publishedIdentity = work.Output.PublishedIdentity; return saved;
                }, cancellation).ConfigureAwait(false);
                var output = work.Output; work.Output = null;
                await AnalysisIo.ReleaseScratchAsync(_io, devices, output).ConfigureAwait(false);
                await AnalysisIo.Run(_io, devices, token =>
                {
                    work.Incoming = AnalysisPrivateInventoryStore.BeginDatasetRead(data.Store!.BorrowDirectory(), name, token);
                    if (work.Incoming.Identity != publishedIdentity) throw new InvalidDataException("The newly published history object changed before retention.");
                    return true;
                }, cancellation).ConfigureAwait(false);
                var confirmed = await AnalysisInventoryFormat.InspectContainerAsync(work.Incoming!, _io, devices, cancellation).ConfigureAwait(false);
                if (confirmed.Id != incoming || confirmed.Sha256 != prepared.Sha256 || confirmed.Bytes != prepared.Bytes)
                    throw new InvalidDataException("The newly published complete history bytes could not be confirmed.");
                var confirmedReceipt = receipt ?? throw new InvalidOperationException("The complete history publication has no receipt.");

                foreach (var review in reviews)
                {
                    await AnalysisIo.Run(_io, devices, token =>
                    { work.Incoming!.Verify(); _ = RollingEntries(data, name, token); return true; }, cancellation).ConfigureAwait(false);
                    pruningStarted = true;
                    try
                    {
                        var removed = await DeleteUnderHistoryLockAsync(review, work.Writer!, cancellation).ConfigureAwait(false);
                        if (removed.State != OwnedAnalysisDeletionState.Deleted) throw new IOException("A history retention removal was not confirmed.");
                        pruned.Add(review.Entry.InventoryId);
                    }
                    catch (OwnedAnalysisDeletionException error)
                    { if (error.Outcome.PayloadAbsentConfirmed) pruned.Add(review.Entry.InventoryId); throw; }
                }
                await AnalysisIo.Run(_io, devices, token =>
                {
                    work.Incoming!.Verify(); work.Writer!.Verify();
                    _ = RollingEntries(data, name, token);
                    var catalog = new AnalysisSavedCatalog(data.Store!);
                    using var writing = catalog.BeginPublishedWriteUnderLock(work.Writer, confirmedReceipt, coverage, taint, view, token);
                    writing.Complete(confirmedReceipt, token); cataloged = true;
                    var final = RollingEntries(data, null, token);
                    if (final.Length != plan.RetainedInventories.Length || final.Aggregate((UInt128)0, (total, row) => total + (UInt128)row.Bytes) != plan.RetainedBytes ||
                        !final.Select(row => row.InventoryId).Order().SequenceEqual(plan.RetainedInventories.Order()))
                        throw new InvalidDataException("The final history catalog does not match the complete retention proposal.");
                    return true;
                }, cancellation).ConfigureAwait(false);
                }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                try { await AnalysisIo.ReleaseScratchAsync(_io, devices, work).ConfigureAwait(false); }
                catch (Exception close) { failure = failure is null ? close : new AggregateException(failure, close); }
            }
            if (failure is null) return deferred ?? new(AnalysisRollingCaptureState.Saved, name, receipt, true, pruned.ToImmutable(), "The complete analysis was saved in rolling history. Retention affects only this private history store.");
            if (receipt is null && IsOnlyWriteLimit(failure))
                return new(AnalysisRollingCaptureState.TooLarge, null, null, false, [], "The complete analysis exceeds the history byte cap. No partial inventory was saved; existing history was kept.");
            // Preserve the complete new file after publication, including failed metadata
            // registration. A pending/unindexed entry blocks further automatic capture.
            bool uncertain = AnalysisCloseUnconfirmedException.IsPresent(failure) || PublicationUncertain(failure);
            var state = uncertain ? AnalysisRollingCaptureState.Uncertain : receipt is not null || pruningStarted
                ? AnalysisRollingCaptureState.RecoveryRequired : AnalysisRollingCaptureState.Deferred;
            var outcome = new AnalysisRollingCaptureOutcome(state, receipt is not null || uncertain ? name : null, receipt, cataloged, pruned.ToImmutable(),
                receipt is not null ? "The complete history payload was published, but retention or private cleanup was not confirmed. Keep remaining files and review history before another capture."
                : "History capture was not confirmed. Existing history and any remaining private files were kept; review them before retrying.");
            throw new AnalysisRollingCaptureException(outcome, failure);
        }));
    }

    private async Task<AnalysisRollingCaptureOutcome> FinishRollingAsync(Func<Task<AnalysisRollingCaptureOutcome>> operation,
        IAsyncDisposable? retainedSource = null, bool observeRoot = true)
    {
        AnalysisRollingCaptureOutcome? result = null; Exception? failure = null, cleanup = null;
        try { result = await operation().ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        if (observeRoot) cleanup = _owned.CloseFailure;
        if (retainedSource is not null)
        {
            try { await retainedSource.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
        }
        if (cleanup is not null)
        {
            var known = result ?? (failure as AnalysisRollingCaptureException)?.Outcome ??
                new(AnalysisRollingCaptureState.Uncertain, null, null, false, [], "History capture was not confirmed.");
            throw new AnalysisRollingCaptureException(known with
            {
                State = AnalysisRollingCaptureState.Uncertain,
                Detail = known.Receipt is not null
                    ? "The complete history payload was published, but private cleanup could not be confirmed. Keep remaining files and review history before another capture."
                    : "Private history cleanup could not be confirmed. Keep remaining files and review history before another capture."
            }, failure is null ? cleanup : new AggregateException(failure, cleanup));
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result!;
    }

    private static AnalysisSavedEntry[] RollingEntries(Contents data, string? incoming, CancellationToken cancellation, bool checkDirectory = true)
    {
        if (!data.Rolling) throw new InvalidOperationException("This owner is not an isolated history store.");
        RequireProfile(data, cancellation);
        using var catalog = new AnalysisSavedCatalog(data.Store!).Read(cancellation);
        if (catalog.PendingDeletions.Length != 0 || catalog.PendingReplacements.Length != 0)
            throw new InvalidDataException("History has pending private maintenance. Fresh explicit recovery is required before automatic capture.");
        ValidateRollingShape(data.Store!, catalog, incoming, cancellation, checkDirectory);
        if (catalog.Entries.Aggregate((UInt128)0, (total, row) => total + (UInt128)row.Bytes) > (UInt128)AnalysisRollingHistoryPolicy.ByteHardLimit)
            throw new InvalidDataException("Existing history exceeds its absolute byte bound. Review it before automatic capture.");
        return catalog.Entries.ToArray();
    }

    private static void ValidateRollingShape(AnalysisPrivateInventoryStore store, AnalysisCatalogSnapshot catalog, string? allowedIncoming,
        CancellationToken cancellation, bool checkDirectory = true, bool reviewUnindexed = false)
    {
        if (catalog.Entries.Concat(catalog.PendingDeletions.Select(p => p.Entry)).Any(e => e.Name != RollingName(e.InventoryId)) ||
            catalog.PendingReplacements.Length != 0 || catalog.UnindexedNames.Length > AnalysisSavedCatalog.EntryLimit ||
            catalog.UnindexedNames.Any(n => n != allowedIncoming && !(reviewUnindexed && TryRollingName(n, out _))))
            throw new InvalidDataException("The history store contains non-history or unresolved inventory data. Existing files were preserved.");
        if (!checkDirectory) return;
        var allowed = new HashSet<string>(catalog.Entries.Select(e => e.Name).Concat(catalog.PendingDeletions.Select(p => p.Entry.Name)), StringComparer.Ordinal);
        if (allowedIncoming is not null) allowed.Add(allowedIncoming);
        if (reviewUnindexed) allowed.UnionWith(catalog.UnindexedNames);
        using var directory = store.BorrowDirectory();
        if (directory.Entries(cancellation).Any(name => !AnalysisPrivateNames.IsMetadata(name) && !allowed.Contains(name)))
            throw new InvalidDataException("The history directory contains unrelated private files. Automatic capture was stopped without removing them.");
    }

    private static bool IsOnlyWriteLimit(Exception failure) => failure is AnalysisInventoryWriteLimitException ||
        failure is AggregateException aggregate && aggregate.InnerExceptions.Count != 0 && aggregate.InnerExceptions.All(IsOnlyWriteLimit);
    private static bool PublicationUncertain(Exception failure) => failure is AnalysisPublicationUncertainException ||
        failure is AggregateException aggregate && aggregate.InnerExceptions.Any(PublicationUncertain);

    private sealed class RollingWork : IAnalysisScratchOwned
    {
        internal AnalysisPrivateFile? Writer;
        internal AnalysisPrivateInventoryStore.DatasetWrite? Output;
        internal AnalysisPrivateInventoryStore.DatasetRead? Incoming;
        public void Dispose()
        {
            var failures = new List<Exception>();
            try { Interlocked.Exchange(ref Incoming, null)?.Dispose(); } catch (Exception error) { failures.Add(error); }
            try { Interlocked.Exchange(ref Output, null)?.Dispose(); } catch (Exception error) { failures.Add(error); }
            try { Interlocked.Exchange(ref Writer, null)?.Dispose(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException(failures);
        }
    }
}

public sealed partial class AnalysisDatasetLease
{
    internal Task<AnalysisPrivateInventoryStore.DatasetWrite> PreparePrivateNewAsync(DeviceIoScheduler io, AnalysisIoDevices devices,
        Func<AnalysisPrivateDirectory> directory, string name, Guid id, long maximumBytes, CancellationToken cancellation) =>
        _owned.UseAsync(reader => AnalysisInventoryFormat.PrepareDatasetNewAsync(reader, io, devices, directory, name, id, maximumBytes, cancellation));
}
