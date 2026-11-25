using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Threading;

namespace FileCat.Core.Records;

public enum FileDetailsTier { Summary, Full }
public sealed record FileDetailsObservation(FileDetailsSnapshot Summary, FileDetailsReport? Full);

/// <summary>
/// The item's full file-system record (D-56, <see cref="IFileRecords.Read"/>), between the summaries read just before and
/// just after it in the same owned call, so a record can be bound to one object.
/// </summary>
public sealed record FileRecordObservation(FileDetailsSnapshot Before, InspectionReport Record, FileDetailsSnapshot After)
{
    /// <summary>
    /// Payload, never metadata only: the record can hold stream, extended-attribute and xattr value excerpts and, read from
    /// the volume as administrator or root, resident attribute data, record slack, $LogFile images and raw inode bytes.
    /// </summary>
    public ReportContentKind ContentKind => ReportContentKind.Payload;

    /// <summary>Why the record must not be copied or saved, or null when it may be (see <see cref="OutputRefusal(bool)"/>).</summary>
    public string? OutputRefusal() => OutputRefusal(OperatingSystem.IsWindows());

    /// <summary>
    /// The record's output rule. It is published only while it is bound to one object: the item's identity was read before
    /// and after the record and did not change. On Windows its value excerpts are read through the file system, which
    /// decrypts EFS content, so the item must also have been observed, both times, as not EFS-encrypted (or on a volume
    /// without EFS). Elsewhere there is no EFS, as for every other Unix output source.
    /// </summary>
    public string? OutputRefusal(bool windows)
    {
        if (Before.Identity is not { State: FactState.Known, Value: { } before } || After.Identity is not { State: FactState.Known, Value: { } after })
            return "the item's identity could not be read before and after its record, so the record is not bound to one object.";
        if (before != after)
            return "the item changed while its record was read: the path now names another object. Read the record again.";
        if (!windows) return null;
        static bool Clear(ObservedFact<bool> efs) => efs is { State: FactState.Known, Value: false } or { State: FactState.Unsupported };
        if (Clear(Before.FileEncryption) && Clear(After.FileEncryption)) return null;
        return Before.FileEncryption is { State: FactState.Known, Value: true } || After.FileEncryption is { State: FactState.Known, Value: true }
            ? "the item is EFS-encrypted, and the stream and extended-attribute excerpts in its record are read through Windows, which decrypts them."
            : "whether the item is EFS-encrypted is unknown, and the stream and extended-attribute excerpts in its record would be decrypted if it is.";
    }
}

/// <summary>
/// One captured subject, one real running owner and one replaceable pending request. Cancellation retires publication
/// immediately, but native ownership and its process/device admission remain until RunOwned actually returns.
/// </summary>
public sealed class FileDetailsReader(IFileRecords records, DeviceIoScheduler io, string device, string path) : IDisposable
{
    private readonly object _gate = new();
    private Request? _active, _pending;
    private bool _pumping, _closed;

    /// <summary>
    /// The full record's working-memory reservation, for the whole process: one record read at a time. As administrator its
    /// raw readers hold up to 512 MiB of $LogFile (with a run-sized copy), 64 MiB per $Extend view index and 16 MiB of a
    /// folder's index, and scan up to 2 GiB of the change journal, far beyond a summary's bounds.
    /// </summary>
    private static readonly SemaphoreSlim RecordSlot = new(1, 1);

    /// <summary>
    /// Appended to the device key for the scheduler queue that runs a record read. Admission stays the item's device's;
    /// only the queue differs, so a long administrator read (a large change journal, $LogFile catching up for seconds) is
    /// never taken for the device hanging, which would stop other work on that drive (Disk usage, for one).
    /// </summary>
    public const string RecordQueueSuffix = " · file-system record";

    private sealed class Request : IDisposable
    {
        private readonly CancellationTokenRegistration _cancelResult;
        public Request(FileDetailsTier tier, bool record, CancellationToken token, Action? waiting = null)
        {
            Tier = tier;
            Waiting = waiting;
            if (record) RecordResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
            else Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            // The publication task retires immediately for caller cancellation. RunOwned still
            // owns the in-flight native call and its permit until the delegate actually returns.
            _cancelResult = Cancel.Token.Register(static state =>
            {
                var request = (Request)state!;
                request.Result?.TrySetCanceled(request.Cancel.Token);
                request.RecordResult?.TrySetCanceled(request.Cancel.Token);
            }, this);
        }
        public FileDetailsTier Tier { get; }
        /// <summary>Called, on a pool thread, when a record request must wait for another record read to finish.</summary>
        public Action? Waiting { get; }
        public CancellationTokenSource Cancel { get; }
        public TaskCompletionSource<FileDetailsObservation>? Result { get; }
        public TaskCompletionSource<FileRecordObservation>? RecordResult { get; }
        public void Canceled()
        {
            Result?.TrySetCanceled(new CancellationToken(true));
            RecordResult?.TrySetCanceled(new CancellationToken(true));
        }
        public void Failed(Exception ex)
        {
            Result?.TrySetException(ex);
            RecordResult?.TrySetException(ex);
        }
        public void Retire() => Cancel.Cancel();
        public void Dispose() { _cancelResult.Dispose(); Cancel.Dispose(); }
    }

