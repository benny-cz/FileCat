using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FileCat.Core.Archives;

public enum ArchiveContentCleanupPhase { Cataloged, DeleteIntent, Deleted, Completed, NeedsReview }

/// <summary>Checksummed observations of private job artifacts. Records alone grant no native deletion authority.</summary>
public sealed record ArchiveContentCleanupArtifact
{
    public string Path { get; }
    internal ArchiveArtifactKind Kind { get; }
    public ArchiveObjectVersion Version { get; }
    public string MetadataSha256 { get; }
    public string OriginSha256 { get; }
    internal ArchiveContentCleanupArtifact(string path, ArchiveArtifactKind kind, ArchiveObjectVersion version, string metadata, string origin)
    {
        ArchiveTransactionFacts.ValidatePath(path); ArgumentNullException.ThrowIfNull(version);
        if (!Enum.IsDefined(kind)) throw Invalid();
        ArchiveWritePolicy.ValidateDigest(metadata); ArchiveWritePolicy.ValidateDigest(origin);
        Path = path; Kind = kind; Version = version; MetadataSha256 = metadata; OriginSha256 = origin;
    }
    internal static ArchiveContentCleanupArtifact Capture(string path, ArchiveArtifactKind kind, ArchiveFileObservation observation) =>
        new(path, kind, observation.Version, observation.MetadataDigest, observation.Origin.Digest);
    private static ArgumentException Invalid() => new("Invalid content-cleanup artifact observation.");
}

public sealed class ArchiveContentCleanupFacts
{
    internal const string RecordType = "archive-content-cleanup";
    public const int SchemaVersion = 1;
    public const int MaximumTransitions = 2 * ArchiveTransactionFacts.MaxArtifacts + 3;
    public Guid Nonce { get; }
    public int Sequence { get; }
    public ArchiveContentCleanupPhase Phase { get; }
    public string ScopeSha256 { get; }
    public string ParentIdentity { get; }
    public string ArchivePath { get; }
    public int DeletedCount { get; }
    public ReadOnlyCollection<ArchiveContentCleanupArtifact> Artifacts { get; }
    public bool IsResolved => Phase == ArchiveContentCleanupPhase.Completed;

