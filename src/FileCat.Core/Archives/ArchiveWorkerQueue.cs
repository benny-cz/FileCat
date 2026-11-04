namespace FileCat.Core.Archives;

/// <summary>One active archive process globally, with at most eight waiting assessments/transforms/verifications.</summary>
public sealed class ArchiveWorkerQueue
{
    public static ArchiveWorkerQueue Shared { get; } = new();
    private readonly SemaphoreSlim _active = new(1, 1);
    private int _waiting;

    public async ValueTask<IDisposable> EnterAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_active.Wait(0)) return new Reservation(_active);
        int waiting = Interlocked.Increment(ref _waiting);
        try
        {
            if (waiting > 8) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded,
                "Eight archive tasks are already waiting. Try again when one finishes.");
            await _active.WaitAsync(ct).ConfigureAwait(false);
            return new Reservation(_active);
        }
        finally { Interlocked.Decrement(ref _waiting); }
    }

    private sealed class Reservation(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) semaphore.Release(); }
    }
}
