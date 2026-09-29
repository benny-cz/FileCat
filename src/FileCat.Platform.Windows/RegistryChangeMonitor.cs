using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows;

/// <summary>
/// One native, re-armed notification for a visible Registry key or an edit dialog. A burst of changes (an installer
/// writing many values) is reported once, when it settles; a key that never settles stops being followed.
/// </summary>
public sealed partial class RegistryChangeMonitor : IDisposable
{
    private const uint Filter = 0x00000001 | 0x00000004 | 0x00000008 | 0x10000000;
    /// <summary>Changes this close together are one change.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(200);
    /// <summary>A burst is reported after this long even when it goes on.</summary>
    private static readonly TimeSpan LongestBurst = TimeSpan.FromSeconds(2);
    /// <summary>More reports than this within <see cref="Window"/>: the key never settles, and a view rereading it would never rest.</summary>
    private const int MostReports = 20;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _worker;
    private int _disposed;

    public RegistryChangeMonitor(Location location, Action changed, Action<Exception>? failed = null)
    {
        if (location.Scheme != Schemes.Registry || location.Path.Length == 0)
            throw new ArgumentException("A concrete Registry key is required.", nameof(location));
        // A thread of its own: it waits for as long as the key is shown, which would hold a pool thread all that time.
        _worker = Task.Factory.StartNew(() => Watch(location, changed, failed), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public Task Ready => _ready.Task;

    private void Watch(Location location, Action changed, Action<Exception>? failed)
    {
        var keys = new List<RegistryKey>();
        var signals = new List<EventWaitHandle>();
        try
        {
            keys.AddRange(RegistryRaw.OpenForWatching(location));
            foreach (var _ in keys) signals.Add(new EventWaitHandle(false, EventResetMode.AutoReset));
            var waits = signals.Cast<WaitHandle>().Append(_stop.Token.WaitHandle).ToArray();
            int stop = waits.Length - 1;
            for (int i = 0; i < keys.Count; i++) Arm(keys[i], signals[i]);
            _ready.TrySetResult();
            var reports = new Queue<long>();
            while (!_stop.IsCancellationRequested)
            {
                int hit = WaitHandle.WaitAny(waits);
                if (hit == stop) break;
                Arm(keys[hit], signals[hit]);
                // The rest of a burst, until it settles.
                var burst = Stopwatch.StartNew();
                while (burst.Elapsed < LongestBurst)
                {
                    int more = WaitHandle.WaitAny(waits, Quiet);
                    if (more == WaitHandle.WaitTimeout) break;
                    if (more == stop) return;
                    Arm(keys[more], signals[more]);
                }
                if (_stop.IsCancellationRequested) break;
                long now = Environment.TickCount64;
                reports.Enqueue(now);
                while (now - reports.Peek() > Window.TotalMilliseconds) reports.Dequeue();
                if (reports.Count > MostReports)
                    throw new IOException("This key reports changes all the time, so FileCat stopped following them.");
                changed();
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or RegistryLinkException)
        {
            _ready.TrySetException(ex);
            if (!_stop.IsCancellationRequested) failed?.Invoke(ex);
        }
        finally
        {
            foreach (var signal in signals) signal.Dispose();
            foreach (var key in keys) key.Dispose();
        }
    }

    private static void Arm(RegistryKey key, EventWaitHandle signal)
    {
        int code = RegNotifyChangeKeyValue(key.Handle, false, Filter, signal.SafeWaitHandle, true);
        if (code != 0) throw new Win32Exception(code);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _ = _worker.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }

    [LibraryImport("advapi32.dll", EntryPoint = "RegNotifyChangeKeyValue")]
    private static partial int RegNotifyChangeKeyValue(SafeRegistryHandle key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint filter, SafeWaitHandle signal, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
