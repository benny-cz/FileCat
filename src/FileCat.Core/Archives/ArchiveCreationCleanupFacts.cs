using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FileCat.Core.Archives;

public enum ArchiveCreationCleanupPhase { Cataloged, DeleteIntent, Deleted, Absent, Completed, NeedsReview }

/// <summary>Bounded creation-sidecar observations and outcomes. These records never grant native deletion authority.</summary>
public sealed class ArchiveCreationCleanupFacts
{
    internal const string RecordType = "archive-creation-cleanup";
    public const int SchemaVersion = 1, MaximumTransitions = 128;
    public Guid Nonce { get; }
    public int Sequence { get; }
    public ArchiveCreationCleanupPhase Phase { get; }
    public string ScopeSha256 { get; }
    public ArchiveObjectVersion Target { get; }
    public string TargetMetadataSha256 { get; }
    public string TargetOriginSha256 { get; }
    public int ReviewedCount { get; }
    public uint DeletedMask { get; }
    public uint AbsentMask { get; }
    public IReadOnlyList<ArchiveContentCleanupArtifact> Artifacts { get; }
    public bool IsResolved => Phase == ArchiveCreationCleanupPhase.Completed;

    private ArchiveCreationCleanupFacts(Guid nonce, int sequence, ArchiveCreationCleanupPhase phase, string scope,
        ArchiveObjectVersion target, string metadata, string origin, int reviewed, uint deleted, uint absent,
        IReadOnlyList<ArchiveContentCleanupArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (nonce == Guid.Empty || sequence is < 1 or > MaximumTransitions || !Enum.IsDefined(phase) ||
            artifacts.Count is < 4 or > ArchiveTransactionFacts.MaxArtifacts || reviewed < 0 || reviewed > artifacts.Count ||
            phase == ArchiveCreationCleanupPhase.DeleteIntent && reviewed == artifacts.Count ||
            phase == ArchiveCreationCleanupPhase.Completed && reviewed != artifacts.Count ||
            (deleted & absent) != 0 || (deleted | absent) != (1u << reviewed) - 1) throw Invalid();
        ArchiveWritePolicy.ValidateDigest(scope); ArchiveWritePolicy.ValidateDigest(metadata); ArchiveWritePolicy.ValidateDigest(origin);
        var paths = new HashSet<string>(StringComparer.Ordinal); var identities = new HashSet<string>(StringComparer.Ordinal) { target.Identity };
        foreach (var artifact in artifacts)
            if (artifact is null || !paths.Add(artifact.Path) || !identities.Add(artifact.Version.Identity)) throw Invalid();
        Nonce = nonce; Sequence = sequence; Phase = phase; ScopeSha256 = scope; Target = target;
        TargetMetadataSha256 = metadata; TargetOriginSha256 = origin; ReviewedCount = reviewed; DeletedMask = deleted; AbsentMask = absent;
        Artifacts = Array.AsReadOnly(artifacts.ToArray());
    }

    internal static ArchiveCreationCleanupFacts Begin(ArchiveTransactionFacts preparation, ArchiveFileObservation published,
        IReadOnlyList<ArchiveContentCleanupArtifact> artifacts)
    {
        if (published.ParentIdentity != preparation.ParentIdentity || published.LeafName != Path.GetFileName(preparation.ArchivePath)) throw Invalid();
        var facts = new ArchiveCreationCleanupFacts(preparation.Nonce, 1, ArchiveCreationCleanupPhase.Cataloged, Scope(preparation),
            published.Version, published.MetadataDigest, published.Origin.Digest, 0, 0, 0, artifacts);
        facts.ValidateAfter(preparation, null); return facts;
    }

