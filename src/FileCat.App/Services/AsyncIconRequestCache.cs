using System.Threading.Channels;

namespace FileCat.App.Services;

/// <summary>
/// Bounded per-item icon work. Rows never wait for the workers; rejected demand can retry on a later redraw.
/// Completion belongs to a particular entry, so evicted/cleared work cannot repopulate the cache.
/// </summary>
internal sealed class AsyncIconRequestCache<T>(Action<T> releaseUnpublished, int capacity = 4096, int queueCapacity = 256) where T : class
{
    private readonly object _gate = new();
    private readonly Dictionary<(int Size, string Key), Request> _entries = new(new KeyComparer());
    private readonly LinkedList<Request> _lru = new();
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly Channel<Request> _queue = Channel.CreateBounded<Request>(
        queueCapacity > 0 ? queueCapacity : throw new ArgumentOutOfRangeException(nameof(queueCapacity)));

    internal sealed class Request((int Size, string Key) key, Func<Task<T>> load)
    {
        internal (int Size, string Key) Key { get; } = key;
        internal Func<Task<T>> Load { get; } = load;
        internal T? Value;
        internal LinkedListNode<Request>? Node;
    }

    internal int Count { get { lock (_gate) return _entries.Count; } }
    internal int QueuedCount => _queue.Reader.Count;

    internal T? Get((int Size, string Key) key, Func<Task<T>> load)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var current))
            {
                Touch(current);
                return current.Value;
            }
            var request = new Request(key, load);
            if (!_queue.Writer.TryWrite(request)) return null;
            if (_entries.Count == _capacity)
            {
                var oldest = _lru.First!.Value;
                _entries.Remove(oldest.Key);
                _lru.RemoveFirst();
                oldest.Node = null;
                // Published images can still be borrowed by rows. Only unpublished results are ours to dispose.
            }
            request.Node = _lru.AddLast(request);
            _entries.Add(key, request);
            return null;
        }
    }

    internal async IAsyncEnumerable<Request> Requests(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var request in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            bool current;
            lock (_gate) current = IsCurrent(request);
            if (current) yield return request;
        }
    }

    internal bool Complete(Request request, T value, bool retry = false)
    {
        lock (_gate)
        {
            if (IsCurrent(request))
            {
                if (!retry)
                {
                    request.Value = value;
                    Touch(request);
                    return true;
                }
                _entries.Remove(request.Key);
                _lru.Remove(request.Node!);
                request.Node = null;
            }
        }
        releaseUnpublished(value);
        return false;
    }

    internal void Clear()
    {
        lock (_gate)
        {
            foreach (var request in _entries.Values) request.Node = null;
            _entries.Clear();
            _lru.Clear();
            while (_queue.Reader.TryRead(out _)) { }
        }
    }

    private bool IsCurrent(Request request) =>
        _entries.TryGetValue(request.Key, out var current) && ReferenceEquals(request, current);

    private void Touch(Request request)
    {
        _lru.Remove(request.Node!);
        _lru.AddLast(request.Node!);
    }

    private sealed class KeyComparer : IEqualityComparer<(int Size, string Key)>
    {
        public bool Equals((int Size, string Key) x, (int Size, string Key) y) =>
            x.Size == y.Size && StringComparer.OrdinalIgnoreCase.Equals(x.Key, y.Key);
        public int GetHashCode((int Size, string Key) key) =>
            HashCode.Combine(key.Size, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Key));
    }
}
