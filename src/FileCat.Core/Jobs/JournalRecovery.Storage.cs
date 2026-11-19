using System.Collections.Immutable;
using FileCat.Core.FileSystem;
using FileCat.Core.State;

namespace FileCat.Core.Jobs;

public sealed record StorageHistoryRoot(int SourceIndex, int PinIndex, string Path, StorageObservation Observation);
public sealed record StorageHistoryCall(long Call, int SourceIndex, string Path, StorageObjectIdentity Identity,
    StorageObservation Before, StorageObservationState Desired, StorageCallDisposition? Disposition, StorageNoCallReason? NoCallReason,
    StorageMutationResult? Result)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? EfsConsentId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StorageEfsReviewedKey? EfsReviewedKey { get; init; }
}
public sealed record StorageHistorySnapshot(StorageJournalPrefix Prefix, ImmutableArray<StorageHistoryRoot> Roots,
    StorageAction? Action, StorageScope? Scope, Guid? ReviewRevision, bool EffectUncertain, bool ReviewPending,
    bool Damaged, bool Ended, bool LimitReached, StorageResultSnapshot? Result, StorageHistoryCall? LastCall)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StorageEfsConsent? EfsConsent { get; init; }
    internal int? JournalSchema { get; init; }

    // Versions one and two already round-trip through the action. Keep their retained JSON shape so previously
    // acknowledged reports still match an extant journal. Encrypt can use versions two or three.
    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonPropertyName("journalSchema")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    internal int? RetainedJournalSchema
    {
        get => JournalSchema == StorageJournalPolicy.EfsRecoveryVersion ? JournalSchema : null;
        init => JournalSchema = value;
    }
}
public sealed record StorageHistoryRow(string FileName, Guid? JobId, StorageAction? Action, StorageScope? Scope,
    int? RootCount, string Status)
{
    public bool RetainedReview { get; init; }
}
/// <summary>Detached continuation only. It owns no cursor, evidence stream or mutation authority.</summary>
public sealed record StorageHistoryBookmark(string? DirectoryIdentity, string FileName, string? EntryIdentity);
public sealed record StorageHistoryPage(ImmutableArray<StorageHistoryRow> Rows, bool More, int Examined, int HeaderBytes)
{
    public StorageHistoryBookmark? Continuation { get; init; }
    public int ReplayedEntries { get; init; }
}

public static partial class JournalRecovery
{
    internal static InterruptedJob StoragePlaceholder(string path) => new(path, nameof(JobKind.StorageTransform),
        "Storage changes — history verification required", DateTime.MinValue, [], null, [], [], 0, 0);

    public static StorageHistorySnapshot ReadStorageHistory(string path, StorageJournalPrefix? prefix = null, CancellationToken ct = default)
    {
        if (WindowsTokenAuthority.IsHighIntegrity) throw new IOException("Storage history must be reviewed in the normal user process.");
        using var envelope = StorageEvidenceLanes.EnterEnvelope(ct);
        using var fileCharge = envelope.Claim();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        Guid expected = StorageJournalPolicy.TryJobId(Path.GetFileName(path), out var named) ? named : Guid.Empty;
        var read = StorageTransformJournalReader.Read(stream, prefix?.JobId ?? expected, prefix?.ByteLength, prefix?.Sha256, ct);
        if (expected == Guid.Empty || read.Collector.Header?.JobId != expected) read.Collector.Fault();
        return DescribeStorageHistory(read);
    }

    internal static StorageHistorySnapshot ReadStorageHistory(string path, StorageJournalPrefix? prefix, StorageEvidenceBackend backend, CancellationToken ct)
    {
        using var evidence = (backend.OpenHistory ?? throw new IOException("The bound storage history reader is unavailable."))
            (Path.GetDirectoryName(path) ?? throw new IOException("Missing storage history directory."), Path.GetFileName(path), ct);
        Guid expected = StorageJournalPolicy.TryJobId(Path.GetFileName(path), out var named) ? named : Guid.Empty;
        var read = StorageTransformJournalReader.Read(evidence.Stream, prefix?.JobId ?? expected, prefix?.ByteLength, prefix?.Sha256, ct);
        if (expected == Guid.Empty || read.Collector.Header?.JobId != expected) read.Collector.Fault();
        return DescribeStorageHistory(read);
    }

