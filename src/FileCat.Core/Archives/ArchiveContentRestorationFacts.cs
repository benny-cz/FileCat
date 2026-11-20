using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FileCat.Core.Archives;

public enum ArchiveContentRestorationPhase { CommitIntent, Committed, NeedsReview }

/// <summary>Link to a fresh, verified restore job. This never authorizes replay of an interrupted write.</summary>
public sealed class ArchiveContentRestorationFacts
{
    internal const string RecordType = "archive-content-restoration";
    public const int SchemaVersion = 1;
    public const int MaximumAttempts = 16;
    public Guid Nonce { get; }
    public int Sequence { get; }
    public int Attempt { get; }
    public ArchiveContentRestorationPhase Phase { get; }
    public string ContentSha256 { get; }
    public string CleanupSha256 { get; }
    public Guid RestoreNonce { get; }
    public string RestoreJournalPath { get; }
    public Guid NativeTransactionId { get; }
    public ArchiveObjectVersion Before { get; }
    public string BeforeMetadataSha256 { get; }
    public string BeforeOriginSha256 { get; }
    public ArchiveObjectVersion Restored { get; }
    public string RestoredMetadataSha256 { get; }
    public string RestoredOriginSha256 { get; }

    private ArchiveContentRestorationFacts(Guid nonce, int sequence, int attempt, ArchiveContentRestorationPhase phase, string content, string cleanup,
        Guid restoreNonce, string restoreJournalPath, Guid transactionId, ArchiveObjectVersion before, string beforeMetadata, string beforeOrigin,
        ArchiveObjectVersion restored, string restoredMetadata, string restoredOrigin)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(restored);
        ArchiveTransactionFacts.ValidatePath(restoreJournalPath);
        if (nonce == Guid.Empty || restoreNonce == Guid.Empty || restoreNonce == nonce || transactionId == Guid.Empty ||
            sequence is < 1 or > 64 || attempt is < 1 or > MaximumAttempts || !Enum.IsDefined(phase) || before.Identity != restored.Identity) throw Invalid();
        foreach (string digest in new[] { content, cleanup, beforeMetadata, beforeOrigin, restoredMetadata, restoredOrigin }) ArchiveWritePolicy.ValidateDigest(digest);
        Nonce = nonce; Sequence = sequence; Attempt = attempt; Phase = phase; ContentSha256 = content; CleanupSha256 = cleanup;
        RestoreNonce = restoreNonce; RestoreJournalPath = restoreJournalPath; NativeTransactionId = transactionId; Before = before; BeforeMetadataSha256 = beforeMetadata; BeforeOriginSha256 = beforeOrigin;
        Restored = restored; RestoredMetadataSha256 = restoredMetadata; RestoredOriginSha256 = restoredOrigin;
    }

    internal static ArchiveContentRestorationFacts Begin(ArchiveTransactionFacts preparation, ArchiveContentUpdateFacts content,
        ArchiveContentCleanupFacts cleanup, ArchiveContentRestorationFacts? previous, ArchiveContentUpdateFacts fresh, string restoreJournalPath,
        ArchiveFileObservation before, ArchiveFileObservation snapshot, ArchiveFileObservation restored)
    {
        var row = cleanup.Artifacts[^1];
        if (fresh.Phase != ArchiveContentUpdatePhase.CommitIntent || fresh.NativeTransactionId is not { } transaction ||
            fresh.PrivateOutput != restored.Version || fresh.PrivateMetadataSha256 != restored.MetadataDigest || fresh.RequiredOriginSha256 != restored.Origin.Digest ||
            fresh.OriginalMetadataSha256 != before.MetadataDigest || snapshot.Version != content.Snapshot || snapshot.Version != row.Version ||
            snapshot.MetadataDigest != row.MetadataSha256 || snapshot.Origin.Digest != row.OriginSha256 ||
            before.ParentIdentity != preparation.ParentIdentity || before.LeafName != Path.GetFileName(preparation.ArchivePath) ||
            restored.ParentIdentity != before.ParentIdentity || restored.LeafName != before.LeafName) throw Invalid();
        var facts = new ArchiveContentRestorationFacts(content.Nonce, (previous?.Sequence ?? 0) + 1, (previous?.Attempt ?? 0) + 1,
            ArchiveContentRestorationPhase.CommitIntent, Digest(content.ToJson()), Digest(cleanup.ToJson()), fresh.Nonce, restoreJournalPath, transaction,
            before.Version, before.MetadataDigest, before.Origin.Digest, restored.Version, restored.MetadataDigest, restored.Origin.Digest);
        facts.ValidateAfter(preparation, content, cleanup, previous); return facts;
    }
    internal ArchiveContentRestorationFacts Advance(ArchiveContentRestorationPhase phase, ArchiveFileObservation? published = null)
    {
        if (phase == ArchiveContentRestorationPhase.Committed && (published is null || published.Version != Restored ||
            published.MetadataDigest != RestoredMetadataSha256 || published.Origin.Digest != RestoredOriginSha256)) throw Invalid();
        return new(Nonce, Sequence + 1, Attempt, phase, ContentSha256, CleanupSha256, RestoreNonce, RestoreJournalPath, NativeTransactionId,
            Before, BeforeMetadataSha256, BeforeOriginSha256, Restored, RestoredMetadataSha256, RestoredOriginSha256);
    }
    internal bool MatchesRestored(ArchiveFileObservation observed) => observed.Version == Restored &&
        observed.MetadataDigest == RestoredMetadataSha256 && observed.Origin.Digest == RestoredOriginSha256;
    internal void ValidateAfter(ArchiveTransactionFacts preparation, ArchiveContentUpdateFacts content, ArchiveContentCleanupFacts cleanup,
        ArchiveContentRestorationFacts? previous)
    {
        if (Nonce != preparation.Nonce || Nonce != content.Nonce || Nonce != cleanup.Nonce || content.Snapshot is null ||
            content.PrivateOutput is null || ContentSha256 != Digest(content.ToJson()) || CleanupSha256 != Digest(cleanup.ToJson()) ||
            cleanup.IsResolved || cleanup.Artifacts[^1].Version != content.Snapshot || Before != content.PrivateOutput ||
            BeforeMetadataSha256 != content.PrivateMetadataSha256 || BeforeOriginSha256 != content.RequiredOriginSha256 ||
            Restored.Length != content.Snapshot.Length || Restored.Sha256 != content.Snapshot.Sha256 || Restored == Before) throw Invalid();
        if (previous is null)
        { if (Sequence != 1 || Attempt != 1 || Phase != ArchiveContentRestorationPhase.CommitIntent) throw Invalid(); return; }
        bool retry = Attempt == previous.Attempt + 1 && Phase == ArchiveContentRestorationPhase.CommitIntent &&
            previous.Phase is ArchiveContentRestorationPhase.CommitIntent or ArchiveContentRestorationPhase.NeedsReview &&
            RestoreNonce != previous.RestoreNonce && RestoreJournalPath != previous.RestoreJournalPath && NativeTransactionId != previous.NativeTransactionId;
        bool sameAttempt = Attempt == previous.Attempt && RestoreNonce == previous.RestoreNonce && RestoreJournalPath == previous.RestoreJournalPath && NativeTransactionId == previous.NativeTransactionId &&
            previous.Phase == ArchiveContentRestorationPhase.CommitIntent && Phase is ArchiveContentRestorationPhase.Committed or ArchiveContentRestorationPhase.NeedsReview;
        if (!(retry || sameAttempt) || Sequence != previous.Sequence + 1 || Nonce != previous.Nonce || ContentSha256 != previous.ContentSha256 ||
            CleanupSha256 != previous.CleanupSha256 || Before != previous.Before || BeforeMetadataSha256 != previous.BeforeMetadataSha256 ||
            BeforeOriginSha256 != previous.BeforeOriginSha256 || Restored != previous.Restored || (!retry && RestoredMetadataSha256 != previous.RestoredMetadataSha256) ||
            RestoredOriginSha256 != previous.RestoredOriginSha256) throw Invalid();
    }
    internal string Sha256 => Digest(ToJson());
    internal JsonObject ToJson() => new()
    {
        ["t"] = RecordType, ["v"] = SchemaVersion, ["nonce"] = Nonce.ToString("N"), ["sequence"] = Sequence, ["attempt"] = Attempt,
        ["phase"] = Phase.ToString(), ["content"] = ContentSha256, ["cleanup"] = CleanupSha256, ["restoreNonce"] = RestoreNonce.ToString("N"),
        ["restoreJournal"] = RestoreJournalPath, ["transaction"] = NativeTransactionId.ToString("N"), ["before"] = Version(Before), ["beforeMetadata"] = BeforeMetadataSha256,
        ["beforeOrigin"] = BeforeOriginSha256, ["restored"] = Version(Restored), ["restoredMetadata"] = RestoredMetadataSha256, ["restoredOrigin"] = RestoredOriginSha256,
    };
    internal static ArchiveContentRestorationFacts Parse(JsonObject node)
    {
        string[] keys = ["t", "v", "nonce", "sequence", "attempt", "phase", "content", "cleanup", "restoreNonce", "restoreJournal", "transaction", "before", "beforeMetadata", "beforeOrigin", "restored", "restoredMetadata", "restoredOrigin"];
        if (node.Count != keys.Length || keys.Any(key => !node.ContainsKey(key)) || Text(node, "t") != RecordType || Number(node, "v") != SchemaVersion ||
            !Guid.TryParseExact(Text(node, "nonce"), "N", out var nonce) || !Guid.TryParseExact(Text(node, "restoreNonce"), "N", out var restore) ||
            !Guid.TryParseExact(Text(node, "transaction"), "N", out var transaction) ||
            !Enum.TryParse<ArchiveContentRestorationPhase>(Text(node, "phase"), out var phase) || phase.ToString() != Text(node, "phase")) throw Invalid();
        return new(nonce, Number(node, "sequence"), Number(node, "attempt"), phase, Text(node, "content"), Text(node, "cleanup"), restore, Text(node, "restoreJournal"), transaction,
            ReadVersion(node["before"]), Text(node, "beforeMetadata"), Text(node, "beforeOrigin"), ReadVersion(node["restored"]), Text(node, "restoredMetadata"), Text(node, "restoredOrigin"));
    }
    private static JsonObject Version(ArchiveObjectVersion version) => new() { ["identity"] = version.Identity, ["length"] = version.Length, ["sha256"] = version.Sha256 };
    private static ArchiveObjectVersion ReadVersion(JsonNode? node)
    {
        if (node is not JsonObject value || value.Count != 3 || !value.ContainsKey("identity") || !value.ContainsKey("sha256") ||
            value["length"] is not JsonValue length || !length.TryGetValue<long>(out var size)) throw Invalid();
        return new(Text(value, "identity"), size, Text(value, "sha256"));
    }
    private static string Digest(JsonObject node) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));
    private static string Text(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw Invalid();
    private static int Number(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : throw Invalid();
    private static ArgumentException Invalid() => new("Invalid retained-content restoration history.");
}
