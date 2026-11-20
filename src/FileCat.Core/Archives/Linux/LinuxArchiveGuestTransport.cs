using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Linux;

/// <summary>Fixed guest serial grants and bounded cancellable protocol IO; not a launch/admission API.</summary>
internal static class LinuxArchiveGuestTransport
{
    internal static int Run(Stream diagnostics, Func<Stream, Stream, CancellationToken, int> action)
    {
        var cancellation = new CancellationTokenSource(); int malformed = 0;
        new Thread(() =>
        {
            using var control = new FileStream(new SafeFileHandle((nint)3, ownsHandle: false), FileAccess.Read, 1);
            try { if (control.ReadByte() != 1) Interlocked.Exchange(ref malformed, 1); }
            catch (IOException) { Interlocked.Exchange(ref malformed, 1); }
            cancellation.Cancel();
        }) { IsBackground = true, Name = "FileCat guest cancellation" }.Start();
        // Process-lifetime handles: no cross-thread disposal of synchronous IO which may still be blocked.
        var input = new FileStream(new SafeFileHandle((nint)0, ownsHandle: false), FileAccess.Read, 1);
        var output = new FileStream(new SafeFileHandle((nint)1, ownsHandle: false), FileAccess.Write, 1);
        using var transport = new ArchiveWorkerTransport(input, output, cancellation.Token);
        try
        {
            int result = action(transport.Input, transport.Output, cancellation.Token);
            return Volatile.Read(ref malformed) != 0 ? 64 : result;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            diagnostics.Write("FILECAT_GUEST_CANCELED\n"u8); diagnostics.Flush();
            return Volatile.Read(ref malformed) == 0 ? 0 : 64;
        }
    }

    internal static (Guid Nonce, byte Role) ReadStartup(Stream input, Stream output, byte maximumRole = 3)
    {
        if (maximumRole is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(maximumRole));
        output.Write("FCVMRDY1"u8); output.Flush();
        Span<byte> header = stackalloc byte[32]; input.ReadExactly(header);
        if (!header[..8].SequenceEqual("FCVMIPC1"u8) || header[25..].IndexOfAnyExcept((byte)0) >= 0 || header[24] is 0 || header[24] > maximumRole)
            throw new ArchiveWriteException(ArchiveWriteReason.ProtocolViolation, "Invalid guest startup authorization.");
        var nonce = new Guid(header.Slice(8, 16), bigEndian: true);
        if (nonce == Guid.Empty) throw new ArchiveWriteException(ArchiveWriteReason.ProtocolViolation, "Invalid guest startup identity.");
        return (nonce, header[24]);
    }
}
