using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>
/// Shared analysis admission for actual native work, including preparation and retained-handle cleanup.
/// Cancellation never completes an already running native request before its delegate returns.
/// </summary>
public static class AnalysisIo
{
    private static readonly SemaphoreSlim Requests = new(AnalysisLimits.IoRequests, AnalysisLimits.IoRequests);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Device> Devices = new(StringComparer.OrdinalIgnoreCase);
    private sealed class Device { public readonly SemaphoreSlim Turn = new(1, 1); public int References; }

    public static Task<T> Run<T>(DeviceIoScheduler scheduler, string device, Func<CancellationToken, T> work,
        CancellationToken cancellation = default) => RunCore(scheduler, device, work, cancellation, releaseOnly: false);

    /// <summary>Transfers an owned metadata backend to bounded cleanup; it is never disposed by a UI continuation.</summary>
    public static Task ReleaseAsync(DeviceIoScheduler scheduler, IAnalysisBackend source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return RunCore(scheduler, source.IoDeviceKey, _ => { source.Dispose(); return true; }, CancellationToken.None, releaseOnly: true);
    }

    public static IAsyncDisposable OwnCursor(DeviceIoScheduler scheduler, string device, IEnumerator<AnalysisEntry> cursor) =>
        new Cursor(scheduler, device, cursor);

    private sealed class Cursor(DeviceIoScheduler scheduler, string device, IEnumerator<AnalysisEntry> cursor) : IAsyncDisposable
    {
        private IEnumerator<AnalysisEntry>? _cursor = cursor;
        public ValueTask DisposeAsync()
        {
            var owned = Interlocked.Exchange(ref _cursor, null);
            return owned is null ? ValueTask.CompletedTask : new(RunCore(scheduler, device,
                _ => { owned.Dispose(); return true; }, CancellationToken.None, releaseOnly: true));
        }
    }

    private static async Task<T> RunCore<T>(DeviceIoScheduler scheduler, string device, Func<CancellationToken, T> work,
        CancellationToken cancellation, bool releaseOnly)
    {
        ArgumentNullException.ThrowIfNull(scheduler); ArgumentNullException.ThrowIfNull(work);
        if (string.IsNullOrWhiteSpace(device) || device.Length > 1024) throw new ArgumentException("A bounded provider device key is required.", nameof(device));
        using var admission = await Admit(device, cancellation).ConfigureAwait(false);
        T Invoke(CancellationToken _)
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (!releaseOnly && AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
                return work(cancellation);
            }
            finally { admission.Dispose(); }
        }
        // DeviceIoScheduler cancellation is intentionally NOT the caller's token. Its shutdown cancels queued
        // delegates only; running delegates remain owned until Execute returns. The admission therefore covers
        // real work and quarantined calls, rather than the lifetime of an early-canceled awaiting task.
        var actual = scheduler.Run(device, IoPriority.Background, Invoke, CancellationToken.None);
        try { return await actual.ConfigureAwait(false); }
        catch (OperationCanceledException) when (releaseOnly && actual.IsCanceled && scheduler.IsStopped)
        {
            // An application shutdown may retire its queue before an owned handle release enters it. Only
            // release of existing metadata capabilities can use this one-thread process-wide close worker.
            // It retains the SAME global/device admission, and cannot execute scans, persistence or new opens.
            return await CloseWorker.Scheduler.Run("analysis-owned-handle-release", IoPriority.Background, Invoke, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static class CloseWorker
    {
        internal static readonly DeviceIoScheduler Scheduler = new(threadsPerDevice: 1, maxThreadsPerDevice: 1);
    }

    private static async Task<IDisposable> Admit(string key, CancellationToken cancellation)
    {
        await Requests.WaitAsync(cancellation).ConfigureAwait(false);
        Device device;
        lock (Gate)
        {
            if (!Devices.TryGetValue(key, out device!)) Devices.Add(key, device = new());
            device.References++;
        }
        try
        {
            await device.Turn.WaitAsync(cancellation).ConfigureAwait(false);
            return new Admission(key, device);
        }
        catch { Forget(key, device); Requests.Release(); throw; }
    }
    private static void Forget(string key, Device device)
    {
        lock (Gate)
        {
            if (--device.References != 0) return;
            Devices.Remove(key); device.Turn.Dispose();
        }
    }
    private sealed class Admission(string key, Device device) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            device.Turn.Release(); Forget(key, device); Requests.Release();
        }
    }
}
