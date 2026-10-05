namespace FileCat.App.Services;

/// <summary>Shared admission before a picture starts a process or borrows its input.</summary>
internal sealed class PictureDecoderAdmission(int workers = 4, int waiting = 32)
{
    private readonly SemaphoreSlim _slots = new(workers, workers);
    private readonly int _limit = checked(workers + waiting);
    private int _requests;

    internal int Requests => Volatile.Read(ref _requests);

    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        while (true)
        {
            int count = Volatile.Read(ref _requests);
            if (count >= _limit)
                throw new IOException("too many pictures are waiting to be decoded; close a viewer and try again");
            if (Interlocked.CompareExchange(ref _requests, count + 1, count) == count) break;
        }
        try
        {
            await _slots.WaitAsync(ct).ConfigureAwait(false);
            return new Lease(this);
        }
        catch
        {
            Interlocked.Decrement(ref _requests);
            throw;
        }
    }

    private sealed class Lease(PictureDecoderAdmission owner) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Interlocked.Decrement(ref owner._requests);
            owner._slots.Release();
        }
    }
}
