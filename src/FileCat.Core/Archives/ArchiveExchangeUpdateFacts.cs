using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FileCat.Core.Archives;

public enum ArchiveExchangeUpdatePhase
{
    Reserved, MetadataApplied, ExchangeIntent, Exchanged, NotPublished, CleanupIntent, Cleaned, Completed, NeedsReview,
}

/// <summary>
/// Unix existing-archive update through one atomic two-directory name exchange. The verified stage is prepared in a
/// creator-private stage directory beside the archive, receives the original's promised outer metadata there, and then
/// swaps names with the held original, which stays at the stage's private name until exact cleanup. These facts bind the
/// completed preparation, both directory identities and names, and the exact versions on each side. They never grant
/// recovery mutation authority by themselves: recovery must reopen and re-observe every object first.
/// </summary>
public sealed class ArchiveExchangeUpdateFacts
{
    internal const string RecordType = "archive-exchange-update";
    public const int SchemaVersion = 1;
    public const int MaximumTransitions = 2 * (ArchiveTransactionFacts.MaxArtifacts + 1) + 8;
    internal const string PrivateDirectoryPrefix = ".filecat-archive-stage-";

    public Guid Nonce { get; }
    public int Sequence { get; }
    public int PreparationSequence { get; }
    public string PreparationSha256 { get; }
    public ArchiveExchangeUpdatePhase Phase { get; }
    public string ArchivePath { get; }
    public string PublicParentIdentity { get; }
    public string PrivateDirectoryPath { get; }
    public string PrivateDirectoryIdentity { get; }
    public string StagePath { get; }
    public ArchiveObjectVersion Original { get; }
    public string OriginalMetadataSha256 { get; }
    public string PromisedMetadataSha256 { get; }
    public string RequiredOriginSha256 { get; }
    public ArchiveObjectVersion Stage { get; }
    public string StageMetadataSha256 { get; }
    public string? AppliedMetadataSha256 { get; }
    public ArchiveObjectVersion? Published { get; }
    public ArchiveObjectVersion? Retired { get; }
    public string? RetiredMetadataSha256 { get; }
    /// <summary>Sealed sidecars in the private directory, followed during cleanup by the object at <see cref="StagePath"/>.</summary>
    public ReadOnlyCollection<ArchiveContentCleanupArtifact> Sidecars { get; }
    public int CleanedCount { get; }
    public int CleanupCount => Sidecars.Count + 1;
    public bool IsResolved => Phase == ArchiveExchangeUpdatePhase.Completed;
    public bool WasPublished => Published is not null;

