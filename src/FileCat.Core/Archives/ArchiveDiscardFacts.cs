using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FileCat.Core.Archives;

public enum ArchiveDiscardPhase { Cataloged, DeleteIntent, Deleted, Absent, Completed, NeedsReview }

/// <summary>
/// One object an unpublished transaction recorded: its path, the identity recorded when FileCat created it, and its sealed
/// version when the preparation recorded one (sidecars, never the stage, which is discarded as scratch).
/// </summary>
public sealed record ArchiveDiscardArtifact(string Path, string Identity, ArchiveObjectVersion? Version);

/// <summary>
/// Exact discard of an archive transaction that never reached publication. Plan §7.1: a failure in Reserved, Rebuilding,
/// Finalizing or Verifying ends with "no publication; safe owned cleanup or NeedsReview". Every listed object is FileCat's
/// own scratch object bound by its recorded identity; reserved names that never received an identity must be absent.
/// The record freezes the preparation facts it was derived from. These records never grant pathname authority.
/// </summary>
public sealed class ArchiveDiscardFacts
{
    internal const string RecordType = "archive-discard";
    public const int SchemaVersion = 1, MaximumTransitions = 128;
    public Guid Nonce { get; }
    public int Sequence { get; }
    public ArchiveDiscardPhase Phase { get; }
    public string ScopeSha256 { get; }
    public int ReviewedCount { get; }
    public uint DeletedMask { get; }
    public uint AbsentMask { get; }
    public IReadOnlyList<ArchiveDiscardArtifact> Artifacts { get; }
    public IReadOnlyList<string> Unrecorded { get; }
    public bool IsResolved => Phase == ArchiveDiscardPhase.Completed;

