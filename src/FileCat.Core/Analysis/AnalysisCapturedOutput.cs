using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>Owns captured data through the actual export producer, output request and final release.</summary>
public abstract class AnalysisCapturedOutput : IAsyncDisposable
{
    protected DeviceIoScheduler Io { get; }
    private readonly AnalysisIoDevices? _devices;
    private readonly object _gate = new(), _cancelGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed, _closing, _active, _cancelled;
    internal AnalysisCapturedOutput(DeviceIoScheduler io, AnalysisIoDevices? devices)
    { Io = io; _devices = devices; }
    protected abstract Task<AnalysisExportReceipt> ExportCoreAsync(string name, AnalysisIoDevices devices,
        Func<AnalysisExportDestination> createDestination, bool redactPaths, CancellationToken cancellation);
    protected abstract ValueTask ReleaseAsync();

    public async Task<AnalysisExportReceipt> ExportNewAsync(string name, AnalysisIoDevices destinationDevices, Func<AnalysisExportDestination> createDestination,
        bool redactPaths, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(destinationDevices); ArgumentNullException.ThrowIfNull(createDestination);
        var devices = _devices ?? destinationDevices; foreach (string key in destinationDevices.Keys) devices = devices.Include(key);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active) throw new InvalidOperationException("This captured report already has an export in progress.");
            _active = true;
        }
        bool published = false;
        try
        {
            try
            {
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
                try
                {
                    var receipt = await ExportCoreAsync(name, devices, createDestination, redactPaths, cancel.Token).ConfigureAwait(false);
                    published = true; return receipt;
                }
                catch (AnalysisExportUncertainException) { published = true; throw; }
            }
            finally
            {
                bool close, disposed; lock (_gate) { _active = false; disposed = _disposed; close = BeginClose(); }
                if (close) await CloseAsync().ConfigureAwait(false);
                if (disposed) await _closed.Task.ConfigureAwait(false);
            }
        }
        catch (Exception error) when (published && error is not AnalysisExportUncertainException)
        { throw new AnalysisExportUncertainException(name, error); }
    }
    public ValueTask DisposeAsync()
    {
        bool close;
        lock (_gate) { _disposed = true; close = BeginClose(); }
        try { lock (_cancelGate) { if (!_cancelled) { _cancelled = true; _lifetime.Cancel(); } } }
        finally { if (close) _ = CloseAsync(); }
        return new(_closed.Task);
    }
    private bool BeginClose() { if (!_disposed || _active || _closing) return false; _closing = true; return true; }
    private async Task CloseAsync()
    {
        Exception? failure = null;
        try { await ReleaseAsync().ConfigureAwait(false); } catch (Exception error) { failure = AnalysisCloseUnconfirmedException.From(error); }
        lock (_cancelGate) { _cancelled = true; _lifetime.Dispose(); }
        if (failure is null) _closed.TrySetResult(); else _closed.TrySetException(failure);
    }
}
