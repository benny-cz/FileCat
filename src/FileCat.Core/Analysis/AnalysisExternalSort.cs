using System.Buffers.Binary;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal readonly record struct AnalysisSortEntry(byte[] Key, ulong Value);
internal readonly record struct AnalysisSortInput(AnalysisSortEntry? Entry, int Bytes);
internal readonly record struct AnalysisIndexRange(ulong First, ulong Count);

/// <summary>
/// Exact binary-key sort for bounded graph indexes. Input, spool and merge I/O enter the shared background
/// admission in batches; CPU sorting happens outside the device worker. Runs share one anonymous spool
/// arena, which is retired after merge instead of accumulating obsolete COW/compaction pages.
/// </summary>
internal static class AnalysisExternalSort
{
    // A captured name can expand from one-byte native text to two-byte ordinal UTF-16 units.
    internal const int MaximumKeyBytes = AnalysisLimits.RawNameBytes * 2 + 64, MaximumRuns = 256;
    internal const int RunMemoryBytes = 16 * 1024 * 1024;
    private static readonly SemaphoreSlim Sorts = new(AnalysisLimits.ActiveScans, AnalysisLimits.ActiveScans);
    internal static readonly IComparer<AnalysisSortEntry> Order = Comparer<AnalysisSortEntry>.Create((a, b) =>
    { int key = a.Key.AsSpan().SequenceCompareTo(b.Key); return key != 0 ? key : a.Value.CompareTo(b.Value); });

    // The caller owns source. Its MoveNext/Current may only read this workspace's captured device;
    // no source enumeration, connection or content work can be smuggled into an index build.
    internal static async Task<AnalysisSortedIndex> BuildAsync(AnalysisScratchWorkspace workspace, DeviceIoScheduler io, AnalysisIoDevices device,
        IEnumerator<AnalysisSortEntry> source, CancellationToken cancellation = default, int runMemoryBytes = RunMemoryBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        IEnumerable<AnalysisSortInput> Steps()
        { while (source.MoveNext()) { var item = source.Current; yield return new(item, (item.Key?.Length ?? 0) + 12); } }
        using var steps = Steps().GetEnumerator();
        return await BuildSparseAsync(workspace, io, device, steps, cancellation, runMemoryBytes).ConfigureAwait(false);
    }