    private ArchiveDiscardFacts(Guid nonce, int sequence, ArchiveDiscardPhase phase, string scope, int reviewed, uint deleted, uint absent,
        IReadOnlyList<ArchiveDiscardArtifact> artifacts, IReadOnlyList<string> unrecorded)
    {
        if (nonce == Guid.Empty || sequence is < 1 or > MaximumTransitions || !Enum.IsDefined(phase) ||
            artifacts.Count > ArchiveTransactionFacts.MaxArtifacts || unrecorded.Count > ArchiveTransactionFacts.MaxArtifacts ||
            reviewed < 0 || reviewed > artifacts.Count ||
            phase == ArchiveDiscardPhase.DeleteIntent && reviewed == artifacts.Count ||
            phase == ArchiveDiscardPhase.Completed && reviewed != artifacts.Count ||
            (deleted & absent) != 0 || (deleted | absent) != (reviewed == 32 ? uint.MaxValue : (1u << reviewed) - 1)) throw Invalid();
        ArchiveWritePolicy.ValidateDigest(scope);
        var paths = new HashSet<string>(StringComparer.Ordinal); var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in artifacts)
        {
            if (artifact is null || !paths.Add(artifact.Path) || !identities.Add(artifact.Identity) ||
                artifact.Version is not null && artifact.Version.Identity != artifact.Identity) throw Invalid();
            ArchiveTransactionFacts.ValidatePath(artifact.Path); ArchiveTransactionFacts.ValidateText(artifact.Identity, 4096);
        }
        foreach (string path in unrecorded) { ArchiveTransactionFacts.ValidatePath(path); if (!paths.Add(path)) throw Invalid(); }
        Nonce = nonce; Sequence = sequence; Phase = phase; ScopeSha256 = scope; ReviewedCount = reviewed; DeletedMask = deleted; AbsentMask = absent;
        Artifacts = Array.AsReadOnly(artifacts.ToArray()); Unrecorded = Array.AsReadOnly(unrecorded.ToArray());
    }

    /// <summary>
    /// True for facts that durably prove publication never started: a preparation phase or ReadyToPublish (Publishing is
    /// recorded before any visible step), or a new archive whose review record was written before its claim (that record
    /// lists only FileCat's own objects, never the archive name). Other recipes (exchange, content, cleanup) are excluded by
    /// the journal.
    /// </summary>
    internal static bool IsDiscardable(ArchiveTransactionFacts preparation)
    {
        // ManifestPath and OriginEvidencePath only mirror the sealed sidecars in the current schema; a retained path is never a scratch object.
        if (preparation.RecordVersion != ArchiveTransactionFacts.SchemaVersion || preparation.Published is not null || preparation.Retired is not null ||
            preparation.RetainedPath is not null) return false;
        if (preparation.Phase is ArchiveTransactionPhase.Reserved or ArchiveTransactionPhase.Rebuilding or ArchiveTransactionPhase.Finalizing or
            ArchiveTransactionPhase.Verifying or ArchiveTransactionPhase.ReadyToPublish) return preparation.UnresolvedArtifacts.Count == 0;
        if (preparation.Phase != ArchiveTransactionPhase.NeedsReview || preparation.Original is not null || preparation.UnresolvedArtifacts.Count == 0) return false;
        var own = preparation.Sidecars.Select(sidecar => sidecar.Path).Append(preparation.StagePath).ToHashSet(StringComparer.Ordinal);
        return !preparation.UnresolvedArtifacts.Contains(preparation.ArchivePath, StringComparer.Ordinal) && preparation.UnresolvedArtifacts.All(own.Contains);
    }

    internal static ArchiveDiscardFacts Begin(ArchiveTransactionFacts preparation)
    {
        if (!IsDiscardable(preparation)) throw Invalid();
        var (artifacts, unrecorded) = Derive(preparation);
        var facts = new ArchiveDiscardFacts(preparation.Nonce, 1, ArchiveDiscardPhase.Cataloged, Scope(preparation), 0, 0, 0, artifacts, unrecorded);
        facts.ValidateAfter(preparation, null); return facts;
    }

    internal ArchiveDiscardFacts Advance(ArchiveDiscardPhase phase)
    {
        bool processed = phase is ArchiveDiscardPhase.Deleted or ArchiveDiscardPhase.Absent;
        return new(Nonce, Sequence + 1, phase, ScopeSha256, ReviewedCount + (processed ? 1 : 0),
            phase == ArchiveDiscardPhase.Deleted ? DeletedMask | 1u << ReviewedCount : DeletedMask,
            phase == ArchiveDiscardPhase.Absent ? AbsentMask | 1u << ReviewedCount : AbsentMask, Artifacts, Unrecorded);
    }

    internal void ValidateAfter(ArchiveTransactionFacts preparation, ArchiveDiscardFacts? previous)
    {
        ValidatePreparation(preparation);
        if (previous is null)
        {
            if (Sequence != 1 || Phase != ArchiveDiscardPhase.Cataloged || ReviewedCount != 0 || DeletedMask != 0 || AbsentMask != 0) throw Invalid();
            return;
        }
        bool deleted = previous.Phase == ArchiveDiscardPhase.DeleteIntent && Phase == ArchiveDiscardPhase.Deleted;
        bool absent = previous.Phase is not (ArchiveDiscardPhase.Completed or ArchiveDiscardPhase.NeedsReview) && Phase == ArchiveDiscardPhase.Absent;
        bool intent = previous.Phase is ArchiveDiscardPhase.Cataloged or ArchiveDiscardPhase.Deleted or ArchiveDiscardPhase.Absent &&
            Phase == ArchiveDiscardPhase.DeleteIntent;
        bool complete = previous.Phase is ArchiveDiscardPhase.Cataloged or ArchiveDiscardPhase.Deleted or ArchiveDiscardPhase.Absent &&
            Phase == ArchiveDiscardPhase.Completed && ReviewedCount == Artifacts.Count;
        bool fail = previous.Phase is not (ArchiveDiscardPhase.Completed or ArchiveDiscardPhase.NeedsReview) && Phase == ArchiveDiscardPhase.NeedsReview;
        bool retry = previous.Phase == ArchiveDiscardPhase.NeedsReview && Phase == ArchiveDiscardPhase.Cataloged;
        if (!(deleted || absent || intent || complete || fail || retry) || Sequence != previous.Sequence + 1 || Nonce != previous.Nonce ||
            ScopeSha256 != previous.ScopeSha256 || !Artifacts.SequenceEqual(previous.Artifacts) || !Unrecorded.SequenceEqual(previous.Unrecorded) ||
            ReviewedCount != previous.ReviewedCount + (deleted || absent ? 1 : 0) ||
            DeletedMask != (deleted ? previous.DeletedMask | 1u << previous.ReviewedCount : previous.DeletedMask) ||
            AbsentMask != (absent ? previous.AbsentMask | 1u << previous.ReviewedCount : previous.AbsentMask)) throw Invalid();
    }

    internal void ValidatePreparation(ArchiveTransactionFacts preparation)
    {
        if (preparation.Nonce != Nonce || !IsDiscardable(preparation) || Scope(preparation) != ScopeSha256) throw Invalid();
        var (artifacts, unrecorded) = Derive(preparation);
        if (!artifacts.SequenceEqual(Artifacts) || !unrecorded.SequenceEqual(Unrecorded, StringComparer.Ordinal)) throw Invalid();
    }

    /// <summary>
    /// The creator-private stage folder below the archive folder that holds every object, or null when the objects were
    /// created directly in the archive folder (the Windows layout).
    /// </summary>
    internal static string? PrivateFolder(ArchiveTransactionFacts preparation)
    {
        string archiveFolder = Path.GetDirectoryName(preparation.ArchivePath)!, stageFolder = Path.GetDirectoryName(preparation.StagePath)!;
        return stageFolder != archiveFolder && Path.GetDirectoryName(stageFolder) == archiveFolder &&
            Path.GetFileName(stageFolder).StartsWith(ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix, StringComparison.Ordinal) ? stageFolder : null;
    }

    private static (ArchiveDiscardArtifact[] Artifacts, string[] Unrecorded) Derive(ArchiveTransactionFacts preparation)
    {
        // The stage first: it is the largest object, so space returns as early as possible.
        var artifacts = new List<ArchiveDiscardArtifact>(); var unrecorded = new List<string>();
        string folder = PrivateFolder(preparation) ?? Path.GetDirectoryName(preparation.ArchivePath)!;
        if (preparation.StageIdentity is { } stage) artifacts.Add(new(preparation.StagePath, stage, null)); else unrecorded.Add(preparation.StagePath);
        foreach (var sidecar in preparation.Sidecars)
        {
            if (sidecar.Identity is { } identity) artifacts.Add(new(sidecar.Path, identity, sidecar.Version)); else unrecorded.Add(sidecar.Path);
        }
        if (artifacts.Any(artifact => Path.GetDirectoryName(artifact.Path) != folder || artifact.Path == preparation.ArchivePath) ||
            unrecorded.Any(path => Path.GetDirectoryName(path) != folder || path == preparation.ArchivePath)) throw Invalid();
        return (artifacts.ToArray(), unrecorded.ToArray());
    }

    internal JsonObject ToJson() => new()
    {
        ["t"] = RecordType, ["v"] = SchemaVersion, ["nonce"] = Nonce.ToString("N"), ["sequence"] = Sequence, ["phase"] = Phase.ToString(),
        ["scope"] = ScopeSha256, ["reviewed"] = ReviewedCount, ["deleted"] = DeletedMask, ["absent"] = AbsentMask,
        ["artifacts"] = new JsonArray(Artifacts.Select(artifact => (JsonNode)new JsonObject
        {
            ["path"] = artifact.Path, ["identity"] = artifact.Identity,
            ["version"] = artifact.Version is null ? null : new JsonObject
                { ["identity"] = artifact.Version.Identity, ["length"] = artifact.Version.Length, ["sha256"] = artifact.Version.Sha256 },
        }).ToArray()),
        ["unrecorded"] = new JsonArray(Unrecorded.Select(path => (JsonNode)JsonValue.Create(path)!).ToArray()),
    };

    internal static ArchiveDiscardFacts Parse(JsonObject node)
    {
        Keys(node, "t", "v", "nonce", "sequence", "phase", "scope", "reviewed", "deleted", "absent", "artifacts", "unrecorded");
        if (Text(node, "t") != RecordType || Number<int>(node, "v") != SchemaVersion || !Guid.TryParseExact(Text(node, "nonce"), "N", out var nonce) ||
            !Enum.TryParse<ArchiveDiscardPhase>(Text(node, "phase"), out var phase) || phase.ToString() != Text(node, "phase") ||
            node["artifacts"] is not JsonArray rows || rows.Count > ArchiveTransactionFacts.MaxArtifacts ||
            node["unrecorded"] is not JsonArray names || names.Count > ArchiveTransactionFacts.MaxArtifacts) throw Invalid();
        var artifacts = new ArchiveDiscardArtifact[rows.Count];
        for (int i = 0; i < artifacts.Length; i++)
        {
            if (rows[i] is not JsonObject row) throw Invalid();
            Keys(row, "path", "identity", "version");
            ArchiveObjectVersion? version = null;
            if (row["version"] is JsonObject value)
            {
                Keys(value, "identity", "length", "sha256");
                version = new(Text(value, "identity"), Number<long>(value, "length"), Text(value, "sha256"));
            }
            else if (row["version"] is not null) throw Invalid();
            artifacts[i] = new(Text(row, "path"), Text(row, "identity"), version);
        }
        var unrecorded = names.Select(name => name is JsonValue value && value.TryGetValue<string>(out var path) && path is not null ? path : throw Invalid()).ToArray();
        return new(nonce, Number<int>(node, "sequence"), phase, Text(node, "scope"), Number<int>(node, "reviewed"),
            Number<uint>(node, "deleted"), Number<uint>(node, "absent"), artifacts, unrecorded);
    }

    // The discard freezes the complete latest preparation record: any later transaction record is refused.
    private static string Scope(ArchiveTransactionFacts preparation)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(preparation.ToJson().ToJsonString())));
    private static void Keys(JsonObject node, params string[] expected)
    { if (node.Count != expected.Length || expected.Any(key => !node.ContainsKey(key))) throw Invalid(); }
    private static string Text(JsonObject node, string key)
        => node[key] is JsonValue value && value.TryGetValue<string>(out var result) && result is not null ? result : throw Invalid();
    private static T Number<T>(JsonObject node, string key)
        => node[key] is JsonValue value && value.TryGetValue<T>(out var result) ? result : throw Invalid();
    private static ArgumentException Invalid() => new("Invalid or inconsistent archive discard facts.");
}
