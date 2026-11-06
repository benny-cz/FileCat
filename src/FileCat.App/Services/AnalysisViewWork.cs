using FileCat.Core.Analysis;

namespace FileCat.App.Services;

/// <summary>Counts accepted work through its actual finally/lease release, not a canceled await.</summary>
internal sealed class AnalysisViewWork
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private bool _closing;
    private Exception? _closeError;
    internal bool HasUnconfirmedClose { get { lock (_gate) return _closeError is not null; } }

    internal void RecordUnconfirmedClose(Exception error)
    {
        if (!AnalysisCloseUnconfirmedException.IsPresent(error)) return;
        lock (_gate) { _closeError ??= error; if (_closing && _active == 0) Complete(); }
    }
    internal static Exception PreserveCloseFailure(Exception? first, Exception next)
    {
        if (first is null) return next;
        return AnalysisCloseUnconfirmedException.IsPresent(next) && !AnalysisCloseUnconfirmedException.IsPresent(first)
            ? new AnalysisCloseUnconfirmedException(new AggregateException(first, next)) : first;
    }
    private void Complete()
    { if (_closeError is { } error) _drained.TrySetException(error); else _drained.TrySetResult(); }

    internal IDisposable Enter()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_closeError is { } error) throw new AnalysisCloseUnconfirmedException(error);
            _active++; return new Owner(this);
        }
    }
    internal Task CloseAsync()
    {
        lock (_gate) { _closing = true; if (_active == 0) Complete(); return _drained.Task; }
    }
    private void Release()
    {
        lock (_gate) { if (--_active == 0 && _closing) Complete(); }
    }
    private sealed class Owner(AnalysisViewWork owner) : IDisposable
    {
        private AnalysisViewWork? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
