using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed record OwnedAnalysisPreparedInventory(Guid InventoryId, long Bytes, NamespaceCoverage Coverage, AnalysisTaint Taint,
    DateTimeOffset StartedUtc, DateTimeOffset ObservedUtc, string Sha256);
public enum OwnedAnalysisReplacementState { Kept, Replaced, ReplacedPreviousKept, RecoveryRequired, Uncertain }
public sealed record OwnedAnalysisReplacementOutcome(OwnedAnalysisReplacementState State, Guid PreviousId, Guid IncomingId,
    string Name, Guid? PendingOperationId, AnalysisInventoryReceipt? Receipt, string Detail);
public sealed class OwnedAnalysisReplacementException(OwnedAnalysisReplacementOutcome outcome, Exception inner) : IOException(outcome.Detail, inner)
{
    public OwnedAnalysisReplacementOutcome Outcome { get; } = outcome;
}

/// <summary>A one-use decision over complete immutable bytes. Dispose keeps the existing analysis and awaits actual cleanup.</summary>
public sealed class OwnedAnalysisReplacementReview : IAsyncDisposable
{
    internal sealed record Decision(bool Replace, CancellationToken Cancellation);
    private readonly TaskCompletionSource<Decision> _decision;
    internal readonly OwnedAnalysisStore Owner;
    internal readonly Task<OwnedAnalysisReplacementOutcome> Completion;
    internal OwnedAnalysisReplacementReview(OwnedAnalysisStore owner, OwnedAnalysisInventoryDetails previous, OwnedAnalysisPreparedInventory incoming,
        TaskCompletionSource<Decision> decision, Task<OwnedAnalysisReplacementOutcome> completion)
    { Owner = owner; Previous = previous; Incoming = incoming; _decision = decision; Completion = completion; }
    public OwnedAnalysisInventoryDetails Previous { get; }
    public OwnedAnalysisPreparedInventory Incoming { get; }
    internal Task<OwnedAnalysisReplacementOutcome> Choose(OwnedAnalysisStore owner, bool replace, CancellationToken cancellation)
    {
        if (!ReferenceEquals(owner, Owner)) throw new ArgumentException("This prepared review belongs to another private store.");
        if (!_decision.TrySetResult(new(replace, cancellation))) throw new InvalidOperationException("This prepared replacement was already decided.");
        return Completion;
    }
    public ValueTask DisposeAsync()
    {
        _decision.TrySetResult(new(false, CancellationToken.None));
        return new(Completion);
    }
}

public sealed partial class OwnedAnalysisStore
{
    public Task<OwnedAnalysisReplacementOutcome> ReplaceAsync(OwnedAnalysisReplacementReview review, CancellationToken cancellation = default)
    { ArgumentNullException.ThrowIfNull(review); return review.Choose(this, true, cancellation); }
    public Task<OwnedAnalysisReplacementOutcome> KeepExistingAsync(OwnedAnalysisReplacementReview review)
    { ArgumentNullException.ThrowIfNull(review); return review.Choose(this, false, CancellationToken.None); }

    public Task<OwnedAnalysisReplacementReview> PrepareReplacementAsync(Guid previousId, AnalysisSnapshot snapshot,
        AnalysisSavedView view, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var saved = AnalysisSavedCatalog.ViewForInventory(snapshot, view);
        return PrepareReplacementCoreAsync(previousId, saved, _device,
            (directory, name, id, token) => AnalysisInventoryFormat.PrepareSnapshotReplacementAsync(snapshot, _io, _device, directory, name, id, token),
            null, cancellation);
    }

    public async Task<OwnedAnalysisReplacementReview> PrepareReplacementAsync(Guid previousId, AnalysisDatasetLease source,
        AnalysisSavedView view, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var saved = AnalysisSavedCatalog.ViewForInventory(source.Descriptor, view);
        var devices = source.ReplacementDevices(_device.Primary);
        var retained = await source.RetainAsync(cancellation).ConfigureAwait(false);
        // Core owns the retained source even when preparation fails. Source disposal by a tab
        // cannot release this revision while encoding or while its decision is outstanding.
        return await PrepareReplacementCoreAsync(previousId, saved, devices,
            (directory, name, id, token) => retained.PreparePrivateReplacementAsync(_io, devices, directory, name, id, token),
            retained, cancellation).ConfigureAwait(false);
    }

