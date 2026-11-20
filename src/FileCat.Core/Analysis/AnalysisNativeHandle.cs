using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Analysis;

/// <summary>
/// One counted native owner. Only a confirmed native close returns its reservation. SafeHandle
/// pins delay the one close attempt until actual callers return; failed closes never retry.
/// </summary>
internal sealed partial class AnalysisNativeHandle : SafeHandle
{
    private enum Kind { UnixFile, WindowsFile, UnixDirectory }
    private readonly Kind _kind;
    private readonly AnalysisResourceQuarantine _quarantine;
    private readonly Func<nint, bool>? _close;
    private IDisposable? _reservation;
    private Exception? _failure;
    private int _closeState; // 0: live/pinned, 1: confirmed, 2: unconfirmed

    private AnalysisNativeHandle(nint value, Kind kind, IDisposable reservation,
        AnalysisResourceQuarantine? quarantine = null, Func<nint, bool>? close = null) : base(-1, ownsHandle: true)
    {
        _kind = kind; _reservation = reservation; _quarantine = quarantine ?? AnalysisResourceQuarantine.Shared; _close = close;
        SetHandle(value);
        if (IsInvalid) { reservation.Dispose(); _reservation = null; _closeState = 1; }
    }
    public override bool IsInvalid => handle == -1 || _kind != Kind.UnixFile && handle == 0;

    internal static AnalysisNativeHandle UnixFile(int fd, IDisposable reservation, Func<nint, bool>? close = null) => new(fd, Kind.UnixFile, reservation, close: close);

    // Transfer is only for a freshly returned native open, before any I/O or publication.
    internal static AnalysisNativeHandle Adopt(SafeHandle original, IDisposable reservation,
        AnalysisResourceQuarantine? quarantine = null, Func<nint, bool>? close = null)
    {
        bool held = false;
        try
        {
            original.DangerousAddRef(ref held);
            var result = new AnalysisNativeHandle(original.DangerousGetHandle(),
                OperatingSystem.IsWindows() ? Kind.WindowsFile : Kind.UnixFile, reservation, quarantine, close);
            original.SetHandleAsInvalid();
            return result;
        }
        finally { if (held) original.DangerousRelease(); }
    }

    // fdopendir has already accepted the descriptor. The reservation follows DIR*, whose
    // single closedir attempt owns both the stream and fd; no independent close(fd) follows.
    internal AnalysisNativeHandle TransferToDirectory(nint stream)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        if (_kind != Kind.UnixFile || stream == 0 || _reservation is null) throw new InvalidOperationException("Invalid directory ownership transfer.");
        var result = new AnalysisNativeHandle(stream, Kind.UnixDirectory, _reservation, _quarantine);
        SetHandleAsInvalid(); _reservation = null; Volatile.Write(ref _closeState, 1);
        return result;
    }

    internal void CloseChecked()
    {
        Dispose();
        if (Volatile.Read(ref _closeState) == 1) return;
        _quarantine.Hold(this);
        throw AnalysisCloseUnconfirmedException.From(_failure ?? new IOException("Native release is still owned by an active caller."));
    }
    protected override bool ReleaseHandle()
    {
        try
        {
            bool confirmed = _close?.Invoke(handle) ?? (_kind switch
            {
                Kind.WindowsFile => CloseWindows(handle),
                Kind.UnixDirectory => CloseDirectory(handle) == 0,
                _ => CloseUnix(checked((int)handle)) == 0
            });
            if (!confirmed) throw new IOException("Native resource release was not confirmed.", new Win32Exception(Marshal.GetLastPInvokeError()));
            _reservation?.Dispose(); _reservation = null; Volatile.Write(ref _closeState, 1);
            return true;
        }
        catch (Exception error)
        {
            _failure = error; Volatile.Write(ref _closeState, 2); _quarantine.Hold(this);
            return false; // SafeHandle never retries a failed ReleaseHandle.
        }
    }

    /// <summary>A non-owning FileStream view pinned to this checked owner for its entire lifetime.</summary>
    internal FileStream OpenStream(FileAccess access, int bufferSize = 64 * 1024)
    {
        var pin = BorrowFile();
        try { return new PinnedStream(pin, access, bufferSize); }
        catch { pin.Dispose(); throw; }
    }
    internal FilePin BorrowFile()
    {
        bool held = false;
        try
        {
            DangerousAddRef(ref held);
            var pin = new FilePin(this, new SafeFileHandle(DangerousGetHandle(), ownsHandle: false));
            held = false; return pin;
        }
        finally { if (held) DangerousRelease(); }
    }
    internal sealed class FilePin(AnalysisNativeHandle owner, SafeFileHandle view) : IDisposable
    {
        private AnalysisNativeHandle? _owner = owner;
        internal SafeFileHandle View => view;
        ~FilePin() => Dispose();
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is not { } captured) return;
            GC.SuppressFinalize(this);
            try { view.Dispose(); } finally { captured.DangerousRelease(); }
        }
    }
    private sealed class PinnedStream(FilePin pin, FileAccess access, int bufferSize)
        : FileStream(pin.View, access, bufferSize, isAsync: false)
    {
        private FilePin? _pin = pin;
        protected override void Dispose(bool disposing)
        { try { base.Dispose(disposing); } finally { Interlocked.Exchange(ref _pin, null)?.Dispose(); } }
        public override async ValueTask DisposeAsync()
        { try { await base.DisposeAsync().ConfigureAwait(false); } finally { Interlocked.Exchange(ref _pin, null)?.Dispose(); } }
    }

    internal static void CloseAll(IEnumerable<IDisposable> owners)
    {
        List<Exception>? errors = null;
        foreach (var owner in owners)
            try { owner.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is { Count: 1 }) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors is not null) throw new AggregateException(errors);
    }
    internal static bool CloseNativeFile(nint value) => OperatingSystem.IsWindows() ? CloseWindows(value) : CloseUnix(checked((int)value)) == 0;
    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseWindows(nint handle);
    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)] private static partial int CloseUnix(int fd);
    [LibraryImport("libc", EntryPoint = "closedir", SetLastError = true)] private static partial int CloseDirectory(nint stream);
}
