using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

/// <summary>Exact storage evidence; no result is accepted and no next intent is admitted before its durable flush.</summary>
internal sealed class StorageTransformJournalWriter : IDisposable
{
    private readonly object _lock = new();
    private readonly Stream _stream;
    private readonly Action _flush;
    private readonly StorageJournalHeader _header;
    private volatile StorageTransformJournalCollector _collector = new();
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly long _byteLimit;
    private readonly int _recordLimit;
    private long _bytes, _sequence, _call;
    private long _remainingRootBytes;
    private int _remainingRootRecords;
    private bool _callReserved;
    private volatile bool _faulted;
    private volatile bool _disposed;
    private StorageJournalPrefix? _prefix;
    internal StorageTransformJournalCollector Collector => _collector;
    internal StorageJournalHeader Header => _header;
    internal bool IsFaulted => _faulted || _disposed;
    internal StorageJournalPrefix? Prefix => Volatile.Read(ref _prefix);
    internal bool NeedsRecovery { get { lock (_lock) return _faulted || !_collector.CanFinish || _collector.Summary is { ReviewPending: true } or { PersistenceFault: true }; } }

    internal StorageTransformJournalWriter(Stream stream, Action flush, StorageJournalHeader header,
        long byteLimit = StorageJournalPolicy.JournalBytes, int recordLimit = StorageJournalPolicy.RecordLimit)
    {
        StorageTransformJournalCollector.ValidateHeader(header);
        _stream = stream; _flush = flush; _header = header; _byteLimit = byteLimit; _recordLimit = recordLimit;
        _remainingRootRecords = header.Roots.Length;
        _remainingRootBytes = (long)header.Roots.Length * StorageJournalPolicy.RootSummaryBytes;
        byte[] begin = Encode("storage-begin", 1, header.JobId, header);
        if (byteLimit < begin.Length + _remainingRootBytes + StorageJournalPolicy.ClosureBytes ||
            recordLimit < 1 + _remainingRootRecords + StorageJournalPolicy.ClosureRecords)
            throw new IOException("The storage journal cannot reserve its fixed summary and closure capacity.");
        Append(begin, "storage-begin");
    }

    internal StorageJournalCall FlushIntent(int sourceIndex, string path, StorageObservation before)
    {
        lock (_lock)
        {
            RequireWritable();
            if (_callReserved || _collector.Pending is not null || _collector.Summary is not null || _collector.EffectUncertain) throw new InvalidOperationException("One resolved storage call must settle before another intent.");
            if (_bytes > _byteLimit - _remainingRootBytes - StorageJournalPolicy.ClosureBytes - StorageJournalPolicy.CallBytes ||
                _sequence > _recordLimit - _remainingRootRecords - StorageJournalPolicy.ClosureRecords - StorageJournalPolicy.CallRecords)
                throw new StorageJournalLimitException();
            var intent = new StorageJournalIntent(checked(_call + 1), sourceIndex, path, before.Identity, before,
                _header.Action == StorageAction.Compress ? StorageObservationState.Enabled : StorageObservationState.Disabled);
            byte[] record = Encode("storage-intent", _sequence + 1, _header.JobId, intent);
            _callReserved = true; // whole 1 MiB/four-record allowance, in addition to every remaining fixed slot
            try
            {
                Append(record, "storage-intent"); // collector becomes pending before the append/flush attempt
                _call = intent.Call;
                return new(this, _header.JobId, intent);
            }
            catch { _faulted = true; _collector.Fault(); throw; }
        }
    }

