namespace FileCat.Core.Analysis;

/// <summary>
/// One scratch owner and publication boundary for related record tables. A failed multi-table flush
/// preserves the last complete publication; it never exposes a half-published graph to a reader.
/// Schema relationships are the typed producer's responsibility. All operations belong on admitted workers.
/// </summary>
internal sealed class AnalysisScratchComposite : IDisposable
{
    internal const int MaximumTables = 16;
    private readonly object _gate = new();
    private readonly AnalysisScratchArena _arena;
    private readonly AnalysisScratchVector[] _tables;
    private AnalysisScratchCompositePublication _published;
    private bool _failed, _disposed;

    // Ownership of the arena transfers on success; the caller retains it on construction failure.
    internal AnalysisScratchComposite(AnalysisScratchArena arena, params int[] recordLimits)
    {
        ArgumentNullException.ThrowIfNull(arena); ArgumentNullException.ThrowIfNull(recordLimits);
        if (recordLimits.Length is < 1 or > MaximumTables || recordLimits.Any(n => n is < 1 or > AnalysisLimits.Observations))
            throw new ArgumentOutOfRangeException(nameof(recordLimits));
        _arena = arena; _tables = new AnalysisScratchVector[recordLimits.Length];
        try
        {
            for (int i = 0; i < _tables.Length; i++) _tables[i] = new(arena, recordLimit: recordLimits[i], ownsArena: false);
            _published = Publication(0);
        }
        catch { foreach (var table in _tables) table?.Dispose(); throw; }
    }

    internal bool Failed { get { lock (_gate) return _failed; } }
    internal ulong Append(int table, ReadOnlySpan<byte> record, CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            GuardWriter(); CheckTable(table);
            try { return _tables[table].Append(record, cancellation); }
            catch (Exception error) when (error is IOException or OperationCanceledException) { _failed = true; throw; }
        }
    }
    internal byte[] ReadCurrent(int table, ulong id, CancellationToken cancellation = default)
    { lock (_gate) { GuardWriter(); CheckTable(table); return _tables[table].ReadCurrent(id, cancellation); } }

    internal void Publish(CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            GuardWriter();
            try
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (var table in _tables) table.Flush(cancellation);
                var candidate = Publication(checked(_published.Generation + 1));
                cancellation.ThrowIfCancellationRequested();
                _published = candidate;
            }
            catch { _failed = true; throw; }
        }
    }
    private AnalysisScratchCompositePublication Publication(long generation) =>
        new(_arena, generation, _arena.CommittedEnd, _tables.Select(t => t.CapturePublication()).ToArray());
    internal AnalysisScratchCompositePublication PublishedToken()
    { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); return _published; } }
    internal AnalysisScratchCompositeRevision Acquire(AnalysisScratchCompositePublication publication)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(publication.Owner, _arena)) throw new InvalidDataException("The publication belongs to another store.");
            return AnalysisScratchCompositeRevision.Open(publication, _arena.CapturePublished(publication.End));
        }
    }

    internal AnalysisScratchCompositeRevision AcquireCommitted()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var lease = _arena.CapturePublished(_published.End);
            return AnalysisScratchCompositeRevision.Open(_published, lease);
        }
    }

    private void GuardWriter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed) throw new IOException("The related analysis tables could not be published together. Keep the last complete partial revision.");
    }
    private void CheckTable(int table)
    { if ((uint)table >= (uint)_tables.Length) throw new ArgumentOutOfRangeException(nameof(table)); }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            try { foreach (var table in _tables) table.Dispose(); }
            finally { _arena.Dispose(); }
        }
    }
}

internal sealed record AnalysisScratchCompositePublication(AnalysisScratchArena Owner, long Generation, long End, AnalysisScratchVectorPublication[] Tables);

/// <summary>One independently admitted consumer, with component views borrowed from that consumer's native lifetime.</summary>
internal sealed class AnalysisScratchCompositeRevision : IDisposable
{
    private readonly object _gate = new();
    private readonly AnalysisScratchCompositePublication _publication;
    private readonly AnalysisScratchCheckpoint _lease;
    private readonly AnalysisScratchVectorCheckpoint[] _tables;
    private bool _disposed;
    private AnalysisScratchCompositeRevision(AnalysisScratchCompositePublication publication, AnalysisScratchCheckpoint lease,
        AnalysisScratchVectorCheckpoint[] tables) { _publication = publication; _lease = lease; _tables = tables; }
    internal long Generation => _publication.Generation;
    internal int TableCount => _tables.Length;

    // Takes ownership of lease, including a component-open failure.
    internal static AnalysisScratchCompositeRevision Open(AnalysisScratchCompositePublication publication, AnalysisScratchCheckpoint lease)
    {
        AnalysisScratchVectorCheckpoint[]? tables = null;
        try
        {
            tables = new AnalysisScratchVectorCheckpoint[publication.Tables.Length];
            for (int i = 0; i < tables.Length; i++) tables[i] = publication.Tables[i].Open(lease);
            return new(publication, lease, tables);
        }
        catch
        {
            if (tables is not null) foreach (var table in tables) table?.Dispose();
            lease.Dispose(); throw;
        }
    }

    internal ulong Count(int table) { lock (_gate) { Guard(table); return _tables[table].Count; } }
    internal byte[] Read(int table, ulong id, CancellationToken cancellation = default)
    { lock (_gate) { Guard(table); return _tables[table].Read(id, cancellation); } }
    internal AnalysisScratchCompositeRevision Retain()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Open(_publication, _lease.RetainReader());
        }
    }
    // A typed owner counts its whole graph consumers separately. Its related table views share this
    // underlying consumer rather than consuming an extra native reader for each graph component.
    internal AnalysisScratchCompositeRevision Borrow()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Open(_publication, _lease.Borrow());
        }
    }
    private void Guard(int table)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)table >= (uint)_tables.Length) throw new ArgumentOutOfRangeException(nameof(table));
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            try { foreach (var table in _tables) table.Dispose(); }
            finally { _lease.Dispose(); }
        }
    }
}
