using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FileCat.Core.Archives;

public enum ArchiveContentUpdatePhase
{
    SnapshotReserved, SnapshotCreated, TransactionOpened, SnapshotSealed, Ready, CommitIntent,
    CommittedWithRetainedArtifacts, RolledBackWithRetainedArtifacts, NeedsReview,
}

/// <summary>
/// A distinct same-object Windows NTFS transaction recipe, bound to a completed preparation snapshot.
/// These facts never describe a stage rename or grant recovery mutation authority. Every outcome retains artifacts.
/// </summary>
public sealed class ArchiveContentUpdateFacts
{
    internal const string RecordType = "archive-content-update";
    public const int SchemaVersion = 1;
    public const int MaximumTransitions = 16;
    public Guid Nonce { get; }
    public int Sequence { get; }
    public int PreparationSequence { get; }
    public string PreparationSha256 { get; }
    public ArchiveContentUpdatePhase Phase { get; }
    public string SnapshotPath { get; }
    public long SnapshotMaximumLength { get; }
    public string OriginalMetadataSha256 { get; }
    public string RequiredOriginSha256 { get; }
    public string? SnapshotIdentity { get; }
    public string? SnapshotCreationPolicySha256 { get; }
    public ArchiveObjectVersion? Snapshot { get; }
    public Guid? NativeTransactionId { get; }
    public ArchiveObjectVersion? PrivateOutput { get; }
    public string? PrivateMetadataSha256 { get; }
    public ArchiveObjectVersion? Published { get; }

    private ArchiveContentUpdateFacts(Guid nonce, int sequence, int preparationSequence, string preparationSha256,
        ArchiveContentUpdatePhase phase, string snapshotPath, long maximumLength, string originalMetadata, string requiredOrigin,
        string? snapshotIdentity = null, string? creationPolicy = null, Guid? transactionId = null, ArchiveObjectVersion? snapshot = null,
        ArchiveObjectVersion? privateOutput = null, string? privateMetadata = null, ArchiveObjectVersion? published = null)
    {
        if (nonce == Guid.Empty || sequence is < 1 or > MaximumTransitions || preparationSequence is < 1 or > ArchiveTransactionFacts.MaxTransitions ||
            !Enum.IsDefined(phase) || maximumLength is < 0 or > ArchiveWritePolicy.MaxPhysicalBytes || transactionId == Guid.Empty) throw Invalid();
        ArchiveWritePolicy.ValidateDigest(preparationSha256); ArchiveWritePolicy.ValidateDigest(originalMetadata); ArchiveWritePolicy.ValidateDigest(requiredOrigin);
        ArchiveTransactionFacts.ValidatePath(snapshotPath);
        if (snapshotIdentity is not null) ArchiveTransactionFacts.ValidateText(snapshotIdentity, 4096);
        if (creationPolicy is not null) ArchiveWritePolicy.ValidateDigest(creationPolicy);
        if (privateMetadata is not null) ArchiveWritePolicy.ValidateDigest(privateMetadata);
        if ((snapshotIdentity is null) != (creationPolicy is null) || (privateOutput is null) != (privateMetadata is null) ||
            snapshot is not null && (snapshot.Identity != snapshotIdentity || snapshot.Length != maximumLength) ||
            transactionId is not null && snapshotIdentity is null || snapshot is not null && transactionId is null ||
            privateOutput is not null && snapshot is null || published is not null && published != privateOutput) throw Invalid();
        Nonce = nonce; Sequence = sequence; PreparationSequence = preparationSequence; PreparationSha256 = preparationSha256;
        Phase = phase; SnapshotPath = snapshotPath; SnapshotMaximumLength = maximumLength; OriginalMetadataSha256 = originalMetadata;
        RequiredOriginSha256 = requiredOrigin; SnapshotIdentity = snapshotIdentity; SnapshotCreationPolicySha256 = creationPolicy;
        NativeTransactionId = transactionId; Snapshot = snapshot; PrivateOutput = privateOutput; PrivateMetadataSha256 = privateMetadata; Published = published;
    }

    internal static ArchiveContentUpdateFacts Reserve(ArchiveTransactionFacts preparation, ArchiveArtifactReservation reservation,
        ArchiveFileObservation original, ArchiveOriginSnapshot requiredOrigin)
    {
        if (preparation.Original != original.Version || original.ParentIdentity != preparation.ParentIdentity ||
            original.LeafName != Path.GetFileName(preparation.ArchivePath) || reservation.Nonce != preparation.Nonce ||
            reservation.Kind != ArchiveArtifactKind.Stage || reservation.ParentIdentity != preparation.ParentIdentity ||
            reservation.MaximumLength != original.Version.Length) throw Invalid();
        var facts = new ArchiveContentUpdateFacts(preparation.Nonce, 1, preparation.Sequence, Digest(preparation),
            ArchiveContentUpdatePhase.SnapshotReserved, reservation.Path, reservation.MaximumLength, original.MetadataDigest, requiredOrigin.Digest);
        facts.ValidateAfter(preparation, null); return facts;
    }

