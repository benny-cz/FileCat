using FileCat.Core.Threading;

namespace FileCat.Core.Records;

public enum FileDetailsTier { Summary, Full }
public sealed record FileDetailsObservation(FileDetailsSnapshot Summary, FileDetailsReport? Full);

/// <summary>
/// One captured subject, one real running owner and one replaceable pending request. Cancellation retires publication
/// immediately, but native ownership and its process/device admission remain until RunOwned actually returns.
/// </summary>
public sealed class FileDetailsReader(IFileRecords records, DeviceIoScheduler io, string device, string path) : IDisposable
{
    private readonly object _gate = new();
    private Request? _active, _pending;
    private bool _pumping, _closed;

    private sealed class Request : IDisposable
    {
        private readonly CancellationTokenRegistration _cancelResult;
        public Request(FileDetailsTier tier, CancellationToken token)
        {
            Tier = tier;
            Cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            // The publication task retires immediately for caller cancellation. RunOwned still
            // owns the in-flight native call and its permit until the delegate actually returns.
            _cancelResult = Cancel.Token.Register(static state =>
            {
                var request = (Request)state!;
                request.Result.TrySetCanceled(request.Cancel.Token);
            }, this);
        }
        public FileDetailsTier Tier { get; }
        public CancellationTokenSource Cancel { get; }
        public TaskCompletionSource<FileDetailsObservation> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Retire() => Cancel.Cancel();
        public void Dispose() { _cancelResult.Dispose(); Cancel.Dispose(); }
    }

    public Task<FileDetailsObservation> ReadAsync(FileDetailsTier tier, CancellationToken ct)
    {
        if (!Enum.IsDefined(tier)) throw new ArgumentOutOfRangeException(nameof(tier));
        Request request;
        bool start;
        lock (_gate)
        {
            if (_closed) return Task.FromCanceled<FileDetailsObservation>(new CancellationToken(true));
            _active?.Retire();
            if (_pending is { } previous) { previous.Retire(); previous.Dispose(); }
            request = _pending = new Request(tier, ct);
            start = !_pumping;
            _pumping = true;
        }
        if (start) _ = PumpAsync();
        return request.Result.Task;
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _active?.Retire();
            if (_pending is { } previous) { previous.Retire(); previous.Dispose(); _pending = null; }
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            Request request;
            lock (_gate)
            {
                if (_closed || _pending is null) { _pumping = false; return; }
                request = _active = _pending;
                _pending = null;
            }
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Cancel.Token, io.StoppingToken);
                var ct = linked.Token;
                var result = await FileObservationRequests.RunOwned(io, device, IoPriority.Interactive, token =>
                {
                    token.ThrowIfCancellationRequested();
                    if (request.Tier == FileDetailsTier.Full)
                    {
                        var full = records.ReadFull(path, FileDetailsPolicy.MetadataOnly, token);
                        token.ThrowIfCancellationRequested();
                        return new FileDetailsObservation(full.Summary, full);
                    }
                    var summary = records.ReadSummary(path, token);
                    token.ThrowIfCancellationRequested();
                    return new FileDetailsObservation(summary, null);
                }, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                request.Result.TrySetResult(result);
            }
            catch (OperationCanceledException) { request.Result.TrySetCanceled(new CancellationToken(true)); }
            catch (Exception ex) { request.Result.TrySetException(ex); }
            finally
            {
                lock (_gate) { if (ReferenceEquals(_active, request)) _active = null; }
                request.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate) { _closed = true; Cancel(); }
    }
}

/// <summary>Joint process/device reservation, shared by details, supplementary inventory and detail-column producers.</summary>
internal static class DetailsAdmission
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Devices = new(StringComparer.Ordinal);
    private static int _total;
    private static TaskCompletionSource _changed = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static async Task<IDisposable> AcquireAsync(string device, CancellationToken ct)
    {
        while (true)
        {
            Task wait;
            lock (Gate)
            {
                ct.ThrowIfCancellationRequested();
                int count = Devices.GetValueOrDefault(device);
                if (_total < 8 && count < 4)
                {
                    _total++; Devices[device] = count + 1;
                    return new Permit(device);
                }
                wait = _changed.Task;
            }
            await wait.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    private sealed class Permit(string device) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            TaskCompletionSource signal;
            lock (Gate)
            {
                if (--Devices[device] == 0) Devices.Remove(device);
                _total--;
                signal = _changed; _changed = NewSignal();
            }
            signal.TrySetResult();
        }
    }
}