    public Task<FileDetailsObservation> ReadAsync(FileDetailsTier tier, CancellationToken ct)
    {
        if (!Enum.IsDefined(tier)) throw new ArgumentOutOfRangeException(nameof(tier));
        return Enqueue(tier, record: false, ct)?.Result!.Task ?? Task.FromCanceled<FileDetailsObservation>(new CancellationToken(true));
    }

    /// <summary>
    /// The full file-system record (D-56), only on this explicit request. It shares this reader's single running owner and
    /// pending slot with the summary and full tiers (a new request of either kind retires the other), is admitted on the
    /// item's device like them and runs on a scheduler worker of its own queue (<see cref="RecordQueueSuffix"/>), never on
    /// the caller's thread, and holds the process's one record reservation while it reads: <paramref name="waiting"/> is
    /// called first when another record read holds it. On macOS it reads with dataless-file downloads disabled on its thread,
    /// as the details tiers do.
    /// </summary>
    public Task<FileRecordObservation> ReadRecordAsync(CancellationToken ct, Action? waiting = null) =>
        Enqueue(FileDetailsTier.Summary, record: true, ct, waiting)?.RecordResult!.Task ?? Task.FromCanceled<FileRecordObservation>(new CancellationToken(true));

    private Request? Enqueue(FileDetailsTier tier, bool record, CancellationToken ct, Action? waiting = null)
    {
        Request request;
        bool start;
        lock (_gate)
        {
            if (_closed) return null;
            _active?.Retire();
            if (_pending is { } previous) { previous.Retire(); previous.Dispose(); }
            request = _pending = new Request(tier, record, ct, waiting);
            start = !_pumping;
            _pumping = true;
        }
        if (start) _ = PumpAsync();
        return request;
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
                if (request.RecordResult is { } recordResult)
                {
                    // Waiting for the reservation holds no device permit, and is said. RunOwned returns only once a started
                    // native read has returned, so the reservation is never released while its acquisition still runs.
                    if (!RecordSlot.Wait(0))
                    {
                        request.Waiting?.Invoke();
                        await RecordSlot.WaitAsync(ct).ConfigureAwait(false);
                    }
                    try
                    {
                        var record = await FileObservationRequests.RunOwned(io, device, IoPriority.Interactive, ReadRecord, ct,
                            queue: device + RecordQueueSuffix).ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        recordResult.TrySetResult(record);
                    }
                    finally { RecordSlot.Release(); }
                }
                else
                {
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
                    request.Result!.TrySetResult(result);
                }
            }
            catch (OperationCanceledException) { request.Canceled(); }
            catch (Exception ex) { request.Failed(ex); }
            finally
            {
                lock (_gate) { if (ReferenceEquals(_active, request)) _active = null; }
                request.Dispose();
            }
        }
    }

    /// <summary>
    /// On the device worker: the item's identity and EFS state, its record, then both again. The summaries are the details
    /// tier's own metadata-only reads; the record is the unrestricted reader, which can read values and raw volumes.
    /// </summary>
    private FileRecordObservation ReadRecord(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var before = records.ReadSummary(path, token);
        token.ThrowIfCancellationRequested();
        var record = OperatingSystem.IsMacOS()
            ? MacMaterializationPolicy.Run<InspectionReport>(token, () => records.Read(path, token),
                (_, reason) => throw new IOException("The file-system record was not read: " + reason))
            : records.Read(path, token);
        token.ThrowIfCancellationRequested();
        var after = records.ReadSummary(path, token);
        token.ThrowIfCancellationRequested();
        return new FileRecordObservation(before, record, after);
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
    // Match DeviceIoScheduler's device queue identity, including Windows drive/UNC case variants.
    private static readonly Dictionary<string, int> Devices = new(StringComparer.OrdinalIgnoreCase);
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
