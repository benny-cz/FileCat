using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed class AnalysisDeviceUnavailableException() : IOException("The device is not responding. Wait until it responds before starting new analysis work there.");

// Only trusted scratch owners implement this marker. Cleanup cannot admit new source work or decoded paths.
internal interface IAnalysisScratchOwned : IDisposable;
internal readonly struct AnalysisWorkerClose(IDisposable source) : IDisposable
{ public void Dispose() => AnalysisIo.CloseOnWorker(source); }

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

    public static Task<T> Run<T>(DeviceIoScheduler scheduler, AnalysisIoDevices device, Func<CancellationToken, T> work,
        CancellationToken cancellation = default) => RunCore(scheduler, device, work, cancellation, releaseOnly: false);

    /// <summary>Transfers an owned metadata backend to bounded cleanup; it is never disposed by a UI continuation.</summary>
    public static Task ReleaseAsync(DeviceIoScheduler scheduler, IAnalysisBackend source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CloseCore(scheduler, () => source.IoDeviceKey, source.Dispose);
    }

    /// <summary>Closes an existing private-directory capability through bounded cleanup, including scheduler shutdown.</summary>
    public static Task ReleaseAsync(DeviceIoScheduler scheduler, AnalysisIoDevices device, AnalysisPrivateDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        return CloseCore(scheduler, () => device, directory.Dispose);
    }

    internal static Task ReleaseScratchAsync(DeviceIoScheduler scheduler, AnalysisIoDevices device, IAnalysisScratchOwned source) =>
        CloseCore(scheduler, () => device, source.Dispose);

    internal static Task ReleaseContentAsync(DeviceIoScheduler scheduler, AnalysisIoDevices device, IAnalysisBoundContent source) =>
        CloseCore(scheduler, () => device, source.Dispose);

    // Called only by trusted native setup/rollback code already on its admitted worker.
    internal static void CloseOnWorker(IDisposable source)
    {
        try { source.Dispose(); }
        catch (Exception error) { throw AnalysisCloseUnconfirmedException.From(error); }
    }
    private static async Task CloseCore(DeviceIoScheduler scheduler, Func<AnalysisIoDevices> devices, Action close)
    {
        try
        {
            await RunCore(scheduler, devices(), _ =>
            {
                // In particular, an OCE thrown by Dispose is a failed close, not a queued
                // scheduler cancellation eligible for the shutdown release-worker handoff.
                try { close(); return true; }
                catch (Exception error) { throw AnalysisCloseUnconfirmedException.From(error); }
            }, CancellationToken.None, releaseOnly: true).ConfigureAwait(false);
        }
        catch (Exception error) { throw AnalysisCloseUnconfirmedException.From(error); }
    }

    public static IAsyncDisposable OwnCursor(DeviceIoScheduler scheduler, AnalysisIoDevices device, IEnumerator<AnalysisEntry> cursor) =>
        new Cursor(scheduler, device, cursor);

    private sealed class Cursor(DeviceIoScheduler scheduler, AnalysisIoDevices device, IEnumerator<AnalysisEntry> cursor) : IAsyncDisposable
    {
        private IEnumerator<AnalysisEntry>? _cursor = cursor;
        public ValueTask DisposeAsync()
        {
            var owned = Interlocked.Exchange(ref _cursor, null);
            return owned is null ? ValueTask.CompletedTask : new(CloseCore(scheduler, () => device, owned.Dispose));
        }
    }

    private static async Task<T> RunCore<T>(DeviceIoScheduler scheduler, AnalysisIoDevices device, Func<CancellationToken, T> work,
        CancellationToken cancellation, bool releaseOnly)
    {
        ArgumentNullException.ThrowIfNull(scheduler); ArgumentNullException.ThrowIfNull(work); ArgumentNullException.ThrowIfNull(device);
        bool Unavailable() => device.Keys.Any(key => scheduler.GetHealth(key) == DeviceHealth.NotResponding);
        if (!releaseOnly && Unavailable()) throw new AnalysisDeviceUnavailableException();
        using var admission = await Admit(device, cancellation).ConfigureAwait(false);
        T Invoke(DeviceIoScheduler actualScheduler)
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (!releaseOnly && Unavailable()) throw new AnalysisDeviceUnavailableException();
                if (!releaseOnly && AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
                // Includes synchronous capacity-owner work on the profile device. Its completion, not
                // a canceled await, retires both health observations and the shared device admissions.
                using var observed = actualScheduler.ObserveActualDevices(device.Keys);
                return work(cancellation);
            }
            finally { admission.Dispose(); }
        }
        // DeviceIoScheduler cancellation is intentionally NOT the caller's token. Its shutdown cancels queued
        // delegates only; running delegates remain owned until Execute returns. The admission therefore covers
        // real work and quarantined calls, rather than the lifetime of an early-canceled awaiting task.
        var actual = scheduler.Run(device.Primary, IoPriority.Background, _ => Invoke(scheduler), CancellationToken.None);
        try { return await actual.ConfigureAwait(false); }
        catch (OperationCanceledException) when (releaseOnly && actual.IsCanceled && scheduler.IsStopped)
        {
            // An application shutdown may retire its queue before an owned handle release enters it. Only
            // release of existing metadata capabilities can use this one-thread process-wide close worker.
            // It retains the SAME global/device admission, and cannot execute scans, persistence or new opens.
            return await CloseWorker.Scheduler.Run("analysis-owned-handle-release", IoPriority.Background, _ => Invoke(CloseWorker.Scheduler), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static class CloseWorker
    {
        internal static readonly DeviceIoScheduler Scheduler = new(threadsPerDevice: 1, maxThreadsPerDevice: 1);
    }

    private static async Task<IDisposable> Admit(AnalysisIoDevices keys, CancellationToken cancellation)
    {
        await Requests.WaitAsync(cancellation).ConfigureAwait(false);
        var acquired = new List<(string Key, Device Device)>(keys.Keys.Length);
        try
        {
            // Every caller uses the same total order, including reversed primary/profile pairs.
            // A waiting/canceled set owns only its acquired turns and releases each exactly once.
            foreach (string key in keys.Keys)
            {
                Device device;
                lock (Gate)
                {
                    if (!Devices.TryGetValue(key, out device!)) Devices.Add(key, device = new());
                    device.References++;
                }
                try { await device.Turn.WaitAsync(cancellation).ConfigureAwait(false); }
                catch { Forget(key, device); throw; }
                acquired.Add((key, device));
            }
            return new Admission(acquired);
        }
        catch { new Admission(acquired).Dispose(); throw; }
    }
    private static void Forget(string key, Device device)
    {
        lock (Gate)
        {
            if (--device.References != 0) return;
            Devices.Remove(key); device.Turn.Dispose();
        }
    }
    private sealed class Admission(List<(string Key, Device Device)> acquired) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            for (int i = acquired.Count - 1; i >= 0; i--)
            {
                var (key, device) = acquired[i]; device.Turn.Release(); Forget(key, device);
            }
            Requests.Release();
        }
    }
}
