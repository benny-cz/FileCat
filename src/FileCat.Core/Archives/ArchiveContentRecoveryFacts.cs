using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FileCat.Core.Archives;

public enum ArchiveContentRecoveryPhase { Reviewed, DeleteIntent, Deleted, Absent, Completed, NeedsReview }
public enum ArchiveContentRecoveryTarget { OriginalIntact, PublishedIntact, RestoredIntact }

/// <summary>Separate recovery observations/outcomes; absence never claims a confirmed unlink by FileCat.</summary>
public sealed class ArchiveContentRecoveryFacts
{
    internal const string RecordType = "archive-content-recovery";
    public const int SchemaVersion = 2;
    public int RecordVersion { get; }
    public const int MaximumTransitions = 128;
    public Guid Nonce { get; }
    public int Sequence { get; }
    public ArchiveContentRecoveryPhase Phase { get; }
    public string ContentSha256 { get; }
    public string CleanupSha256 { get; }
    public string? RestorationSha256 { get; }
    public ArchiveContentRecoveryTarget TargetState { get; }
    public ArchiveObjectVersion Target { get; }
    public string TargetMetadataSha256 { get; }
    public string TargetOriginSha256 { get; }
    public int ArtifactCount { get; }
    public int InitialCount { get; }
    public int ReviewedCount { get; }
    public uint DeletedMask { get; }
    public uint AbsentMask { get; }
    public bool IsResolved => Phase == ArchiveContentRecoveryPhase.Completed;

