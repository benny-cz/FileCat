using FileCat.Core.Archives.Linux;

// Diagnostic roles use the same production cancellation/transport implementation.
internal static class GuestCancellation
{
    internal static int Run(Stream diagnostics, Func<Stream, Stream, CancellationToken, int> action)
        => LinuxArchiveGuestTransport.Run(diagnostics, action);
}