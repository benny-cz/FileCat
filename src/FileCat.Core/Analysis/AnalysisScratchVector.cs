using System.Buffers.Binary;

namespace FileCat.Core.Analysis;

/// <summary>
/// Dense, reversible record storage without a managed object per observation. Admitted workers pack at most
/// 512 records/256 KiB per authenticated block. Only the bounded block directory stays in memory.
/// This is a storage primitive, not a materialized AnalysisSnapshot or a source/action capability.
/// </summary>
internal sealed class AnalysisScratchVector : IDisposable
{
    internal const int DirectoryBytes = 16 + 4 * (AnalysisLimits.BatchRecords + 1);
    internal const int MaximumRecordBytes = AnalysisLimits.BatchBytes - DirectoryBytes;
    internal const int DirectoryPage = 1024, MaximumBlocks = 65536;
    internal const uint Format = 0x31565346; // FSV1
    private readonly AnalysisScratchArena _arena;
    private readonly byte[] _buffer = new byte[AnalysisLimits.BatchBytes];
    private readonly List<AnalysisScratchBlock[]> _directory = [];
    private readonly object _gate = new();
    private readonly int _recordLimit, _blockLimit;
    private readonly bool _ownsArena;
    private int _used = DirectoryBytes, _pending, _blocks;
    private ulong _accepted, _committed;
    private bool _failed, _disposed;
    private int _cachedBlock = -1;
    private byte[]? _cache;

    internal AnalysisScratchVector(AnalysisScratchArena arena, int recordLimit = AnalysisLimits.Observations, int blockLimit = MaximumBlocks, bool ownsArena = true)
    {
        ArgumentNullException.ThrowIfNull(arena);
        if (recordLimit is < 1 or > AnalysisLimits.GroupOrderRows) throw new ArgumentOutOfRangeException(nameof(recordLimit));
        if (blockLimit is < 1 or > MaximumBlocks) throw new ArgumentOutOfRangeException(nameof(blockLimit));
        _arena = arena; _recordLimit = recordLimit; _blockLimit = blockLimit; _ownsArena = ownsArena; ResetBuffer();
    }
    internal ulong AcceptedCount { get { lock (_gate) return _accepted; } }
    internal ulong CommittedCount { get { lock (_gate) return _committed; } }
    internal bool Failed { get { lock (_gate) return _failed; } }

    internal ulong Append(ReadOnlySpan<byte> record, CancellationToken cancellation = default)
    {
        if (record.Length > MaximumRecordBytes) throw new ArgumentOutOfRangeException(nameof(record));
        lock (_gate)
        {
            Guard(); cancellation.ThrowIfCancellationRequested();
            if (_accepted >= (ulong)_recordLimit) { _failed = true; throw new IOException("The indexed analysis record limit was reached. Keep the partial result."); }
            if (_pending == AnalysisLimits.BatchRecords || record.Length > _buffer.Length - _used) FlushCore(cancellation);
            record.CopyTo(_buffer.AsSpan(_used)); _used += record.Length; _pending++;
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(16 + _pending * 4), _used);
            return ++_accepted;
        }
    }

    internal void Flush(CancellationToken cancellation = default)
    { lock (_gate) { Guard(); cancellation.ThrowIfCancellationRequested(); FlushCore(cancellation); } }

    private void FlushCore(CancellationToken cancellation)
    {
        if (_pending == 0) return;
        try
        {
            if (_blocks >= _blockLimit) throw new IOException("The indexed analysis block-directory limit was reached. Keep the partial result.");
            // Allocate directory capacity before the native write; no allocation can strand a committed block.
            if (_blocks / DirectoryPage == _directory.Count) _directory.Add(new AnalysisScratchBlock[DirectoryPage]);
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer, Format);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(4), _pending);
            BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(8), _committed + 1);
            var address = _arena.Append(_buffer.AsSpan(0, _used), cancellation);
            _directory[_blocks / DirectoryPage][_blocks % DirectoryPage] = new(_committed + 1, _pending, address);
            _blocks++; _committed += (ulong)_pending; ResetBuffer();
        }
        catch { _failed = true; throw; }
    }
    private void ResetBuffer()
    {
        _buffer.AsSpan(0, DirectoryBytes).Clear(); _used = DirectoryBytes; _pending = 0;
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(16), DirectoryBytes);
    }
    private void Guard()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed) throw new IOException("The indexed analysis store cannot accept more records. Keep its last published partial result.");
    }

    // Needed for parent/witness lookup while constructing a hierarchical store. Only the owning writer
    // sees its bounded pending buffer; external checkpoints still see exactly their published prefix.
    internal byte[] ReadCurrent(ulong id, CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            Guard(); cancellation.ThrowIfCancellationRequested();
            if (id == 0 || id > _accepted) throw new ArgumentOutOfRangeException(nameof(id));
            if (id > _committed) return AnalysisScratchBlockReader.Extract(_buffer, checked((int)(id - _committed - 1)));
            int at = AnalysisScratchBlockReader.Find(_directory, _blocks, id);
            var block = _directory[at / DirectoryPage][at % DirectoryPage];
            if (_cachedBlock != at)
            {
                byte[] bytes = _arena.ReadCommitted(block.Address, cancellation);
                AnalysisScratchBlockReader.Validate(bytes, block); _cache = bytes; _cachedBlock = at;
            }
            return AnalysisScratchBlockReader.Extract(_cache!, checked((int)(id - block.First)));
        }
    }

    // Publication is explicit. This never attempts a failed/pending flush or includes buffered observations.
    internal AnalysisScratchVectorCheckpoint CaptureCommitted()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var publication = CapturePublication(); using var lease = _arena.Capture();
            return publication.Open(lease);
        }
    }

    internal AnalysisScratchVectorPublication CapturePublication()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int pages = (_blocks + DirectoryPage - 1) / DirectoryPage;
            var directory = new AnalysisScratchBlock[pages][];
            for (int i = 0; i < pages; i++)
            {
                int count = Math.Min(DirectoryPage, _blocks - i * DirectoryPage);
                directory[i] = count == DirectoryPage ? _directory[i] : _directory[i].AsSpan(0, count).ToArray();
            }
            long requiredEnd = _blocks == 0 ? 0 : AnalysisScratchArena.RecordEnd(_directory[(_blocks - 1) / DirectoryPage][(_blocks - 1) % DirectoryPage].Address);
            return new(_arena, directory, _blocks, _committed, requiredEnd);
        }
    }

    public void Dispose()
    { lock (_gate) { if (_disposed) return; _disposed = true; _cache = null; _directory.Clear(); if (_ownsArena) _arena.Dispose(); } }
}

