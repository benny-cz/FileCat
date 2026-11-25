using FileCat.Core.State;

namespace FileCat.Core.Jobs;

public sealed record StorageHistoryAccounting(long EvidenceBytes, long ReservedReviewBytes, long Journals,
    long PendingHistories, long Artifacts, bool Complete, string? Problem)
{
    public long ChargedBytes => EvidenceBytes > long.MaxValue - ReservedReviewBytes ? long.MaxValue : EvidenceBytes + ReservedReviewBytes;
}

public static partial class JournalRecovery
{
    /// <summary>A cancellable streaming census. A page of history is never used as the quota inventory.</summary>
    public static StorageHistoryAccounting AccountStorageHistory(string directory, CancellationToken ct = default) =>
        StorageEvidenceAccounting.Census(directory, null, ct);
}

internal static partial class StorageEvidenceAccounting
{
    internal static StorageHistoryAccounting Census(string directory, IReadOnlySet<Guid>? live, CancellationToken ct, StorageEvidenceBackend? backend = null)
    {
        if (backend is not null) return QualifiedCensus(directory, live, backend, ct);
        if (ProcessAuthority.IsElevatedOrRoot) throw new IOException("Storage history must be accounted in the normal user process.");
        long bytes = 0, reserve = 0, journals = 0, pending = 0, artifacts = 0;
        bool complete = true; string? problem = null;
        using var cursor = StorageEvidenceLanes.EnterCursor(ct);
        try
        {
            // The dedicated cursor is never awaited while holding the evidence envelope. Each journal releases the
            // envelope before the next one, allowing interactive detail reads during a long census.
            foreach (string path in Journals(directory))
            {
                ct.ThrowIfCancellationRequested();
                using var envelope = StorageEvidenceLanes.EnterEnvelope(ct);
                journals++;
                bool named = StorageJournalPolicy.TryJobId(Path.GetFileName(path), out Guid job);
                if (named && HasOtherJournal(directory, path, job, ct))
                    throw new IOException("More than one storage journal claims the same job identity; review is required.");
                if (named && live?.Contains(job) == true) continue; // that owner's whole potential is charged separately
                var before = OrdinaryFile(path);
                bytes = checked(bytes + before.Length);
                bool needsReview = true;
                if (named && before.Length <= StorageJournalPolicy.JournalBytes)
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
                    var read = StorageTransformJournalReader.Read(stream, job, ct: ct);
                    needsReview = read.Collector.ReviewPending;
                }
                var after = OrdinaryFile(path);
                if (before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc || before.CreationTimeUtc != after.CreationTimeUtc)
                    throw new IOException("Storage evidence changed during accounting; check history again.");
                long associated = named ? AssociatedBytes(directory, job, ct) : 0;
                if (needsReview)
                {
                    pending++;
                    reserve = checked(reserve + Math.Max(0, StorageJournalPolicy.ReviewWriteBytes - associated));
                }
                if (!named) { complete = false; problem ??= "A storage journal has an unrecognized identity; review is required."; }
            }
            // Count every private storage artifact, including malformed names and crash temporaries, exactly once.
            // Per-journal association above only reduces its remaining reserved capacity; it adds no physical bytes.
            foreach (string path in Files(Path.Join(directory, "reviewed"), "*"))
            {
                ct.ThrowIfCancellationRequested();
                string name = Path.GetFileName(path);
                if (!StorageJournalPolicy.IsReviewArtifact(name)) continue;
                using var envelope = StorageEvidenceLanes.EnterEnvelope(ct);
                bool named = StorageJournalPolicy.TryArtifactJobId(name, out Guid job);
                artifacts++; bytes = checked(bytes + OrdinaryFile(path).Length);
                if (!named || !HasJournal(directory, job, ct))
                { complete = false; problem ??= "Unassociated storage review artifacts require reconciliation."; }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OverflowException)
        { complete = false; problem ??= "Storage history accounting could not finish: " + error.Message; }
        return new(bytes, reserve, journals, pending, artifacts, complete, problem);
    }

