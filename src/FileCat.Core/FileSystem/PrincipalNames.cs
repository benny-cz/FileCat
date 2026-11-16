using System.Diagnostics;

namespace FileCat.Core.FileSystem;

/// <summary>Demand-based account lookup. Native calls use dedicated threads and four process-wide slots,
/// including calls that outlive invalidation/disposal. No mutable names or sources are process-global.</summary>
public sealed class PrincipalNames : IDisposable
{
    private static readonly SemaphoreSlim NativeSlots = new(4, 4);
    private static int _nativeActive;
    private readonly object _gate = new();
    private readonly IOwnership _source;
    private readonly Func<long> _clock;
    private readonly int _maxEntries, _maxChars, _maxQueued;
    private readonly long _slowAfter;
    private readonly Dictionary<Principal, Entry> _cache = [];
    private readonly LinkedList<Entry> _recent = [];
    private readonly Queue<Entry> _queue = [];
    private readonly HashSet<Entry> _running = [], _retiring = [];
    private readonly CancellationTokenSource _stopping = new();
    private CancellationTokenSource _generationCancellation = new();
    private readonly Timer _watchdog;
    private TaskCompletionSource _idle = CompletedIdle();
    private int _generation, _workers, _chars;
    private bool _disposed, _signalsDisposed;

    private sealed class Entry(Principal principal, int generation)
    {
        public Principal Principal { get; } = principal;
        public int Generation { get; } = generation;
        public PrincipalName Answer = new(NameStatus.Pending);
        public bool Pending = true;
        public long At, Started;
        public int Chars = 2 * (principal.Id.Length + principal.Authority.Length);
        public LinkedListNode<Entry>? Node;
    }

    public PrincipalNames(IOwnership source) : this(source, () => Environment.TickCount64) { }

