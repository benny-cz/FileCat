using FileCat.Core.Archives;
using Microsoft.Win32.SafeHandles;

internal static class GuestCancellation
{
    // The native init gives only the fixed read-only hvc2 descriptor. Serial EOF is not pipe EOF: the host
    // relays its dedicated pipe closure as exactly one byte, without waiting for archive protocol capacity.
    internal static int Run(Stream diagnostics, Func<Stream, Stream, CancellationToken, int> action)
    {
        var cancellation = new CancellationTokenSource();
        int malformed = 0;
        new Thread(() =>
        {
            using var control = new FileStream(new SafeFileHandle((nint)3, ownsHandle: false), FileAccess.Read, 1);
            try { if (control.ReadByte() != 1) Interlocked.Exchange(ref malformed, 1); }
            catch (IOException) { Interlocked.Exchange(ref malformed, 1); }
            cancellation.Cancel();
        }) { IsBackground = true, Name = "FileCat guest cancellation" }.Start();
        // Process-lifetime streams: disposal from this thread must not wait for background OS IO still blocked
        // in either direction. Returning from this disposable worker closes them with the process.
        var input = new FileStream(new SafeFileHandle((nint)0, ownsHandle: false), FileAccess.Read, 1);
        var output = new FileStream(new SafeFileHandle((nint)1, ownsHandle: false), FileAccess.Write, 1);
        using var transport = new ArchiveWorkerTransport(input, output, cancellation.Token);
        try { return action(transport.Input, transport.Output, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            diagnostics.Write("FILECAT_GUEST_CANCELED\n"u8); diagnostics.Flush();
            return Volatile.Read(ref malformed) == 0 ? 0 : 64;
        }
    }
}
