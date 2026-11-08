namespace FileCat.Core.Analysis;

public sealed partial class OwnedAnalysisStore
{
    private async Task<OwnedAnalysisDeletionOutcome> FinishDeletionAsync(Guid id, string name,
        Func<Task<OwnedAnalysisDeletionOutcome>> operation)
    {
        OwnedAnalysisDeletionOutcome? result = null; Exception? failure = null;
        try { result = await operation().ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        // Observe only a completed close. Waiting for owner disposal here would deadlock
        // when this deletion is nested under the still-active rolling transaction.
        if (_owned.CloseFailure is { } close)
        {
            var known = result ?? (failure as OwnedAnalysisDeletionException)?.Outcome ??
                new(OwnedAnalysisDeletionState.Uncertain, id, name, false, null, "No completed removal is claimed.");
            string fact = known.PayloadAbsentConfirmed ? "The reviewed private payload was removed. "
                : known.State == OwnedAnalysisDeletionState.Restored ? "The private catalog entry was restored and its saved bytes were kept. "
                : "No completed payload removal is claimed. ";
            throw new OwnedAnalysisDeletionException(known with
            {
                State = OwnedAnalysisDeletionState.Uncertain,
                Detail = fact + "Private store cleanup could not be confirmed. Keep remaining files and inspect the current store before retrying."
            }, failure is null ? close : new AggregateException(failure, close));
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result!;
    }

    private static bool ContainsUncertainDeletion(Exception error) =>
        error is OwnedAnalysisDeletionException { Outcome.State: OwnedAnalysisDeletionState.Uncertain } ||
        error is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsUncertainDeletion) ||
        error.InnerException is { } inner && ContainsUncertainDeletion(inner);
}
