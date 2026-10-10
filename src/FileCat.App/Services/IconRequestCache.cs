using System.Collections.Concurrent;
using Avalonia.Media;

namespace FileCat.App.Services;

/// <summary>
/// Bounds native icon sources' retained icons and waiting work. A full queue leaves the row's vector fallback;
/// a later redraw can ask again. Each request carries its entry identity so evicted or cleared work cannot return.
/// </summary>
internal sealed class IconRequestCache(int capacity = 4096, int queueCapacity = 256)
{
    private readonly object _gate = new();
    private readonly Dictionary<(int Size, string Key), Request> _entries = new();
    private readonly LinkedList<Request> _lru = new();
    private readonly BlockingCollection<Request> _queue = new(queueCapacity > 0 ? queueCapacity : throw new ArgumentOutOfRangeException(nameof(queueCapacity)));
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    internal sealed class Request((int Size, string Key) key)
    {
        internal (int Size, string Key) Key { get; } = key;
        internal IImage? Image;
        internal LinkedListNode<Request>? Node;
    }

    internal int Count { get { lock (_gate) return _entries.Count; } }
    internal int QueuedCount => _queue.Count;

    internal IImage? Get((int Size, string Key) key)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var present))
            {
                Touch(present);
                return present.Image;
            }
            var request = new Request(key);
            // TryAdd never waits for the native worker. It cannot consume/check this entry until we release the gate.
            if (!_queue.TryAdd(request)) return null;
            if (_entries.Count == _capacity)
            {
                var oldest = _lru.First!.Value;
                _entries.Remove(oldest.Key);
                _lru.RemoveFirst();
                oldest.Node = null;
                oldest.Image = null;
                // Rows borrow images: an evicted image can still be displayed and must not be disposed here.
            }
            request.Node = _lru.AddLast(request);
            _entries.Add(key, request);
            return null;
        }
    }

    internal IEnumerable<Request> Requests(CancellationToken ct = default)
    {
        foreach (var request in _queue.GetConsumingEnumerable(ct))
        {
            bool current;
            lock (_gate) current = IsCurrent(request);
            if (current) yield return request;
        }
    }

    /// <summary>Publishes only the still-current request; an unpublished bitmap has no borrower and is disposed.</summary>
    internal bool Complete(Request request, IImage? image)
    {
        lock (_gate)
        {
            if (IsCurrent(request))
            {
                request.Image = image;
                Touch(request);
                return true;
            }
        }
        (image as IDisposable)?.Dispose();
        return false;
    }

    internal void Clear()
    {
        lock (_gate)
        {
            foreach (var request in _entries.Values)
            {
                request.Node = null;
                // An idle consuming enumerator can still hold the retired request. Actual row borrowers keep
                // their own image reference, so release ours without disposing a published image.
                request.Image = null;
            }
            _entries.Clear();
            _lru.Clear();
            while (_queue.TryTake(out _)) { }
        }
    }

    private bool IsCurrent(Request request) =>
        _entries.TryGetValue(request.Key, out var present) && ReferenceEquals(request, present);

    private void Touch(Request request)
    {
        _lru.Remove(request.Node!);
        _lru.AddLast(request.Node!);
    }
}