    internal static StorageHistorySnapshot DescribeStorageHistory(StorageJournalReadResult read)
    {
        var c = read.Collector;
        var intent = c.LastIntent;
        var outcome = c.LastOutcome?.Call == intent?.Call ? c.LastOutcome : null;
        return new(read.Prefix, c.Header?.Roots.Select(r => new StorageHistoryRoot(r.SourceIndex, r.PinIndex, r.Path, r.Observation)).ToImmutableArray() ?? [],
            c.Header?.Action, c.Header?.Scope, c.Header?.ReviewRevision, c.EffectUncertain, c.ReviewPending, c.Damaged, c.Ended,
            read.LimitReached, c.Summary is { } summary ? c.ToSnapshot(summary) with { DurablePrefix = read.Prefix } : null,
            intent is null ? null : new(intent.Call, intent.SourceIndex, intent.Path, intent.Identity, intent.Before, intent.Desired,
                outcome?.Disposition, outcome?.NoCallReason, outcome?.Result)
                { EfsConsentId = intent.EfsConsentId, EfsReviewedKey = intent.EfsReviewedKey })
            { EfsConsent = c.Header?.EfsConsent, JournalSchema = c.JournalSchema };
    }

    internal static bool HasStorageRecords(string path)
    {
        if (StorageJournalPolicy.IsCandidate(path)) return true;
        try
        {
            if (new FileInfo(path).Length > StorageJournalPolicy.JournalBytes) return true;
            int seen = 0;
            foreach (var record in JobJournal.ReadRecords(path))
            {
                if (++seen > StorageJournalPolicy.RecordLimit || record.Type.StartsWith("storage-", StringComparison.Ordinal) ||
                    record.Type == "begin" && record.Get("kind") == nameof(JobKind.StorageTransform)) return true;
            }
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return true; }
    }

    public static StorageHistoryDiscovery OpenStorageHistory(string directory, CancellationToken ct = default) => new(directory, ct);
    internal static StorageHistoryDiscovery OpenStorageHistory(string directory, StorageEvidenceBackend backend, CancellationToken ct) => new(directory, ct, backend);

    public static StorageHistoryPage ReadStorageHistoryPage(string directory, StorageHistoryBookmark? after = null,
        bool retainedReviews = false, CancellationToken ct = default) => ReadStorageHistoryPage(directory, after, retainedReviews, null, ct);

    internal static StorageHistoryPage ReadStorageHistoryPage(string directory, StorageHistoryBookmark? after,
        bool retainedReviews, StorageEvidenceBackend? backend, CancellationToken ct)
    {
        using var discovery = new StorageHistoryDiscovery(directory, ct, backend, retainedReviews);
        int replayed = after is null ? 0 : discovery.ResumeAfter(after, ct);
        return discovery.ReadPage(ct) with { ReplayedEntries = replayed };
    }
}