    internal ArchiveContentUpdateFacts Advance(ArchiveContentUpdatePhase phase, string? snapshotIdentity = null, string? creationPolicy = null,
        Guid? transactionId = null, ArchiveObjectVersion? snapshot = null, ArchiveFileObservation? privateOutput = null, ArchiveObjectVersion? published = null)
    {
        if (privateOutput is not null && privateOutput.Origin.Digest != RequiredOriginSha256) throw Invalid();
        return new(Nonce, checked(Sequence + 1), PreparationSequence, PreparationSha256, phase, SnapshotPath, SnapshotMaximumLength,
            OriginalMetadataSha256, RequiredOriginSha256, snapshotIdentity ?? SnapshotIdentity, creationPolicy ?? SnapshotCreationPolicySha256,
            transactionId ?? NativeTransactionId, snapshot ?? Snapshot, privateOutput?.Version ?? PrivateOutput,
            privateOutput?.MetadataDigest ?? PrivateMetadataSha256, published ?? Published);
    }

    internal void ValidateAfter(ArchiveTransactionFacts preparation, ArchiveContentUpdateFacts? previous)
    {
        if (preparation.RecordVersion != ArchiveTransactionFacts.SchemaVersion || preparation.Phase != ArchiveTransactionPhase.Verifying ||
            preparation.Original is not { } original || preparation.VerifiedStage is not { } stage || preparation.Manifest is null ||
            preparation.OriginEvidence is null || preparation.Nonce != Nonce || preparation.Sequence != PreparationSequence ||
            Digest(preparation) != PreparationSha256 || SnapshotMaximumLength != original.Length) throw Invalid();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(SnapshotPath), Path.GetDirectoryName(preparation.ArchivePath), comparison) ||
            new[] { preparation.ArchivePath, preparation.StagePath }.Concat(preparation.Sidecars.Select(s => s.Path))
                .Any(path => string.Equals(path, SnapshotPath, comparison)) ||
            SnapshotIdentity is not null && (SnapshotIdentity == original.Identity || SnapshotIdentity == stage.Identity ||
                preparation.Sidecars.Any(s => s.Identity == SnapshotIdentity)) ||
            Snapshot is not null && (Snapshot.Length != original.Length || Snapshot.Sha256 != original.Sha256) ||
            PrivateOutput is not null && (PrivateOutput.Identity != original.Identity || PrivateOutput.Length != stage.Length || PrivateOutput.Sha256 != stage.Sha256)) throw Invalid();
        if (previous is null)
        {
            if (Sequence != 1 || Phase != ArchiveContentUpdatePhase.SnapshotReserved || SnapshotIdentity is not null || Snapshot is not null ||
                NativeTransactionId is not null || PrivateOutput is not null || Published is not null) throw Invalid();
            return;
        }
        if (Nonce != previous.Nonce || Sequence != previous.Sequence + 1 || PreparationSequence != previous.PreparationSequence ||
            PreparationSha256 != previous.PreparationSha256 || SnapshotPath != previous.SnapshotPath || SnapshotMaximumLength != previous.SnapshotMaximumLength ||
            OriginalMetadataSha256 != previous.OriginalMetadataSha256 || RequiredOriginSha256 != previous.RequiredOriginSha256) throw Invalid();
        bool created = previous.Phase == ArchiveContentUpdatePhase.SnapshotReserved && Phase == ArchiveContentUpdatePhase.SnapshotCreated;
        bool opened = previous.Phase == ArchiveContentUpdatePhase.SnapshotCreated && Phase == ArchiveContentUpdatePhase.TransactionOpened;
        bool sealedSnapshot = previous.Phase == ArchiveContentUpdatePhase.TransactionOpened && Phase == ArchiveContentUpdatePhase.SnapshotSealed;
        bool ready = previous.Phase == ArchiveContentUpdatePhase.SnapshotSealed && Phase == ArchiveContentUpdatePhase.Ready;
        bool committed = previous.Phase == ArchiveContentUpdatePhase.CommitIntent && Phase == ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts;
        bool rollback = (previous.Phase is ArchiveContentUpdatePhase.TransactionOpened or ArchiveContentUpdatePhase.SnapshotSealed or
            ArchiveContentUpdatePhase.Ready or ArchiveContentUpdatePhase.CommitIntent) && Phase == ArchiveContentUpdatePhase.RolledBackWithRetainedArtifacts;
        bool review = Phase == ArchiveContentUpdatePhase.NeedsReview && previous.Phase != ArchiveContentUpdatePhase.NeedsReview;
        if (!(created || opened || sealedSnapshot || ready || committed || rollback || review ||
              previous.Phase == ArchiveContentUpdatePhase.Ready && Phase == ArchiveContentUpdatePhase.CommitIntent) ||
            (created ? SnapshotIdentity is null || SnapshotCreationPolicySha256 is null : SnapshotIdentity != previous.SnapshotIdentity || SnapshotCreationPolicySha256 != previous.SnapshotCreationPolicySha256) ||
            (opened ? NativeTransactionId is null : NativeTransactionId != previous.NativeTransactionId) ||
            (sealedSnapshot ? Snapshot is null : Snapshot != previous.Snapshot) ||
            (ready ? PrivateOutput is null || PrivateMetadataSha256 is null : PrivateOutput != previous.PrivateOutput || PrivateMetadataSha256 != previous.PrivateMetadataSha256) ||
            (committed ? Published is null : Published != previous.Published)) throw Invalid();
    }

    internal JsonObject ToJson() => new()
    {
        ["t"] = RecordType, ["v"] = SchemaVersion, ["recipe"] = "windows-ntfs-transactional-data", ["nonce"] = Nonce.ToString("N"),
        ["sequence"] = Sequence, ["preparationSequence"] = PreparationSequence, ["preparationSha256"] = PreparationSha256,
        ["phase"] = Phase.ToString(), ["snapshotPath"] = SnapshotPath, ["snapshotMaximum"] = SnapshotMaximumLength,
        ["originalMetadata"] = OriginalMetadataSha256, ["requiredOrigin"] = RequiredOriginSha256,
        ["snapshotIdentity"] = SnapshotIdentity, ["creationPolicy"] = SnapshotCreationPolicySha256,
        ["transactionId"] = NativeTransactionId?.ToString("N"), ["snapshot"] = VersionJson(Snapshot),
        ["privateOutput"] = VersionJson(PrivateOutput), ["privateMetadata"] = PrivateMetadataSha256, ["published"] = VersionJson(Published),
    };
    internal static ArchiveContentUpdateFacts Parse(JsonObject node)
    {
        string[] keys = ["t", "v", "recipe", "nonce", "sequence", "preparationSequence", "preparationSha256", "phase", "snapshotPath", "snapshotMaximum",
            "originalMetadata", "requiredOrigin", "snapshotIdentity", "creationPolicy", "transactionId", "snapshot", "privateOutput", "privateMetadata", "published"];
        if (node.Count != keys.Length || keys.Any(k => !node.ContainsKey(k)) || Text(node, "t") != RecordType || Number<int>(node, "v") != SchemaVersion ||
            Text(node, "recipe") != "windows-ntfs-transactional-data" || !Guid.TryParseExact(Text(node, "nonce"), "N", out var nonce) ||
            !Enum.TryParse<ArchiveContentUpdatePhase>(Text(node, "phase"), out var phase) || phase.ToString() != Text(node, "phase")) throw Invalid();
        Guid? transaction = null;
        if (OptionalText(node, "transactionId") is { } transactionText)
        { if (!Guid.TryParseExact(transactionText, "N", out var id)) throw Invalid(); transaction = id; }
        return new(nonce, Number<int>(node, "sequence"), Number<int>(node, "preparationSequence"), Text(node, "preparationSha256"), phase,
            Text(node, "snapshotPath"), Number<long>(node, "snapshotMaximum"), Text(node, "originalMetadata"), Text(node, "requiredOrigin"),
            OptionalText(node, "snapshotIdentity"), OptionalText(node, "creationPolicy"), transaction, ReadVersion(node, "snapshot"),
            ReadVersion(node, "privateOutput"), OptionalText(node, "privateMetadata"), ReadVersion(node, "published"));
    }
    private static string Digest(ArchiveTransactionFacts facts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(facts.ToJson().ToJsonString())));
    private static JsonObject? VersionJson(ArchiveObjectVersion? version) => version is null ? null :
        new() { ["identity"] = version.Identity, ["length"] = version.Length, ["sha256"] = version.Sha256 };
    private static ArchiveObjectVersion? ReadVersion(JsonObject node, string key)
    {
        if (node[key] is null) return null;
        if (node[key] is not JsonObject value || value.Count != 3 || !value.ContainsKey("identity") || !value.ContainsKey("length") || !value.ContainsKey("sha256")) throw Invalid();
        return new(Text(value, "identity"), Number<long>(value, "length"), Text(value, "sha256"));
    }
    private static string Text(JsonObject node, string key) => OptionalText(node, key) ?? throw Invalid();
    private static string? OptionalText(JsonObject node, string key) => node[key] is null ? null :
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw Invalid();
    private static T Number<T>(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<T>(out var number) ? number : throw Invalid();
    private static ArgumentException Invalid() => new("Invalid, stale or inconsistent native archive content-update facts.");
}