    internal ArchiveCreationCleanupFacts Advance(ArchiveCreationCleanupPhase phase)
    {
        bool processed = phase is ArchiveCreationCleanupPhase.Deleted or ArchiveCreationCleanupPhase.Absent;
        return new(Nonce, Sequence + 1, phase, ScopeSha256, Target, TargetMetadataSha256, TargetOriginSha256,
            ReviewedCount + (processed ? 1 : 0), phase == ArchiveCreationCleanupPhase.Deleted ? DeletedMask | 1u << ReviewedCount : DeletedMask,
            phase == ArchiveCreationCleanupPhase.Absent ? AbsentMask | 1u << ReviewedCount : AbsentMask, Artifacts);
    }

    internal void ValidateAfter(ArchiveTransactionFacts preparation, ArchiveCreationCleanupFacts? previous)
    {
        ValidatePreparation(preparation);
        if (previous is null)
        {
            if (Sequence != 1 || Phase != ArchiveCreationCleanupPhase.Cataloged || ReviewedCount != 0 || DeletedMask != 0 || AbsentMask != 0 ||
                preparation.Phase != ArchiveTransactionPhase.Cleaning) throw Invalid();
            return;
        }
        bool deleted = previous.Phase == ArchiveCreationCleanupPhase.DeleteIntent && Phase == ArchiveCreationCleanupPhase.Deleted;
        bool absent = (previous.Phase is ArchiveCreationCleanupPhase.Cataloged or ArchiveCreationCleanupPhase.DeleteIntent or
            ArchiveCreationCleanupPhase.Deleted or ArchiveCreationCleanupPhase.Absent) && Phase == ArchiveCreationCleanupPhase.Absent;
        bool intent = (previous.Phase is ArchiveCreationCleanupPhase.Cataloged or ArchiveCreationCleanupPhase.Deleted or ArchiveCreationCleanupPhase.Absent) &&
            Phase == ArchiveCreationCleanupPhase.DeleteIntent;
        bool complete = (previous.Phase is ArchiveCreationCleanupPhase.Cataloged or ArchiveCreationCleanupPhase.Deleted or ArchiveCreationCleanupPhase.Absent) &&
            Phase == ArchiveCreationCleanupPhase.Completed && ReviewedCount == Artifacts.Count;
        bool fail = previous.Phase is not (ArchiveCreationCleanupPhase.Completed or ArchiveCreationCleanupPhase.NeedsReview) && Phase == ArchiveCreationCleanupPhase.NeedsReview;
        bool retry = previous.Phase == ArchiveCreationCleanupPhase.NeedsReview && Phase == ArchiveCreationCleanupPhase.Cataloged;
        if (!(deleted || absent || intent || complete || fail || retry) || Sequence != previous.Sequence + 1 || Nonce != previous.Nonce ||
            ScopeSha256 != previous.ScopeSha256 || Target != previous.Target || TargetMetadataSha256 != previous.TargetMetadataSha256 ||
            TargetOriginSha256 != previous.TargetOriginSha256 || !Artifacts.SequenceEqual(previous.Artifacts) ||
            ReviewedCount != previous.ReviewedCount + (deleted || absent ? 1 : 0) ||
            DeletedMask != (deleted ? previous.DeletedMask | 1u << previous.ReviewedCount : previous.DeletedMask) ||
            AbsentMask != (absent ? previous.AbsentMask | 1u << previous.ReviewedCount : previous.AbsentMask)) throw Invalid();
    }
    internal void ValidatePreparation(ArchiveTransactionFacts preparation)
    {
        if (preparation.RecordVersion != ArchiveTransactionFacts.SchemaVersion || preparation.Original is not null ||
            preparation.Nonce != Nonce || preparation.Published != Target || preparation.VerifiedStage != Target || Scope(preparation) != ScopeSha256 ||
            preparation.SecuritySnapshotSha256 is null || preparation.Sidecars.Count != Artifacts.Count) throw Invalid();
        for (int i = 0; i < Artifacts.Count; i++)
        {
            var sidecar = preparation.Sidecars[i]; var artifact = Artifacts[i];
            if (sidecar.Version is null || artifact.Path != sidecar.Path || artifact.Version != sidecar.Version ||
                artifact.Kind != (sidecar.Role == ArchiveSidecarRole.OriginEvidence ? ArchiveArtifactKind.Provenance : ArchiveArtifactKind.Manifest) ||
                Path.GetDirectoryName(artifact.Path) != Path.GetDirectoryName(preparation.ArchivePath) || artifact.Path == preparation.ArchivePath) throw Invalid();
        }
    }

