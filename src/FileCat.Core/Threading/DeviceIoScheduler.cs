using System.Collections.Concurrent;
using System.Diagnostics;

namespace FileCat.Core.Threading;

public enum IoPriority
{
    Interactive = 0,
    Normal = 1,
    Background = 2,
}

public enum DeviceHealth
{
    Responsive,
    NotResponding,
}

/// <summary>Marshals work to the UI thread; implemented by the UI layer and by tests.</summary>
public interface IUiDispatcher
{
    bool CheckAccess();
    void Post(Action action);
}

/// <summary>Runs posted actions inline; for tests and headless tools.</summary>
public sealed class InlineDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;
    public void Post(Action action) => action();
}

/// <summary>
/// Bounded per-device worker threads for calls that can hang (SMB redirector, removable media, cloud
/// recall; plan §6.3). A call running longer than the hang threshold is abandoned: its thread is
/// quarantined, a replacement may start (up to a hard cap), and the device reports "not responding".
/// Thread usage is bounded per device, not per tab.
/// </summary>
public sealed class DeviceIoScheduler : IDisposable
{
    private readonly ConcurrentDictionary<string, DeviceQueue> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _watchdog;
    private volatile bool _disposed;

    public DeviceIoScheduler(TimeSpan? hangThreshold = null, int threadsPerDevice = 2, int maxThreadsPerDevice = 6)
    {
        HangThreshold = hangThreshold ?? TimeSpan.FromSeconds(8);
        ThreadsPerDevice = Math.Max(1, threadsPerDevice);
        MaxThreadsPerDevice = Math.Max(ThreadsPerDevice, maxThreadsPerDevice);
        _watchdog = new Timer(_ => Watch(), null, 500, 500);
    }

    public TimeSpan HangThreshold { get; }
    public int ThreadsPerDevice { get; }
    public int MaxThreadsPerDevice { get; }

    /// <summary>Raised (on a pool thread) when a device changes between responsive and not responding.</summary>
    public event Action<string, DeviceHealth>? HealthChanged;

    public DeviceHealth GetHealth(string deviceKey) =>
        _devices.TryGetValue(deviceKey, out var q) ? q.Health : DeviceHealth.Responsive;

    public Task Run(string deviceKey, IoPriority priority, Action<CancellationToken> work, CancellationToken ct = default) =>
        Run<bool>(deviceKey, priority, c => { work(c); return true; }, ct);

    /// <summary>Queues work for a device. After shutdown began the work is not run and the task is canceled.</summary>
    public Task<T> Run<T>(string deviceKey, IoPriority priority, Func<CancellationToken, T> work, CancellationToken ct = default)
    {
        // Late requests (a watcher notification posted just before exit) are canceled, not thrown at the UI.
        if (_disposed) return Task.FromCanceled<T>(new CancellationToken(canceled: true));
        var item = new WorkItem<T>(work, ct);
        _devices.GetOrAdd(deviceKey, k => new DeviceQueue(this, k)).Enqueue(item, priority);
        return item.Task;
    }

    private void Watch()
    {
        if (_disposed) return;
        foreach (var q in _devices.Values) q.Watch();
    }

