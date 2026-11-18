using System.Collections.Immutable;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

public enum StorageCallDisposition { Observed, NotAttempted }
public enum StorageNoCallReason { Canceled, Paused, StateChanged, PreparationFailed }

internal sealed record StorageJournalRoot(int SourceIndex, int PinIndex, string Path, StorageObservation Observation);
internal sealed record StorageJournalHeader(Guid JobId, DateTime CreatedUtc, StorageAction Action, StorageScope Scope,
    Guid ReviewRevision, ImmutableArray<StorageJournalRoot> Roots)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StorageEfsConsent? EfsConsent { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequiresEfsRecoveryEvidence { get; init; }
}
internal sealed record StorageJournalIntent(long Call, int SourceIndex, string Path, StorageObjectIdentity Identity,
    StorageObservation Before, StorageObservationState Desired)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? EfsConsentId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StorageEfsReviewedKey? EfsReviewedKey { get; init; }
}
internal sealed record StorageJournalOutcome(long Call, StorageCallDisposition Disposition, StorageNoCallReason? NoCallReason,
    StorageMutationResult? Result);
internal sealed record StorageJournalCounts(long Changed, long AlreadySatisfied, long Skipped, long Failed, long Uncertain, long NotProcessed)
{
    internal static StorageJournalCounts From(StorageOutcomeCounts c) => new(c.Changed, c.AlreadySatisfied, c.Skipped, c.Failed, c.Uncertain, c.NotProcessed);
    internal StorageOutcomeCounts ToCounts() => new(Changed, AlreadySatisfied, Skipped, Failed, Uncertain, NotProcessed);
}
internal sealed record StorageJournalRootSummary(int SourceIndex, StorageJournalCounts Counts, StorageCompleteness Completeness, bool Changed);
internal sealed record StorageJournalSummary(StorageJournalCounts Counts, ImmutableArray<StorageRootResult> Roots,
    ImmutableArray<StorageReasonCount> Reasons, StorageCompleteness Discovery, bool Canceled, bool PersistenceFault,
    bool ReviewPending, bool ExecutionRefused)
{
    public ImmutableArray<StorageOutcomeExample> Examples { get; init; } = [];
    public long OmittedExamples { get; init; }
    public long AmbiguousDuplicates { get; init; }
    public bool ObservationCounts { get; init; }
}
internal sealed record StorageJournalTerminal(JobState State);

/// <summary>A stable, published durable prefix. Reading it never requires the live writer's lock.</summary>
public sealed record StorageJournalPrefix(Guid JobId, long Sequence, long ByteLength, string Sha256);

internal static class StorageJournalPolicy
{
    internal const int Version = 1, RecordBytes = 256 * 1024, RootSummaryBytes = 64 * 1024, RecordLimit = 131072;
    internal const int EfsVersion = 2, EfsRecoveryVersion = 3;
    internal static bool IsEfs(StorageAction action) => action is StorageAction.EncryptEfs or StorageAction.DecryptEfs;
    internal static int VersionFor(StorageAction action) => IsEfs(action) ? EfsVersion : Version;
    internal static int VersionFor(StorageJournalHeader header) => header.RequiresEfsRecoveryEvidence ? EfsRecoveryVersion : VersionFor(header.Action);
    internal static StorageObservationState Desired(StorageAction action) => action is StorageAction.Compress or StorageAction.EncryptEfs
        ? StorageObservationState.Enabled : StorageObservationState.Disabled;
    internal static StorageObservationState State(StorageAction action, StorageObservation observation) => IsEfs(action) ? observation.Efs : observation.Compression;
    internal const long JournalBytes = 64L * 1024 * 1024, ClosureBytes = 1024 * 1024, CallBytes = 1024 * 1024;
    internal const int ClosureRecords = 4, CallRecords = 4;
    internal const long ReviewBytes = 1024 * 1024, ReviewWriteBytes = 2 * ReviewBytes,
        ProcessBytes = 256L * 1024 * 1024;
    internal const string NamePrefix = "job-storage-";
    internal static bool IsCandidate(string path) => Path.GetFileName(path).StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase);
    internal static string FileName(DateTime created, Guid id) => $"{NamePrefix}{created:yyyyMMddHHmmss}-{id:N}.fcj";

    internal static bool TryJobId(string name, out Guid id)
    {
        id = default;
        return name.Length == NamePrefix.Length + 14 + 1 + 32 + 4 && name.StartsWith(NamePrefix, StringComparison.Ordinal) &&
            name.EndsWith(".fcj", StringComparison.Ordinal) && name[NamePrefix.Length + 14] == '-' &&
            DateTime.TryParseExact(name.AsSpan(NamePrefix.Length, 14), "yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _) &&
            Guid.TryParseExact(name.AsSpan(NamePrefix.Length + 15, 32), "N", out id) && id != Guid.Empty;
    }

    internal static bool IsReviewArtifact(string name) => name.StartsWith("storage-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(".storage-", StringComparison.OrdinalIgnoreCase);
    internal static bool TryArtifactJobId(string name, out Guid id)
    {
        id = default;
        string prefix = name.StartsWith(".storage-", StringComparison.Ordinal) ? ".storage-" : "storage-";
        // All storage report/acknowledgment temporaries carry the job first, before their kind and nonce.
        return name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length + 33 &&
            name[prefix.Length + 32] == '-' && Guid.TryParseExact(name.AsSpan(prefix.Length, 32), "N", out id) && id != Guid.Empty;
    }
}