    internal void FlushResult(StorageJournalCall call, StorageMutationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.NativeCallAttempted && !call.IsAdmitted)
            throw new InvalidOperationException("A native result requires this call's one-shot admission.");
        Complete(call, new(call.Intent.Call, result.NativeCallAttempted ? StorageCallDisposition.Observed : StorageCallDisposition.NotAttempted,
            result.NativeCallAttempted ? null : StorageNoCallReason.StateChanged, result));
    }
    internal void FlushNotAttempted(StorageJournalCall call, StorageNoCallReason reason) =>
        Complete(call, new(call.Intent.Call, StorageCallDisposition.NotAttempted, reason, null));

    private void Complete(StorageJournalCall call, StorageJournalOutcome result)
    {
        lock (_lock)
        {
            RequireWritable(); RequireCall(call);
            byte[] record;
            try { record = Encode("storage-result", _sequence + 1, _header.JobId, result); }
            catch { _faulted = true; _collector.Fault(); throw; }
            try { Append(record, "storage-result"); }
            catch { _faulted = true; _collector.Fault(); throw; }
            // Keep the in-flight receipt and reservation through native return and the durable result boundary.
            call.MarkSettled(); _callReserved = false;
        }
    }

    internal void RootSummary(StorageJournalRootSummary summary)
    {
        lock (_lock)
        {
            RequireWritable();
            if (_callReserved || _remainingRootRecords <= 0) throw new InvalidOperationException("The storage root summary cannot be written now.");
            byte[] record = Encode("storage-root", _sequence + 1, _header.JobId, summary);
            if (record.Length > StorageJournalPolicy.RootSummaryBytes) throw new IOException("The storage root summary exceeds its fixed slot.");
            Append(record, "storage-root");
            _remainingRootRecords--; _remainingRootBytes -= StorageJournalPolicy.RootSummaryBytes;
        }
    }

    internal void Finish(StorageResultSnapshot result)
    {
        lock (_lock)
        {
            RequireWritable();
            if (_callReserved || result.Action != _header.Action || result.Scope != _header.Scope || result.ReviewRevision != _header.ReviewRevision)
                throw new IOException("Unresolved storage effects cannot receive a terminal record.");
            var summary = new StorageJournalSummary(StorageJournalCounts.From(result.Counts), result.Roots, result.Reasons,
                result.Discovery, result.Canceled, result.PersistenceFault, result.ReviewPending, result.ExecutionRefused)
            {
                Examples = result.Examples, OmittedExamples = result.OmittedExamples,
                AmbiguousDuplicates = result.AmbiguousDuplicates, ObservationCounts = result.ObservationCounts,
            };
            Append(Encode("storage-summary", _sequence + 1, _header.JobId, summary), "storage-summary");
            if (!_collector.CanFinish || result.PersistenceFault || result.ReviewPending) return; // durable summary, deliberately no end
            Append(Encode("storage-end", _sequence + 1, _header.JobId, new StorageJournalTerminal(result.FinalState())), "storage-end");
        }
    }

    internal void Fault() { lock (_lock) { _faulted = true; _collector.Fault(); } }
    private void RequireWritable()
    {
        if (_disposed || _faulted || _collector.Damaged || _collector.Ended) throw new IOException("The storage writer is faulted or already closed; retain its evidence.");
    }
    private void RequireCall(StorageJournalCall call)
    {
        if (!ReferenceEquals(call.Writer, this) || call.JobId != _header.JobId || call.IsSettled || !_callReserved ||
            _collector.Pending?.Call != call.Intent.Call) throw new InvalidOperationException("This storage call receipt is stale or belongs to another writer.");
    }
    private void Append(byte[] bytes, string type)
    {
        if (_bytes > _byteLimit - bytes.Length || _sequence >= _recordLimit) throw new StorageJournalLimitException();
        using var document = Decode(bytes);
        var next = _collector.Fork(); next.Read(document.RootElement);
        if (next.Damaged) { _faulted = true; _collector.Fault(); throw new IOException("The storage record violates the collector protocol: " + type); }
        if (type == "storage-intent") _collector = next; // pending BEFORE attempting an intent write; no rollback after uncertain I/O
        try
        {
            _stream.Write(bytes); _flush();
            _collector = next; // a result/summary/end changes live interpretation only after its durable flush
            _hash.AppendData(bytes); _bytes += bytes.Length; _sequence++;
            Volatile.Write(ref _prefix, new(_header.JobId, _sequence, _bytes, Convert.ToHexString(_hash.GetCurrentHash())));
        }
        catch { _faulted = true; _collector.Fault(); throw; }
    }

    internal static byte[] Encode<T>(string type, long sequence, Guid job, T data)
    {
        var envelope = new StorageTransformJournalCollector.StorageEnvelope(type, StorageJournalPolicy.Version, sequence, job,
            JsonSerializer.SerializeToElement(data, StorageTransformJournalCollector.Json));
        using var bounded = new BoundedRecord();
        JsonSerializer.Serialize(bounded, envelope, StorageTransformJournalCollector.Json);
        byte[] json = bounded.ToArray(), result = new byte[json.Length + 10];
        System.Text.Encoding.ASCII.GetBytes(Crc32.HashToUInt32(json).ToString("x8"), result.AsSpan(0, 8));
        result[8] = (byte)' '; json.CopyTo(result, 9); result[^1] = (byte)'\n';
        return result;
    }
    internal static JsonDocument Decode(ReadOnlySpan<byte> line)
    {
        if (line.Length is < 12 or > StorageJournalPolicy.RecordBytes || line[^1] != '\n' || line[8] != ' ' ||
            !uint.TryParse(System.Text.Encoding.ASCII.GetString(line[..8]), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint checksum) || Crc32.HashToUInt32(line[9..^1]) != checksum)
            throw new InvalidDataException("The storage record is torn, oversized or has an invalid checksum.");
        return JsonDocument.Parse(line[9..^1].ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
    }
    public void Dispose()
    {
        lock (_lock) { if (_disposed) return; _disposed = true; _hash.Dispose(); }
    }
    private sealed class BoundedRecord : Stream
    {
        private readonly MemoryStream _memory = new();
        internal byte[] ToArray() => _memory.ToArray();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Length > StorageJournalPolicy.RecordBytes - 10 - buffer.Length) throw new IOException("The exact storage record exceeds 256 KiB; no path or identity was truncated.");
            _memory.Write(buffer);
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _memory.Length;
        public override long Position { get => _memory.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _memory.Dispose(); base.Dispose(disposing); }
    }
}

internal sealed class StorageJournalCall(StorageTransformJournalWriter writer, Guid jobId, StorageJournalIntent intent)
{
    private int _state; // 0 durable/unclaimed, 1 admitted, 2 settled, 3 denied before call
    internal StorageTransformJournalWriter Writer => writer;
    internal Guid JobId => jobId;
    internal StorageJournalIntent Intent => intent;
    internal bool IsSettled => Volatile.Read(ref _state) == 2;
    internal bool IsAdmitted => Volatile.Read(ref _state) == 1;
    internal bool TryAdmit() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
    internal bool TryDeny() => Interlocked.CompareExchange(ref _state, 3, 0) == 0;
    internal void MarkSettled() => Interlocked.Exchange(ref _state, 2);
}
internal sealed class StorageJournalLimitException() : IOException("The storage recovery-record limit was reached; select smaller scopes.");