    internal void RaiseHealth(string key, DeviceHealth health)
    {
        try { HealthChanged?.Invoke(key, health); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    public void Dispose()
    {
        _disposed = true;
        _watchdog.Dispose();
        foreach (var q in _devices.Values) q.Shutdown();
    }

    private abstract class WorkItem
    {
        public long StartedTimestamp;
        public abstract CancellationToken Token { get; }
        public abstract bool IsCompleted { get; }
        public abstract void Execute();
        public abstract void Cancel();
    }

    private sealed class WorkItem<T> : WorkItem
    {
        private readonly Func<CancellationToken, T> _work;
        private readonly CancellationToken _ct;
        private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _reg;

        public WorkItem(Func<CancellationToken, T> work, CancellationToken ct)
        {
            _work = work;
            _ct = ct;
            if (ct.CanBeCanceled) _reg = ct.Register(static s => ((WorkItem<T>)s!).Cancel(), this);
        }

        public Task<T> Task => _tcs.Task;
        public override CancellationToken Token => _ct;
        public override bool IsCompleted => _tcs.Task.IsCompleted;

        public override void Execute()
        {
            try
            {
                _ct.ThrowIfCancellationRequested();
                _tcs.TrySetResult(_work(_ct));
            }
            catch (OperationCanceledException oce) when (_ct.IsCancellationRequested)
            {
                _tcs.TrySetCanceled(oce.CancellationToken);
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }
            finally
            {
                _reg.Dispose();
            }
        }

        // Canceling a running item only completes its task; the thread finishes at its own safe boundary.
        public override void Cancel() => _tcs.TrySetCanceled(_ct);
    }

    private sealed class Worker
    {
        public Thread? Thread;
        public WorkItem? Current;
        public bool Quarantined;
    }

    private sealed class DeviceQueue(DeviceIoScheduler owner, string key)
    {
        private readonly object _lock = new();
        private readonly PriorityQueue<WorkItem, (int, long)> _queue = new();
        private readonly List<Worker> _workers = [];
        private long _seq;
        private bool _shutdown;
        private int _idle;

        public DeviceHealth Health { get; private set; }

        public void Enqueue(WorkItem item, IoPriority priority)
        {
            lock (_lock)
            {
                _queue.Enqueue(item, ((int)priority, _seq++));
                if (Diagnostics.FileCatEventSource.Log.IsEnabled()) Diagnostics.FileCatEventSource.Log.QueueDepth(key, _queue.Count);
                if (_idle > 0) Monitor.Pulse(_lock);
                else if (ActiveWorkers() < owner.ThreadsPerDevice || (_queue.Count > 0 && _workers.Count < owner.MaxThreadsPerDevice && ActiveWorkers() < owner.ThreadsPerDevice))
                    StartWorker();
            }
        }

        private int ActiveWorkers()
        {
            int n = 0;
            foreach (var w in _workers) if (!w.Quarantined) n++;
            return n;
        }

        private void StartWorker()
        {
            var w = new Worker();
            var t = new Thread(() => Loop(w))
            {
                IsBackground = true,
                Name = $"FileCat I/O {key}",
                Priority = ThreadPriority.Normal,
            };
            w.Thread = t;
            _workers.Add(w);
            t.Start();
        }

        private void Loop(Worker self)
        {
            while (true)
            {
                WorkItem item;
                lock (_lock)
                {
                    while (true)
                    {
                        if (_shutdown || self.Quarantined && ActiveWorkers() >= owner.ThreadsPerDevice)
                        {
                            _workers.Remove(self);
                            UpdateHealthLocked();
                            return;
                        }
                        if (_queue.TryDequeue(out var next, out _))
                        {
                            if (next.IsCompleted) continue;
                            item = next;
                            break;
                        }
                        _idle++;
                        bool signaled = Monitor.Wait(_lock, TimeSpan.FromSeconds(30));
                        _idle--;
                        if (!signaled && _queue.Count == 0 && ActiveWorkers() > 1)
                        {
                            _workers.Remove(self);
                            return;
                        }
                    }
                    self.Current = item;
                    item.StartedTimestamp = Stopwatch.GetTimestamp();
                }
                item.Execute();
                lock (_lock)
                {
                    self.Current = null;
                    if (self.Quarantined)
                    {
                        // The hung call eventually returned; the thread may rejoin if the pool is short.
                        self.Quarantined = ActiveWorkers() >= owner.ThreadsPerDevice;
                        UpdateHealthLocked();
                    }
                }
            }
        }

        public void Watch()
        {
            lock (_lock)
            {
                long now = Stopwatch.GetTimestamp();
                foreach (var w in _workers)
                {
                    if (w.Quarantined || w.Current is null) continue;
                    if (Stopwatch.GetElapsedTime(w.Current.StartedTimestamp, now) > owner.HangThreshold)
                    {
                        w.Quarantined = true;
                        if (_queue.Count > 0 && _workers.Count < owner.MaxThreadsPerDevice) StartWorker();
                    }
                }
                UpdateHealthLocked();
            }
        }

        private void UpdateHealthLocked()
        {
            bool hung = _workers.Any(w => w.Quarantined && w.Current is not null);
            var health = hung ? DeviceHealth.NotResponding : DeviceHealth.Responsive;
            if (health == Health) return;
            Health = health;
            ThreadPool.QueueUserWorkItem(_ => owner.RaiseHealth(key, health));
        }

        public void Shutdown()
        {
            lock (_lock)
            {
                _shutdown = true;
                while (_queue.TryDequeue(out var item, out _)) item.Cancel();
                Monitor.PulseAll(_lock);
            }
        }
    }
}
