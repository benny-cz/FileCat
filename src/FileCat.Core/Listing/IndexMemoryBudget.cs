namespace FileCat.Core.Listing;

/// <summary>Shared reservation for transient and retained integer listing indexes.</summary>
public sealed class IndexMemoryBudget
{
    private readonly object _gate = new();
    private readonly Dictionary<object, long> _reservations = new();
    private long _used;

    public IndexMemoryBudget(long limitBytes = 512L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limitBytes, 1);
        LimitBytes = limitBytes;
    }

    public long LimitBytes { get; }
    public long ReservedBytes { get { lock (_gate) return _used; } }

    public bool TryReserve(object owner, long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        lock (_gate)
        {
            long previous = _reservations.GetValueOrDefault(owner);
            if (bytes > previous && bytes - previous > LimitBytes - _used) return false;
            _used += bytes - previous;
            if (bytes == 0) _reservations.Remove(owner);
            else _reservations[owner] = bytes;
            return true;
        }
    }

    public void Release(object owner) => TryReserve(owner, 0);
}
