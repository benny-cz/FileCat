using System.Collections.Immutable;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Jobs;

public sealed partial class JobManager
{
    /// <summary>Platform/build availability captured at composition, without native work in command presentation.</summary>
    public StorageProblem? CompressionUnavailable { get; }

    private readonly Dictionary<Job, StorageReviewOwner> _storageOwners = [];

    /// <summary>Published once after terminal retirement, with no live writer, traversal or review pins.</summary>
    public event Action<InterruptedJob>? StorageReviewRequired;

    public InterruptedJob StorageHistoryCandidate(string fileName)
    {
        _ = StorageHistoryPath(fileName);
        return JournalRecovery.StoragePlaceholder(fileName) with { StorageJournalLeaf = fileName };
    }

    private void PublishStorageReview(Job job)
    {
        if (job.Kind != JobKind.StorageTransform || job.StorageResult is not { } result ||
            !(result.ReviewPending || result.PersistenceFault || result.Counts.Uncertain > 0)) return;
        Notify(StorageReviewRequired, StorageHistoryCandidate(job));
    }

    public InterruptedJob StorageHistoryCandidate(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Kind != JobKind.StorageTransform) throw new ArgumentException("Select a storage operation.", nameof(job));
        return StorageHistoryCandidate(StorageJournalPolicy.FileName(job.CreatedUtc, job.Id))
            with { Title = job.Title, CreatedUtc = job.CreatedUtc, SourceCount = job.Request.Sources.Count };
    }

    public StorageHistoryDiscovery OpenStorageHistory(CancellationToken ct = default) => _storageEvidenceBackend is { } backend
        ? JournalRecovery.OpenStorageHistory(_journalDirectory, backend, ct) : JournalRecovery.OpenStorageHistory(_journalDirectory, ct);

    public bool SupportsStorageHistory => _storageEvidenceBackend is not null;

    public StorageHistoryPage ReadStorageHistoryPage(StorageHistoryBookmark? after = null, bool retainedReviews = false,
        CancellationToken ct = default) => JournalRecovery.ReadStorageHistoryPage(_journalDirectory, after, retainedReviews, _storageEvidenceBackend, ct);

    public StorageRetainedReviewSnapshot ReadRetainedStorageReview(string acknowledgmentLeaf, CancellationToken ct = default)
    {
        if (!StorageReviewNames.TryParse(acknowledgmentLeaf, out var association, out var kind, out _, out var temporary) || kind != "ack" || temporary)
            throw new ArgumentException("Select one retained storage acknowledgment from this manager's history.", nameof(acknowledgmentLeaf));
        var backend = _storageEvidenceBackend ?? throw new NotSupportedException("Private storage review is unavailable on this platform.");
        using var evidence = backend.OpenArtifacts(_journalDirectory, ct);
        var catalog = evidence.Artifacts(association, StorageReviewNames.ArtifactCount, ct);
        if (catalog.Count > StorageReviewNames.ArtifactCount) throw new IOException("The bounded retained storage inventory was exceeded.");
        var info = catalog.SingleOrDefault(i => i.Leaf == acknowledgmentLeaf) ?? throw new FileNotFoundException("This retained storage acknowledgment is no longer available.");
        return JournalRecovery.ReadRetainedStorageReview(evidence, info, ct)
            ?? throw new IOException("The retained storage acknowledgment/report pair could not be verified.");
    }

    public StorageHistoryAccounting AccountStorageHistory(CancellationToken ct = default) =>
        StorageEvidenceAccounting.Census(_journalDirectory, null, ct, _storageEvidenceBackend);

    public bool IsInterrupted(InterruptedJob interrupted, CancellationToken ct = default)
    {
        if (!interrupted.IsStorageTransform) return JournalRecovery.IsInterrupted(interrupted);
        if (_storageEvidenceBackend is null) return interrupted.StorageJournalLeaf is not null || JournalRecovery.IsInterrupted(interrupted);
        string leaf = interrupted.StorageJournalLeaf ?? Path.GetFileName(interrupted.JournalPath);
        if (!StoragePaths.IsChildName(leaf) || !StorageJournalPolicy.IsCandidate(leaf) || interrupted.StorageJournalLeaf is null && !string.Equals(Path.GetFullPath(interrupted.JournalPath),
            Path.GetFullPath(Path.Join(_journalDirectory, leaf)), StringComparison.Ordinal)) return true;
        try { return ReadStorageReview(leaf, ct).ReviewPending; }
        catch (IOException busy) when ((busy.HResult & 0xffff) is 32 or 33) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return true; }
    }

    public StorageHistorySnapshot ReadStorageHistory(string fileName, StorageJournalPrefix? prefix = null, CancellationToken ct = default)
    {
        string path = StorageHistoryPath(fileName);
        return _storageEvidenceBackend is { } backend ? JournalRecovery.ReadStorageHistory(path, prefix, backend, ct)
            : JournalRecovery.ReadStorageHistory(path, prefix, ct);
    }

    public StorageReviewStatus ReadStorageReview(string fileName, CancellationToken ct = default)
    {
        _ = StorageHistoryPath(fileName);
        var backend = _storageEvidenceBackend ?? throw new NotSupportedException("Private storage review is unavailable on this platform.");
        using var owner = backend.OpenJournal(_journalDirectory, fileName, ct);
        return JournalRecovery.ReadStorageReviewStatus(owner, ct);
    }

    public StorageCallEvidencePage ReadStorageCalls(string fileName, StorageJournalPrefix prefix, long afterCall = 0,
        CancellationToken ct = default)
    {
        string path = StorageHistoryPath(fileName);
        var backend = _storageEvidenceBackend ?? throw new NotSupportedException("Private storage call evidence is unavailable on this platform.");
        return JournalRecovery.ReadStorageCalls(path, prefix, afterCall, backend, ct);
    }

    /// <summary>Writes the whole verified captured prefix to a caller-owned staging output; never publishes it.</summary>
    public void CopyStorageCalls(string fileName, StorageJournalPrefix prefix, Func<Stream> createOutput, CancellationToken ct = default)
    {
        string path = StorageHistoryPath(fileName);
        var backend = _storageEvidenceBackend ?? throw new NotSupportedException("Private storage call evidence is unavailable on this platform.");
        JournalRecovery.CopyStorageCalls(path, prefix, createOutput, backend, ct);
    }

    /// <summary>Publishes a complete captured prefix through a qualified same-object output owner.</summary>
    public StorageExportResult ExportStorageCalls(string fileName, StorageJournalPrefix prefix, string destination, CancellationToken ct = default)
    {
        string path = StorageHistoryPath(fileName);
        var backend = _storageEvidenceBackend ?? throw new NotSupportedException("Private storage call evidence is unavailable on this platform.");
        return JournalRecovery.ExportStorageCalls(path, prefix, destination, backend, ct);
    }

    public StorageExportResult ExportStorageReview(StorageReviewedJournal reviewed, string destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed); _ = StorageHistoryPath(reviewed.JournalLeaf);
        var backend = _storageEvidenceBackend ?? throw new NotSupportedException("Private storage review is unavailable on this platform.");
        using var owner = backend.OpenJournal(_journalDirectory, reviewed.JournalLeaf, ct);
        return JournalRecovery.ExportStorageReview(owner, reviewed, destination, ct);
    }

    public StorageReviewStatus AcknowledgeStorageReview(StorageReviewedJournal reviewed, string reviewText,
        StorageReviewChoice choice, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed); _ = StorageHistoryPath(reviewed.JournalLeaf);
        var backend = _storageEvidenceBackend ?? throw new NotSupportedException("Private storage review is unavailable on this platform.");
        using var owner = backend.OpenJournal(_journalDirectory, reviewed.JournalLeaf, ct);
        return JournalRecovery.AcknowledgeStorageReview(owner, reviewed, reviewText, choice, ct);
    }

    /// <summary>Streams exact retained evidence for a previously inspected inactive history; no user-file content.</summary>
    public void CopyStorageReview(StorageReviewedJournal reviewed, Stream destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed); _ = StorageHistoryPath(reviewed.JournalLeaf);
        var backend = _storageEvidenceBackend ?? throw new NotSupportedException("Private storage review is unavailable on this platform.");
        using var owner = backend.OpenJournal(_journalDirectory, reviewed.JournalLeaf, ct);
        JournalRecovery.CopyStorageReview(owner, reviewed, destination, ct);
    }

    private string StorageHistoryPath(string fileName)
    {
        if (!StoragePaths.IsChildName(fileName) || !StorageJournalPolicy.IsCandidate(fileName))
            throw new ArgumentException("Select one storage history filename from this manager's journal directory.", nameof(fileName));
        return Path.Join(_journalDirectory, fileName);
    }

    /// <summary>Call on an owned I/O delegate. Cancellation never transfers or retires a still-running delegate's pins.</summary>
    public ReviewedStorageRoots PrepareStorageReview(IReadOnlyList<ItemRef> sources, StorageAction? action, CancellationToken ct) =>
        ReviewedStorageRoots.Prepare(_storageTransforms, sources, action, ct);

    public StorageSelectionReview CaptureStorageSelection(IReadOnlyList<ItemRef> sources) => new(_storageTransforms, sources);

    internal StorageProblem? EfsSelectionUnavailable => _storageTransforms is not IStorageEfsJobAdapter
        ? new(StorageReasonKind.FileCatBoundary, "efs-unavailable", "EFS changes are unavailable in this build.", false)
        : _storageTransforms.Unavailable(StorageAction.DecryptEfs) is null ? null : _storageTransforms.Unavailable(StorageAction.EncryptEfs);

    internal StorageProblem? EfsFileBackupUnavailable => _storageTransforms is IStorageEfsFileBackup
        ? _storageTransforms.Unavailable(StorageAction.DecryptEfs)
        : new(StorageReasonKind.FileCatBoundary, "efs-backup-unavailable", "Backup of selected EFS keys is unavailable in this build.", false);

    internal StorageEfsFileBackupReview PrepareEfsFileBackup(IReadOnlyList<ItemRef> sources, CancellationToken ct) =>
        RequireFileBackup().InspectFiles(StoragePaths.Capture(sources), ct);

    internal StorageEfsFileBackupResult BackUpEfsFileKey(StorageEfsFileBackupReview review, string certificateSha1,
        nint ownerWindow, CancellationToken ct) => RequireFileBackup().BackUpFileKey(review, certificateSha1, ownerWindow, ct);

    private IStorageEfsFileBackup RequireFileBackup() => EfsFileBackupUnavailable is { } unavailable
        ? throw new StorageInspectionException(unavailable) : (IStorageEfsFileBackup)_storageTransforms;

    /// <summary>The registration names the captured origin, or explicitly supplies a detached no-selection disposition.</summary>
    public Job SubmitStorage(ReviewedStorageRoots reviewed, StorageReviewApproval approval, Action<Job> register, QueueMode mode = QueueMode.Start)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentNullException.ThrowIfNull(register);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        reviewed.Validate(_storageTransforms, approval); // action, paths, scope, revision and pins, before transfer/job/journal
        var request = new JobRequest
        {
            Kind = JobKind.StorageTransform, Sources = reviewed.Sources, Mode = mode,
            StorageTransform = new(approval.Action, approval.Scope, approval.ReviewRevision) { Roots = reviewed.Roots, EfsConsent = approval.EfsConsent },
        };
        _ = JobJournal.CaptureStorageHeader(request, Guid.NewGuid(), DateTime.UnixEpoch); // before owner transfer/job creation
        return Submit(request, null, register, storageOwner: reviewed.Transfer(_storageTransforms, approval));
    }

    private void ValidateStorageRequest(JobRequest request, StorageReviewOwner? owner)
    {
        if (request.Kind != JobKind.StorageTransform)
        {
            if (request.StorageTransform is not null || owner is not null) throw new ArgumentException("Storage approval does not match this operation kind.", nameof(request));
            return;
        }
        var payload = request.StorageTransform ?? throw new ArgumentException("A storage approval is required.", nameof(request));
        ReviewedStorageRoots.RequireAction(_storageTransforms, payload.Action);
        ReviewedStorageRoots.RequireScope(_storageTransforms, payload.Action, payload.Scope);
        if (payload.ReviewRevision == Guid.Empty || !Enum.IsDefined(request.Mode))
            throw new ArgumentException("The storage action, scope or review revision is invalid.", nameof(request));
        _ = StoragePaths.Capture(request.Sources);
        if (owner is null || !owner.IsAlive) throw new InvalidOperationException("Submit storage changes through their live reviewed owner.");
        if (payload.Roots.IsDefault || payload.Roots.Length != request.Sources.Count)
            throw new ArgumentException("The original-root mappings are missing.", nameof(request));
        StorageEfsConsent.Require(payload.EfsConsent, payload.ReviewRevision, payload.Action, payload.Scope,
            payload.Roots.Select(r => r.Observation.Identity).ToImmutableArray());
        for (int i = 0; i < payload.Roots.Length; i++)
        {
            var root = payload.Roots[i];
            if (root.SourceIndex != i || root.PinIndex < 0 || root.PinIndex >= owner.Leases.Length ||
                root.Observation.Identity != owner.Leases[root.PinIndex].Observation.Identity || root.Observation.Ineligible is not null)
                throw new ArgumentException("The original-root mapping no longer belongs to its live review.", nameof(request));
        }
    }

    private void ReleaseStorageOwner(Job job)
    {
        StorageReviewOwner? owner;
        lock (_lock) _storageOwners.Remove(job, out owner);
        owner?.Dispose();
    }
}
