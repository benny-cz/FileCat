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
        catch
        {
            try
            {
                try { if (_file is not null) AnalysisIo.CloseOnWorker(_file); }
                catch (Exception error) { _budget.Quarantine(this, error); throw; }
                AnalysisIo.CloseOnWorker(directory);
            }
            finally { CryptographicOperations.ZeroMemory(_key); }
            throw;
        }
    }

    internal long ReservedBytes { get { lock (_gate) return _reserved; } }
    internal long CommittedEnd { get { lock (_gate) return _end; } }
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
            return CaptureCore(_end);
        }
    }

    internal AnalysisScratchCheckpoint CapturePublished(long end)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_ownerClosed, this);
            if (end < 0 || end > _end) throw new InvalidDataException("The private scratch publication exceeds its committed extent.");
            return CaptureCore(end);
        }
    }

    internal AnalysisScratchCheckpoint RetainReader(AnalysisScratchCheckpoint source)
    {
        lock (_gate)
        {
            // A reader may retain its old revision after the writer closes; a disposed source cannot.
            if (!Owns(source)) throw new ObjectDisposedException(nameof(source));
            return CaptureCore(source.End);
        }
    }

    private AnalysisScratchCheckpoint CaptureCore(long end)
    {
        if (_references >= AnalysisLimits.SnapshotConsumers + (_ownerClosed ? 0 : 1))
            throw new IOException("The private scratch revision lease limit was reached.");
        // Allocate before claiming the native lifetime reservation, including allocation failure.
        var result = new AnalysisScratchCheckpoint(this, end); _references++; return result;
    }

    internal bool Owns(AnalysisScratchCheckpoint checkpoint) => ReferenceEquals(checkpoint.Owner, this);
    internal static long RecordEnd(AnalysisScratchRecord record) => checked(record.Offset + HeaderBytes + record.Length + TagBytes);

    internal byte[] Read(AnalysisScratchCheckpoint checkpoint, AnalysisScratchRecord record, CancellationToken cancellation)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(checkpoint.Owner, this)) throw new ObjectDisposedException(nameof(checkpoint));
            return ReadCore(checkpoint.End, record, cancellation);
        }
    }

    // A writer can inspect its already committed blocks without consuming an external revision lease.
    // Its ownership and this lock retain the file throughout real I/O; no unpublished tail is readable.
    internal byte[] ReadCommitted(AnalysisScratchRecord record, CancellationToken cancellation)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_ownerClosed, this);
            return ReadCore(_end, record, cancellation);
        }
    }

    private byte[] ReadCore(long end, AnalysisScratchRecord record, CancellationToken cancellation)
    {
        if (record.Length is < 0 or > AnalysisLimits.RecordBytes || record.Offset < 0 ||
            record.Offset > end || HeaderBytes + (long)record.Length + TagBytes > end - record.Offset)
            throw new InvalidDataException("The scratch record is outside this published revision.");
        cancellation.ThrowIfCancellationRequested(); Guard(); _file.Verify();
        if (_file.Length < end) throw new InvalidDataException("The private scratch revision was truncated.");
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
            try
            {
                try { AnalysisIo.CloseOnWorker(_file); }
                catch (Exception error) { _failed = true; _budget.Quarantine(this, error); throw; }
                try { _budget.Release(_reserved); }
                catch (Exception error) { _failed = true; _budget.Quarantine(this, error); throw; }
                _reserved = 0;
                AnalysisIo.CloseOnWorker(_directory);
            }
            finally { CryptographicOperations.ZeroMemory(_key); }
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
    private sealed class Shared(AnalysisScratchArena owner)
    {
        internal readonly AnalysisScratchArena Owner = owner;
        internal int References = 1;
    }
    private Shared? _shared;
    internal AnalysisScratchArena? Owner => Volatile.Read(ref _shared)?.Owner;
    internal long End { get; }
    internal AnalysisScratchCheckpoint(AnalysisScratchArena owner, long end) { _shared = new(owner); End = end; }
    private AnalysisScratchCheckpoint(Shared shared, long end) { _shared = shared; End = end; }

    // Component views borrow one outer consumer's lifetime. They do not bypass the consumer limit:
    // creating another independently retained revision uses RetainReader instead.
    internal AnalysisScratchCheckpoint Borrow()
    {
        var shared = Volatile.Read(ref _shared) ?? throw new ObjectDisposedException(nameof(AnalysisScratchCheckpoint));
        lock (shared)
        {
            if (!ReferenceEquals(_shared, shared)) throw new ObjectDisposedException(nameof(AnalysisScratchCheckpoint));
            if (shared.References >= AnalysisLimits.Components + 1) throw new IOException("The scratch revision component limit was reached.");
            var result = new AnalysisScratchCheckpoint(shared, End); shared.References++; return result;
        }
    }
    internal AnalysisScratchCheckpoint RetainReader() =>
        (Owner ?? throw new ObjectDisposedException(nameof(AnalysisScratchCheckpoint))).RetainReader(this);
    internal byte[] Read(AnalysisScratchRecord record, CancellationToken cancellation = default) =>
        (Owner ?? throw new ObjectDisposedException(nameof(AnalysisScratchCheckpoint))).Read(this, record, cancellation);
    public void Dispose()
    {
        var shared = Interlocked.Exchange(ref _shared, null);
        if (shared is null) return;
        bool release; lock (shared) release = --shared.References == 0;
        if (release) shared.Owner.Release();
    }
}