    private ArchiveContentCleanupFacts(Guid nonce, int sequence, ArchiveContentCleanupPhase phase, string scope, string parent, string archive,
        int deleted, IReadOnlyList<ArchiveContentCleanupArtifact> artifacts)
    {
        if (nonce == Guid.Empty || sequence is < 1 or > MaximumTransitions || !Enum.IsDefined(phase) || artifacts.Count is < 2 or > ArchiveTransactionFacts.MaxArtifacts ||
            deleted < 0 || deleted > artifacts.Count || phase == ArchiveContentCleanupPhase.DeleteIntent && deleted == artifacts.Count ||
            phase == ArchiveContentCleanupPhase.Completed && deleted != artifacts.Count) throw Invalid();
        ArchiveWritePolicy.ValidateDigest(scope); ArchiveTransactionFacts.ValidateText(parent, 4096); ArchiveTransactionFacts.ValidatePath(archive);
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal) { archive };
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in artifacts)
            if (artifact is null || !paths.Add(artifact.Path) || !identities.Add(artifact.Version.Identity)) throw Invalid();
        Nonce = nonce; Sequence = sequence; Phase = phase; ScopeSha256 = scope; ParentIdentity = parent; ArchivePath = archive;
        DeletedCount = deleted; Artifacts = Array.AsReadOnly(artifacts.ToArray());
    }

    internal static ArchiveContentCleanupFacts Reserve(ArchiveTransactionFacts preparation, ArchiveContentUpdateFacts content,
        IReadOnlyList<ArchiveContentCleanupArtifact> artifacts)
    {
        var result = new ArchiveContentCleanupFacts(content.Nonce, 1, ArchiveContentCleanupPhase.Cataloged, Scope(content), preparation.ParentIdentity,
            preparation.ArchivePath, 0, artifacts); result.ValidateAfter(preparation, content, null); return result;
    }
    internal ArchiveContentCleanupFacts Advance(ArchiveContentCleanupPhase phase) => new(Nonce, Sequence + 1, phase, ScopeSha256, ParentIdentity,
        ArchivePath, phase == ArchiveContentCleanupPhase.Deleted ? checked(DeletedCount + 1) : DeletedCount, Artifacts);

    internal void ValidateAfter(ArchiveTransactionFacts preparation, ArchiveContentUpdateFacts content, ArchiveContentCleanupFacts? previous)
    {
        if (content.Nonce != Nonce || content.Snapshot is null || Scope(content) != ScopeSha256 || preparation.Nonce != Nonce ||
            preparation.ParentIdentity != ParentIdentity || preparation.ArchivePath != ArchivePath || preparation.VerifiedStage is null ||
            preparation.Sidecars.Count < 4 || preparation.Sidecars.Any(sidecar => sidecar.Version is null) || Artifacts.Count != preparation.Sidecars.Count + 2)
            throw Invalid();
        var expected = new List<(string Path, ArchiveArtifactKind Kind, ArchiveObjectVersion Version)> { (preparation.StagePath, ArchiveArtifactKind.Stage, preparation.VerifiedStage) };
        expected.AddRange(preparation.Sidecars.Select(sidecar => (sidecar.Path, sidecar.Role == ArchiveSidecarRole.OriginEvidence ? ArchiveArtifactKind.Provenance : ArchiveArtifactKind.Manifest, sidecar.Version!)));
        expected.Add((content.SnapshotPath, ArchiveArtifactKind.Stage, content.Snapshot));
        for (int i = 0; i < expected.Count; i++)
            if (Artifacts[i].Path != expected[i].Path || Artifacts[i].Kind != expected[i].Kind || Artifacts[i].Version != expected[i].Version ||
                Artifacts[i].Version.Identity == preparation.Original?.Identity || Path.GetDirectoryName(Artifacts[i].Path) != Path.GetDirectoryName(ArchivePath)) throw Invalid();
        if (previous is null)
        {
            if (Sequence != 1 || Phase != ArchiveContentCleanupPhase.Cataloged || DeletedCount != 0 || content.Phase != ArchiveContentUpdatePhase.SnapshotSealed) throw Invalid();
            return;
        }
        if (Sequence != previous.Sequence + 1 || Nonce != previous.Nonce || ScopeSha256 != previous.ScopeSha256 || ParentIdentity != previous.ParentIdentity ||
            ArchivePath != previous.ArchivePath || !Artifacts.SequenceEqual(previous.Artifacts)) throw Invalid();
        bool intent = (previous.Phase is ArchiveContentCleanupPhase.Cataloged or ArchiveContentCleanupPhase.Deleted) && Phase == ArchiveContentCleanupPhase.DeleteIntent;
        bool deleted = previous.Phase == ArchiveContentCleanupPhase.DeleteIntent && Phase == ArchiveContentCleanupPhase.Deleted;
        bool completed = previous.Phase == ArchiveContentCleanupPhase.Deleted && Phase == ArchiveContentCleanupPhase.Completed && DeletedCount == Artifacts.Count;
        bool review = previous.Phase is not (ArchiveContentCleanupPhase.Completed or ArchiveContentCleanupPhase.NeedsReview) && Phase == ArchiveContentCleanupPhase.NeedsReview;
        if (!(intent || deleted || completed || review) || DeletedCount != previous.DeletedCount + (deleted ? 1 : 0) ||
            !review && content.Phase != ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts) throw Invalid();
    }
    private static string Scope(ArchiveContentUpdateFacts content)
    {
        var node = content.ToJson(); node["sequence"] = 0; node["phase"] = "SnapshotSealed";
        node["privateOutput"] = null; node["privateMetadata"] = null; node["published"] = null;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }
    internal JsonObject ToJson() => new()
    {
        ["t"] = RecordType, ["v"] = SchemaVersion, ["nonce"] = Nonce.ToString("N"), ["sequence"] = Sequence, ["phase"] = Phase.ToString(),
        ["scope"] = ScopeSha256, ["parent"] = ParentIdentity, ["archive"] = ArchivePath, ["deleted"] = DeletedCount,
        ["artifacts"] = new JsonArray(Artifacts.Select(a => (JsonNode)new JsonObject { ["path"] = a.Path, ["kind"] = (int)a.Kind,
            ["version"] = new JsonObject { ["identity"] = a.Version.Identity, ["length"] = a.Version.Length, ["sha256"] = a.Version.Sha256 },
            ["metadata"] = a.MetadataSha256, ["origin"] = a.OriginSha256 }).ToArray()),
    };
    internal static ArchiveContentCleanupFacts Parse(JsonObject node)
    {
        Keys(node, "t", "v", "nonce", "sequence", "phase", "scope", "parent", "archive", "deleted", "artifacts");
        if (Text(node, "t") != RecordType || Number<int>(node, "v") != SchemaVersion || !Guid.TryParseExact(Text(node, "nonce"), "N", out var nonce) ||
            !Enum.TryParse<ArchiveContentCleanupPhase>(Text(node, "phase"), out var phase) || phase.ToString() != Text(node, "phase") ||
            node["artifacts"] is not JsonArray rows || rows.Count is < 2 or > ArchiveTransactionFacts.MaxArtifacts) throw Invalid();
        var artifacts = new ArchiveContentCleanupArtifact[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not JsonObject row || row["version"] is not JsonObject version) throw Invalid();
            Keys(row, "path", "kind", "version", "metadata", "origin"); Keys(version, "identity", "length", "sha256");
            artifacts[i] = new(Text(row, "path"), (ArchiveArtifactKind)Number<int>(row, "kind"),
                new(Text(version, "identity"), Number<long>(version, "length"), Text(version, "sha256")), Text(row, "metadata"), Text(row, "origin"));
        }
        return new(nonce, Number<int>(node, "sequence"), phase, Text(node, "scope"), Text(node, "parent"), Text(node, "archive"), Number<int>(node, "deleted"), artifacts);
    }
    private static void Keys(JsonObject node, params string[] keys)
    { if (node.Count != keys.Length || keys.Any(key => !node.ContainsKey(key))) throw Invalid(); }
    private static string Text(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<string>(out var text) && text is not null ? text : throw Invalid();
    private static T Number<T>(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<T>(out var number) ? number : throw Invalid();
    private static ArgumentException Invalid() => new("Invalid, stale or inconsistent archive artifact-cleanup facts.");
}