internal readonly record struct AnalysisScratchBlock(ulong First, int Count, AnalysisScratchRecord Address);

/// <summary>Immutable directory metadata only. Opening a view requires a retained checkpoint from its owning arena.</summary>
internal sealed class AnalysisScratchVectorPublication(AnalysisScratchArena arena, AnalysisScratchBlock[][] directory, int blocks, ulong count, long requiredEnd)
{
    internal ulong Count => count;
    internal AnalysisScratchVectorCheckpoint Open(AnalysisScratchCheckpoint checkpoint)
    {
        if (!arena.Owns(checkpoint) || checkpoint.End < requiredEnd)
            throw new InvalidDataException("The record table does not belong to this committed scratch revision.");
        var lease = checkpoint.Borrow();
        try { return new(lease, directory, blocks, count); }
        catch { lease.Dispose(); throw; }
    }
}

/// <summary>A bounded immutable directory with globally budgeted block caching, used on admitted workers.</summary>
internal sealed class AnalysisScratchVectorCheckpoint : IDisposable
{
    private readonly AnalysisScratchCheckpoint _lease;
    private AnalysisScratchBlock[][] _directory;
    private readonly int _blocks;
    private readonly object _gate = new();
    private readonly AnalysisScratchReadCache.Owner _cacheOwner = new();
    private bool _disposed;
    internal ulong Count { get; }
    internal AnalysisScratchVectorCheckpoint(AnalysisScratchCheckpoint lease, AnalysisScratchBlock[][] directory, int blocks, ulong count)
    { _lease = lease; _directory = directory; _blocks = blocks; Count = count; }

    internal AnalysisScratchVectorCheckpoint Retain()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var lease = _lease.RetainReader();
            try { return new(lease, _directory, _blocks, Count); }
            catch { lease.Dispose(); throw; }
        }
    }

    internal byte[] Read(ulong id, CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); cancellation.ThrowIfCancellationRequested();
            if (id == 0 || id > Count) throw new ArgumentOutOfRangeException(nameof(id));
            int at = AnalysisScratchBlockReader.Find(_directory, _blocks, id);
            var block = _directory[at / AnalysisScratchVector.DirectoryPage][at % AnalysisScratchVector.DirectoryPage];
            if (!AnalysisScratchReadCache.Shared.TryGet(_cacheOwner, at, out var bytes))
            {
                bytes = _lease.Read(block.Address, cancellation);
                AnalysisScratchBlockReader.Validate(bytes, block); AnalysisScratchReadCache.Shared.Put(_cacheOwner, at, bytes);
            }
            return AnalysisScratchBlockReader.Extract(bytes, checked((int)(id - block.First)));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true; AnalysisScratchReadCache.Shared.RemoveOwner(_cacheOwner); _directory = [];
            _lease.Dispose();
        }
    }
}

internal static class AnalysisScratchBlockReader
{
    internal static int Find(IReadOnlyList<AnalysisScratchBlock[]> directory, int blocks, ulong id)
    {
        int low = 0, high = blocks - 1;
        while (low <= high)
        {
            int at = low + (high - low) / 2; var block = directory[at / AnalysisScratchVector.DirectoryPage][at % AnalysisScratchVector.DirectoryPage];
            if (id < block.First) { high = at - 1; continue; }
            if (id - block.First >= (ulong)block.Count) { low = at + 1; continue; }
            return at;
        }
        throw new InvalidDataException("The indexed analysis directory does not contain this observation.");
    }

    internal static byte[] Extract(byte[] bytes, int row)
    {
        int start = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16 + row * 4));
        int end = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16 + (row + 1) * 4));
        return bytes.AsSpan(start, end - start).ToArray();
    }

    internal static void Validate(byte[] bytes, AnalysisScratchBlock block)
    {
        const int directory = AnalysisScratchVector.DirectoryBytes;
        if (bytes.Length < directory || bytes.Length > AnalysisLimits.BatchBytes || block.Count is < 1 or > AnalysisLimits.BatchRecords ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes) != AnalysisScratchVector.Format ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) != block.Count || BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8)) != block.First)
            throw new InvalidDataException("The indexed analysis block header is invalid.");
        int previous = directory;
        for (int i = 0; i <= block.Count; i++)
        {
            int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16 + i * 4));
            if (offset < previous || offset > bytes.Length || i == 0 && offset != directory || i == block.Count && offset != bytes.Length)
                throw new InvalidDataException("The indexed analysis record extent is invalid.");
            previous = offset;
        }
    }

}
