using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace FileCat.Core.Archives;

// These are durable parent facts, not worker instructions or permission to mutate a named path.
public enum ArchiveTransactionPhase
{
    Reserved = 0, Rebuilding = 1, Verifying = 2, ReadyToPublish = 3, Publishing = 4,
    Reconciling = 5, Cleaning = 6, Committed = 7, CommittedWithRetainedArtifacts = 8,
    NotCommitted = 9, RolledBack = 10, NeedsReview = 11,
}

public sealed record ArchiveObjectVersion
{
    public string Identity { get; }
    public long Length { get; }
    public string Sha256 { get; }

    public ArchiveObjectVersion(string identity, long length, string sha256)
    {
        ArchiveTransactionFacts.ValidateText(identity, 4096);
        if (length < 0 || length > ArchiveWritePolicy.MaxPhysicalBytes)
            throw new ArgumentException("An archive object length is outside the supported range.");
        ArchiveWritePolicy.ValidateDigest(sha256);
        Identity = identity; Length = length; Sha256 = sha256;
    }
}

/// <summary>
/// A complete immutable snapshot repeated at every durable transition. Paths are evidence only: recovery must acquire
/// fresh native object leases and compare identities, bytes and security metadata before any action.
/// </summary>
public sealed class ArchiveTransactionFacts
{
    public const int SchemaVersion = 3;
    public const int MaxArtifacts = 16;
    public const int MaxTransitions = 64;
    public Guid Nonce { get; }
    public int RecordVersion { get; }
    public int Sequence { get; }
    public ArchiveWriteProfile Profile { get; }
    public ArchiveTransactionPhase Phase { get; }
    public string ArchivePath { get; }
    public string ParentIdentity { get; }
    public ArchiveObjectVersion? Original { get; }
    public string StagePath { get; }
    public string? StageIdentity { get; }
    public long StageMaximumLength { get; }
    public ArchiveObjectVersion? VerifiedStage { get; }
    public string? RetainedPath { get; }
    public string? ManifestPath { get; }
    public ArchiveObjectVersion? Manifest { get; }
    public string? OriginEvidencePath { get; }
    public string? OriginEvidenceIdentity { get; }
    public int? OriginEvidenceSources { get; }
    public ArchiveObjectVersion? OriginEvidence { get; }
    public string? SecuritySnapshotSha256 { get; }
    public ArchiveObjectVersion? Published { get; }
    public ArchiveObjectVersion? Retired { get; }
    public ReadOnlyCollection<string> UnresolvedArtifacts { get; }
    public bool IsResolved => Phase is ArchiveTransactionPhase.Committed or ArchiveTransactionPhase.NotCommitted or ArchiveTransactionPhase.RolledBack;

    public ArchiveTransactionFacts(Guid nonce, int sequence, ArchiveWriteProfile profile, ArchiveTransactionPhase phase,
        string archivePath, string parentIdentity, ArchiveObjectVersion? original, string stagePath, string? stageIdentity,
        ArchiveObjectVersion? verifiedStage = null, string? retainedPath = null, string? manifestPath = null,
        ArchiveObjectVersion? manifest = null, string? securitySnapshotSha256 = null,
        ArchiveObjectVersion? published = null, ArchiveObjectVersion? retired = null,
        IReadOnlyList<string>? unresolvedArtifacts = null, string? originEvidencePath = null,
        string? originEvidenceIdentity = null, int? originEvidenceSources = null, ArchiveObjectVersion? originEvidence = null,
        long stageMaximumLength = ArchiveWritePolicy.MaxPhysicalBytes)
        : this(SchemaVersion, nonce, sequence, profile, phase, archivePath, parentIdentity, original, stagePath, stageIdentity,
            verifiedStage, retainedPath, manifestPath, manifest, securitySnapshotSha256, published, retired, unresolvedArtifacts,
            originEvidencePath, originEvidenceIdentity, originEvidenceSources, originEvidence, stageMaximumLength)
    { }

