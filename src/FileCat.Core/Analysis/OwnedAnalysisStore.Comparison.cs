namespace FileCat.Core.Analysis;

/// <summary>One-use review of an exact comparison bundle in the current owned private directory.</summary>
public sealed class OwnedAnalysisComparisonDeleteReview
{
    internal readonly OwnedAnalysisStore Owner;
    internal readonly string NativeIdentity, Sha256;
    private int _used;
    internal OwnedAnalysisComparisonDeleteReview(OwnedAnalysisStore owner, string name, string nativeIdentity,
        AnalysisComparisonFormat.ContainerInspection inspected)
    {
        Owner = owner; Name = name; NativeIdentity = nativeIdentity; Sha256 = inspected.Sha256;
        ComparisonId = inspected.Id; Bytes = inspected.Bytes; SavedUtc = inspected.SavedUtc; Taint = inspected.Taint;
    }
    public string Name { get; }
    public Guid ComparisonId { get; }
    public long Bytes { get; }
    public DateTimeOffset SavedUtc { get; }
    public AnalysisTaint Taint { get; }
    internal void Consume(OwnedAnalysisStore owner)
    {
        if (!ReferenceEquals(owner, Owner)) throw new ArgumentException("This comparison review belongs to another private-store capability.");
        if (Interlocked.Exchange(ref _used, 1) != 0) throw new InvalidOperationException("This comparison review was already used. Obtain a fresh review.");
    }
}

public sealed record OwnedAnalysisComparisonDeletionOutcome(OwnedAnalysisDeletionState State, Guid ComparisonId,
    string Name, bool RemovalConfirmed, string Detail);
public sealed class OwnedAnalysisComparisonDeletionException(OwnedAnalysisComparisonDeletionOutcome outcome, Exception inner) : IOException(outcome.Detail, inner)
{
    public OwnedAnalysisComparisonDeletionOutcome Outcome { get; } = outcome;
}

public sealed partial class OwnedAnalysisStore
{
    private static void RequireComparison(Contents data, string name, CancellationToken cancellation)
    {
        RequireProfile(data, cancellation);
        if (!data.Store!.ComparisonNames(cancellation).Contains(name, StringComparer.Ordinal))
            throw new FileNotFoundException("The selected comparison is absent from this private profile.");
    }
    private static AnalysisPrivateFile OpenComparison(Contents data, string name, bool maintenance)
    {
        using var directory = data.Store!.BorrowDirectory();
        return maintenance ? directory.OpenMaintenance(name) : directory.OpenRead(name);
    }

    public Task<OwnedAnalysisComparisonDeleteReview> PrepareComparisonDeleteAsync(string name, CancellationToken cancellation = default)
    {
        AnalysisPrivateInventoryStore.ValidateComparisonName(name);
        return _owned.UseAsync<OwnedAnalysisComparisonDeleteReview>(async data =>
        {
            var work = new DeletionWork();
            try
            {
                var basis = await AnalysisIo.Run(_io, _device, token =>
                {
                    RequireComparison(data, name, token); work.Payload = OpenComparison(data, name, maintenance: false);
                    if (work.Payload.Stream.Length is < 1 or > AnalysisLimits.InventoryBytes) throw new InvalidDataException("The saved comparison exceeds its size limit.");
                    return (Identity: work.Payload.Identity, Bytes: work.Payload.Stream.Length);
                }, cancellation).ConfigureAwait(false);
                var inspected = await AnalysisComparisonFormat.InspectContainerAsync(work.Payload!.Stream, work.Payload.Verify, _io, _device, cancellation).ConfigureAwait(false);
                if (inspected.Bytes != basis.Bytes) throw new InvalidDataException("The saved comparison size changed during review.");
                await AnalysisIo.Run(_io, _device, token =>
                {
                    RequireComparison(data, name, token); work.Payload!.Verify(); work.ClosePayload();
                    work.Payload = OpenComparison(data, name, maintenance: true);
                    if (work.Payload.Identity != basis.Identity || work.Payload.Stream.Length != basis.Bytes)
                        throw new InvalidDataException("The selected comparison object changed. Obtain a fresh review.");
                    work.ClosePayload(); return true;
                }, cancellation).ConfigureAwait(false);
                return new(this, name, basis.Identity, inspected);
            }
            finally { await AnalysisIo.ReleaseScratchAsync(_io, _device, work).ConfigureAwait(false); }
        });
    }

    public Task<OwnedAnalysisComparisonDeletionOutcome> DeleteComparisonAsync(OwnedAnalysisComparisonDeleteReview review, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(review); review.Consume(this);
        return _owned.UseAsync<OwnedAnalysisComparisonDeletionOutcome>(async data =>
        {
            var work = new DeletionWork(); Exception? failure = null; bool attempted = false, removed = false;
            try
            {
                await AnalysisIo.Run(_io, _device, token =>
                {
                    work.Writer = data.Store!.OpenCatalogLock(token); RequireComparison(data, review.Name, token);
                    work.Payload = OpenComparison(data, review.Name, maintenance: true);
                    if (work.Payload.Identity != review.NativeIdentity || work.Payload.Stream.Length != review.Bytes)
                        throw new InvalidDataException("The reviewed comparison object changed. Obtain a fresh review.");
                    return true;
                }, cancellation).ConfigureAwait(false);
                var inspected = await AnalysisComparisonFormat.InspectContainerAsync(work.Payload!.Stream, work.Payload.Verify, _io, _device, cancellation).ConfigureAwait(false);
                if (inspected.Id != review.ComparisonId || inspected.Bytes != review.Bytes || inspected.Sha256 != review.Sha256)
                    throw new InvalidDataException("The reviewed comparison bytes changed. Obtain a fresh review.");
                await AnalysisIo.Run(_io, _device, token =>
                {
                    RequireProfile(data, token); work.Writer!.Verify(); work.Payload!.Verify(); token.ThrowIfCancellationRequested();
                    attempted = true; work.Payload.RemoveOwned(); work.ClosePayload(); removed = true; return true;
                }, cancellation).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failure = error;
                if (work.Payload is { RemovalRequested: false }) attempted = false;
            }
            finally
            {
                try { await AnalysisIo.ReleaseScratchAsync(_io, _device, work).ConfigureAwait(false); }
                catch (Exception close) { failure = failure is null ? close : new AggregateException(failure, close); }
            }
            if (failure is null) return new(OwnedAnalysisDeletionState.Deleted, review.ComparisonId, review.Name, true,
                "The saved comparison, its two captured inputs and its saved view were deleted. Source files and independent open observations were not changed.");
            bool uncertain = attempted && !removed || AnalysisCloseUnconfirmedException.IsPresent(failure);
            if (!uncertain && failure is OperationCanceledException) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            throw new OwnedAnalysisComparisonDeletionException(new(uncertain ? OwnedAnalysisDeletionState.Uncertain : OwnedAnalysisDeletionState.Kept,
                review.ComparisonId, review.Name, removed, uncertain
                    ? "The comparison removal outcome could not be confirmed. Keep remaining files and inspect this private profile before retrying."
                    : "The saved comparison was kept; removal was not completed. " + AnalysisNames.Display(failure.Message, 400)), failure);
        });
    }
}
