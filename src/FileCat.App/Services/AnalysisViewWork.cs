namespace FileCat.App.Services;

/// <summary>Counts accepted work through its actual finally/lease release, not a canceled await.</summary>
internal sealed class AnalysisViewWork
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private bool _closing;

    internal IDisposable Enter()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            _active++; return new Owner(this);
        }
    }
    internal Task CloseAsync()
    {
        lock (_gate) { _closing = true; if (_active == 0) _drained.TrySetResult(); return _drained.Task; }
    }
    private void Release()
    {
        lock (_gate) { if (--_active == 0 && _closing) _drained.TrySetResult(); }
    }
    private sealed class Owner(AnalysisViewWork owner) : IDisposable
    {
        private AnalysisViewWork? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