    // Legacy records can be inspected but never written/upgraded in place or treated as complete provenance evidence.
    private ArchiveTransactionFacts(int recordVersion, Guid nonce, int sequence, ArchiveWriteProfile profile, ArchiveTransactionPhase phase,
        string archivePath, string parentIdentity, ArchiveObjectVersion? original, string stagePath, string? stageIdentity,
        ArchiveObjectVersion? verifiedStage, string? retainedPath, string? manifestPath, ArchiveObjectVersion? manifest,
        string? securitySnapshotSha256, ArchiveObjectVersion? published, ArchiveObjectVersion? retired,
        IReadOnlyList<string>? unresolvedArtifacts, string? originEvidencePath, string? originEvidenceIdentity,
        int? originEvidenceSources, ArchiveObjectVersion? originEvidence, long stageMaximumLength)
    {
        if (recordVersion is not (1 or 2 or SchemaVersion)) throw new ArgumentException("Unsupported archive transaction schema.");
        if (nonce == Guid.Empty || sequence is < 1 or > MaxTransitions || !Enum.IsDefined(phase))
            throw new ArgumentException("The archive transaction phase or sequence is invalid.");
        ArgumentNullException.ThrowIfNull(profile);
        ValidatePath(archivePath); ValidateText(parentIdentity, 4096); ValidatePath(stagePath);
        if (stageIdentity is not null) ValidateText(stageIdentity, 4096);
        if (stageMaximumLength < 0 || stageMaximumLength > ArchiveWritePolicy.MaxPhysicalBytes ||
            verifiedStage?.Length > stageMaximumLength)
            throw new ArgumentException("The stage exceeds its reserved physical bound.");
        if (stageIdentity is null && (recordVersion < 3 || phase is not (ArchiveTransactionPhase.Reserved or ArchiveTransactionPhase.NeedsReview)))
            throw new ArgumentException("A stage without recorded identity must remain reserved or require review.");
        if (string.Equals(archivePath, stagePath, StringComparison.Ordinal)) throw new ArgumentException("The stage cannot be the archive.");
        if (original is not null && original.Identity == stageIdentity) throw new ArgumentException("The stage cannot alias the original object.");
        if (verifiedStage is not null && verifiedStage.Identity != stageIdentity)
            throw new ArgumentException("The verified stage is a different object.");
        if (retainedPath is not null)
        {
            ValidatePath(retainedPath);
            if (retainedPath == archivePath || retainedPath == stagePath) throw new ArgumentException("The retained name is not distinct.");
        }
        if ((manifestPath is null) != (manifest is null)) throw new ArgumentException("Manifest evidence is incomplete.");
        if (manifestPath is not null)
        {
            ValidatePath(manifestPath);
            if (manifestPath == archivePath || manifestPath == stagePath || manifestPath == retainedPath ||
                manifest!.Length > ArchiveWritePolicy.MaxMetadataBytes)
                throw new ArgumentException("Manifest evidence is invalid.");
            if (manifest.Identity == stageIdentity || manifest.Identity == original?.Identity)
                throw new ArgumentException("The manifest cannot alias an archive object.");
        }
        if ((originEvidencePath is null) != (originEvidenceIdentity is null) ||
            (originEvidencePath is null) != (originEvidenceSources is null) || originEvidence is not null && originEvidencePath is null)
            throw new ArgumentException("Origin evidence reservation is incomplete.");
        if (originEvidencePath is not null)
        {
            ValidatePath(originEvidencePath); ValidateText(originEvidenceIdentity!, 4096);
            if (originEvidencePath == archivePath || originEvidencePath == stagePath || originEvidencePath == retainedPath || originEvidencePath == manifestPath ||
                originEvidenceIdentity == stageIdentity || originEvidenceIdentity == original?.Identity || originEvidenceIdentity == manifest?.Identity ||
                originEvidenceSources < (original is null ? 0 : 1) || originEvidenceSources > ArchiveWritePolicy.MaxMembers + (original is null ? 0 : 1))
                throw new ArgumentException("Origin evidence reservation aliases an archive artifact or exceeds its scope.");
            if (originEvidence is not null && (originEvidence.Identity != originEvidenceIdentity ||
                                               originEvidence.Length < 52 + ArchiveOriginEvidenceWriter.MinFramedRecordBytes * (long)originEvidenceSources!.Value ||
                                               originEvidence.Length > ArchiveWritePolicy.MaxMetadataBytes))
                throw new ArgumentException("Finalized origin evidence is a different or unsupported object.");
        }
        if (securitySnapshotSha256 is not null) ArchiveWritePolicy.ValidateDigest(securitySnapshotSha256);
        bool publicationPhase = phase is ArchiveTransactionPhase.ReadyToPublish or ArchiveTransactionPhase.Publishing or
            ArchiveTransactionPhase.Reconciling or ArchiveTransactionPhase.Cleaning or ArchiveTransactionPhase.Committed or
            ArchiveTransactionPhase.CommittedWithRetainedArtifacts or ArchiveTransactionPhase.RolledBack;
        if (publicationPhase && (verifiedStage is null || manifest is null || securitySnapshotSha256 is null ||
                                recordVersion >= 2 && originEvidence is null ||
                                original is not null && retainedPath is null))
            throw new ArgumentException("Publication requires complete verified stage, manifest, origin evidence, security and retention facts.");
        if (phase is ArchiveTransactionPhase.Cleaning or ArchiveTransactionPhase.Committed or ArchiveTransactionPhase.CommittedWithRetainedArtifacts &&
            (published is null || original is not null && retired is null))
            throw new ArgumentException("A committed archive requires actual publication and retired-object facts.");
        if (published is not null && verifiedStage is not null && published != verifiedStage)
            throw new ArgumentException("The publication is not the verified stage object and bytes.");
        if (retired is not null && original is null) throw new ArgumentException("A creation cannot retire an original.");
        if (phase is ArchiveTransactionPhase.Reserved or ArchiveTransactionPhase.Rebuilding or ArchiveTransactionPhase.Verifying or
            ArchiveTransactionPhase.ReadyToPublish or ArchiveTransactionPhase.NotCommitted && (published is not null || retired is not null))
            throw new ArgumentException("Publication observations cannot precede publication.");
        if (phase is ArchiveTransactionPhase.Committed or ArchiveTransactionPhase.CommittedWithRetainedArtifacts && original != retired)
            throw new ArgumentException("A committed update must reconcile the exact original object and bytes.");
        if (phase == ArchiveTransactionPhase.RolledBack && (original is null || retired is null))
            throw new ArgumentException("Rollback requires actual retired-original evidence.");
        unresolvedArtifacts ??= [];
        if (unresolvedArtifacts.Count > MaxArtifacts) throw new ArgumentException("Too many unresolved archive artifacts.");
        var frozenArtifacts = new string[unresolvedArtifacts.Count];
        var artifacts = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < frozenArtifacts.Length; i++)
        {
            string path = unresolvedArtifacts[i];
            ValidatePath(path);
            if (!artifacts.Add(path)) throw new ArgumentException("Duplicate unresolved archive artifact.");
            frozenArtifacts[i] = path;
        }
        if (phase is ArchiveTransactionPhase.Committed or ArchiveTransactionPhase.NotCommitted or ArchiveTransactionPhase.RolledBack && artifacts.Count != 0)
            throw new ArgumentException("A resolved transaction cannot have unresolved artifacts.");
        if (phase == ArchiveTransactionPhase.CommittedWithRetainedArtifacts && artifacts.Count == 0)
            throw new ArgumentException("A cleanup warning must name its retained artifacts.");
        RecordVersion = recordVersion; Nonce = nonce; Sequence = sequence; Profile = profile; Phase = phase; ArchivePath = archivePath;
        ParentIdentity = parentIdentity; Original = original; StagePath = stagePath; StageIdentity = stageIdentity; StageMaximumLength = stageMaximumLength;
        VerifiedStage = verifiedStage; RetainedPath = retainedPath; ManifestPath = manifestPath; Manifest = manifest;
        OriginEvidencePath = originEvidencePath; OriginEvidenceIdentity = originEvidenceIdentity;
        OriginEvidenceSources = originEvidenceSources; OriginEvidence = originEvidence;
        SecuritySnapshotSha256 = securitySnapshotSha256; Published = published; Retired = retired;
        UnresolvedArtifacts = Array.AsReadOnly(frozenArtifacts);
        // Individual path/identity ceilings do not imply that their simultaneous escaped forms fit a CRC line.
        // Refuse the combined snapshot before any journal append or corresponding namespace mutation.
        if (System.Text.Encoding.UTF8.GetByteCount(ToJson().ToJsonString()) > Jobs.JobJournal.MaxJournalLineBytes - 10)
            throw ArchiveWriteBudget.Limit();
    }

    internal static void ValidateText(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length > maxChars || text.IndexOf('\0') >= 0)
            throw new ArgumentException("An archive transaction field is empty or too long.");
        try { _ = ArchiveWritePolicy.Utf8.GetByteCount(text); }
        catch (System.Text.EncoderFallbackException) { throw new ArgumentException("An archive transaction field is not exact UTF-8."); }
    }

    private static void ValidatePath(string path)
    {
        ValidateText(path, ArchiveWritePolicy.MaxNameChars);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An archive transaction path must be absolute.");
    }

    internal void ValidateAfter(ArchiveTransactionFacts? previous)
    {
        if (previous is null)
        {
            if (Sequence != 1 || Phase != ArchiveTransactionPhase.Reserved) throw new ArgumentException("An archive transaction must start with reservation.");
            return;
        }
        if (RecordVersion != previous.RecordVersion || Nonce != previous.Nonce || Sequence != previous.Sequence + 1 || Profile != previous.Profile ||
            ArchivePath != previous.ArchivePath || ParentIdentity != previous.ParentIdentity || Original != previous.Original ||
            StagePath != previous.StagePath || StageMaximumLength != previous.StageMaximumLength ||
            previous.StageIdentity is not null && StageIdentity != previous.StageIdentity ||
            previous.RetainedPath is not null && RetainedPath != previous.RetainedPath ||
            previous.VerifiedStage is not null && VerifiedStage != previous.VerifiedStage ||
            previous.Manifest is not null && (Manifest != previous.Manifest || ManifestPath != previous.ManifestPath) ||
            previous.OriginEvidencePath is not null && (OriginEvidencePath != previous.OriginEvidencePath ||
                OriginEvidenceIdentity != previous.OriginEvidenceIdentity || OriginEvidenceSources != previous.OriginEvidenceSources) ||
            previous.OriginEvidence is not null && OriginEvidence != previous.OriginEvidence ||
            previous.SecuritySnapshotSha256 is not null && SecuritySnapshotSha256 != previous.SecuritySnapshotSha256 ||
            previous.Published is not null && Published != previous.Published || previous.Retired is not null && Retired != previous.Retired)
            throw new ArgumentException("Archive transaction evidence changed or a transition is missing.");
        bool allowed = (previous.Phase, Phase) switch
        {
            (ArchiveTransactionPhase.Reserved, ArchiveTransactionPhase.Reserved) => RecordVersion >= 3 &&
                previous.StageIdentity is null && StageIdentity is not null,
            (ArchiveTransactionPhase.Reserved, ArchiveTransactionPhase.Rebuilding) => true,
            (ArchiveTransactionPhase.Rebuilding, ArchiveTransactionPhase.Verifying) => true,
            (ArchiveTransactionPhase.Verifying, ArchiveTransactionPhase.ReadyToPublish) => true,
            (ArchiveTransactionPhase.ReadyToPublish, ArchiveTransactionPhase.Publishing) => true,
            (ArchiveTransactionPhase.Publishing, ArchiveTransactionPhase.Reconciling) => true,
            (ArchiveTransactionPhase.Reconciling, ArchiveTransactionPhase.Cleaning or ArchiveTransactionPhase.RolledBack) => true,
            (ArchiveTransactionPhase.Cleaning, ArchiveTransactionPhase.Committed or ArchiveTransactionPhase.CommittedWithRetainedArtifacts) => true,
            (ArchiveTransactionPhase.Reserved or ArchiveTransactionPhase.Rebuilding or ArchiveTransactionPhase.Verifying or
                ArchiveTransactionPhase.ReadyToPublish or ArchiveTransactionPhase.Publishing, ArchiveTransactionPhase.NotCommitted) => true,
            (_, ArchiveTransactionPhase.NeedsReview) => !previous.IsResolved && previous.Phase is not
                (ArchiveTransactionPhase.NeedsReview or ArchiveTransactionPhase.CommittedWithRetainedArtifacts),
            _ => false,
        };
        if (!allowed) throw new ArgumentException("The archive transaction transition is invalid.");
    }

    internal JsonObject ToJson()
    {
        var node = new JsonObject
        {
        ["t"] = "archive-transaction", ["v"] = RecordVersion, ["nonce"] = Nonce.ToString("N"), ["sequence"] = Sequence,
        ["profile"] = new JsonObject { ["format"] = (int)Profile.Format, ["version"] = Profile.Version,
            ["level"] = Profile.CompressionLevel, ["dictionary"] = Profile.DictionaryBytes },
        ["phase"] = Phase.ToString(), ["archive"] = ArchivePath, ["parent"] = ParentIdentity,
        ["original"] = VersionJson(Original), ["stage"] = StagePath, ["stageIdentity"] = StageIdentity,
        ["verified"] = VersionJson(VerifiedStage), ["retained"] = RetainedPath, ["manifestPath"] = ManifestPath,
        ["manifest"] = VersionJson(Manifest), ["security"] = SecuritySnapshotSha256,
        ["published"] = VersionJson(Published), ["retired"] = VersionJson(Retired),
        ["unresolved"] = new JsonArray(UnresolvedArtifacts.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
        };
        if (RecordVersion >= 2)
        {
            node["originPath"] = OriginEvidencePath; node["originIdentity"] = OriginEvidenceIdentity;
            node["originSources"] = OriginEvidenceSources; node["originEvidence"] = VersionJson(OriginEvidence);
        }
        if (RecordVersion >= 3) node["stageMaximum"] = StageMaximumLength;
        return node;
    }

    internal ArchiveTransactionFacts BindCreatedStage(string identity)
    {
        if (RecordVersion != SchemaVersion || Phase != ArchiveTransactionPhase.Reserved || StageIdentity is not null)
            throw new InvalidOperationException("Only a fresh stage reservation can record its created identity.");
        return new(Nonce, Sequence + 1, Profile, Phase, ArchivePath, ParentIdentity, Original, StagePath, identity,
            VerifiedStage, RetainedPath, ManifestPath, Manifest, SecuritySnapshotSha256, Published, Retired, UnresolvedArtifacts,
            OriginEvidencePath, OriginEvidenceIdentity, OriginEvidenceSources, OriginEvidence, StageMaximumLength);
    }

    private static JsonObject? VersionJson(ArchiveObjectVersion? v) => v is null ? null :
        new() { ["identity"] = v.Identity, ["length"] = v.Length, ["sha256"] = v.Sha256 };

    internal static ArchiveTransactionFacts Parse(JsonObject node)
    {
        int recordVersion = Int(node, "v");
        string[] keys = ["t", "v", "nonce", "sequence", "profile", "phase", "archive", "parent", "original", "stage", "stageIdentity",
            "verified", "retained", "manifestPath", "manifest", "security", "published", "retired", "unresolved"];
        if (recordVersion >= 2) keys = [.. keys, "originPath", "originIdentity", "originSources", "originEvidence"];
        if (recordVersion >= 3) keys = [.. keys, "stageMaximum"];
        ExactKeys(node, keys);
        if (recordVersion is not (1 or 2 or SchemaVersion) || Text(node, "t") != "archive-transaction" ||
            !Guid.TryParseExact(Text(node, "nonce"), "N", out var nonce) ||
            !Enum.TryParse(Text(node, "phase"), out ArchiveTransactionPhase phase) || phase.ToString() != Text(node, "phase") ||
            node["profile"] is not JsonObject profile || node["unresolved"] is not JsonArray artifacts || artifacts.Count > MaxArtifacts)
            throw new ArgumentException("Unsupported or incomplete archive transaction facts.");
        ExactKeys(profile, "format", "version", "level", "dictionary");
        return new(recordVersion, nonce, Int(node, "sequence"), new((ArchiveWriteFormat)Int(profile, "format"), Int(profile, "version"),
                Int(profile, "level"), Int(profile, "dictionary")), phase, Text(node, "archive"), Text(node, "parent"),
            ParseVersion(node["original"]), Text(node, "stage"), recordVersion >= 3 ? OptionalText(node, "stageIdentity") : Text(node, "stageIdentity"), ParseVersion(node["verified"]),
            OptionalText(node, "retained"), OptionalText(node, "manifestPath"), ParseVersion(node["manifest"]), OptionalText(node, "security"),
            ParseVersion(node["published"]), ParseVersion(node["retired"]), artifacts.Select(n =>
                n is JsonValue v && v.TryGetValue<string>(out var p) ? p : throw new ArgumentException("Invalid artifact path.")).ToArray(),
            recordVersion >= 2 ? OptionalText(node, "originPath") : null, recordVersion >= 2 ? OptionalText(node, "originIdentity") : null,
            recordVersion >= 2 && node["originSources"] is not null ? Int(node, "originSources") : null,
            recordVersion >= 2 ? ParseVersion(node["originEvidence"]) : null,
            recordVersion >= 3 && node["stageMaximum"] is JsonValue maximum && maximum.TryGetValue<long>(out long bound)
                ? bound : recordVersion < 3 ? ArchiveWritePolicy.MaxPhysicalBytes : throw new ArgumentException("Invalid stage bound."));
    }

    private static ArchiveObjectVersion? ParseVersion(JsonNode? node)
    {
        if (node is null) return null;
        if (node is not JsonObject version) throw new ArgumentException("Invalid object version.");
        ExactKeys(version, "identity", "length", "sha256");
        if (version["length"] is not JsonValue value || !value.TryGetValue<long>(out long length))
            throw new ArgumentException("Invalid object length.");
        return new(Text(version, "identity"), length, Text(version, "sha256"));
    }

    private static string Text(JsonObject node, string key) => OptionalText(node, key) ?? throw new ArgumentException("Missing transaction field.");
    private static string? OptionalText(JsonObject node, string key) => node[key] is null ? null :
        node[key] is JsonValue value && value.TryGetValue<string>(out string? text) ? text : throw new ArgumentException("Invalid transaction text.");
    private static int Int(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<int>(out int number)
        ? number : throw new ArgumentException("Invalid transaction number.");
    private static void ExactKeys(JsonObject node, params string[] keys)
    {
        if (node.Count != keys.Length || keys.Any(k => !node.ContainsKey(k))) throw new ArgumentException("Unsupported transaction fields.");
    }
}

/// <summary>Bounded recovery facts. Even a valid digest here never authorizes pathname deletion or publication replay.</summary>
public sealed record ArchiveTransactionRecovery(IReadOnlyList<ArchiveTransactionFacts> Transactions, bool HasRecords, string? Problem)
{
    public bool RequiresReview => HasRecords && (Problem is not null || Transactions.Any(t => !t.IsResolved));
}

internal sealed class ArchiveTransactionCollector
{
    private const int MaxTransactions = 16;
    private readonly Dictionary<Guid, ArchiveTransactionFacts> latest = [];
    private bool hasRecords;
    private bool damaged;
    private bool legacy;
    private bool began, ended;
    private int? headerVersion;
    private string? problem;
    public bool RequiresReview => hasRecords && (problem is not null || damaged || legacy || latest.Values.Any(t => !t.IsResolved));
    public ArchiveTransactionRecovery Snapshot => new(Array.AsReadOnly(latest.Values.ToArray()), hasRecords,
        problem ?? (legacy ? "This legacy archive journal lacks complete provenance evidence; retain its recorded artifacts for archive-specific review." :
            hasRecords && damaged ? "The archive transaction journal contains damaged or incomplete records." : null));
    public void DamagedRecord() => damaged = true;
    public void ValidateNext(ArchiveTransactionFacts facts)
    {
        if (problem is not null || damaged) throw new InvalidOperationException("The archive transaction journal is not writable.");
        if (!latest.ContainsKey(facts.Nonce) && latest.Count >= MaxTransactions) throw new ArgumentException("Too many archive transactions.");
        facts.ValidateAfter(latest.GetValueOrDefault(facts.Nonce));
    }
    public void Accept(ArchiveTransactionFacts facts) { ValidateNext(facts); latest[facts.Nonce] = facts; hasRecords = true; }
    public void Read(string type, JsonObject node)
    {
        // A later header/end cannot turn out-of-order archive history into a completed transaction.
        // These flags affect archive recovery only; the generic journal reader keeps its existing behavior.
        if (type == "begin")
        {
            if (began || hasRecords || ended) damaged = true;
            began = true;
        }
        else if (!began) damaged = true;
        if (type == "end") { if (ended) damaged = true; ended = true; }
        // A version marker in the original durable header also protects a transaction whose first fact is torn.
        // A clean header with no transitions can still mean refusal/cancellation before any archive mutation.
        if (type == "begin" && node.ContainsKey("archiveJournalVersion"))
        {
            hasRecords = true;
            if (node["archiveJournalVersion"] is not JsonValue value || !value.TryGetValue<int>(out int version) ||
                version is not (1 or 2 or ArchiveTransactionFacts.SchemaVersion))
                problem = "This archive journal version is unsupported; retain all related artifacts for review.";
            else { headerVersion = version; legacy |= version == 1; }
            return;
        }
        if (!type.StartsWith("archive-", StringComparison.Ordinal))
        {
            if (type is not ("begin" or "end" or "intent" or "done" or "replacement-recovery" or "stagedir" or "fill" or "note"))
                damaged = true;
            return;
        }
        hasRecords = true;
        if (problem is not null) return;
        if (ended) damaged = true;
        try
        {
            var facts = ArchiveTransactionFacts.Parse(node);
            if (headerVersion is { } declared && declared != facts.RecordVersion)
                throw new ArgumentException("Archive fact schema differs from its durable header.");
            legacy |= facts.RecordVersion == 1; Accept(facts);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArchiveWriteException)
        { problem = "Archive transaction facts are unsupported, incomplete or inconsistent; retain all related artifacts for review."; }
    }
}