    internal JsonObject ToJson() => new()
    {
        ["t"] = RecordType, ["v"] = SchemaVersion, ["nonce"] = Nonce.ToString("N"), ["sequence"] = Sequence, ["phase"] = Phase.ToString(),
        ["scope"] = ScopeSha256, ["target"] = new JsonObject { ["identity"] = Target.Identity, ["length"] = Target.Length, ["sha256"] = Target.Sha256 },
        ["metadata"] = TargetMetadataSha256, ["origin"] = TargetOriginSha256, ["reviewed"] = ReviewedCount, ["deleted"] = DeletedMask, ["absent"] = AbsentMask,
        ["artifacts"] = new JsonArray(Artifacts.Select(artifact => (JsonNode)new JsonObject
        {
            ["path"] = artifact.Path, ["kind"] = (int)artifact.Kind, ["identity"] = artifact.Version.Identity,
            ["length"] = artifact.Version.Length, ["sha256"] = artifact.Version.Sha256,
            ["metadata"] = artifact.MetadataSha256, ["origin"] = artifact.OriginSha256,
        }).ToArray()),
    };
    internal static ArchiveCreationCleanupFacts Parse(JsonObject node)
    {
        Keys(node, "t", "v", "nonce", "sequence", "phase", "scope", "target", "metadata", "origin", "reviewed", "deleted", "absent", "artifacts");
        if (Text(node, "t") != RecordType || Number<int>(node, "v") != SchemaVersion || !Guid.TryParseExact(Text(node, "nonce"), "N", out var nonce) ||
            !Enum.TryParse<ArchiveCreationCleanupPhase>(Text(node, "phase"), out var phase) || phase.ToString() != Text(node, "phase") ||
            node["target"] is not JsonObject target || node["artifacts"] is not JsonArray rows || rows.Count is < 4 or > ArchiveTransactionFacts.MaxArtifacts) throw Invalid();
        Keys(target, "identity", "length", "sha256"); var artifacts = new ArchiveContentCleanupArtifact[rows.Count];
        for (int i = 0; i < artifacts.Length; i++)
        {
            if (rows[i] is not JsonObject row) throw Invalid();
            Keys(row, "path", "kind", "identity", "length", "sha256", "metadata", "origin");
            artifacts[i] = new(Text(row, "path"), (ArchiveArtifactKind)Number<int>(row, "kind"),
                new(Text(row, "identity"), Number<long>(row, "length"), Text(row, "sha256")), Text(row, "metadata"), Text(row, "origin"));
        }
        return new(nonce, Number<int>(node, "sequence"), phase, Text(node, "scope"),
            new(Text(target, "identity"), Number<long>(target, "length"), Text(target, "sha256")), Text(node, "metadata"), Text(node, "origin"),
            Number<int>(node, "reviewed"), Number<uint>(node, "deleted"), Number<uint>(node, "absent"), artifacts);
    }
    private static string Scope(ArchiveTransactionFacts preparation)
    {
        var node = preparation.ToJson(); node.Remove("sequence"); node.Remove("phase"); node.Remove("unresolved");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }
    private static void Keys(JsonObject node, params string[] expected)
    { if (node.Count != expected.Length || expected.Any(key => !node.ContainsKey(key))) throw Invalid(); }
    private static string Text(JsonObject node, string key)
        => node[key] is JsonValue value && value.TryGetValue<string>(out var result) && result is not null ? result : throw Invalid();
    private static T Number<T>(JsonObject node, string key)
        => node[key] is JsonValue value && value.TryGetValue<T>(out var result) ? result : throw Invalid();
    private static ArgumentException Invalid() => new("Invalid or inconsistent archive creation cleanup facts.");
}
