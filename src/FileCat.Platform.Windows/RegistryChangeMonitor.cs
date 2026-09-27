using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows;

/// <summary>One native, re-armed notification for a visible Registry key or an edit dialog.</summary>
public sealed partial class RegistryChangeMonitor : IDisposable
{
    private const uint Filter = 0x00000001 | 0x00000004 | 0x00000008 | 0x10000000;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _worker;
    private int _disposed;

    public RegistryChangeMonitor(Location location, Action changed, Action<Exception>? failed = null)
    {
        if (location.Scheme != Schemes.Registry || location.Path.Length == 0)
            throw new ArgumentException("A concrete Registry key is required.", nameof(location));
        _worker = Task.Run(() => Watch(location, changed, failed));
    }

    public Task Ready => _ready.Task;

    private void Watch(Location location, Action changed, Action<Exception>? failed)
    {
        try
        {
            using var key = WindowsRegistryProvider.Open(location, false);
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset);
            var handles = new WaitHandle[] { signal, _stop.Token.WaitHandle };
            while (!_stop.IsCancellationRequested)
            {
                int code = RegNotifyChangeKeyValue(key.Handle, false, Filter, signal.SafeWaitHandle, true);
                if (code != 0) throw new Win32Exception(code);
                _ready.TrySetResult();
                if (WaitHandle.WaitAny(handles) == 1) break;
                if (!_stop.IsCancellationRequested) changed();
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or RegistryLinkException)
        { _ready.TrySetException(ex); if (!_stop.IsCancellationRequested) failed?.Invoke(ex); }
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
