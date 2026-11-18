namespace FileCat.Core.Jobs;

public sealed partial class Job
{
    internal T RunEfsRecoveryInteraction<T>(Func<T> interaction)
    {
        Checkpoint();
        if (Kind != JobKind.StorageTransform || Request.StorageTransform?.Action != FileSystem.StorageAction.EncryptEfs || State != JobState.Running)
            throw new InvalidOperationException("The running encryption job must own its EFS recovery interaction.");
        SetState(JobState.AwaitingDecision);
        try { return interaction(); }
        finally { if (!IsCancellationRequested && State == JobState.AwaitingDecision) SetState(JobState.Running); }
    }

    private StorageTransformJournalWriter? _storageWriter;
    private StorageJournalCall? _storageCall;
    internal void AttachStorageWriter(StorageTransformJournalWriter writer)
    {
        lock (_startLock)
        {
            if (Kind != JobKind.StorageTransform || writer.Header.JobId != Id || _storageWriter is not null)
                throw new InvalidOperationException("This job cannot adopt that storage writer.");
            _storageWriter = writer;
        }
    }

    /// <summary>Parameters/held preparation, result reservation and a durable intent precede this decision.
    /// No observer, I/O, wait or native callback runs under the control lock.</summary>
    internal StorageCallAdmission TryAdmitStorageCall(StorageJournalCall call)
    {
        lock (_startLock)
        {
            if (!ReferenceEquals(call.Writer, _storageWriter) || call.JobId != Id || call.IsSettled || call.Writer.IsFaulted ||
                Request.StorageTransform is not { } request || call.Writer.Header.Action != request.Action ||
                call.Writer.Header.Scope != request.Scope || call.Writer.Header.ReviewRevision != request.ReviewRevision)
                return StorageCallAdmission.Stale;
            if (_storageCall is { IsSettled: false }) return StorageCallAdmission.Busy;
            if (IsCancellationRequested)
                return call.TryDeny() ? StorageCallAdmission.Canceled : StorageCallAdmission.Stale;
            if (!_runGate.IsSet || State is JobState.Paused or JobState.Pausing)
                return call.TryDeny() ? StorageCallAdmission.Paused : StorageCallAdmission.Stale;
            if (State != JobState.Running) return call.TryDeny() ? StorageCallAdmission.NotRunning : StorageCallAdmission.Stale;
            if (!call.TryAdmit()) return StorageCallAdmission.Stale;
            _storageCall = call;
            return StorageCallAdmission.Admitted;
        }
    }
    private StorageResultSnapshot? _storageResult;
    public StorageResultSnapshot? StorageResult => Volatile.Read(ref _storageResult);

    internal void SetStorageResult(StorageResultSnapshot result)
    {
        var request = Request.StorageTransform;
        if (Kind != JobKind.StorageTransform || request is null || result.Action != request.Action ||
            result.Scope != request.Scope || result.ReviewRevision != request.ReviewRevision)
            throw new InvalidOperationException("The storage result does not belong to this approved request.");
        Interlocked.Exchange(ref _storageResult, result);
        var c = result.Counts;
        Interlocked.Exchange(ref _itemsTotal, c.Observed);
        Interlocked.Exchange(ref _itemsDone, c.Successful);
        Interlocked.Exchange(ref _itemsSkipped, c.Skipped);
        Interlocked.Exchange(ref _itemsFailed, checked(c.Failed + c.Uncertain));
        SetSummary($"{c.Changed:N0} changed, {c.AlreadySatisfied:N0} already satisfied, {c.Skipped:N0} skipped, " +
            $"{c.Failed:N0} failed, {c.Uncertain:N0} uncertain, {c.NotProcessed:N0} not processed" +
            (result.Discovery == StorageCompleteness.Complete ? "." : result.Discovery == StorageCompleteness.UnknownRemainder
                ? ". Additional items may not have been discovered." : ". The requested scope was not fully completed."));
    }
}

internal enum StorageCallAdmission { Admitted, Canceled, Paused, NotRunning, Stale, Busy }
