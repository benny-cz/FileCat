using System.Diagnostics;
using FileCat.Core.Resources;

namespace FileCat.Core.Jobs;

/// <summary>
/// A running or finished operation. Counters are updated by the job thread with interlocked writes and
/// read by the UI at a bounded rate; the UI never receives one notification per byte (PI-01).
/// A job captures its sources and destination at creation; later UI changes cannot retarget it (AI-07).
/// </summary>
public sealed class Job
{
    private readonly object _lock = new();
    private readonly List<JobIssue> _issues = [];
    private ulong[] _completedRoots = [];
    private ulong[] _failedRoots = [];
    private int _completedRootCount, _failedRootCount;
    private readonly List<UndoStep> _undo = [];
    private readonly ManualResetEventSlim _runGate = new(true);
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, Decision> _applyToAll = new(StringComparer.Ordinal);
    private long _bytesTotal, _bytesDone, _itemsTotal, _itemsDone, _itemsFailed, _itemsSkipped;
    private volatile JobState _state = JobState.Planning;
    private volatile string? _currentItem;
    private PendingDecision? _decision;
    private long _rateLimit;
    private readonly Stopwatch _clock = new();
    private long _speedMarkBytes;
    private TimeSpan _speedMarkTime;
    private double _speed;

    internal Job(JobRequest request, string title, string deviceKey, IReadOnlyList<string> readSet, IReadOnlyList<string> writeSet)
    {
        Request = request;
        Title = title;
        DeviceKey = deviceKey;
        ReadSet = readSet;
        WriteSet = writeSet;
        _rateLimit = request.Options.RateLimit;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string ShortId => Id.ToString("N")[..8];
    public JobRequest Request { get; }
    public JobKind Kind => Request.Kind;
    public string Title { get; }
    public string DeviceKey { get; }
    public IReadOnlyList<string> ReadSet { get; }
    public IReadOnlyList<string> WriteSet { get; }
    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; private set; }
    public DateTime? FinishedUtc { get; private set; }
    public Job? WaitingFor { get; internal set; }
    public string? WaitReason { get; internal set; }
    public int QueueOrder { get; internal set; }

    /// <summary>Raised (on any thread) when state, decision, or issues change. Progress is polled.</summary>
    public event Action<Job>? Changed;

    public JobState State => _state;
    public string? CurrentItem => _currentItem;
    public long BytesTotal => Interlocked.Read(ref _bytesTotal);
    public long BytesDone => Interlocked.Read(ref _bytesDone);
    public long ItemsTotal => Interlocked.Read(ref _itemsTotal);
    public long ItemsDone => Interlocked.Read(ref _itemsDone);
    public long ItemsFailed => Interlocked.Read(ref _itemsFailed);
    public long ItemsSkipped => Interlocked.Read(ref _itemsSkipped);
    /// <summary>Discovery finished; before that totals may still grow and no ETA is claimed.</summary>
    public bool TotalsFinal { get; internal set; }
    public PendingDecision? Decision => Volatile.Read(ref _decision);
    public CancellationToken Token => _cts.Token;
    public bool IsCancellationRequested => _cts.IsCancellationRequested;
    public bool IsPaused => !_runGate.IsSet;

    public long RateLimit
    {
        get => Interlocked.Read(ref _rateLimit);
        set => Interlocked.Exchange(ref _rateLimit, Math.Max(0, value));
    }

    public double BytesPerSecond
    {
        get
        {
            lock (_lock)
            {
                var now = _clock.Elapsed;
                var dt = (now - _speedMarkTime).TotalSeconds;
                if (dt >= 1)
                {
                    long b = BytesDone;
                    double instant = (b - _speedMarkBytes) / dt;
                    _speed = _speed <= 0 ? instant : _speed * 0.6 + instant * 0.4;
                    _speedMarkBytes = b;
                    _speedMarkTime = now;
                }
                return _speed;
            }
        }
    }

    public IReadOnlyList<JobIssue> Issues
    {
        get
        {
            lock (_lock) return _issues.ToList();
        }
    }

    public int IssueCount
    {
        get
        {
            lock (_lock) return _issues.Count;
        }
    }

