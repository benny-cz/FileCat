using FileCat.Core.Jobs;
using FileCat.Core.Threading;

namespace FileCat.App.Services;

internal sealed record StorageAccountingState(bool Running, bool Stopping, bool UserStopped, bool Interactive,
    bool NeedsScan, StorageHistoryAccounting? Result, DateTime? ObservedUtc, string? Error);

/// <summary>One background census. Interactive leases cancel and await its actual owned delegate, then prevent a
/// replacement census until their own evidence operation has retired. No native owner crosses a user decision.</summary>
internal sealed class StorageHistoryAccountingRunner(DeviceIoScheduler io, string device,
    Func<CancellationToken, StorageHistoryAccounting> account) : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _stop;
    private Task? _work;
    private int _interactive;
    private long _generation, _attempted = -1;
    private bool _userStopped, _disposed;
    private StorageHistoryAccounting? _result;
    private DateTime? _observed;
    private string? _error;
    internal event Action? Changed;
    internal StorageAccountingState State
    {
        get
        {
            lock (_gate) return new(_work is not null, _stop?.IsCancellationRequested == true, _userStopped,
                _interactive > 0, _attempted != _generation, _result, _observed, _error);
        }
    }

    internal void Start()
    {
        CancellationTokenSource stop; TaskCompletionSource completion; long generation;
        lock (_gate)
        {
            if (_disposed || _userStopped || _work is not null || _interactive > 0 || _attempted == _generation) return;
            stop = _stop = CancellationTokenSource.CreateLinkedTokenSource(io.StoppingToken);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _work = completion.Task;
            generation = _generation;
        }
        Notify();
        _ = RunAsync(stop, completion, generation);
    }

    private async Task RunAsync(CancellationTokenSource stop, TaskCompletionSource completion, long generation)
    {
        StorageHistoryAccounting? result = null; string? error = null;
        try { result = await io.RunOwned(device, IoPriority.Background, account, stop.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested || io.IsStopped) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { error = ex.Message.Length <= 1024 ? ex.Message : ex.Message[..1024] + "…"; }
        finally
        {
            lock (_gate)
            {
                // A changed history, user Stop or interactive cancellation cannot publish an old complete inventory.
                if (!_disposed && !stop.IsCancellationRequested && !io.IsStopped && generation == _generation)
                {
                    _attempted = generation; _result = result; _observed = DateTime.UtcNow; _error = error;
                }
                _work = null; _stop = null;
            }
            stop.Dispose(); completion.TrySetResult(); Notify();
        }
    }

    internal void Invalidate(bool refresh = false)
    {
        CancellationTokenSource? stop;
        lock (_gate)
        {
            if (_disposed) return;
            _generation++; _result = null; _observed = null; _error = null;
            if (refresh) _userStopped = false;
            stop = _stop;
        }
        Cancel(stop); Notify();
    }

    internal void Stop()
    {
        CancellationTokenSource? stop;
        lock (_gate) { _userStopped = true; stop = _stop; }
        Cancel(stop); Notify();
    }

    internal async Task<IDisposable> EnterInteractiveAsync(CancellationToken ct)
    {
        Task? previous; CancellationTokenSource? stop;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _interactive++; previous = _work; stop = _stop;
        }
        Cancel(stop); Notify();
        try
        {
            if (previous is not null) await previous.WaitAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return new InteractiveLease(this);
        }
        catch { LeaveInteractive(); throw; }
    }

    internal Task WhenIdle()
    {
        lock (_gate) return _work ?? Task.CompletedTask;
    }
    private void LeaveInteractive() { lock (_gate) _interactive--; Notify(); }
    private sealed class InteractiveLease(StorageHistoryAccountingRunner owner) : IDisposable
    {
        private StorageHistoryAccountingRunner? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.LeaveInteractive();
    }
    private static void Cancel(CancellationTokenSource? stop)
    {
        try { stop?.Cancel(); }
        catch (ObjectDisposedException) { /* The owned scan already retired. */ }
    }
    private void Notify()
    {
        if (Changed is not { } handlers) return;
        foreach (Action observer in handlers.GetInvocationList())
            try { observer(); }
            catch (Exception ex) { FileCat.Core.Diagnostics.AppLog.Warn("Storage history observer failed", ex); }
    }
    public void Dispose()
    {
        CancellationTokenSource? stop;
        lock (_gate) { _disposed = true; _userStopped = true; stop = _stop; }
        Cancel(stop); Notify(); // the started delegate retains its CTS/owners until RunOwned actually returns
    }
}
