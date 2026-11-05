using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FileCat.Core.Analysis;

/// <summary>
/// Append-only backing for indexed analysis records, used exclusively by admitted I/O workers.
/// Checkpoints retain the native directory/file and quota after the writer closes. No disk I/O belongs on the UI thread.
/// </summary>
internal sealed class AnalysisScratchArena : IDisposable
{
    private const int HeaderBytes = 16, TagBytes = 32;
    private const uint Format = 0x31534146; // FAS1; private runtime data, not an inventory format.
    private readonly object _gate = new();
    private readonly AnalysisPrivateDirectory _directory;
    private readonly AnalysisPrivateScratchFile _file;
    private readonly AnalysisScratchBudget _budget;
    private readonly long _limit;
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private long _end, _reserved;
    private int _references = 1;
    private bool _ownerClosed, _failed;

    internal AnalysisScratchArena(AnalysisPrivateDirectory directory)
        : this(directory, AnalysisScratchBudget.Shared, AnalysisLimits.ScratchBytes) { }

    // Smaller budgets make failure behavior testable without writing gigabytes. No larger override is allowed.
    internal AnalysisScratchArena(AnalysisPrivateDirectory directory, AnalysisScratchBudget budget, long limit)
    {
        ArgumentNullException.ThrowIfNull(directory); ArgumentNullException.ThrowIfNull(budget);
        if (limit is < 1 or > AnalysisLimits.ScratchBytes) throw new ArgumentOutOfRangeException(nameof(limit));
        _directory = directory; _budget = budget; _limit = limit;
        try { Guard(); _file = directory.CreateScratch(); _file.Verify(); if (_file.Length != 0) throw new IOException("Private scratch was not created empty."); }
        catch { try { _file?.Dispose(); } finally { directory.Dispose(); CryptographicOperations.ZeroMemory(_key); } throw; }
    }

    internal long ReservedBytes { get { lock (_gate) return _reserved; } }
    internal bool Failed { get { lock (_gate) return _failed; } }
    private void Guard()
    {
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        _directory.Verify();
    }