    /// <summary>Positions in <see cref="JobRequest.Sources"/> of roots whose whole operation committed.</summary>
    public IReadOnlyList<int> CompletedRootIndices
    {
        get
        {
            lock (_lock) return Positions(_completedRoots);
        }
    }

    /// <summary>Positions of roots that failed or were skipped (partly or wholly).</summary>
    public IReadOnlyList<int> FailedRootIndices
    {
        get
        {
            lock (_lock) return Positions(_failedRoots);
        }
    }

    public int CompletedRootCount
    {
        get
        {
            lock (_lock) return _completedRootCount;
        }
    }

    public int FailedRootCount
    {
        get
        {
            lock (_lock) return _failedRootCount;
        }
    }

    private static List<int> Positions(ulong[] bits)
    {
        var list = new List<int>();
        for (int w = 0; w < bits.Length; w++)
        {
            for (ulong word = bits[w]; word != 0; word &= word - 1)
                list.Add((w << 6) + System.Numerics.BitOperations.TrailingZeroCount(word));
        }
        return list;
    }

    public IReadOnlyList<UndoStep> UndoSteps
    {
        get
        {
            lock (_lock) return _undo.ToList();
        }
    }

    public bool CanUndo
    {
        get
        {
            if (State is not (JobState.Completed or JobState.CompletedWithIssues)) return false;
            lock (_lock) return _undo.Count > 0;
        }
    }

    /// <summary>Undo is recorded for at most this many steps; larger operations say why it is not offered.</summary>
    public const int UndoLimit = 100_000;

