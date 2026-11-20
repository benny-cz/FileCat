using System.Collections.Immutable;
using System.Diagnostics;

namespace FileCat.Core.Threading;

public sealed partial class DeviceIoScheduler
{
    private readonly object _observedGate = new();
    private readonly HashSet<ActualDevices> _observed = [];

    // AnalysisIo alone calls this after shared admission. At most eight actual analysis requests
    // exist process-wide. Observation creates no queue, worker, replacement or alternate I/O route.
    internal IDisposable ObserveActualDevices(ImmutableArray<string> keys)
    {
        var actual = new ActualDevices(this, keys);
        lock (_observedGate) _observed.Add(actual);
        return actual;
    }
    private bool ObservedNotResponding(string key)
    {
        lock (_observedGate) return _observed.Any(a => a.Hung && a.Keys.Contains(key, StringComparer.OrdinalIgnoreCase));
    }
    private void WatchObserved()
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_observedGate)
        {
            long now = Stopwatch.GetTimestamp();
            foreach (var actual in _observed)
            {
                if (actual.Hung || Stopwatch.GetElapsedTime(actual.Started, now) <= HangThreshold) continue;
                actual.Hung = true;
                changed.UnionWith(actual.Keys);
            }
        }
        NotifyObserved(changed);
    }
    private void ForgetObserved(ActualDevices actual)
    {
        bool changed;
        lock (_observedGate) changed = _observed.Remove(actual) && actual.Hung;
        if (changed) NotifyObserved(actual.Keys);
    }
    private void NotifyObserved(IEnumerable<string> keys)
    {
        foreach (string key in keys)
            ThreadPool.QueueUserWorkItem(_ => RaiseHealth(key, GetHealth(key)));
    }
    private sealed class ActualDevices(DeviceIoScheduler owner, ImmutableArray<string> keys) : IDisposable
    {
        internal readonly ImmutableArray<string> Keys = keys;
        internal readonly long Started = Stopwatch.GetTimestamp();
        internal bool Hung;
        public void Dispose() => owner.ForgetObserved(this);
    }
}
