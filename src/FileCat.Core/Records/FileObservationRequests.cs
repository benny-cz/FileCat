using FileCat.Core.Threading;

namespace FileCat.Core.Records;

/// <summary>The shared owned lane for detail snapshots, supplementary inventories and native detail columns.</summary>
public static class FileObservationRequests
{
    [ThreadStatic] private static string? _admittedDevice;
    public static bool HasAdmission => _admittedDevice is not null;
    public static bool IsAdmittedFor(string device) => string.Equals(_admittedDevice, device, StringComparison.OrdinalIgnoreCase);

    /// <param name="queue">
    /// The scheduler queue that runs the read, when not the device's own (admission is still the device's). The full
    /// file-system record uses one so that a long administrator read is not reported as the device hanging.
    /// </param>
    public static async Task<T> RunOwned<T>(DeviceIoScheduler io, string device, IoPriority priority,
        Func<CancellationToken, T> read, CancellationToken ct = default, string? queue = null)
    {
        if (DeviceIoScheduler.IsDeviceWorker)
        {
            if (!IsAdmittedFor(device)) throw new NotSupportedException("This producer has no observation admission for the original device.");
            ct.ThrowIfCancellationRequested();
            // Already admitted synchronous producer; never queue or wait on its own device.
            var current = read(ct);
            ct.ThrowIfCancellationRequested();
            return current;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, io.StoppingToken);
        using var permit = await DetailsAdmission.AcquireAsync(device, linked.Token).ConfigureAwait(false);
        var result = await io.RunOwned(queue ?? device, priority, token =>
        {
            token.ThrowIfCancellationRequested();
            string? previous = _admittedDevice;
            _admittedDevice = device;
            try { return read(token); }
            finally { _admittedDevice = previous; }
        }, linked.Token).ConfigureAwait(false);
        // A started native observation can return after cancellation. Its owner still retires at the real I/O boundary.
        linked.Token.ThrowIfCancellationRequested();
        return result;
    }
}
