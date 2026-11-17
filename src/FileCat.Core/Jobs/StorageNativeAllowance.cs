namespace FileCat.Core.Jobs;

/// <summary>One storage-purpose native owner: 128 ancestor/traversal handles and 60 scratch handles, including its writer.</summary>
internal sealed class StorageNativeAllowance : IDisposable
{
    [ThreadStatic] private static StorageNativeAllowance? _current;
    private readonly object _lock = new();
    private Action? _release;
    private int _traversal, _scratch;
    private bool _retiring;
    internal static StorageNativeAllowance? Current => _current;
    internal StorageNativeAllowance(Action release) { _release = release; _current = this; }
    internal static StorageNativeAllowance RequireCurrent() => _current ?? throw new InvalidOperationException("No owned storage native allowance is active on this thread.");

    internal IDisposable ClaimTraversal() => Claim(true);
    internal IDisposable ClaimScratch() => Claim(false);
    private IDisposable Claim(bool traversal)
    {
        lock (_lock)
        {
            if (_retiring || !ReferenceEquals(_current, this)) throw new InvalidOperationException("This storage native allowance cannot admit another handle.");
            ref int used = ref (traversal ? ref _traversal : ref _scratch);
            if (used >= (traversal ? 128 : 60)) throw new InvalidOperationException("The storage native handle bound was reached.");
            used++;
        }
        return new Slot(this, traversal);
    }
    private void Return(bool traversal)
    {
        Action? release = null;
        lock (_lock)
        {
            if (traversal) _traversal--; else _scratch--;
            if (_retiring && _traversal + _scratch == 0) { release = _release; _release = null; }
        }
        release?.Invoke();
    }
    public void Dispose()
    {
        Action? release = null;
        lock (_lock)
        {
            if (_retiring) return;
            _retiring = true;
            if (ReferenceEquals(_current, this)) _current = null;
            if (_traversal + _scratch == 0) { release = _release; _release = null; }
        }
        // Any started owner keeps the exclusive allowance until its last charged handle actually retires.
        release?.Invoke();
    }
    private sealed class Slot(StorageNativeAllowance owner, bool traversal) : IDisposable
    {
        private StorageNativeAllowance? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Return(traversal);
    }
}
