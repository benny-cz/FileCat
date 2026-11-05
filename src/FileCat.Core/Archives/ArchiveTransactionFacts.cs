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
    public const int SchemaVersion = 1;
    public const int MaxArtifacts = 16;
    public const int MaxTransitions = 64;
    public Guid Nonce { get; }
    public int Sequence { get; }
    public ArchiveWriteProfile Profile { get; }
    public ArchiveTransactionPhase Phase { get; }
    public string ArchivePath { get; }
    public string ParentIdentity { get; }
    public ArchiveObjectVersion? Original { get; }
    public string StagePath { get; }
    public string StageIdentity { get; }
    public ArchiveObjectVersion? VerifiedStage { get; }
    public string? RetainedPath { get; }
    public string? ManifestPath { get; }
    public ArchiveObjectVersion? Manifest { get; }
    public string? SecuritySnapshotSha256 { get; }
    public ArchiveObjectVersion? Published { get; }
    public ArchiveObjectVersion? Retired { get; }
    public ReadOnlyCollection<string> UnresolvedArtifacts { get; }
    public bool IsResolved => Phase is ArchiveTransactionPhase.Committed or ArchiveTransactionPhase.NotCommitted or ArchiveTransactionPhase.RolledBack;

    public ArchiveTransactionFacts(Guid nonce, int sequence, ArchiveWriteProfile profile, ArchiveTransactionPhase phase,
        string archivePath, string parentIdentity, ArchiveObjectVersion? original, string stagePath, string stageIdentity,
        ArchiveObjectVersion? verifiedStage = null, string? retainedPath = null, string? manifestPath = null,
        ArchiveObjectVersion? manifest = null, string? securitySnapshotSha256 = null,
        ArchiveObjectVersion? published = null, ArchiveObjectVersion? retired = null,
        IReadOnlyList<string>? unresolvedArtifacts = null)
    {
        if (nonce == Guid.Empty || sequence is < 1 or > MaxTransitions || !Enum.IsDefined(phase))
            throw new ArgumentException("The archive transaction phase or sequence is invalid.");
        ArgumentNullException.ThrowIfNull(profile);
        ValidatePath(archivePath); ValidateText(parentIdentity, 4096); ValidatePath(stagePath); ValidateText(stageIdentity, 4096);
        if (string.Equals(archivePath, stagePath, StringComparison.Ordinal)) throw new ArgumentException("The stage cannot be the archive.");
        if (original?.Identity == stageIdentity) throw new ArgumentException("The stage cannot alias the original object.");
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
        if (securitySnapshotSha256 is not null) ArchiveWritePolicy.ValidateDigest(securitySnapshotSha256);
        bool publicationPhase = phase is ArchiveTransactionPhase.ReadyToPublish or ArchiveTransactionPhase.Publishing or
            ArchiveTransactionPhase.Reconciling or ArchiveTransactionPhase.Cleaning or ArchiveTransactionPhase.Committed or
            ArchiveTransactionPhase.CommittedWithRetainedArtifacts or ArchiveTransactionPhase.RolledBack;
        if (publicationPhase && (verifiedStage is null || manifest is null || securitySnapshotSha256 is null ||
                                original is not null && retainedPath is null))
            throw new ArgumentException("Publication requires complete verified stage, manifest, security and retention facts.");
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
        Nonce = nonce; Sequence = sequence; Profile = profile; Phase = phase; ArchivePath = archivePath;
        ParentIdentity = parentIdentity; Original = original; StagePath = stagePath; StageIdentity = stageIdentity;
        VerifiedStage = verifiedStage; RetainedPath = retainedPath; ManifestPath = manifestPath; Manifest = manifest;
        SecuritySnapshotSha256 = securitySnapshotSha256; Published = published; Retired = retired;
        UnresolvedArtifacts = Array.AsReadOnly(frozenArtifacts);
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
        if (Nonce != previous.Nonce || Sequence != previous.Sequence + 1 || Profile != previous.Profile ||
            ArchivePath != previous.ArchivePath || ParentIdentity != previous.ParentIdentity || Original != previous.Original ||
            StagePath != previous.StagePath || StageIdentity != previous.StageIdentity ||
            previous.RetainedPath is not null && RetainedPath != previous.RetainedPath ||
            previous.VerifiedStage is not null && VerifiedStage != previous.VerifiedStage ||
            previous.Manifest is not null && (Manifest != previous.Manifest || ManifestPath != previous.ManifestPath) ||
            previous.SecuritySnapshotSha256 is not null && SecuritySnapshotSha256 != previous.SecuritySnapshotSha256 ||
            previous.Published is not null && Published != previous.Published || previous.Retired is not null && Retired != previous.Retired)
            throw new ArgumentException("Archive transaction evidence changed or a transition is missing.");
        bool allowed = (previous.Phase, Phase) switch
        {
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

    internal JsonObject ToJson() => new()
    {
        ["t"] = "archive-transaction", ["v"] = SchemaVersion, ["nonce"] = Nonce.ToString("N"), ["sequence"] = Sequence,
        ["profile"] = new JsonObject { ["format"] = (int)Profile.Format, ["version"] = Profile.Version,
            ["level"] = Profile.CompressionLevel, ["dictionary"] = Profile.DictionaryBytes },
        ["phase"] = Phase.ToString(), ["archive"] = ArchivePath, ["parent"] = ParentIdentity,
        ["original"] = VersionJson(Original), ["stage"] = StagePath, ["stageIdentity"] = StageIdentity,
        ["verified"] = VersionJson(VerifiedStage), ["retained"] = RetainedPath, ["manifestPath"] = ManifestPath,
        ["manifest"] = VersionJson(Manifest), ["security"] = SecuritySnapshotSha256,
        ["published"] = VersionJson(Published), ["retired"] = VersionJson(Retired),
        ["unresolved"] = new JsonArray(UnresolvedArtifacts.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
    };

    private static JsonObject? VersionJson(ArchiveObjectVersion? v) => v is null ? null :
        new() { ["identity"] = v.Identity, ["length"] = v.Length, ["sha256"] = v.Sha256 };

    internal static ArchiveTransactionFacts Parse(JsonObject node)
    {
        ExactKeys(node, "t", "v", "nonce", "sequence", "profile", "phase", "archive", "parent", "original", "stage", "stageIdentity",
            "verified", "retained", "manifestPath", "manifest", "security", "published", "retired", "unresolved");
        if (Int(node, "v") != SchemaVersion || Text(node, "t") != "archive-transaction" ||
            !Guid.TryParseExact(Text(node, "nonce"), "N", out var nonce) ||
            !Enum.TryParse(Text(node, "phase"), out ArchiveTransactionPhase phase) || phase.ToString() != Text(node, "phase") ||
            node["profile"] is not JsonObject profile || node["unresolved"] is not JsonArray artifacts || artifacts.Count > MaxArtifacts)
            throw new ArgumentException("Unsupported or incomplete archive transaction facts.");
        ExactKeys(profile, "format", "version", "level", "dictionary");
        return new(nonce, Int(node, "sequence"), new((ArchiveWriteFormat)Int(profile, "format"), Int(profile, "version"),
                Int(profile, "level"), Int(profile, "dictionary")), phase, Text(node, "archive"), Text(node, "parent"),
            ParseVersion(node["original"]), Text(node, "stage"), Text(node, "stageIdentity"), ParseVersion(node["verified"]),
            OptionalText(node, "retained"), OptionalText(node, "manifestPath"), ParseVersion(node["manifest"]), OptionalText(node, "security"),
            ParseVersion(node["published"]), ParseVersion(node["retired"]), artifacts.Select(n =>
                n is JsonValue v && v.TryGetValue<string>(out var p) ? p : throw new ArgumentException("Invalid artifact path.")).ToArray());
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
    private string? problem;
    public bool RequiresReview => hasRecords && (problem is not null || damaged || latest.Values.Any(t => !t.IsResolved));
    public ArchiveTransactionRecovery Snapshot => new(Array.AsReadOnly(latest.Values.ToArray()), hasRecords,
        problem ?? (hasRecords && damaged ? "The archive transaction journal contains damaged or incomplete records." : null));
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
        // A version marker in the original durable header also protects a transaction whose first fact is torn.
        // A clean header with no transitions can still mean refusal/cancellation before any archive mutation.
        if (type == "begin" && node.ContainsKey("archiveJournalVersion"))
        {
            hasRecords = true;
            if (node["archiveJournalVersion"] is not JsonValue value || !value.TryGetValue<int>(out int version) ||
                version != ArchiveTransactionFacts.SchemaVersion)
                problem = "This archive journal version is unsupported; retain all related artifacts for review.";
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
        try { Accept(ArchiveTransactionFacts.Parse(node)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArchiveWriteException)
        { problem = "Archive transaction facts are unsupported, incomplete or inconsistent; retain all related artifacts for review."; }
    }
}