    internal AnalysisScratchRecord Append(ReadOnlySpan<byte> payload, CancellationToken cancellation = default)
    {
        if (payload.Length > AnalysisLimits.RecordBytes) throw new ArgumentOutOfRangeException(nameof(payload));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_ownerClosed, this);
            if (_failed) throw new IOException("Private scratch cannot accept more records. Keep the last published partial result.");
            cancellation.ThrowIfCancellationRequested();
            int bytes = checked(HeaderBytes + payload.Length + TagBytes);
            try
            {
                Guard(); _file.Verify();
                if (_reserved > _limit - bytes || !_budget.TryReserve(bytes))
                    throw new IOException("The private analysis scratch budget was reached. Keep the partial result or choose a narrower scope.");
                // Retain the ENTIRE reservation if a native write partially succeeds or cancellation arrives.
                _reserved += bytes;
                Span<byte> header = stackalloc byte[HeaderBytes];
                BinaryPrimitives.WriteUInt32LittleEndian(header, Format);
                BinaryPrimitives.WriteInt32LittleEndian(header[4..], payload.Length);
                BinaryPrimitives.WriteInt64LittleEndian(header[8..], _end);
                using var mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, _key);
                mac.AppendData(header); mac.AppendData(payload); Span<byte> tag = stackalloc byte[TagBytes]; mac.GetHashAndReset(tag);
                long offset = _end;
                Write(header, offset, cancellation); Write(payload, offset + HeaderBytes, cancellation);
                Write(tag, offset + HeaderBytes + payload.Length, cancellation);
                cancellation.ThrowIfCancellationRequested(); Guard(); _file.Verify();
                _end += bytes;
                return new(offset, payload.Length);
            }
            catch { _failed = true; throw; }
        }
    }

    private void Write(ReadOnlySpan<byte> bytes, long offset, CancellationToken cancellation)
    {
        while (!bytes.IsEmpty)
        {
            cancellation.ThrowIfCancellationRequested(); Guard();
            int take = Math.Min(bytes.Length, 64 * 1024); var space = _directory.Space();
            if (space.Capacity == 0 || space.Available > space.Capacity ||
                space.Available < AnalysisPrivateInventoryStore.RequiredReserve(space.Capacity) + (uint)take)
                throw new IOException("Not enough private scratch space remains above the analysis reserve. Keep the partial result.");
            _file.WriteAt(bytes[..take], offset); offset += take; bytes = bytes[take..];
        }
    }

    internal AnalysisScratchCheckpoint Capture()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_ownerClosed, this);
            // These are short-lived revision owners, not per-record managed metadata.
            if (_references >= AnalysisLimits.Sessions + 1) throw new IOException("The private scratch revision lease limit was reached.");
            _references++; return new(this, _end);
        }
    }

    internal byte[] Read(AnalysisScratchCheckpoint checkpoint, AnalysisScratchRecord record, CancellationToken cancellation)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(checkpoint.Owner, this)) throw new ObjectDisposedException(nameof(checkpoint));
            if (record.Length is < 0 or > AnalysisLimits.RecordBytes || record.Offset < 0 ||
                record.Offset > checkpoint.End || HeaderBytes + (long)record.Length + TagBytes > checkpoint.End - record.Offset)
                throw new InvalidDataException("The scratch record is outside this published revision.");
            cancellation.ThrowIfCancellationRequested(); Guard(); _file.Verify();
            if (_file.Length < checkpoint.End) throw new InvalidDataException("The private scratch revision was truncated.");
            Span<byte> header = stackalloc byte[HeaderBytes]; ReadExactly(header, record.Offset, cancellation);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Format || BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != record.Length ||
                BinaryPrimitives.ReadInt64LittleEndian(header[8..]) != record.Offset)
                throw new InvalidDataException("The private scratch record header changed.");
            byte[] payload = new byte[record.Length]; ReadExactly(payload, record.Offset + HeaderBytes, cancellation);
            Span<byte> stored = stackalloc byte[TagBytes], actual = stackalloc byte[TagBytes];
            ReadExactly(stored, record.Offset + HeaderBytes + record.Length, cancellation);
            using var mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, _key);
            mac.AppendData(header); mac.AppendData(payload); mac.GetHashAndReset(actual);
            if (!CryptographicOperations.FixedTimeEquals(stored, actual)) throw new InvalidDataException("The private scratch record changed.");
            Guard(); _file.Verify(); return payload;
        }
    }

    private void ReadExactly(Span<byte> bytes, long offset, CancellationToken cancellation)
    {
        while (!bytes.IsEmpty)
        {
            cancellation.ThrowIfCancellationRequested();
            int take = Math.Min(bytes.Length, 64 * 1024), read = _file.ReadAt(bytes[..take], offset);
            if (read <= 0 || read > take) throw new InvalidDataException("The private scratch record is incomplete.");
            offset += read; bytes = bytes[read..];
        }
    }

    internal void Release()
    {
        lock (_gate)
        {
            if (--_references != 0) return;
            try { _file.Dispose(); }
            finally
            {
                try { _directory.Dispose(); }
                finally { _budget.Release(_reserved); _reserved = 0; CryptographicOperations.ZeroMemory(_key); }
            }
        }
    }
    public void Dispose()
    {
        lock (_gate) { if (_ownerClosed) return; _ownerClosed = true; Release(); }
    }
}

internal readonly record struct AnalysisScratchRecord(long Offset, int Length);

internal sealed class AnalysisScratchCheckpoint : IDisposable
{
    private AnalysisScratchArena? _owner;
    internal AnalysisScratchArena? Owner => Volatile.Read(ref _owner);
    internal long End { get; }
    internal AnalysisScratchCheckpoint(AnalysisScratchArena owner, long end) { _owner = owner; End = end; }
    internal byte[] Read(AnalysisScratchRecord record, CancellationToken cancellation = default) =>
        (Owner ?? throw new ObjectDisposedException(nameof(AnalysisScratchCheckpoint))).Read(this, record, cancellation);
    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
}

/// <summary>One process-wide ceiling, shared by all analysis profiles, including reader-retained files.</summary>
internal sealed class AnalysisScratchBudget
{
    internal static readonly AnalysisScratchBudget Shared = new(AnalysisLimits.ProfileScratchBytes);
    private readonly long _limit;
    private long _reserved;
    internal AnalysisScratchBudget(long limit)
    {
        if (limit is < 1 or > AnalysisLimits.ProfileScratchBytes) throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit;
    }
    internal long ReservedBytes => Volatile.Read(ref _reserved);
    internal bool TryReserve(long bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        while (true)
        {
            long before = Volatile.Read(ref _reserved);
            if (bytes > _limit - before) return false;
            if (Interlocked.CompareExchange(ref _reserved, before + bytes, before) == before) return true;
        }
    }
    internal void Release(long bytes) => Interlocked.Add(ref _reserved, -bytes);
}