/// <summary>One bounded sequential storage page; no full-directory sorting or retained candidate array.</summary>
public sealed class StorageHistoryDiscovery : IDisposable
{
    private IEnumerator<string>? _entries;
    private IDisposable? _cursorCharge;
    private StorageEvidenceCursor? _nativeCursor;
    private readonly StorageEvidenceBackend? _backend;
    private readonly string _directory;
    private readonly bool _retainedReviews;
    private IReadOnlyList<StorageReviewArtifactInfo> _batch = [];
    private int _batchIndex;
    private bool _complete, _disposed;
    private StorageHistoryBookmark? _continuation;
    internal StorageHistoryDiscovery(string directory, CancellationToken ct, StorageEvidenceBackend? backend = null, bool retainedReviews = false)
    {
        if (WindowsTokenAuthority.IsHighIntegrity) throw new IOException("Storage history must be reviewed in the normal user process.");
        _backend = backend; _retainedReviews = retainedReviews;
        _directory = backend is null && retainedReviews ? Path.Join(directory, "reviewed") : directory;
        if (backend is not null)
        {
            try { _nativeCursor = backend.OpenCursor(directory, retainedReviews, ct); }
            catch (FileNotFoundException) { _complete = true; }
            return;
        }
        _cursorCharge = StorageEvidenceLanes.EnterCursor(ct);
        try { _entries = Directory.Exists(_directory) ? Directory.EnumerateFileSystemEntries(_directory,
            retainedReviews ? "storage-*" : "job-*").Where(p => retainedReviews || StorageJournalPolicy.IsCandidate(p)).GetEnumerator() : Enumerable.Empty<string>().GetEnumerator(); }
        catch { _cursorCharge.Dispose(); _cursorCharge = null; throw; }
    }
    public StorageHistoryPage ReadPage(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rows = ImmutableArray.CreateBuilder<StorageHistoryRow>();
        int examined = 0, headerBytes = 0;
        using var envelope = _backend is null ? StorageEvidenceLanes.EnterEnvelope(ct) : null; // native factory owns its envelope
        while (!_complete && examined < 64 && headerBytes <= 1024 * 1024 - StorageJournalPolicy.RecordBytes)
        {
            ct.ThrowIfCancellationRequested();
            if (!MoveNext(ct, out var path, out var entry)) { _complete = true; break; }
            examined++;
            string name = Path.GetFileName(path);
            _continuation = new(_nativeCursor?.DirectoryIdentity, name, entry?.Identity);
            if (_retainedReviews)
            {
                if (StorageReviewNames.TryParse(name, out var association, out var kind, out _, out var temporary) && kind == "ack" && !temporary)
                    rows.Add(new(name, association, null, null, null, "Retained storage review — verification required") { RetainedReview = true });
                continue;
            }
            if (!StorageJournalPolicy.IsCandidate(name)) continue;
            StorageJournalHeader? header = null; bool damaged = false;
            try
            {
                using var fileCharge = envelope?.Claim();
                using var native = _backend is null ? null : (_backend.OpenHistory ?? throw new IOException("The bound storage header reader is unavailable."))(_directory, name, ct);
                if (native is not null)
                {
                    if (native.DirectoryIdentity != _nativeCursor!.DirectoryIdentity || native.Identity != entry!.Identity || native.Stream.Length != entry.ByteLength)
                        throw new IOException("The discovered storage header changed its native identity or length.");
                }
                else
                {
                    var info = new FileInfo(path);
                    if ((info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 || info.LinkTarget is not null)
                        throw new IOException("Storage history is not an ordinary file.");
                }
                using var portable = native is null ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096) : null;
                var stream = native?.Stream ?? portable!;
                using var record = new MemoryStream();
                for (int value; (value = stream.ReadByte()) != -1;)
                {
                    if ((record.Length & 4095) == 0) ct.ThrowIfCancellationRequested();
                    if (record.Length >= StorageJournalPolicy.RecordBytes) { damaged = true; break; }
                    record.WriteByte((byte)value); headerBytes++;
                    if (value == '\n') break;
                }
                if (!damaged)
                {
                    using var document = StorageTransformJournalWriter.Decode(record.GetBuffer().AsSpan(0, (int)record.Length));
                    var collector = new StorageTransformJournalCollector(); collector.Read(document.RootElement);
                    header = collector.Header; damaged = collector.Damaged || header is null ||
                        !StorageJournalPolicy.TryJobId(name, out var named) || header.JobId != named;
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { damaged = true; }
            // Exact roots and history are loaded on demand. This descriptor stays far below 4 KiB.
            rows.Add(new(name.Length <= 256 ? name : "Invalid storage journal name", header?.JobId, header?.Action, header?.Scope,
                header?.Roots.Length, damaged ? "Unreadable or damaged storage header — review required" : "History accounting/verification still in progress"));
        }
        return new(rows.ToImmutable(), !_complete, examined, headerBytes) { Continuation = _continuation };
    }
    internal int ResumeAfter(StorageHistoryBookmark after, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(after);
        if (!StoragePaths.IsChildName(after.FileName) || after.FileName.Length > 255 ||
            _nativeCursor is not null && _nativeCursor.DirectoryIdentity != after.DirectoryIdentity)
            throw new IOException("The storage history namespace changed; refresh history to start a new discovery.");
        // Re-enumerate names only, without opening old headers or retaining a candidate array. New header work is
        // still limited to 64 candidates/1 MiB. Replay is cancellable and has a separate finite observation bound.
        for (int replayed = 1; replayed <= 1_000_000; replayed++)
        {
            ct.ThrowIfCancellationRequested();
            if (_complete || !MoveNext(ct, out var path, out var entry)) break;
            if (Path.GetFileName(path) != after.FileName) continue;
            if (_nativeCursor is not null && entry?.Identity != after.EntryIdentity)
                throw new IOException("The storage history continuation object changed; refresh history.");
            return replayed;
        }
        throw new IOException("The storage history continuation is no longer available or exceeded its replay bound; refresh history.");
    }
    private bool MoveNext(CancellationToken ct, out string path, out StorageReviewArtifactInfo? entry)
    {
        entry = null; path = string.Empty;
        if (_backend is null)
        {
            if (!_entries!.MoveNext()) return false;
            path = _entries.Current; return true;
        }
        while (_batchIndex == _batch.Count)
        {
            _batch = _nativeCursor!.ReadBatch(ct); _batchIndex = 0;
            if (_batch.Count == 0) return false;
        }
        entry = _batch[_batchIndex++]; path = Path.Join(_directory, entry.Leaf); return true;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _batch = [];
        try { Interlocked.Exchange(ref _entries, null)?.Dispose(); }
        finally
        {
            try { Interlocked.Exchange(ref _nativeCursor, null)?.Dispose(); }
            finally { Interlocked.Exchange(ref _cursorCharge, null)?.Dispose(); }
        }
    }
}

internal static class StorageEvidenceLanes
{
    private static readonly SemaphoreSlim Cursor = new(1, 1), Envelope = new(1, 1);
    internal static IDisposable EnterCursor(CancellationToken ct) { Cursor.Wait(ct); return new Slot(Cursor); }
    internal static StorageEvidenceEnvelope EnterEnvelope(CancellationToken ct) { Envelope.Wait(ct); return new(Envelope); }
    private sealed class Slot(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}

/// <summary>The independent three-handle envelope retains its lane until every started handle actually closes.</summary>
internal sealed class StorageEvidenceEnvelope(SemaphoreSlim lane) : IDisposable
{
    private readonly object _lock = new();
    private int _handles;
    private int _maximumHandles;
    private bool _retiring, _released;
    internal int Handles { get { lock (_lock) return _handles; } }
    internal int MaximumHandles { get { lock (_lock) return _maximumHandles; } }
    internal IDisposable Claim()
    {
        lock (_lock)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(StorageEvidenceEnvelope));
            if (_handles == 3) throw new IOException("The independent storage evidence handle allowance was reached.");
            _handles++; _maximumHandles = Math.Max(_maximumHandles, _handles); return new HandleSlot(this);
        }
    }
    private void Return()
    {
        bool release;
        lock (_lock) { _handles--; release = _retiring && _handles == 0 && !_released; if (release) _released = true; }
        if (release) lane.Release();
    }
    public void Dispose()
    {
        bool release;
        lock (_lock) { if (_retiring) return; _retiring = true; release = _handles == 0; if (release) _released = true; }
        if (release) lane.Release();
    }
    private sealed class HandleSlot(StorageEvidenceEnvelope envelope) : IDisposable
    {
        private StorageEvidenceEnvelope? _envelope = envelope;
        public void Dispose() => Interlocked.Exchange(ref _envelope, null)?.Return();
    }
}
