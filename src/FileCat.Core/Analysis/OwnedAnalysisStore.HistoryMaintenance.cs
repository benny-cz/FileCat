using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed partial class OwnedAnalysisStore
{
    // A fresh policy proposal over the isolated store. No new acquisition is necessary.
    // Prepare every exact-object review before removing anything, then reuse the same
    // native deletion transaction used by capture and explicit history maintenance.
    internal Task<AnalysisRollingCaptureOutcome> PruneRollingAsync(AnalysisRollingHistoryPolicy policy,
        IReadOnlySet<Guid>? protectedInventories, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(policy); policy.Validate();
        return FinishRollingAsync(() => _owned.UseAsync<AnalysisRollingCaptureOutcome>(async data =>
        {
            if (!data.Rolling) throw new InvalidOperationException("An isolated history owner is required.");
            if (!policy.Enabled) return new(AnalysisRollingCaptureState.Disabled, null, null, false, [], "History is off. Existing history was kept.");
            var work = new RollingWork(); var removed = ImmutableArray.CreateBuilder<Guid>();
            Exception? failure = null; bool mutationStarted = false;
            AnalysisRollingCaptureOutcome? result = null;
            try
            {
                var entries = await AnalysisIo.Run(_io, _device, token =>
                {
                    RequireProfile(data, token); work.Writer = data.Store!.OpenCatalogLock(token);
                    return RollingEntries(data, null, token);
                }, cancellation).ConfigureAwait(false);
                var plan = AnalysisRollingHistoryPlanner.Plan(policy,
                    entries.Select(entry => new AnalysisRollingHistoryEntry(entry.InventoryId, entry.Bytes, entry.SavedUtc)).ToArray(),
                    DateTimeOffset.UtcNow, protectedInventories: protectedInventories, cancellation: cancellation);
                if (plan.State != AnalysisHistoryPlanState.Ready)
                    result = new(AnalysisRollingCaptureState.Deferred, null, null, false, [],
                        "Automatic history maintenance was deferred because the clock, limits or protected entries could not be confirmed. Existing history was kept.");
                else
                {
                    var reviews = new List<OwnedAnalysisDeleteReview>(plan.ProposedEvictions.Length);
                    foreach (var eviction in plan.ProposedEvictions)
                        reviews.Add(await PrepareDeleteAsync(eviction.InventoryId, cancellation).ConfigureAwait(false));
                    await AnalysisIo.Run(_io, _device, token =>
                    {
                        work.Writer!.Verify();
                        if (!RollingEntries(data, null, token).SequenceEqual(entries))
                            throw new InvalidDataException("History changed while retention was prepared. Existing entries were kept.");
                        return true;
                    }, cancellation).ConfigureAwait(false);
                    foreach (var review in reviews)
                    {
                        await AnalysisIo.Run(_io, _device, token => { work.Writer!.Verify(); _ = RollingEntries(data, null, token); return true; }, cancellation).ConfigureAwait(false);
                        mutationStarted = true;
                        try
                        {
                            var outcome = await DeleteUnderHistoryLockAsync(review, work.Writer!, cancellation).ConfigureAwait(false);
                            if (outcome.State != OwnedAnalysisDeletionState.Deleted) throw new IOException("History removal was not confirmed.");
                            removed.Add(review.Entry.InventoryId);
                        }
                        catch (OwnedAnalysisDeletionException error)
                        { if (error.Outcome.PayloadAbsentConfirmed) removed.Add(review.Entry.InventoryId); throw; }
                    }
                    await AnalysisIo.Run(_io, _device, token =>
                    {
                        work.Writer!.Verify(); var final = RollingEntries(data, null, token);
                        if (!final.Select(entry => entry.InventoryId).Order().SequenceEqual(plan.RetainedInventories.Order()) ||
                            final.Aggregate((UInt128)0, (sum, entry) => sum + (UInt128)entry.Bytes) != plan.RetainedBytes)
                            throw new InvalidDataException("The history catalog no longer matches the retention proposal.");
                        return true;
                    }, cancellation).ConfigureAwait(false);
                    result = new(AnalysisRollingCaptureState.Maintained, null, null, false, removed.ToImmutable(),
                        removed.Count == 0 ? "History is within its enabled retention limits."
                            : $"Removed {removed.Count:N0} expired or excess private history copies. Named analyses and source files were unchanged.");
                }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                try { await AnalysisIo.ReleaseScratchAsync(_io, _device, work).ConfigureAwait(false); }
                catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            }
            if (failure is null) return result!;
            var state = AnalysisCloseUnconfirmedException.IsPresent(failure) ? AnalysisRollingCaptureState.Uncertain
                : mutationStarted ? AnalysisRollingCaptureState.RecoveryRequired : AnalysisRollingCaptureState.Deferred;
            throw new AnalysisRollingCaptureException(new(state, null, null, false, removed.ToImmutable(),
                mutationStarted ? "History maintenance was not fully confirmed. Keep remaining files and review current history before retrying."
                    : "Automatic history maintenance was deferred. Existing history was kept. " + AnalysisNames.Display(failure.Message, 400)), failure);
        }));
    }
}