    // A null entry is one consumed input row with no indexed value. Its captured byte cost still
    // counts toward the worker/observation bound; sparse input cannot hide an unbounded MoveNext loop.
    internal static async Task<AnalysisSortedIndex> BuildSparseAsync(AnalysisScratchWorkspace workspace, DeviceIoScheduler io, AnalysisIoDevices device,
        IEnumerator<AnalysisSortInput> source, CancellationToken cancellation = default, int runMemoryBytes = RunMemoryBytes)
    {
        ArgumentNullException.ThrowIfNull(workspace); ArgumentNullException.ThrowIfNull(io); ArgumentNullException.ThrowIfNull(source);
        if (runMemoryBytes is < 1024 or > RunMemoryBytes) throw new ArgumentOutOfRangeException(nameof(runMemoryBytes));
        await Sorts.WaitAsync(cancellation).ConfigureAwait(false);
        var cleanup = new Cleanup(); var runs = cleanup.Runs; var entries = new List<AnalysisSortEntry>();
        AnalysisSortEntry? carry = null; long bytes = 0, total = 0, inputs = 0; bool finished = false;
        AnalysisSortedIndex? result = null;
        try
        {
            cleanup.Spool = await AnalysisIo.Run(io, device, _ => workspace.CreateArena(), cancellation).ConfigureAwait(false);
            while (!finished || carry.HasValue)
            {
                bool full = await AnalysisIo.Run(io, device, token =>
                {
                    int batch = 0; long batchBytes = 0;
                    while (batch < AnalysisLimits.BatchRecords && batchBytes < AnalysisLimits.BatchBytes)
                    {
                        token.ThrowIfCancellationRequested(); AnalysisSortEntry entry;
                        if (carry is { } pending) { entry = pending; carry = null; }
                        else
                        {
                            if (!source.MoveNext()) { finished = true; return false; }
                            if (++inputs > AnalysisLimits.Observations) throw new IOException("The analysis index input-row limit was reached.");
                            var input = source.Current;
                            if (input.Bytes is < 1 or > AnalysisScratchVector.MaximumRecordBytes) throw Invalid();
                            batchBytes += input.Bytes;
                            if (input.Entry is not { } item) { batch++; continue; }
                            if (item.Key is null || item.Key.Length is < 1 or > MaximumKeyBytes || item.Value == 0) throw Invalid();
                            if (++total > AnalysisLimits.Observations) throw new IOException("The analysis index record limit was reached.");
                            entry = new(item.Key.ToArray(), item.Value);
                        }
                        long cost = 128L + entry.Key.Length;
                        if (entries.Count != 0 && cost > runMemoryBytes - bytes) { carry = entry; return true; }
                        entries.Add(entry); bytes += cost; batchBytes += cost; batch++;
                    }
                    return bytes >= runMemoryBytes;
                }, cancellation).ConfigureAwait(false);
                if (!full && !finished) continue;
                if (entries.Count == 0) continue;
                if (runs.Count >= MaximumRuns) throw new IOException("The bounded analysis sort-run limit was reached. Keep the partial result or narrow its scope.");
                await Task.Run(() => entries.Sort(Order), cancellation).ConfigureAwait(false);
                var run = new AnalysisScratchVector(cleanup.Spool!, ownsArena: false); runs.Add(run);
                int position = 0;
                while (position < entries.Count)
                {
                    await AnalysisIo.Run(io, device, token =>
                    {
                        int batch = 0, batchBytes = 0;
                        while (position < entries.Count && batch < AnalysisLimits.BatchRecords && batchBytes < AnalysisLimits.BatchBytes)
                        {
                            token.ThrowIfCancellationRequested(); var item = entries[position++]; run.Append(Encode(item), token);
                            batchBytes += item.Key.Length + 12; batch++;
                        }
                        return true;
                    }, cancellation).ConfigureAwait(false);
                }
                await AnalysisIo.Run(io, device, token => { run.Flush(token); return true; }, cancellation).ConfigureAwait(false);
                entries.Clear(); bytes = 0;
            }

            cleanup.SpoolLease = await AnalysisIo.Run(io, device, _ => cleanup.Spool!.Capture(), cancellation).ConfigureAwait(false);
            var queue = new PriorityQueue<Cursor, AnalysisSortEntry>(Order);
            for (int begin = 0; begin < runs.Count; begin += 16)
            {
                int first = begin;
                await AnalysisIo.Run(io, device, token =>
                {
                    for (int n = first; n < Math.Min(first + 16, runs.Count); n++)
                    {
                        token.ThrowIfCancellationRequested(); var view = runs[n].CapturePublication().Open(cleanup.SpoolLease!);
                        cleanup.Readers.Add(view); var cursor = new Cursor(view);
                        if (cursor.MoveNext(token)) queue.Enqueue(cursor, cursor.Current);
                        runs[n].Dispose();
                    }
                    return true;
                }, cancellation).ConfigureAwait(false);
            }
            // The immutable run views retain directory pages, not the writers' 256-KiB buffers.
            runs.Clear();
            cleanup.Output = await AnalysisIo.Run(io, device, _ => workspace.CreateArena(), cancellation).ConfigureAwait(false);
            cleanup.OutputRows = new(cleanup.Output!, ownsArena: false);
            while (queue.Count != 0)
            {
                await AnalysisIo.Run(io, device, token =>
                {
                    int batch = 0, batchBytes = 0;
                    while (queue.Count != 0 && batch < AnalysisLimits.BatchRecords && batchBytes < AnalysisLimits.BatchBytes)
                    {
                        token.ThrowIfCancellationRequested(); var cursor = queue.Dequeue(); var entry = cursor.Current;
                        cleanup.OutputRows!.Append(Encode(entry), token); batch++; batchBytes += entry.Key.Length + 12;
                        if (cursor.MoveNext(token)) queue.Enqueue(cursor, cursor.Current);
                    }
                    return true;
                }, cancellation).ConfigureAwait(false);
            }
            result = await AnalysisIo.Run(io, device, token =>
            {
                cleanup.OutputRows!.Flush(token); var publication = cleanup.OutputRows.CapturePublication();
                if (publication.Count != (ulong)total) throw new InvalidDataException("The analysis sort did not preserve every input record.");
                return new AnalysisSortedIndex(cleanup.Output!, publication, cleanup.Output!.CommittedEnd);
            }, cancellation).ConfigureAwait(false);
            cleanup.Output = null; return result;
        }
        finally
        {
            try { await AnalysisIo.ReleaseScratchAsync(io, device, cleanup).ConfigureAwait(false); }
            catch
            {
                if (result is not null) await AnalysisIo.ReleaseScratchAsync(io, device, result).ConfigureAwait(false);
                throw;
            }
            finally { Sorts.Release(); }
        }
    }

