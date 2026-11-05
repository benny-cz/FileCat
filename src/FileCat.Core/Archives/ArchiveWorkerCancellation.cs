using System.Globalization;
using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

/// <summary>
/// Worker lifetime signal, separate from protocol data. The parent owns the sole write end and never writes:
/// closing it requests cancellation, including while either protocol pipe is blocked. Only fixed launch arguments
/// supply this handle; no archive byte can select it. Native launchers must verify/inherit exactly this read end.
/// </summary>
internal sealed class ArchiveWorkerCancellation : IDisposable
{
    internal const string Argument = "--archive-cancellation-handle";
    private readonly AnonymousPipeClientStream pipe;
    private readonly CancellationTokenSource cancellation = new();
    private int disposed, malformed;
    internal CancellationToken Token { get; }
    internal bool Malformed => Volatile.Read(ref malformed) != 0;

    internal static long Parse(string option, string value)
    {
        if (option != Argument || value.Length > 20 ||
            !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long handle) ||
            handle < 3 || handle > (long)nint.MaxValue || value != handle.ToString(CultureInfo.InvariantCulture))
            throw new ArgumentException("Invalid archive cancellation handle.");
        return handle;
    }

    internal ArchiveWorkerCancellation(long handle)
    {
        Parse(Argument, handle.ToString(CultureInfo.InvariantCulture));
        Token = cancellation.Token;
        pipe = new AnonymousPipeClientStream(PipeDirection.In, handle.ToString(CultureInfo.InvariantCulture));
        new Thread(Observe) { IsBackground = true, Name = "FileCat archive cancellation" }.Start();
    }

    internal static int Run(long handle, Func<Stream, Stream, CancellationToken, int> action)
    {
        using var signal = new ArchiveWorkerCancellation(handle);
        // These are process-lifetime handles. Closing a console stream from a second thread while its synchronous
        // OS IO is blocked is not a portable cancellation primitive; returning from this worker ends those threads.
        Stream input = OperatingSystem.IsWindows() ? Console.OpenStandardInput() : new FileStream(new SafeFileHandle(0, ownsHandle: false), FileAccess.Read, 1);
        Stream output = OperatingSystem.IsWindows() ? Console.OpenStandardOutput() : new FileStream(new SafeFileHandle(1, ownsHandle: false), FileAccess.Write, 1);
        using var transport = new ArchiveWorkerTransport(input, output, signal.Token);
        try
        {
            int result = action(transport.Input, transport.Output, signal.Token);
            return signal.Malformed ? 64 : result;
        }
        catch (OperationCanceledException) when (signal.Token.IsCancellationRequested) { return signal.Malformed ? 64 : 0; }
    }

    private void Observe()
    {
        try { if (pipe.ReadByte() != -1) Volatile.Write(ref malformed, 1); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        { if (Volatile.Read(ref disposed) == 0) Volatile.Write(ref malformed, 1); }
        finally
        {
            // Never join a read blocked in the OS at normal completion. This is a disposable worker background
            // thread, and process exit closes the handle. Disposal races cannot cancel a disposed token source.
            if (Volatile.Read(ref disposed) == 0) cancellation.Cancel();
            pipe.Dispose(); // Same thread, after its OS read returned; no concurrent-read disposal wait.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        // Unix PipeStream disposal can wait for a pending synchronous read. Do not close it from this thread:
        // normal completion must not wait for the parent's lifetime writer to close after accepting EOF/exit.
        // The observer closes it after its read returns, or worker process exit releases the still-blocked handle.
    }
}
