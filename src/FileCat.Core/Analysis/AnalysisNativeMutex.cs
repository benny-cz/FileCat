using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Analysis;

/// <summary>Thread-owned capacity with a separately checked, counted kernel handle.</summary>
internal sealed class AnalysisNativeMutex : IDisposable
{
    private readonly Mutex _mutex;
    private readonly AnalysisNativeHandle _native;
    private readonly AnalysisResourceQuarantine _quarantine;
    private readonly Action? _beforeRelease;
    private bool _owned, _disposed;
    private Exception? _failure;

    internal AnalysisNativeMutex(string name, AnalysisResourceQuarantine? quarantine = null,
        Func<nint, bool>? close = null, Action? beforeRelease = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        _quarantine = quarantine ?? AnalysisResourceQuarantine.Shared; _beforeRelease = beforeRelease;
        var reservation = AnalysisNativeHandles.Acquire(); Mutex? mutex = null; AnalysisNativeHandle? native = null;
        try
        {
            mutex = new(false, name, new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false });
            native = AnalysisNativeHandle.Adopt(mutex.SafeWaitHandle, reservation, _quarantine, close);
            mutex.SafeWaitHandle = new SafeWaitHandle(native.DangerousGetHandle(), ownsHandle: false);
            _mutex = mutex; _native = native;
        }
        catch
        {
            if (native is not null) native.CloseChecked();
            else { mutex?.Dispose(); reservation.Dispose(); }
            throw;
        }
    }
    internal bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_owned) return true;
        bool held = false;
        try
        {
            _native.DangerousAddRef(ref held);
            try { _owned = _mutex.WaitOne(0); }
            catch (AbandonedMutexException) { _owned = true; }
            return _owned;
        }
        finally { if (held) _native.DangerousRelease(); }
    }
    public void Dispose()
    {
        if (_disposed) { if (_failure is { } prior) throw AnalysisCloseUnconfirmedException.From(prior); return; }
        _disposed = true;
        try
        {
            if (_owned)
            {
                bool held = false;
                try { _native.DangerousAddRef(ref held); _beforeRelease?.Invoke(); _mutex.ReleaseMutex(); _owned = false; }
                finally { if (held) _native.DangerousRelease(); }
            }
            _mutex.Dispose(); // Non-owning wait-handle view only.
            _native.CloseChecked();
        }
        catch (Exception error)
        {
            _failure = error; _quarantine.Hold(this);
            throw AnalysisCloseUnconfirmedException.From(error);
        }
    }
}