    internal static byte[] Encode(AnalysisSortEntry entry)
    {
        if (entry.Key is null || entry.Key.Length is < 1 or > MaximumKeyBytes || entry.Value == 0) throw Invalid();
        byte[] bytes = new byte[checked(entry.Key.Length + 12)]; BinaryPrimitives.WriteInt32LittleEndian(bytes, entry.Key.Length);
        entry.Key.CopyTo(bytes, 4); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(4 + entry.Key.Length), entry.Value); return bytes;
    }
    internal static AnalysisSortEntry Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 13) throw Invalid(); int length = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (length is < 1 or > MaximumKeyBytes || length != bytes.Length - 12) throw Invalid();
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(4 + length)..]); if (value == 0) throw Invalid();
        return new(bytes.Slice(4, length).ToArray(), value);
    }
    private static InvalidDataException Invalid() => new("Invalid binary analysis index entry.");
    private sealed class Cursor(AnalysisScratchVectorCheckpoint rows)
    {
        private ulong _next = 1;
        internal AnalysisSortEntry Current { get; private set; }
        internal bool MoveNext(CancellationToken cancellation)
        { if (_next > rows.Count) return false; Current = Decode(rows.Read(_next++, cancellation)); return true; }
    }
    private sealed class Cleanup : IAnalysisScratchOwned
    {
        internal readonly List<AnalysisScratchVector> Runs = [];
        internal readonly List<AnalysisScratchVectorCheckpoint> Readers = [];
        internal AnalysisScratchCheckpoint? SpoolLease;
        internal AnalysisScratchArena? Spool, Output;
        internal AnalysisScratchVector? OutputRows;
        public void Dispose()
        {
            try { foreach (var reader in Readers) reader.Dispose(); foreach (var run in Runs) run.Dispose(); OutputRows?.Dispose(); }
            finally { try { SpoolLease?.Dispose(); } finally { try { Spool?.Dispose(); } finally { Output?.Dispose(); } } }
        }
    }
}

/// <summary>Immutable exact-key/prefix index, with admitted reads and no source or action authority.</summary>
internal sealed class AnalysisSortedIndex(AnalysisScratchArena arena, AnalysisScratchVectorPublication publication, long end) : IAnalysisScratchOwned
{
    private readonly object _gate = new(); private bool _disposed;
    internal ulong Count => publication.Count;
    internal AnalysisSortedIndexReader Acquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); return AnalysisSortedIndexReader.Open(publication, arena.CapturePublished(end));
        }
    }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; arena.Dispose(); } }
}

internal sealed class AnalysisSortedIndexReader : IAnalysisScratchOwned
{
    private readonly object _gate = new();
    private readonly AnalysisScratchVectorPublication _publication;
    private readonly AnalysisScratchCheckpoint _lease;
    private readonly AnalysisScratchVectorCheckpoint _rows;
    private bool _disposed;
    internal ulong Count => _rows.Count;
    private AnalysisSortedIndexReader(AnalysisScratchVectorPublication publication, AnalysisScratchCheckpoint lease, AnalysisScratchVectorCheckpoint rows)
    { _publication = publication; _lease = lease; _rows = rows; }
    internal static AnalysisSortedIndexReader Open(AnalysisScratchVectorPublication publication, AnalysisScratchCheckpoint lease)
    {
        AnalysisScratchVectorCheckpoint? rows = null;
        try { rows = publication.Open(lease); return new(publication, lease, rows); }
        catch { rows?.Dispose(); lease.Dispose(); throw; }
    }
    internal AnalysisSortEntry Read(ulong id, CancellationToken cancellation = default)
    { lock (_gate) { Guard(); return AnalysisExternalSort.Decode(_rows.Read(id, cancellation)); } }
    internal AnalysisSortedIndexReader Retain()
    { lock (_gate) { Guard(); return Open(_publication, _lease.RetainReader()); } }
    internal AnalysisIndexRange FindRange(ReadOnlySpan<byte> key, CancellationToken cancellation = default) => Range(key, false, cancellation);
    internal AnalysisIndexRange PrefixRange(ReadOnlySpan<byte> key, CancellationToken cancellation = default) => Range(key, true, cancellation);
    private AnalysisIndexRange Range(ReadOnlySpan<byte> key, bool prefix, CancellationToken cancellation)
    {
        if (key.Length > AnalysisExternalSort.MaximumKeyBytes || !prefix && key.IsEmpty) throw new ArgumentOutOfRangeException(nameof(key));
        lock (_gate)
        {
            Guard(); ulong first = Bound(key, prefix, upper: false, cancellation), last = Bound(key, prefix, upper: true, cancellation);
            return new(first, last - first);
        }
    }
    private ulong Bound(ReadOnlySpan<byte> key, bool prefix, bool upper, CancellationToken cancellation)
    {
        ulong low = 1, high = Count + 1;
        while (low < high)
        {
            cancellation.ThrowIfCancellationRequested(); ulong at = low + (high - low) / 2;
            var item = AnalysisExternalSort.Decode(_rows.Read(at, cancellation));
            var candidate = prefix ? item.Key.AsSpan(0, Math.Min(item.Key.Length, key.Length)) : item.Key.AsSpan();
            int order = candidate.SequenceCompareTo(key);
            if (order < 0 || upper && order == 0) low = at + 1; else high = at;
        }
        return low;
    }
    private void Guard() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; try { _rows.Dispose(); } finally { _lease.Dispose(); } } }
}