    private ArchiveExchangeUpdateFacts(Guid nonce, int sequence, int preparationSequence, string preparationSha256, ArchiveExchangeUpdatePhase phase,
        string archivePath, string publicParent, string privatePath, string privateIdentity, string stagePath, ArchiveObjectVersion original,
        string originalMetadata, string promised, string requiredOrigin, ArchiveObjectVersion stage, string stageMetadata, string? applied,
        ArchiveObjectVersion? published, ArchiveObjectVersion? retired, string? retiredMetadata, IReadOnlyList<ArchiveContentCleanupArtifact> sidecars, int cleaned)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(stage); ArgumentNullException.ThrowIfNull(sidecars);
        if (nonce == Guid.Empty || sequence is < 1 or > MaximumTransitions || preparationSequence is < 1 or > ArchiveTransactionFacts.MaxTransitions ||
            !Enum.IsDefined(phase) || sidecars.Count is < 3 or > ArchiveTransactionFacts.MaxArtifacts || cleaned < 0 || cleaned > sidecars.Count + 1)
            throw Invalid();
        foreach (string digest in new[] { preparationSha256, originalMetadata, promised, requiredOrigin, stageMetadata }) ArchiveWritePolicy.ValidateDigest(digest);
        if (applied is not null) ArchiveWritePolicy.ValidateDigest(applied);
        if (retiredMetadata is not null) ArchiveWritePolicy.ValidateDigest(retiredMetadata);
        ArchiveTransactionFacts.ValidatePath(archivePath); ArchiveTransactionFacts.ValidatePath(privatePath); ArchiveTransactionFacts.ValidatePath(stagePath);
        ArchiveTransactionFacts.ValidateText(publicParent, 4096); ArchiveTransactionFacts.ValidateText(privateIdentity, 4096);
        string? leaf = Path.GetFileName(privatePath);
        if (Path.GetDirectoryName(privatePath) != Path.GetDirectoryName(archivePath) || leaf is null ||
            !leaf.StartsWith(PrivateDirectoryPrefix, StringComparison.Ordinal) || Path.GetDirectoryName(stagePath) != privatePath ||
            publicParent == privateIdentity || original.Identity == stage.Identity) throw Invalid();
        var paths = new HashSet<string>(StringComparer.Ordinal) { archivePath, stagePath, privatePath };
        var identities = new HashSet<string>(StringComparer.Ordinal) { original.Identity, stage.Identity };
        foreach (var sidecar in sidecars)
            if (sidecar is null || Path.GetDirectoryName(sidecar.Path) != privatePath || !paths.Add(sidecar.Path) || !identities.Add(sidecar.Version.Identity) ||
                sidecar.Kind == ArchiveArtifactKind.Stage) throw Invalid();
        bool exchanged = published is not null;
        if ((published is null) != (retired is null) || (retired is null) != (retiredMetadata is null) ||
            published is not null && published != stage || retired is not null && retired != original ||
            phase is ArchiveExchangeUpdatePhase.MetadataApplied or ArchiveExchangeUpdatePhase.ExchangeIntent or ArchiveExchangeUpdatePhase.Exchanged && applied is null ||
            phase == ArchiveExchangeUpdatePhase.Exchanged && !exchanged ||
            phase is ArchiveExchangeUpdatePhase.Reserved or ArchiveExchangeUpdatePhase.MetadataApplied or ArchiveExchangeUpdatePhase.ExchangeIntent or
                ArchiveExchangeUpdatePhase.NotPublished && exchanged ||
            phase is ArchiveExchangeUpdatePhase.Reserved or ArchiveExchangeUpdatePhase.MetadataApplied or ArchiveExchangeUpdatePhase.ExchangeIntent or
                ArchiveExchangeUpdatePhase.Exchanged or ArchiveExchangeUpdatePhase.NotPublished && cleaned != 0 ||
            phase == ArchiveExchangeUpdatePhase.CleanupIntent && cleaned == sidecars.Count + 1 ||
            phase == ArchiveExchangeUpdatePhase.Completed && cleaned != sidecars.Count + 1) throw Invalid();
        Nonce = nonce; Sequence = sequence; PreparationSequence = preparationSequence; PreparationSha256 = preparationSha256; Phase = phase;
        ArchivePath = archivePath; PublicParentIdentity = publicParent; PrivateDirectoryPath = privatePath; PrivateDirectoryIdentity = privateIdentity;
        StagePath = stagePath; Original = original; OriginalMetadataSha256 = originalMetadata; PromisedMetadataSha256 = promised;
        RequiredOriginSha256 = requiredOrigin; Stage = stage; StageMetadataSha256 = stageMetadata; AppliedMetadataSha256 = applied;
        Published = published; Retired = retired; RetiredMetadataSha256 = retiredMetadata;
        Sidecars = Array.AsReadOnly(sidecars.ToArray()); CleanedCount = cleaned;
        if (Encoding.UTF8.GetByteCount(ToJson().ToJsonString()) > Jobs.JobJournal.MaxJournalLineBytes - 10) throw ArchiveWriteBudget.Limit();
    }

    internal static ArchiveExchangeUpdateFacts Reserve(ArchiveTransactionFacts preparation, string publicParentIdentity, string privateDirectoryPath,
        ArchiveFileObservation original, string promised, ArchiveFileObservation verifiedStage, IReadOnlyList<ArchiveContentCleanupArtifact> sidecars)
    {
        ArgumentNullException.ThrowIfNull(preparation); ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(verifiedStage);
        var facts = new ArchiveExchangeUpdateFacts(preparation.Nonce, 1, preparation.Sequence, Digest(preparation), ArchiveExchangeUpdatePhase.Reserved,
            preparation.ArchivePath, publicParentIdentity, privateDirectoryPath, preparation.ParentIdentity, preparation.StagePath, original.Version,
            original.MetadataDigest, promised, verifiedStage.Origin.Digest, verifiedStage.Version, verifiedStage.MetadataDigest, null, null, null, null, sidecars, 0);
        if (original.ParentIdentity != publicParentIdentity || original.LeafName != Path.GetFileName(preparation.ArchivePath) ||
            verifiedStage.ParentIdentity != preparation.ParentIdentity || verifiedStage.LeafName != Path.GetFileName(preparation.StagePath)) throw Invalid();
        facts.ValidateAfter(preparation, null); return facts;
    }

    internal ArchiveExchangeUpdateFacts Advance(ArchiveExchangeUpdatePhase phase, string? applied = null, ArchiveObjectVersion? published = null,
        ArchiveObjectVersion? retired = null, string? retiredMetadata = null) =>
        new(Nonce, checked(Sequence + 1), PreparationSequence, PreparationSha256, phase, ArchivePath, PublicParentIdentity, PrivateDirectoryPath,
            PrivateDirectoryIdentity, StagePath, Original, OriginalMetadataSha256, PromisedMetadataSha256, RequiredOriginSha256, Stage, StageMetadataSha256,
            applied ?? AppliedMetadataSha256, published ?? Published, retired ?? Retired, retiredMetadata ?? RetiredMetadataSha256, Sidecars,
            phase == ArchiveExchangeUpdatePhase.Cleaned ? checked(CleanedCount + 1) : CleanedCount);

    /// <summary>The expected exact object at cleanup position <paramref name="index"/> (sidecars, then the stage slot).</summary>
    internal (string Path, ArchiveObjectVersion Version, string? MetadataSha256) CleanupTarget(int index)
    {
        if (index < 0 || index > Sidecars.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (index < Sidecars.Count) return (Sidecars[index].Path, Sidecars[index].Version, Sidecars[index].MetadataSha256);
        // An unpublished stage may carry partially applied outer metadata: identify it by exact content instead.
        return Published is not null ? (StagePath, Retired!, RetiredMetadataSha256) : (StagePath, Stage, null);
    }

    internal void ValidateAfter(ArchiveTransactionFacts preparation, ArchiveExchangeUpdateFacts? previous)
    {
        if (preparation.RecordVersion != ArchiveTransactionFacts.SchemaVersion || preparation.Phase != ArchiveTransactionPhase.Verifying ||
            preparation.Original is not { } original || preparation.VerifiedStage is not { } stage || preparation.Manifest is null ||
            preparation.OriginEvidence is null || preparation.Nonce != Nonce || preparation.Sequence != PreparationSequence ||
            Digest(preparation) != PreparationSha256 || preparation.ArchivePath != ArchivePath || preparation.StagePath != StagePath ||
            preparation.ParentIdentity != PrivateDirectoryIdentity || original != Original || stage != Stage ||
            preparation.Sidecars.Count != Sidecars.Count) throw Invalid();
        for (int i = 0; i < Sidecars.Count; i++)
        {
            var reserved = preparation.Sidecars[i];
            if (reserved.Version is null || reserved.Path != Sidecars[i].Path || reserved.Version != Sidecars[i].Version ||
                Sidecars[i].Kind != (reserved.Role == ArchiveSidecarRole.OriginEvidence ? ArchiveArtifactKind.Provenance : ArchiveArtifactKind.Manifest))
                throw Invalid();
        }
        if (previous is null)
        {
            if (Sequence != 1 || Phase != ArchiveExchangeUpdatePhase.Reserved) throw Invalid();
            return;
        }
        if (Nonce != previous.Nonce || Sequence != previous.Sequence + 1 || PreparationSequence != previous.PreparationSequence ||
            PreparationSha256 != previous.PreparationSha256 || ArchivePath != previous.ArchivePath || PublicParentIdentity != previous.PublicParentIdentity ||
            PrivateDirectoryPath != previous.PrivateDirectoryPath || PrivateDirectoryIdentity != previous.PrivateDirectoryIdentity ||
            StagePath != previous.StagePath || Original != previous.Original || OriginalMetadataSha256 != previous.OriginalMetadataSha256 ||
            PromisedMetadataSha256 != previous.PromisedMetadataSha256 || RequiredOriginSha256 != previous.RequiredOriginSha256 ||
            Stage != previous.Stage || StageMetadataSha256 != previous.StageMetadataSha256 || !Sidecars.SequenceEqual(previous.Sidecars)) throw Invalid();
        var from = previous.Phase; var to = Phase;
        bool applied = from == ArchiveExchangeUpdatePhase.Reserved && to == ArchiveExchangeUpdatePhase.MetadataApplied && AppliedMetadataSha256 is not null;
        bool intent = from == ArchiveExchangeUpdatePhase.MetadataApplied && to == ArchiveExchangeUpdatePhase.ExchangeIntent;
        bool exchanged = from == ArchiveExchangeUpdatePhase.ExchangeIntent && to == ArchiveExchangeUpdatePhase.Exchanged;
        bool notPublished = from is ArchiveExchangeUpdatePhase.Reserved or ArchiveExchangeUpdatePhase.MetadataApplied or ArchiveExchangeUpdatePhase.ExchangeIntent &&
            to == ArchiveExchangeUpdatePhase.NotPublished;
        bool cleanupIntent = from is ArchiveExchangeUpdatePhase.Exchanged or ArchiveExchangeUpdatePhase.NotPublished or ArchiveExchangeUpdatePhase.Cleaned &&
            to == ArchiveExchangeUpdatePhase.CleanupIntent;
        bool cleaned = from == ArchiveExchangeUpdatePhase.CleanupIntent && to == ArchiveExchangeUpdatePhase.Cleaned;
        bool completed = from == ArchiveExchangeUpdatePhase.Cleaned && to == ArchiveExchangeUpdatePhase.Completed;
        bool review = from is not (ArchiveExchangeUpdatePhase.Completed or ArchiveExchangeUpdatePhase.NeedsReview) && to == ArchiveExchangeUpdatePhase.NeedsReview;
        if (!(applied || intent || exchanged || notPublished || cleanupIntent || cleaned || completed || review) ||
            !applied && AppliedMetadataSha256 != previous.AppliedMetadataSha256 ||
            !exchanged && (Published != previous.Published || Retired != previous.Retired || RetiredMetadataSha256 != previous.RetiredMetadataSha256) ||
            exchanged && Published is null ||
            CleanedCount != previous.CleanedCount + (cleaned ? 1 : 0)) throw Invalid();
    }

    internal JsonObject ToJson() => new()
    {
        ["t"] = RecordType, ["v"] = SchemaVersion, ["nonce"] = Nonce.ToString("N"), ["sequence"] = Sequence,
        ["preparationSequence"] = PreparationSequence, ["preparationSha256"] = PreparationSha256, ["phase"] = Phase.ToString(),
        ["archive"] = ArchivePath, ["publicParent"] = PublicParentIdentity, ["privateDirectory"] = PrivateDirectoryPath,
        ["privateIdentity"] = PrivateDirectoryIdentity, ["stage"] = StagePath, ["original"] = VersionJson(Original),
        ["originalMetadata"] = OriginalMetadataSha256, ["promised"] = PromisedMetadataSha256, ["requiredOrigin"] = RequiredOriginSha256,
        ["stageVersion"] = VersionJson(Stage), ["stageMetadata"] = StageMetadataSha256, ["appliedMetadata"] = AppliedMetadataSha256,
        ["published"] = VersionJson(Published), ["retired"] = VersionJson(Retired), ["retiredMetadata"] = RetiredMetadataSha256,
        ["sidecars"] = new JsonArray(Sidecars.Select(a => (JsonNode)new JsonObject { ["path"] = a.Path, ["kind"] = (int)a.Kind,
            ["version"] = VersionJson(a.Version), ["metadata"] = a.MetadataSha256, ["origin"] = a.OriginSha256 }).ToArray()),
        ["cleaned"] = CleanedCount,
    };

    internal static ArchiveExchangeUpdateFacts Parse(JsonObject node)
    {
        Keys(node, "t", "v", "nonce", "sequence", "preparationSequence", "preparationSha256", "phase", "archive", "publicParent", "privateDirectory",
            "privateIdentity", "stage", "original", "originalMetadata", "promised", "requiredOrigin", "stageVersion", "stageMetadata", "appliedMetadata",
            "published", "retired", "retiredMetadata", "sidecars", "cleaned");
        if (Text(node, "t") != RecordType || Number<int>(node, "v") != SchemaVersion || !Guid.TryParseExact(Text(node, "nonce"), "N", out var nonce) ||
            !Enum.TryParse<ArchiveExchangeUpdatePhase>(Text(node, "phase"), out var phase) || phase.ToString() != Text(node, "phase") ||
            node["sidecars"] is not JsonArray rows || rows.Count is < 3 or > ArchiveTransactionFacts.MaxArtifacts) throw Invalid();
        var sidecars = new ArchiveContentCleanupArtifact[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not JsonObject row) throw Invalid();
            Keys(row, "path", "kind", "version", "metadata", "origin");
            sidecars[i] = new(Text(row, "path"), (ArchiveArtifactKind)Number<int>(row, "kind"), ReadVersion(row, "version") ?? throw Invalid(),
                Text(row, "metadata"), Text(row, "origin"));
        }
        return new(nonce, Number<int>(node, "sequence"), Number<int>(node, "preparationSequence"), Text(node, "preparationSha256"), phase,
            Text(node, "archive"), Text(node, "publicParent"), Text(node, "privateDirectory"), Text(node, "privateIdentity"), Text(node, "stage"),
            ReadVersion(node, "original") ?? throw Invalid(), Text(node, "originalMetadata"), Text(node, "promised"), Text(node, "requiredOrigin"),
            ReadVersion(node, "stageVersion") ?? throw Invalid(), Text(node, "stageMetadata"), OptionalText(node, "appliedMetadata"),
            ReadVersion(node, "published"), ReadVersion(node, "retired"), OptionalText(node, "retiredMetadata"), sidecars, Number<int>(node, "cleaned"));
    }

    private static string Digest(ArchiveTransactionFacts facts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(facts.ToJson().ToJsonString())));
    private static JsonObject? VersionJson(ArchiveObjectVersion? version) => version is null ? null :
        new() { ["identity"] = version.Identity, ["length"] = version.Length, ["sha256"] = version.Sha256 };
    private static ArchiveObjectVersion? ReadVersion(JsonObject node, string key)
    {
        if (node[key] is null) return null;
        if (node[key] is not JsonObject value) throw Invalid();
        Keys(value, "identity", "length", "sha256");
        return new(Text(value, "identity"), Number<long>(value, "length"), Text(value, "sha256"));
    }
    private static void Keys(JsonObject node, params string[] keys)
    { if (node.Count != keys.Length || keys.Any(key => !node.ContainsKey(key))) throw Invalid(); }
    private static string Text(JsonObject node, string key) => OptionalText(node, key) ?? throw Invalid();
    private static string? OptionalText(JsonObject node, string key) => node[key] is null ? null :
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw Invalid();
    private static T Number<T>(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<T>(out var number) ? number : throw Invalid();
    private static ArgumentException Invalid() => new("Invalid, stale or inconsistent archive exchange-update facts.");
}