    internal PrincipalNames(IOwnership source, Func<long> clock, int maxEntries = 4096, int maxChars = 262_144,
        int maxQueued = 256, TimeSpan? slowAfter = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxQueued, 1);
        _source = source;
        _clock = clock;
        _maxEntries = maxEntries;
        _maxChars = maxChars;
        _maxQueued = maxQueued;
        _slowAfter = (long)(slowAfter ?? TimeSpan.FromSeconds(3)).TotalMilliseconds;
        _watchdog = new Timer(_ => ExpandSlowWorkers(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event Action? Changed;
    internal static int ActiveNativeCalls => Volatile.Read(ref _nativeActive);
    internal (int Entries, int Characters, int Queued, int Running, int Workers) Usage
    { get { lock (_gate) return (_cache.Count + _retiring.Count, _chars, _queue.Count, _running.Count, _workers); } }

    public PrincipalName Peek(Principal principal)
    {
        lock (_gate)
        {
            if (_disposed || !_cache.TryGetValue(principal, out var entry)) return new(NameStatus.NotRequested);
            Touch(entry);
            return entry.Answer;
        }
    }

    /// <summary>Only an ownership producer calls this; formatting and sorting call Peek/Capture instead.</summary>
    public PrincipalName Request(Principal principal)
    {
        lock (_gate)
        {
            if (_disposed) return new(NameStatus.NotRequested);
            if (_cache.TryGetValue(principal, out var entry))
            {
                Touch(entry);
                if (entry.Pending || _clock() - entry.At < Lifetime(entry.Answer.Status)) return entry.Answer;
                if (_queue.Count >= _maxQueued) return entry.Answer;
                entry.Pending = true;
            }
            else
            {
                long requiredChars = 2L * (principal.Id.Length + (long)principal.Authority.Length);
                if (requiredChars > _maxChars || _queue.Count >= _maxQueued || !MakeRoom((int)requiredChars, true, null)) return new(NameStatus.NotRequested);
                int chars = (int)requiredChars;
                entry = new Entry(principal, _generation);
                entry.Node = _recent.AddLast(entry);
                _cache.Add(principal, entry);
                _chars += chars;
            }
            _queue.Enqueue(entry);
            if (_workers == 0) _watchdog.Change(100, 100);
            if (_idle.Task.IsCompleted) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            while (_workers < 2) StartWorker();
            Monitor.PulseAll(_gate);
            return entry.Answer;
        }
    }

    /// <summary>A single bounded name view for one ordering operation. The caller charges its retained references.</summary>
    public IReadOnlyDictionary<Principal, PrincipalName> Capture()
    {
        lock (_gate) return _cache.ToDictionary(pair => pair.Key, pair => pair.Value.Answer);
    }

    public async Task<bool> WhenIdle(TimeSpan timeout, CancellationToken ct = default)
    {
        Task idle;
        lock (_gate) idle = _idle.Task;
        try { await idle.WaitAsync(timeout, ct).ConfigureAwait(false); return true; }
        catch (TimeoutException) { return false; }
    }

    private static long Lifetime(NameStatus status) => status switch
    {
        NameStatus.Resolved => 600_000,
        NameStatus.NotFound => 120_000,
        _ => 30_000,
    };

    private void StartWorker()
    {
        _workers++;
        new Thread(Work) { IsBackground = true, Name = "FileCat principal names" }.Start();
    }

    private void ExpandSlowWorkers()
    {
        lock (_gate)
            if (!_disposed && _queue.Count > 0 && _workers < 4 && _running.Any(e => _clock() - e.Started >= _slowAfter))
                StartWorker();
    }

    private void Work()
    {
        try
        {
            while (true)
            {
                Entry entry;
                CancellationToken generationToken;
                lock (_gate)
                {
                    while (_queue.Count == 0 && !_disposed) Monitor.Wait(_gate);
                    if (_disposed) return;
                    entry = _queue.Dequeue();
                    entry.Started = _clock();
                    _running.Add(entry);
                    generationToken = _generationCancellation.Token;
                }
                PrincipalName answer = new(NameStatus.Unavailable, Reason: "Account lookup is unavailable just now.");
                bool acquired = false;
                try
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(generationToken, _stopping.Token);
                    NativeSlots.Wait(wait.Token);
                    acquired = true;
                    Interlocked.Increment(ref _nativeActive);
                    wait.Token.ThrowIfCancellationRequested();
                    var elapsed = Stopwatch.StartNew();
                    answer = _source.LookUp(entry.Principal);
                    if (answer.Status == NameStatus.NotFound && elapsed.ElapsedMilliseconds >= _slowAfter)
                        answer = new(NameStatus.Unavailable, Reason: "Account lookup did not return a name promptly. It can be retried later.");
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
                    or EntryPointNotFoundException or DllNotFoundException or ArgumentException)
                { answer = new(NameStatus.Unavailable, Reason: "Account lookup is unavailable just now."); }
                finally
                {
                    if (acquired) { Interlocked.Decrement(ref _nativeActive); NativeSlots.Release(); }
                }
                lock (_gate)
                {
                    _running.Remove(entry);
                    if (_retiring.Remove(entry)) _chars -= entry.Chars;
                    else if (!_disposed && entry.Generation == _generation && _cache.TryGetValue(entry.Principal, out var current) && ReferenceEquals(current, entry))
                    {
                        if (answer.Status is not (NameStatus.Resolved or NameStatus.NotFound or NameStatus.Unavailable) ||
                            answer.Name?.Length > 1024 || AnswerChars(answer) > _maxChars / 2)
                            answer = new(NameStatus.Unavailable, Reason: "The account lookup answer exceeded the supported limit.");
                        int extra = AnswerChars(answer) - AnswerChars(entry.Answer);
                        entry.Pending = false;
                        if (MakeRoom(extra, false, entry))
                        {
                            _chars += extra;
                            entry.Chars += extra;
                            entry.Answer = answer;
                            entry.At = _clock();
                            Changed?.Invoke(); // short memory-only notification; disposal uses this same lock
                        }
                        else Remove(entry);
                    }
                    if (_queue.Count == 0 && _running.Count == 0) _idle.TrySetResult();
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _workers--;
                if (_disposed && _workers == 0) DisposeSignals();
            }
        }
    }

    private static int AnswerChars(PrincipalName answer) => (answer.Name?.Length ?? 0) + (answer.Type?.Length ?? 0) + (answer.Reason?.Length ?? 0);

    private bool MakeRoom(int chars, bool newEntry, Entry? keep)
    {
        while (_chars + chars > _maxChars || newEntry && _cache.Count + _retiring.Count >= _maxEntries)
        {
            var node = _recent.First;
            while (node is not null && (node.Value.Pending || ReferenceEquals(node.Value, keep))) node = node.Next;
            if (node is null) return false;
            Remove(node.Value);
        }
        return true;
    }

    private void Touch(Entry entry)
    {
        _recent.Remove(entry.Node!);
        _recent.AddLast(entry.Node!);
    }

    private void Remove(Entry entry)
    {
        _cache.Remove(entry.Principal);
        _recent.Remove(entry.Node!);
        _chars -= entry.Chars;
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            if (_disposed) return;
            RetireGeneration();
            Changed?.Invoke();
        }
    }

    private void RetireGeneration()
    {
        _generation++;
        _generationCancellation.Cancel();
        _generationCancellation.Dispose();
        _generationCancellation = new();
        foreach (var entry in _cache.Values)
            if (_running.Contains(entry)) _retiring.Add(entry);
            else _chars -= entry.Chars;
        _cache.Clear();
        _recent.Clear();
        _queue.Clear();
        if (_running.Count == 0) _idle.TrySetResult();
        Monitor.PulseAll(_gate);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Changed = null;
            RetireGeneration();
            _stopping.Cancel();
            _watchdog.Dispose();
            if (_workers == 0) DisposeSignals();
            // Blocked natives own their source and admission slot until actual return. Waiting workers wake/exit.
        }
    }

    private void DisposeSignals()
    {
        if (_signalsDisposed) return;
        _signalsDisposed = true;
        _stopping.Dispose();
        _generationCancellation.Dispose();
    }

    private static TaskCompletionSource CompletedIdle()
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        result.SetResult();
        return result;
    }
}