    private ArchiveContentRecoveryFacts(Guid nonce, int sequence, ArchiveContentRecoveryPhase phase, string content, string cleanup,
        ArchiveContentRecoveryTarget state, ArchiveObjectVersion target, string metadata, string origin, int count, int initial, int reviewed, uint deletedMask, uint absentMask,
        int recordVersion = SchemaVersion, string? restorationSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (recordVersion is not (1 or SchemaVersion) || (state == ArchiveContentRecoveryTarget.RestoredIntact) != (restorationSha256 is not null) ||
            recordVersion == 1 && state == ArchiveContentRecoveryTarget.RestoredIntact ||
            nonce == Guid.Empty || sequence is < 1 or > MaximumTransitions || !Enum.IsDefined(phase) || !Enum.IsDefined(state) ||
            count is < 2 or > ArchiveTransactionFacts.MaxArtifacts || initial < 0 || initial > count || reviewed < initial || reviewed > count ||
            phase == ArchiveContentRecoveryPhase.DeleteIntent && reviewed == count || phase == ArchiveContentRecoveryPhase.Completed && reviewed != count)
            throw Invalid();
        uint processed = ((1u << reviewed) - 1) & ~((1u << initial) - 1);
        if ((deletedMask & absentMask) != 0 || (deletedMask | absentMask) != processed) throw Invalid();
        ArchiveWritePolicy.ValidateDigest(content); ArchiveWritePolicy.ValidateDigest(cleanup); ArchiveWritePolicy.ValidateDigest(metadata); ArchiveWritePolicy.ValidateDigest(origin);
        if (restorationSha256 is not null) ArchiveWritePolicy.ValidateDigest(restorationSha256);
        RecordVersion = recordVersion; RestorationSha256 = restorationSha256;
        Nonce = nonce; Sequence = sequence; Phase = phase; ContentSha256 = content; CleanupSha256 = cleanup; TargetState = state;
        Target = target; TargetMetadataSha256 = metadata; TargetOriginSha256 = origin; ArtifactCount = count; InitialCount = initial;
        ReviewedCount = reviewed; DeletedMask = deletedMask; AbsentMask = absentMask;
    }
    internal static ArchiveContentRecoveryFacts Begin(ArchiveTransactionFacts preparation, ArchiveContentUpdateFacts content,
        ArchiveContentCleanupFacts cleanup, ArchiveFileObservation observed, ArchiveContentRestorationFacts? restoration = null)
    {
        if (observed.ParentIdentity != preparation.ParentIdentity || observed.LeafName != Path.GetFileName(preparation.ArchivePath)) throw Invalid();
        ArchiveContentRecoveryTarget state;
        if (restoration?.MatchesRestored(observed) == true) state = ArchiveContentRecoveryTarget.RestoredIntact;
        else if (observed.Version == content.PrivateOutput && observed.MetadataDigest == content.PrivateMetadataSha256 && observed.Origin.Digest == content.RequiredOriginSha256)
            state = ArchiveContentRecoveryTarget.PublishedIntact;
        else if (observed.Version == preparation.Original && observed.MetadataDigest == content.OriginalMetadataSha256)
            state = ArchiveContentRecoveryTarget.OriginalIntact;
        else throw Invalid();
        var facts = new ArchiveContentRecoveryFacts(content.Nonce, 1, ArchiveContentRecoveryPhase.Reviewed, Digest(content.ToJson()), Digest(cleanup.ToJson()),
            state, observed.Version, observed.MetadataDigest, observed.Origin.Digest, cleanup.Artifacts.Count, cleanup.DeletedCount, cleanup.DeletedCount, 0, 0,
            restorationSha256: state == ArchiveContentRecoveryTarget.RestoredIntact ? restoration!.Sha256 : null);
        facts.ValidateAfter(preparation, content, cleanup, null, restoration); return facts;
    }
    internal ArchiveContentRecoveryFacts Advance(ArchiveContentRecoveryPhase phase)
    {
        bool processed = phase is ArchiveContentRecoveryPhase.Deleted or ArchiveContentRecoveryPhase.Absent;
        return new(Nonce, Sequence + 1, phase, ContentSha256, CleanupSha256, TargetState, Target, TargetMetadataSha256, TargetOriginSha256,
            ArtifactCount, InitialCount, ReviewedCount + (processed ? 1 : 0),
            phase == ArchiveContentRecoveryPhase.Deleted ? DeletedMask | (1u << ReviewedCount) : DeletedMask,
            phase == ArchiveContentRecoveryPhase.Absent ? AbsentMask | (1u << ReviewedCount) : AbsentMask, RecordVersion, RestorationSha256);
    }
    internal void ValidateAfter(ArchiveTransactionFacts preparation, ArchiveContentUpdateFacts content, ArchiveContentCleanupFacts cleanup,
        ArchiveContentRecoveryFacts? previous, ArchiveContentRestorationFacts? restoration = null)
    {
        if (Nonce != preparation.Nonce || Nonce != content.Nonce || Nonce != cleanup.Nonce || Digest(content.ToJson()) != ContentSha256 ||
            Digest(cleanup.ToJson()) != CleanupSha256 || ArtifactCount != cleanup.Artifacts.Count || InitialCount != cleanup.DeletedCount ||
            (TargetState == ArchiveContentRecoveryTarget.RestoredIntact
                ? restoration is null || RestorationSha256 != restoration.Sha256 || Target != restoration.Restored || TargetMetadataSha256 != restoration.RestoredMetadataSha256 || TargetOriginSha256 != restoration.RestoredOriginSha256
                : TargetState == ArchiveContentRecoveryTarget.OriginalIntact
                ? Target != preparation.Original || TargetMetadataSha256 != content.OriginalMetadataSha256
                : Target != content.PrivateOutput || TargetMetadataSha256 != content.PrivateMetadataSha256 || TargetOriginSha256 != content.RequiredOriginSha256)) throw Invalid();
        if (previous is null)
        {
            if (Sequence != 1 || Phase != ArchiveContentRecoveryPhase.Reviewed || ReviewedCount != InitialCount || DeletedMask != 0 || AbsentMask != 0) throw Invalid();
            return;
        }
        bool deleted = previous.Phase == ArchiveContentRecoveryPhase.DeleteIntent && Phase == ArchiveContentRecoveryPhase.Deleted;
        bool absent = (previous.Phase is ArchiveContentRecoveryPhase.Reviewed or ArchiveContentRecoveryPhase.DeleteIntent or ArchiveContentRecoveryPhase.Deleted or ArchiveContentRecoveryPhase.Absent) && Phase == ArchiveContentRecoveryPhase.Absent;
        bool intent = (previous.Phase is ArchiveContentRecoveryPhase.Reviewed or ArchiveContentRecoveryPhase.Deleted or ArchiveContentRecoveryPhase.Absent) && Phase == ArchiveContentRecoveryPhase.DeleteIntent;
        bool complete = (previous.Phase is ArchiveContentRecoveryPhase.Reviewed or ArchiveContentRecoveryPhase.Deleted or ArchiveContentRecoveryPhase.Absent) && Phase == ArchiveContentRecoveryPhase.Completed && ReviewedCount == ArtifactCount;
        bool review = previous.Phase is not (ArchiveContentRecoveryPhase.Completed or ArchiveContentRecoveryPhase.NeedsReview) && Phase == ArchiveContentRecoveryPhase.NeedsReview;
        bool retry = previous.Phase == ArchiveContentRecoveryPhase.NeedsReview && Phase == ArchiveContentRecoveryPhase.Reviewed;
        if (!(deleted || absent || intent || complete || review || retry) || Sequence != previous.Sequence + 1 || Nonce != previous.Nonce ||
            RecordVersion != previous.RecordVersion || RestorationSha256 != previous.RestorationSha256 ||
            ContentSha256 != previous.ContentSha256 || CleanupSha256 != previous.CleanupSha256 || TargetState != previous.TargetState || Target != previous.Target ||
            TargetMetadataSha256 != previous.TargetMetadataSha256 || TargetOriginSha256 != previous.TargetOriginSha256 || ArtifactCount != previous.ArtifactCount || InitialCount != previous.InitialCount ||
            ReviewedCount != previous.ReviewedCount + (deleted || absent ? 1 : 0) ||
            DeletedMask != (deleted ? previous.DeletedMask | (1u << previous.ReviewedCount) : previous.DeletedMask) ||
            AbsentMask != (absent ? previous.AbsentMask | (1u << previous.ReviewedCount) : previous.AbsentMask)) throw Invalid();
    }
    internal JsonObject ToJson()
    {
        var result = new JsonObject
        {
        ["t"] = RecordType, ["v"] = RecordVersion, ["nonce"] = Nonce.ToString("N"), ["sequence"] = Sequence, ["phase"] = Phase.ToString(),
        ["content"] = ContentSha256, ["cleanup"] = CleanupSha256, ["state"] = TargetState.ToString(),
        ["target"] = new JsonObject { ["identity"] = Target.Identity, ["length"] = Target.Length, ["sha256"] = Target.Sha256 },
        ["metadata"] = TargetMetadataSha256, ["origin"] = TargetOriginSha256, ["count"] = ArtifactCount, ["initial"] = InitialCount,
        ["reviewed"] = ReviewedCount, ["deletedMask"] = DeletedMask, ["absentMask"] = AbsentMask,
        };
        if (RecordVersion >= 2) result["restoration"] = RestorationSha256;
        return result;
    }
    internal static ArchiveContentRecoveryFacts Parse(JsonObject node)
    {
        string[] keys = ["t", "v", "nonce", "sequence", "phase", "content", "cleanup", "state", "target", "metadata", "origin", "count", "initial", "reviewed", "deletedMask", "absentMask"];
        int version = Number<int>(node, "v");
        if (version == 2) keys = [.. keys, "restoration"];
        if (version is not (1 or SchemaVersion) || node.Count != keys.Length || keys.Any(key => !node.ContainsKey(key)) || Text(node, "t") != RecordType ||
            !Guid.TryParseExact(Text(node, "nonce"), "N", out var nonce) || !Enum.TryParse<ArchiveContentRecoveryPhase>(Text(node, "phase"), out var phase) || phase.ToString() != Text(node, "phase") ||
            !Enum.TryParse<ArchiveContentRecoveryTarget>(Text(node, "state"), out var state) || state.ToString() != Text(node, "state") ||
            node["target"] is not JsonObject target || target.Count != 3 || !target.ContainsKey("identity") || !target.ContainsKey("length") || !target.ContainsKey("sha256")) throw Invalid();
        return new(nonce, Number<int>(node, "sequence"), phase, Text(node, "content"), Text(node, "cleanup"), state,
            new(Text(target, "identity"), Number<long>(target, "length"), Text(target, "sha256")), Text(node, "metadata"), Text(node, "origin"),
            Number<int>(node, "count"), Number<int>(node, "initial"), Number<int>(node, "reviewed"), Number<uint>(node, "deletedMask"), Number<uint>(node, "absentMask"),
            version, version == 2 && node["restoration"] is not null ? Text(node, "restoration") : null);
    }
    private static string Digest(JsonObject node) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));
    private static string Text(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<string>(out var text) && text is not null ? text : throw Invalid();
    private static T Number<T>(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<T>(out var number) ? number : throw Invalid();
    private static ArgumentException Invalid() => new("Invalid, stale or inconsistent archive recovery observations.");
}