    private async Task<OwnedAnalysisReplacementReview> PrepareReplacementCoreAsync(Guid previousId, AnalysisSavedView view, AnalysisIoDevices devices,
        Func<Func<AnalysisPrivateDirectory>, string, Guid, CancellationToken, Task<AnalysisPrivateInventoryStore.DatasetWrite>> prepare,
        IAsyncDisposable? retainedSource, CancellationToken cancellation)
    {
        var ready = new TaskCompletionSource<OwnedAnalysisReplacementReview>(TaskCreationOptions.RunContinuationsAsynchronously);
        var decision = new TaskCompletionSource<OwnedAnalysisReplacementReview.Decision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<OwnedAnalysisReplacementOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Observe failure even when preparation fails before a review can be returned.
        _ = completed.Task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        Task<OwnedAnalysisReplacementOutcome> lifetime;
        OwnedAnalysisReplacementOutcome? completedOperation = null;
        try
        {
            var old = await PrepareDeleteAsync(previousId, cancellation).ConfigureAwait(false);
            if (old.IsRecovery) throw new InvalidOperationException("Finish pending maintenance before replacing this analysis.");
            lifetime = _owned.UseAsync(async data =>
            {
                AnalysisPrivateInventoryStore.DatasetWrite? output = null;
                try
                {
                    Guid incoming = Guid.NewGuid(), operation = Guid.NewGuid();
                    string name = AnalysisPrivateNames.ReplacementIncoming(incoming, operation);
                    output = await prepare(data.Store!.BorrowDirectory, name, incoming, cancellation).ConfigureAwait(false);
                    var inspected = await AnalysisInventoryFormat.InspectContainerAsync(output.PreparedStream, output.Verify, _io, devices, cancellation).ConfigureAwait(false);
                    if (inspected.Id != incoming || inspected.Bytes != output.Bytes) throw new InvalidDataException("The prepared inventory does not match its complete encoded bytes.");
                    await AnalysisIo.Run(_io, devices, token =>
                    {
                        var current = Select(data, previousId, token);
                        if (current.Catalog != old.CatalogId || current.Entry != old.Entry) throw new InvalidDataException("The saved analysis changed during preparation. Review it again.");
                        using var directory = data.Store.BorrowDirectory();
                        var probe = directory.OpenReplacementFile(old.Entry.Name); using var close = new AnalysisWorkerClose(probe);
                        if (probe.Identity != old.PayloadIdentity) throw new InvalidDataException("The previous saved file changed during preparation.");
                        new AnalysisSavedCatalog(data.Store).ValidateReplacementBudget(old.Entry, incoming, inspected.Bytes, inspected.Coverage, inspected.Taint, view, token);
                        return true;
                    }, cancellation).ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    var previous = new OwnedAnalysisInventoryDetails(ProfileId, old.CatalogId, old.Entry, old.Entry.Bytes,
                        old.Inspection!.Started, old.Inspection.Observed, AnalysisPrivacy.Imported(old.Entry.Taint), old.PayloadSha256!, old.Inspection.Records);
                    var prepared = new OwnedAnalysisPreparedInventory(incoming, inspected.Bytes, inspected.Coverage, inspected.Taint,
                        inspected.Started, inspected.Observed, inspected.Sha256);
                    ready.TrySetResult(new(this, previous, prepared, decision, completed.Task));
                    var choice = await decision.Task.ConfigureAwait(false);
                    if (!choice.Replace) return Complete(new OwnedAnalysisReplacementOutcome(OwnedAnalysisReplacementState.Kept, previousId, incoming, old.Entry.Name,
                        null, null, "The existing saved analysis was kept. The prepared inventory was discarded."));
                    var consumed = output; output = null;
                    return Complete(await ExecuteReplacementAsync(data, old, incoming, operation, view, inspected, consumed, devices, choice.Cancellation).ConfigureAwait(false));
                }
                catch (OwnedAnalysisReplacementException error) { completedOperation = error.Outcome; throw; }
                finally
                {
                    if (output is not null) await AnalysisIo.ReleaseScratchAsync(_io, devices, output).ConfigureAwait(false);
                }
            });
        }
        catch
        {
            if (retainedSource is not null) await retainedSource.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        _ = FinishAsync();
        return await ready.Task.ConfigureAwait(false);

        async Task FinishAsync()
        {
            OwnedAnalysisReplacementOutcome? outcome = null; Exception? failure = null;
            try { outcome = await lifetime.ConfigureAwait(false); }
            catch (Exception error) { failure = error; }
            if (_owned.CloseFailure is { } ownerClose)
                failure = failure is null ? ownerClose : new AggregateException(failure, ownerClose);
            if (retainedSource is not null)
            {
                try { await retainedSource.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            }
            if (failure is not null)
            {
                // A source/root close can fail after the compound operation returned a confirmed
                // receipt. Preserve that fact while refusing to confirm cleanup; do not replace
                // it with a bare close exception that suggests the named save never happened.
                if (ready.Task.IsCompletedSuccessfully && completedOperation is { } known && AnalysisCloseUnconfirmedException.IsPresent(failure))
                    failure = new OwnedAnalysisReplacementException(known with
                    {
                        State = OwnedAnalysisReplacementState.Uncertain,
                        Detail = known.Receipt is not null
                            ? "The new named analysis was saved, but private cleanup could not be confirmed. Keep these observations and inspect the saved name before retrying."
                            : "Private replacement cleanup could not be confirmed. Keep remaining files and inspect the saved name before retrying."
                    }, failure);
                ready.TrySetException(failure); completed.TrySetException(failure);
            }
            else completed.TrySetResult(outcome!);
        }
        OwnedAnalysisReplacementOutcome Complete(OwnedAnalysisReplacementOutcome result) { completedOperation = result; return result; }
    }
}

public sealed partial class AnalysisDatasetLease
{
    internal AnalysisIoDevices ReplacementDevices(string destination) => _device.Include(destination);
    internal Task<AnalysisPrivateInventoryStore.DatasetWrite> PreparePrivateReplacementAsync(DeviceIoScheduler io, AnalysisIoDevices devices,
        Func<AnalysisPrivateDirectory> directory, string name, Guid id, CancellationToken cancellation) =>
        _owned.UseAsync(reader => AnalysisInventoryFormat.PrepareDatasetReplacementAsync(reader, io, devices, directory, name, id, cancellation));
}