    /// <summary>Set when the operation was too large to record undo steps.</summary>
    public bool UndoTruncated { get; private set; }

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (ItemsDone > 0) parts.Add($"{ItemsDone:N0} done");
            if (ItemsSkipped > 0) parts.Add($"{ItemsSkipped:N0} skipped");
            if (ItemsFailed > 0) parts.Add($"{ItemsFailed:N0} failed");
            int warnings;
            lock (_lock) warnings = _issues.Count(i => i.Severity == IssueSeverity.Warning);
            if (warnings > 0) parts.Add($"{warnings:N0} warning{(warnings == 1 ? "" : "s")}");
            return parts.Count == 0 ? State.Describe() : string.Join(", ", parts);
        }
    }

    // ---- Control (any thread) ------------------------------------------------------------------------

    public void Pause()
    {
        if (State is not (JobState.Running or JobState.Queued)) return;
        _runGate.Reset();
        if (State == JobState.Running) SetState(JobState.Pausing);
    }

    public void Resume()
    {
        _runGate.Set();
        if (State is JobState.Paused or JobState.Pausing) SetState(JobState.Running);
    }

    /// <summary>Requests cancellation. The job stops at the next safe boundary; cancellation is not rollback (AI-11).</summary>
    public void Cancel()
    {
        if (State.IsFinished()) return;
        _cts.Cancel();
        _runGate.Set();
        Volatile.Read(ref _decision)?.Resolve(new Decision(DecisionAction.CancelJob));
        if (State is JobState.Running or JobState.AwaitingDecision or JobState.Paused or JobState.Pausing) SetState(JobState.Stopping);
        // A queued job never started: the manager finishes it as canceled right away.
        else if (State == JobState.Queued) Changed?.Invoke(this);
    }

    // ---- Executor API (job thread) ------------------------------------------------------------------

    internal void SetState(JobState state)
    {
        if (_state == state) return;
        _state = state;
        if (state == JobState.Running)
        {
            StartedUtc ??= DateTime.UtcNow;
            if (!_clock.IsRunning) _clock.Start();
        }
        if (state.IsFinished())
        {
            FinishedUtc = DateTime.UtcNow;
            _clock.Stop();
        }
        Diagnostics.FileCatEventSource.Log.JobStateChanged(ShortId, state.ToString());
        Changed?.Invoke(this);
    }

    /// <summary>Blocks while paused and throws when canceled. Called at safe boundaries.</summary>
    internal void Checkpoint()
    {
        if (!_runGate.IsSet)
        {
            SetState(JobState.Paused);
            _runGate.Wait(_cts.Token);
            if (!_cts.IsCancellationRequested) SetState(JobState.Running);
        }
        _cts.Token.ThrowIfCancellationRequested();
    }

    internal void AddTotals(long items, long bytes)
    {
        Interlocked.Add(ref _itemsTotal, items);
        Interlocked.Add(ref _bytesTotal, bytes);
    }

    internal void AddBytes(long bytes) => Interlocked.Add(ref _bytesDone, bytes);

    internal void ItemDone() => Interlocked.Increment(ref _itemsDone);

    internal void ItemSkipped() => Interlocked.Increment(ref _itemsSkipped);

    internal void ItemFailed() => Interlocked.Increment(ref _itemsFailed);

    internal void SetCurrent(string? item) => _currentItem = item;

    internal void AddIssue(JobIssue issue)
    {
        lock (_lock)
        {
            if (_issues.Count < 10_000) _issues.Add(issue);
        }
        Changed?.Invoke(this);
    }

    /// <summary>Records the outcome of the root at <paramref name="index"/> in the request's sources.</summary>
    internal void RootCompleted(int index)
    {
        lock (_lock) SetBit(ref _completedRoots, index, ref _completedRootCount);
    }

    internal void RootFailed(int index)
    {
        lock (_lock) SetBit(ref _failedRoots, index, ref _failedRootCount);
    }

    private static void SetBit(ref ulong[] bits, int index, ref int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        int word = index >> 6;
        if (word >= bits.Length) Array.Resize(ref bits, Math.Max(word + 1, bits.Length * 2));
        ulong bit = 1UL << (index & 63);
        if ((bits[word] & bit) != 0) return;
        bits[word] |= bit;
        count++;
    }

    internal void AddUndo(UndoStep step)
    {
        lock (_lock)
        {
            if (UndoTruncated) return;
            if (_undo.Count >= UndoLimit)
            {
                // A partial undo of a huge operation would be misleading; offer none and say why.
                _undo.Clear();
                _undo.TrimExcess();
                UndoTruncated = true;
                return;
            }
            _undo.Add(step);
        }
    }

    /// <summary>Undo was performed (or attempted); the steps are not offered again.</summary>
    public void MarkUndone()
    {
        lock (_lock) _undo.Clear();
        Changed?.Invoke(this);
    }

    /// <summary>Asks the user (or a remembered "apply to all" answer) and blocks the job thread until answered.</summary>
    internal Decision Ask(DecisionRequest request)
    {
        lock (_lock)
        {
            if (_applyToAll.TryGetValue(request.ClassKey, out var remembered)) return remembered;
        }
        var pending = new PendingDecision(this, request);
        Volatile.Write(ref _decision, pending);
        var previous = State;
        SetState(JobState.AwaitingDecision);
        Decision d;
        try
        {
            d = pending.Task.Wait(Timeout.Infinite, _cts.Token) ? pending.Task.Result : new Decision(DecisionAction.CancelJob);
        }
        catch (OperationCanceledException)
        {
            d = new Decision(DecisionAction.CancelJob);
        }
        finally
        {
            Volatile.Write(ref _decision, null);
        }
        if (d.ApplyToAll && d.Action is not (DecisionAction.CancelJob or DecisionAction.Retry))
        {
            // Scoped to this job and conflict class only; never becomes a global default (plan §9.1).
            lock (_lock) _applyToAll[request.ClassKey] = d;
        }
        if (d.Action == DecisionAction.CancelJob) Cancel();
        else SetState(previous == JobState.AwaitingDecision ? JobState.Running : previous is JobState.Planning ? JobState.Running : previous);
        return d;
    }

    /// <summary>Token-bucket rate limiting inside copy callbacks.</summary>
    internal void Throttle(long bytesSinceStart, Stopwatch fileClock)
    {
        long limit = RateLimit;
        if (limit <= 0) return;
        double expected = (double)bytesSinceStart / limit;
        double actual = fileClock.Elapsed.TotalSeconds;
        if (expected > actual)
        {
            var wait = TimeSpan.FromSeconds(Math.Min(1.0, expected - actual));
            _cts.Token.WaitHandle.WaitOne(wait);
        }
    }

    public override string ToString() => $"{Title} [{State}]";
}