    private static IEnumerable<string> Files(string directory, string pattern)
    {
        IEnumerator<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(directory, pattern).GetEnumerator(); }
        catch (DirectoryNotFoundException) { yield break; }
        using (entries)
            while (entries.MoveNext()) yield return entries.Current;
    }
    private static IEnumerable<string> Journals(string directory) => Files(directory, "job-*").Where(StorageJournalPolicy.IsCandidate);
    private static FileInfo OrdinaryFile(string path)
    {
        var file = new FileInfo(path); file.Refresh();
        if (!file.Exists || file.Length < 0 || (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || file.LinkTarget is not null)
            throw new IOException("Storage evidence is missing or is not an ordinary file.");
        return file;
    }
    private static long AssociatedBytes(string directory, Guid job, CancellationToken ct)
    {
        long bytes = 0;
        foreach (string path in Files(Path.Join(directory, "reviewed"), "*"))
        {
            ct.ThrowIfCancellationRequested();
            if (StorageJournalPolicy.TryArtifactJobId(Path.GetFileName(path), out var owner) && owner == job)
                bytes = checked(bytes + OrdinaryFile(path).Length);
        }
        return Math.Min(bytes, StorageJournalPolicy.ReviewWriteBytes);
    }
    private static bool HasJournal(string directory, Guid job, CancellationToken ct)
    {
        foreach (string path in Journals(directory))
        { ct.ThrowIfCancellationRequested(); if (StorageJournalPolicy.TryJobId(Path.GetFileName(path), out var owner) && owner == job) return true; }
        return false;
    }
    private static bool HasOtherJournal(string directory, string original, Guid job, CancellationToken ct)
    {
        foreach (string path in Journals(directory))
        {
            ct.ThrowIfCancellationRequested();
            if (!path.Equals(original, StringComparison.Ordinal) && StorageJournalPolicy.TryJobId(Path.GetFileName(path), out var owner) && owner == job) return true;
        }
        return false;
    }
}

/// <summary>Observed process-local quota, rechecked across every directory used by this process before admission.
/// No reservation is released until its native worker and writer have actually returned and closed.</summary>
internal sealed class StorageEvidenceBudget
{
    internal static readonly StorageEvidenceBudget Shared = new();
    private readonly SemaphoreSlim _admission = new(1, 1);
    private readonly object _lock = new();
    private readonly Dictionary<string, StorageEvidenceBackend?> _directories = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _live = [];
    private readonly Func<string, IReadOnlySet<Guid>, CancellationToken, StorageHistoryAccounting>? _census;
    private readonly long _limit, _potential;
    private long _generation;

    internal StorageEvidenceBudget(long limit = StorageJournalPolicy.ProcessBytes,
        long potential = StorageJournalPolicy.JournalBytes + StorageJournalPolicy.ReviewWriteBytes,
        Func<string, IReadOnlySet<Guid>, CancellationToken, StorageHistoryAccounting>? census = null)
    {
        if (limit < 0 || potential <= 0 || potential > limit) throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit; _potential = potential; _census = census;
    }

    internal IDisposable Reserve(string directory, Guid job, CancellationToken ct, StorageEvidenceBackend? backend = null)
    {
        if (job == Guid.Empty) throw new ArgumentException("A storage reservation needs its job identity.", nameof(job));
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        _admission.Wait(ct);
        try
        {
            lock (_lock)
            {
                if (!_directories.TryGetValue(directory, out var previous) || previous is null && backend is not null)
                { _directories[directory] = backend; _generation++; }
            }
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                KeyValuePair<string, StorageEvidenceBackend?>[] directories; Dictionary<Guid, string> live; long generation;
                lock (_lock)
                {
                    if (_live.ContainsKey(job)) throw new InvalidOperationException("This storage job already owns its evidence reservation.");
                    directories = _directories.ToArray(); live = new(_live); generation = _generation;
                }
                long charge = checked((live.Count + 1L) * _potential);
                foreach (var entry in directories)
                {
                    string root = entry.Key;
                    var ids = live.Where(p => p.Value == root).Select(p => p.Key).ToHashSet();
                    if (entry.Value is { } nativeBackend) StorageEvidenceRetention.ApplyPolicy(root, nativeBackend, ids, ct);
                    var account = _census is null ? StorageEvidenceAccounting.Census(root, ids, ct, entry.Value) : _census(root, ids, ct);
                    if (!account.Complete) throw new IOException(account.Problem ?? "Storage history accounting is still incomplete.");
                    charge = checked(charge + account.ChargedBytes);
                }
                if (charge > _limit)
                {
                    bool pruned = false;
                    foreach (var entry in directories)
                    {
                        if (entry.Value is null) continue;
                        var ids = live.Where(p => p.Value == entry.Key).Select(p => p.Key).ToHashSet();
                        if (StorageEvidenceRetention.PruneOldest(entry.Key, entry.Value, ids, ct)) { pruned = true; break; }
                    }
                    if (pruned) continue; // actual removal is followed by a fresh full census; no assumed capacity credit
                }
                lock (_lock)
                {
                    // A worker may retire without waiting for this lane. Its previously excluded file must then be
                    // accounted afresh rather than disappearing between the census and this reservation.
                    if (_generation != generation) continue;
                    if (charge > _limit) throw new StorageEvidenceLimitException();
                    _live.Add(job, directory); _generation++;
                    return new Reservation(this, job);
                }
            }
        }
        finally { _admission.Release(); }
    }

    private void Retire(Guid job) { lock (_lock) { if (_live.Remove(job)) _generation++; } }
    private sealed class Reservation(StorageEvidenceBudget budget, Guid job) : IDisposable
    {
        private StorageEvidenceBudget? _budget = budget;
        public void Dispose() => Interlocked.Exchange(ref _budget, null)?.Retire(job);
    }
}

internal sealed class StorageEvidenceLimitException() : IOException(
    "The storage-history allowance is full. Review retained storage changes before starting another transform.");
