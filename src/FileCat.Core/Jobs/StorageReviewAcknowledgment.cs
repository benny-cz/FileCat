using System.Collections.Immutable;

namespace FileCat.Core.Jobs;

public enum StorageReviewChoice { AcknowledgeReviewedHistory }

/// <summary>An exact held evidence object, including every byte of a damaged or oversized journal.</summary>
public sealed record StorageEvidenceVersion(string Identity, long ByteLength, string Sha256);

/// <summary>Historical facts and the complete file snapshot that the user reviewed. No mutation authority.</summary>
public sealed record StorageReviewedJournal(Guid AssociationId, string DirectoryIdentity, string JournalLeaf,
    StorageEvidenceVersion Evidence, StorageHistorySnapshot History)
{
    public DateTime? CreatedUtc { get; init; }
}

public sealed record StorageRetainedReviewReport(int Version, Guid ReportId, StorageReviewedJournal Reviewed,
    DateTime ReviewedUtc, string ReviewText);

public sealed record StorageReviewBinding(Guid AssociationId, string DirectoryIdentity, string JournalLeaf,
    StorageEvidenceVersion Evidence, Guid? JobId, long Sequence, int? JournalSchema, bool EffectUncertain,
    bool Damaged, bool LimitReached)
{
    internal static StorageReviewBinding From(StorageReviewedJournal reviewed) => new(reviewed.AssociationId,
        reviewed.DirectoryIdentity, reviewed.JournalLeaf, reviewed.Evidence,
        reviewed.History.Prefix.JobId == Guid.Empty ? null : reviewed.History.Prefix.JobId, reviewed.History.Prefix.Sequence,
        reviewed.History.Action is null ? null : StorageJournalPolicy.Version, reviewed.History.EffectUncertain,
        reviewed.History.Damaged, reviewed.History.LimitReached);
}

public sealed record StorageReviewAcknowledgment(int Version, Guid AcknowledgmentId, Guid ReportId,
    StorageReviewChoice Choice, StorageReviewBinding Reviewed, string ReportLeaf,
    StorageEvidenceVersion ReportEvidence, DateTime AcknowledgedUtc)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string FileName => StorageReviewNames.Published(Reviewed.AssociationId, "ack", AcknowledgmentId);
}

public sealed record StorageReviewStatus(StorageReviewedJournal Current, StorageReviewAcknowledgment? Acknowledgment,
    bool ReviewPending, ImmutableArray<string> Issues)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? RetainedAcknowledgmentLeaf => Acknowledgment is { } ack
        ? ack.FileName : null;
}

/// <summary>Selected retained detail, not a startup row descriptor. It preserves historical uncertainty after pruning.</summary>
public sealed record StorageRetainedReviewSnapshot(string AcknowledgmentLeaf, StorageEvidenceVersion AcknowledgmentEvidence,
    StorageReviewAcknowledgment Acknowledgment, StorageRetainedReviewReport Report, bool JournalPresent);

internal static class StorageReviewNames
{
    internal const int Version = 1, AckBytes = 64 * 1024, ArtifactCount = 4096;
    internal const int ReportBytes = (int)StorageJournalPolicy.ReviewBytes - AckBytes;
    internal static string Temporary(Guid association, string kind, Guid nonce) => $".storage-{association:N}-{kind}-{nonce:N}.tmp";
    internal static string Published(Guid association, string kind, Guid nonce) => $"storage-{association:N}-{kind}-{nonce:N}.json";
    internal static bool TryParse(string? name, out Guid association, out string kind, out Guid nonce, out bool temporary)
    {
        association = default; kind = string.Empty; nonce = default; temporary = false;
        if (string.IsNullOrEmpty(name)) return false;
        temporary = name.StartsWith(".", StringComparison.Ordinal);
        string value = temporary ? name[1..] : name;
        if (!value.StartsWith("storage-", StringComparison.Ordinal)) return false;
        var parts = value.Split('-');
        if (parts.Length != 4 || !Guid.TryParseExact(parts[1], "N", out association) || association == Guid.Empty ||
            parts[2] is not ("report" or "ack")) return false;
        string suffix = temporary ? ".tmp" : ".json";
        if (!parts[3].EndsWith(suffix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(parts[3].AsSpan(0, parts[3].Length - suffix.Length), "N", out nonce) || nonce == Guid.Empty) return false;
        kind = parts[2]; return true;
    }
}