/// <summary>One process-wide ceiling, shared by all analysis profiles, including reader-retained files.</summary>
internal sealed class AnalysisScratchBudget
{
    internal static readonly AnalysisScratchBudget Shared = new(AnalysisLimits.ProfileScratchBytes);
    private readonly object _gate = new();
    private readonly long _limit;
    private readonly AnalysisScratchBudget? _parent;
    private readonly AnalysisProfileQuota? _quota;
    private readonly AnalysisResourceQuarantine _quarantine;
    private Exception? _admissionFailure, _releaseFailure;
    private long _reserved;
    internal AnalysisScratchBudget(long limit, AnalysisScratchBudget? parent = null, AnalysisProfileQuota? quota = null, AnalysisResourceQuarantine? quarantine = null)
    {
        if (limit is < 1 or > AnalysisLimits.ProfileScratchBytes || parent is not null && limit > parent._limit) throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit; _parent = parent; _quota = quota; _quarantine = quarantine ?? AnalysisResourceQuarantine.Shared;
    }
    internal void CheckAdmission()
    {
        lock (_gate)
        {
            if (_admissionFailure is { } error) throw AnalysisCloseUnconfirmedException.From(error);
            _parent?.CheckAdmission();
        }
    }
    internal void Quarantine(object owner, Exception error)
    {
        lock (_gate) _admissionFailure ??= error;
        _quarantine.Hold(owner);
    }
    internal long ReservedBytes => Volatile.Read(ref _reserved);
    internal bool TryReserve(long bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        lock (_gate)
        {
            CheckAdmission();
            if (bytes > _limit - _reserved || _parent is not null && !_parent.TryReserve(bytes)) return false;
            bool acquired;
            try { acquired = _quota is null || _quota.TryReserve(bytes); }
            catch { _parent?.Release(bytes); throw; }
            if (!acquired) { _parent?.Release(bytes); return false; }
            _reserved += bytes; return true;
        }
    }
    internal void Release(long bytes)
    {
        lock (_gate)
        {
            if (bytes < 0 || bytes > _reserved) throw new InvalidOperationException("The private scratch reservation does not belong to this budget.");
            if (_releaseFailure is { } prior) throw AnalysisCloseUnconfirmedException.From(prior);
            try
            {
                _quota?.Release(bytes);
                _parent?.Release(bytes);
                _reserved -= bytes;
            }
            catch (Exception error)
            {
                _releaseFailure = error; Quarantine(this, error);
                throw AnalysisCloseUnconfirmedException.From(error);
            }
        }
    }
}
