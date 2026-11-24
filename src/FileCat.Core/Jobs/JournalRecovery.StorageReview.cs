using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.State;

namespace FileCat.Core.Jobs;

public static partial class JournalRecovery
{
    internal static StorageExportResult ExportStorageReview(StorageReviewEvidence evidence, StorageReviewedJournal reviewed,
        string destination, CancellationToken ct)
    {
        if (!SameStorageReview(ReadStorageReview(evidence, ct), reviewed))
            throw new IOException("The storage evidence changed; review the current snapshot before exporting.");
        using var output = evidence.CreateExport(destination, ct);
        CopyStorageReview(evidence, reviewed, output.Stream, ct);
        return output.Publish(ct);
    }

    internal static void CopyStorageReview(StorageReviewEvidence evidence, StorageReviewedJournal reviewed, Stream destination, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("A writable export stream is required.", nameof(destination));
        var current = ReadStorageReview(evidence, ct);
        if (!SameStorageReview(current, reviewed)) throw new IOException("The storage evidence changed; review the current snapshot before exporting.");
        evidence.Journal.Position = 0;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024]; long remaining = reviewed.Evidence.ByteLength;
        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            int got = evidence.Journal.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
            if (got == 0) throw new IOException("The retained evidence ended during export.");
            destination.Write(buffer, 0, got); digest.AppendData(buffer, 0, got); remaining -= got;
        }
        ct.ThrowIfCancellationRequested();
        if (evidence.Journal.ReadByte() != -1 || Convert.ToHexString(digest.GetHashAndReset()) != reviewed.Evidence.Sha256)
            throw new IOException("The exported evidence no longer matches the reviewed snapshot.");
    }

    internal static StorageReviewedJournal ReadStorageReview(StorageReviewEvidence evidence, CancellationToken ct = default)
    {
        RequireStorageOwner(evidence);
        var version = InspectStorageEvidence(evidence.Journal, () => evidence.JournalIdentity, ct);
        evidence.Journal.Position = 0;
        bool named = StorageJournalPolicy.TryJobId(evidence.JournalLeaf, out var job);
        var read = StorageTransformJournalReader.Read(evidence.Journal, named ? job : Guid.Empty, ct: ct);
        if (!named || read.Collector.Header is null) read.Collector.Fault();
        var after = InspectStorageEvidence(evidence.Journal, () => evidence.JournalIdentity, ct);
        if (version != after) throw new IOException("The owned journal changed during review; review its exact current contents again.");
        Guid association = named ? job : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(evidence.DirectoryIdentity + "\0" + evidence.JournalLeaf)).AsSpan(0, 16));
        return new(association, evidence.DirectoryIdentity, evidence.JournalLeaf, version, DescribeStorageHistory(read))
        { CreatedUtc = read.Collector.Header?.CreatedUtc };
    }

    internal static StorageReviewStatus ReadStorageReviewStatus(StorageReviewEvidence evidence, CancellationToken ct = default)
    {
        var current = ReadStorageReview(evidence, ct);
        var issues = ImmutableArray.CreateBuilder<string>();
        var catalog = StorageCatalog(evidence, current.AssociationId, ct);
        foreach (var info in catalog)
        {
            ct.ThrowIfCancellationRequested();
            if (!StorageReviewNames.TryParse(info.Leaf, out var association, out var kind, out _, out var temporary) ||
                association != current.AssociationId || kind != "ack" || temporary) continue;
            if (TryStoragePair(evidence, info, current, ct) is { } pair)
                return new(current, pair.Acknowledgment, false, issues.ToImmutable());
        }
        return new(current, null, current.History.ReviewPending, issues.ToImmutable());
    }

    /// <summary>Only a platform-owned inactive journal can authorize private artifact publication. The journal is
    /// never appended/repaired, and acknowledgment never converts an uncertain effect into a known effect.</summary>
    internal static StorageReviewStatus AcknowledgeStorageReview(StorageReviewEvidence evidence, StorageReviewedJournal reviewed,
        string reviewText, StorageReviewChoice choice, CancellationToken ct = default)
    {
        if (choice != StorageReviewChoice.AcknowledgeReviewedHistory) throw new ArgumentOutOfRangeException(nameof(choice));
        if (reviewText is null || Encoding.UTF8.GetByteCount(reviewText) > StorageReviewNames.ReportBytes / 2)
            throw new IOException("The retained review text exceeds its storage-history allowance.");
        var current = ReadStorageReview(evidence, ct);
        if (!SameStorageReview(current, reviewed)) throw new IOException("The journal or reviewed facts changed; review the current snapshot before acknowledging.");
        var issues = ImmutableArray.CreateBuilder<string>();
        var catalog = StorageCatalog(evidence, current.AssociationId, ct);
        var pairs = new List<StorageVerifiedPair>();
        foreach (var info in catalog)
        {
            ct.ThrowIfCancellationRequested();
            if (TryStoragePair(evidence, info, current, ct, acceptHistorical: true) is { } pair)
            {
                pairs.Add(pair);
                if (pair.MatchesCurrent && pair.ReviewTextHash == TextHash(reviewText))
                    return new(current, pair.Acknowledgment, false, issues.ToImmutable()); // immutable/idempotent, no extra temporary
            }
        }
        ReconcileStorageArtifacts(evidence, current, catalog, pairs, issues, ct);
        catalog = StorageCatalog(evidence, current.AssociationId, ct);
        StorageAcknowledgmentFailures.RequireCapacity();
        Guid reportId = Guid.NewGuid(), acknowledgmentId = Guid.NewGuid();
        string reportLeaf = StorageReviewNames.Published(current.AssociationId, "report", reportId);
        string acknowledgmentLeaf = StorageReviewNames.Published(current.AssociationId, "ack", acknowledgmentId);
        var report = new StorageRetainedReviewReport(StorageReviewNames.Version, reportId, current, DateTime.UtcNow, reviewText);
        byte[] reportBytes = StorageReviewBytes(report, StorageReviewNames.ReportBytes);
        // The compact acknowledgment's exact size is bounded independently. Retained pair + publication overlap fits 2 MiB.
        long retained = catalog.Aggregate(0L, (sum, item) => checked(sum + item.ByteLength));
        if (retained > StorageJournalPolicy.ReviewWriteBytes - reportBytes.Length - StorageReviewNames.AckBytes)
            throw new IOException("Storage review artifacts fill this journal's write allowance; existing evidence was retained.");

        StorageEvidenceVersion reportVersion;
        using (var artifact = evidence.CreateArtifact(StorageReviewNames.Temporary(current.AssociationId, "report", reportId), ct))
        {
            artifact.Stream.Write(reportBytes); artifact.FlushDurable(ct);
            reportVersion = InspectStorageEvidence(artifact.Stream, () => artifact.Identity, ct);
            evidence.PublishNew(artifact, reportLeaf, ct);
            if (artifact.Leaf != reportLeaf || InspectStorageEvidence(artifact.Stream, () => artifact.Identity, ct) != reportVersion)
                throw new IOException("The published storage report did not retain its exact identity and bytes.");
        }
        // Recheck the actual published report, not the serializer's input or an App-side closure boolean.
        using (var saved = evidence.OpenArtifact(new(reportLeaf, reportVersion.Identity, reportVersion.ByteLength), ct))
        {
            var actual = ReadStorageArtifact<StorageRetainedReviewReport>(saved, StorageReviewNames.ReportBytes, ct);
            if (actual.Version != reportVersion || actual.Value.ReportId != reportId || !SameStorageReview(actual.Value.Reviewed, current))
                throw new IOException("The retained storage report changed before acknowledgment.");
        }
        if (!SameStorageReview(ReadStorageReview(evidence, ct), current))
            throw new IOException("The journal changed before acknowledgment publication; review remains pending.");
        var acknowledgment = new StorageReviewAcknowledgment(StorageReviewNames.Version, acknowledgmentId, reportId,
            choice, StorageReviewBinding.From(current), reportLeaf, reportVersion, DateTime.UtcNow);
        byte[] acknowledgmentBytes = StorageReviewBytes(acknowledgment, StorageReviewNames.AckBytes);
        if (reportBytes.Length + acknowledgmentBytes.Length > StorageJournalPolicy.ReviewBytes) throw new IOException("The retained storage review pair exceeds 1 MiB.");
        using (var artifact = evidence.CreateArtifact(StorageReviewNames.Temporary(current.AssociationId, "ack", acknowledgmentId), ct))
        {
            artifact.Stream.Write(acknowledgmentBytes); artifact.FlushDurable(ct);
            var exact = InspectStorageEvidence(artifact.Stream, () => artifact.Identity, ct);
            try
            {
                // Keep the journal's exclusion owner through both durable publications.
                evidence.PublishNew(artifact, acknowledgmentLeaf, ct);
            }
            catch
            {
                // A namespace publication may have entered before a later flush failed. Readable bytes alone must
                // not make a failed acknowledgment succeed in this session. Retire only our exact owned artifact.
                StorageAcknowledgmentFailures.Record(evidence, acknowledgmentId, exact);
                try { evidence.DeleteArtifact(artifact, exact, CancellationToken.None); StorageAcknowledgmentFailures.Forget(evidence, acknowledgmentId); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                throw;
            }
        }
        var newInfo = StorageCatalog(evidence, current.AssociationId, ct).SingleOrDefault(i => i.Leaf == acknowledgmentLeaf)
            ?? throw new IOException("The new storage acknowledgment is missing; review remains pending.");
        var verified = TryStoragePair(evidence, newInfo, current, ct)
            ?? throw new IOException("The new storage acknowledgment/report pair could not be verified; review remains pending.");
        if (!SameStorageReview(ReadStorageReview(evidence, ct), current))
            throw new IOException("The journal changed after publication; its old acknowledgment no longer applies.");
        // Old reports stay immutable. A deliberately revised review is a new report/pair; obsolete pairs retire only now.
        var retainedReports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var old in pairs)
        {
            if (old.Acknowledgment.AcknowledgmentId == acknowledgmentId) continue;
            if (!TryDeleteStorageArtifact(evidence, old.AckInfo, old.AckVersion, issues, ct)) retainedReports.Add(old.Acknowledgment.ReportLeaf);
        }
        foreach (var old in pairs.DistinctBy(p => p.Acknowledgment.ReportLeaf))
            if (!retainedReports.Contains(old.Acknowledgment.ReportLeaf)) TryDeleteStorageArtifact(evidence,
                new(old.Acknowledgment.ReportLeaf, old.Acknowledgment.ReportEvidence.Identity, old.Acknowledgment.ReportEvidence.ByteLength),
                old.Acknowledgment.ReportEvidence, issues, ct);
        return new(current, verified.Acknowledgment, false, issues.ToImmutable());
    }

    internal static bool PruneStorageJournal(StorageReviewEvidence evidence, StorageReviewedJournal reviewed, CancellationToken ct = default)
    {
        var status = ReadStorageReviewStatus(evidence, ct);
        if (!SameStorageReview(status.Current, reviewed) || status.ReviewPending) return false;
        // No artifact dependency is released until this exact inactive journal has actually been removed.
        evidence.DeleteJournal(status.Current.Evidence, ct);
        return true;
    }

    internal static StorageRetainedReviewSnapshot? ReadRetainedStorageReview(StorageRetainedReviewEvidence evidence,
        StorageReviewArtifactInfo acknowledgment, CancellationToken ct = default)
    {
        RequireStorageArtifacts(evidence);
        if (!StorageReviewNames.TryParse(acknowledgment.Leaf, out var association, out _, out _, out _)) return null;
        var pair = TryStoragePair(evidence, acknowledgment, null, ct, acceptHistorical: true, association);
        if (pair is null) return null;
        using var saved = evidence.OpenArtifact(new(pair.Acknowledgment.ReportLeaf, pair.Acknowledgment.ReportEvidence.Identity,
            pair.Acknowledgment.ReportEvidence.ByteLength), ct);
        var report = ReadStorageArtifact<StorageRetainedReviewReport>(saved, StorageReviewNames.ReportBytes, ct);
        if (report.Version != pair.Acknowledgment.ReportEvidence) return null;
        // Close artifact ownership before asking the platform for independent journal-name observations.
        saved.Dispose();
        bool present = evidence.JournalExists(pair.Acknowledgment.Reviewed.JournalLeaf, association, ct);
        return new(acknowledgment.Leaf, pair.AckVersion, pair.Acknowledgment, report.Value, present);
    }

    internal static bool PruneRetainedStorageReview(StorageRetainedReviewEvidence evidence,
        StorageRetainedReviewSnapshot reviewed, CancellationToken ct = default)
    {
        var current = ReadRetainedStorageReview(evidence, new(reviewed.AcknowledgmentLeaf,
            reviewed.AcknowledgmentEvidence.Identity, reviewed.AcknowledgmentEvidence.ByteLength), ct);
        if (current is null || current.JournalPresent || current.AcknowledgmentEvidence != reviewed.AcknowledgmentEvidence ||
            current.Acknowledgment != reviewed.Acknowledgment) return false;
        var ack = current.Acknowledgment;
        using (var report = evidence.OpenArtifact(new(ack.ReportLeaf, ack.ReportEvidence.Identity, ack.ReportEvidence.ByteLength), ct))
            evidence.DeleteUnreferenced(report, ack.ReportEvidence, ack.Reviewed.JournalLeaf, ack.Reviewed.AssociationId, ct);
        // Failed report removal leaves the matching acknowledgment intact. Failed acknowledgment removal remains charged.
        using var artifact = evidence.OpenArtifact(new(current.AcknowledgmentLeaf, current.AcknowledgmentEvidence.Identity,
            current.AcknowledgmentEvidence.ByteLength), ct);
        evidence.DeleteUnreferenced(artifact, current.AcknowledgmentEvidence, ack.Reviewed.JournalLeaf, ack.Reviewed.AssociationId, ct);
        return true;
    }

    private sealed record StorageVerifiedPair(StorageReviewAcknowledgment Acknowledgment, StorageReviewArtifactInfo AckInfo,
        StorageEvidenceVersion AckVersion, bool MatchesCurrent, string ReviewTextHash);

    private static StorageVerifiedPair? TryStoragePair(StorageReviewArtifacts evidence, StorageReviewArtifactInfo info,
        StorageReviewedJournal? current, CancellationToken ct, bool acceptHistorical = false, Guid? expectedAssociation = null)
    {
        if (!StorageReviewNames.TryParse(info.Leaf, out var association, out var kind, out var nonce, out var temporary) ||
            association != (current?.AssociationId ?? expectedAssociation) || kind != "ack" || temporary) return null;
        try
        {
            StorageReviewAcknowledgment ack; StorageEvidenceVersion ackVersion;
            using (var artifact = evidence.OpenArtifact(info, ct))
            { var read = ReadStorageArtifact<StorageReviewAcknowledgment>(artifact, StorageReviewNames.AckBytes, ct); ack = read.Value; ackVersion = read.Version; }
            if (ack is null || ack.Version != StorageReviewNames.Version || ack.AcknowledgmentId != nonce ||
                ack.Choice != StorageReviewChoice.AcknowledgeReviewedHistory || ack.AcknowledgedUtc.Kind != DateTimeKind.Utc ||
                ack.Reviewed is null || ack.Reviewed.AssociationId != association || ack.Reviewed.DirectoryIdentity != evidence.DirectoryIdentity ||
                !ValidStorageVersion(ack.Reviewed.Evidence) || string.IsNullOrEmpty(ack.Reviewed.JournalLeaf) ||
                !StorageJournalPolicy.IsCandidate(ack.Reviewed.JournalLeaf) || ack.Reviewed.JournalLeaf.Length > 255 ||
                ack.Reviewed.JournalLeaf.IndexOfAny(['\\', '/', ':', '\0']) >= 0 ||
                current is not null && ack.Reviewed.JournalLeaf != current.JournalLeaf || !ValidStorageVersion(ack.ReportEvidence) ||
                !StorageReviewNames.TryParse(ack.ReportLeaf, out var reportAssociation, out var reportKind, out var reportNonce, out var reportTemp) ||
                reportAssociation != association || reportKind != "report" || reportTemp || reportNonce != ack.ReportId) return null;
            if (StorageAcknowledgmentFailures.Find(evidence.DirectoryIdentity, ack.Reviewed.Evidence.Identity, nonce) is not null) return null;
            bool matches = current is not null && ack.Reviewed == StorageReviewBinding.From(current);
            if (!matches && !acceptHistorical) return null;
            using var saved = evidence.OpenArtifact(new(ack.ReportLeaf, ack.ReportEvidence.Identity, ack.ReportEvidence.ByteLength), ct);
            var report = ReadStorageArtifact<StorageRetainedReviewReport>(saved, StorageReviewNames.ReportBytes, ct);
            if (report.Version != ack.ReportEvidence || !ValidStorageReport(report.Value, association, ack.ReportId) ||
                StorageReviewBinding.From(report.Value.Reviewed) != ack.Reviewed || matches && !SameStorageReview(report.Value.Reviewed, current)) return null;
            return new(ack, info, ackVersion, matches, TextHash(report.Value.ReviewText));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or OverflowException)
        { return null; }
    }

    private static void ReconcileStorageArtifacts(StorageReviewEvidence evidence, StorageReviewedJournal current,
        IReadOnlyList<StorageReviewArtifactInfo> catalog, IReadOnlyList<StorageVerifiedPair> pairs,
        ImmutableArray<string>.Builder issues, CancellationToken ct)
    {
        foreach (var info in catalog)
        {
            ct.ThrowIfCancellationRequested();
            if (!StorageReviewNames.TryParse(info.Leaf, out var association, out var kind, out var nonce, out var temporary) || association != current.AssociationId) continue;
            if (kind == "ack" && StorageAcknowledgmentFailures.Find(evidence, nonce) is { } failed)
            {
                if (TryDeleteStorageArtifact(evidence, info, failed, issues, ct)) StorageAcknowledgmentFailures.Forget(evidence, nonce);
                continue;
            }
            if (!temporary && (kind != "report" || pairs.Any(p => p.Acknowledgment.ReportLeaf == info.Leaf))) continue;
            try
            {
                using var artifact = evidence.OpenArtifact(info, ct);
                StorageEvidenceVersion version;
                if (temporary) version = InspectStorageEvidence(artifact.Stream, () => artifact.Identity, ct); // no valid acknowledgment can reference a temporary name
                else
                {
                    var report = ReadStorageArtifact<StorageRetainedReviewReport>(artifact, StorageReviewNames.ReportBytes, ct);
                    if (!ValidStorageReport(report.Value, association, nonce) || report.Value.Reviewed.DirectoryIdentity != evidence.DirectoryIdentity ||
                        report.Value.Reviewed.JournalLeaf != evidence.JournalLeaf) continue;
                    version = report.Version;
                }
                evidence.DeleteArtifact(artifact, version, ct);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            { AddStorageIssue(issues, "An associated storage artifact was retained: " + error.Message); }
        }
    }

    private static IReadOnlyList<StorageReviewArtifactInfo> StorageCatalog(StorageReviewEvidence evidence, Guid association, CancellationToken ct)
    {
        RequireStorageOwner(evidence);
        var catalog = evidence.Artifacts(association, StorageReviewNames.ArtifactCount, ct);
        if (catalog.Count > StorageReviewNames.ArtifactCount || catalog.Any(i => i is null || i.ByteLength < 0 || string.IsNullOrEmpty(i.Identity) || i.Identity.Length > 1024))
            throw new IOException("Storage artifact inventory exceeds its bounded review allowance.");
        return catalog;
    }
    private static bool ValidStorageReport(StorageRetainedReviewReport report, Guid association, Guid nonce) =>
        report is not null && report.Version == StorageReviewNames.Version && report.ReportId == nonce && report.Reviewed is not null &&
        report.Reviewed.AssociationId == association && report.Reviewed.History is not null && report.Reviewed.History.Prefix is not null &&
        (report.Reviewed.History.JournalSchema is null || report.Reviewed.History is
            { JournalSchema: StorageJournalPolicy.EfsRecoveryVersion, Action: StorageAction.EncryptEfs }) &&
        !report.Reviewed.History.Roots.IsDefault &&
        (report.Reviewed.History.Result is null || !report.Reviewed.History.Result.Roots.IsDefault && !report.Reviewed.History.Result.Reasons.IsDefault) &&
        ValidStorageVersion(report.Reviewed.Evidence) && report.Reviewed.Evidence.ByteLength >= report.Reviewed.History.Prefix.ByteLength &&
        report.Reviewed.History.Prefix.Sequence >= 0 && report.Reviewed.History.Prefix.ByteLength >= 0 && report.ReviewedUtc.Kind == DateTimeKind.Utc &&
        report.ReviewText is not null && Encoding.UTF8.GetByteCount(report.ReviewText) <= StorageReviewNames.ReportBytes / 2;
    private static bool ValidStorageVersion(StorageEvidenceVersion version) => version is not null && !string.IsNullOrEmpty(version.Identity) &&
        version.Identity.Length <= 1024 && version.ByteLength >= 0 && version.Sha256 is { Length: 64 } && version.Sha256.All(char.IsAsciiHexDigit);
    private static bool SameStorageReview(StorageReviewedJournal? first, StorageReviewedJournal? second) => first is not null && second is not null &&
        StorageReviewBytes(first, StorageReviewNames.ReportBytes).AsSpan().SequenceEqual(StorageReviewBytes(second, StorageReviewNames.ReportBytes));
    private static byte[] StorageReviewBytes<T>(T value, int maximum)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, StorageTransformJournalCollector.Json);
        if (bytes.Length > maximum) throw new IOException("The exact retained storage record exceeds its byte allowance; no facts were truncated.");
        return bytes;
    }
    private static (T Value, StorageEvidenceVersion Version) ReadStorageArtifact<T>(StorageReviewArtifact artifact, int maximum, CancellationToken ct) where T : class
    {
        if (artifact.Stream.Length > maximum) throw new IOException("The storage review artifact exceeds its schema allowance.");
        var version = InspectStorageEvidence(artifact.Stream, () => artifact.Identity, ct);
        if (version.ByteLength > maximum) throw new IOException("The storage review artifact exceeds its schema allowance.");
        byte[] bytes = new byte[(int)version.ByteLength]; artifact.Stream.Position = 0;
        for (int offset = 0; offset < bytes.Length;)
        { ct.ThrowIfCancellationRequested(); int got = artifact.Stream.Read(bytes, offset, bytes.Length - offset); if (got == 0) throw new IOException("The storage artifact changed during reading."); offset += got; }
        if (artifact.Stream.ReadByte() != -1 || artifact.Identity != version.Identity || Convert.ToHexString(SHA256.HashData(bytes)) != version.Sha256)
            throw new IOException("The storage artifact changed during verification.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        StorageTransformJournalCollector.ValidateTree(document.RootElement);
        return (document.RootElement.Deserialize<T>(StorageTransformJournalCollector.Json) ?? throw new InvalidDataException("The storage review record is missing."), version);
    }
    internal static StorageEvidenceVersion InspectStorageEvidence(Stream stream, Func<string> identity, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!stream.CanRead || !stream.CanSeek) throw new IOException("Complete storage evidence must be readable through its held object.");
        string id = identity(); long length = stream.Length;
        if (string.IsNullOrEmpty(id) || id.Length > 1024 || length < 0) throw new IOException("Storage evidence identity or length is unavailable.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[64 * 1024]; stream.Position = 0;
        long read = 0;
        while (read < length)
        { ct.ThrowIfCancellationRequested(); int got = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, length - read)); if (got == 0) throw new IOException("Storage evidence ended during complete hashing."); hash.AppendData(buffer, 0, got); read += got; }
        if (stream.ReadByte() != -1 || stream.Length != length || identity() != id) throw new IOException("Storage evidence changed during complete hashing.");
        return new(id, length, Convert.ToHexString(hash.GetHashAndReset()));
    }
    private static bool TryDeleteStorageArtifact(StorageReviewEvidence evidence, StorageReviewArtifactInfo info, StorageEvidenceVersion expected,
        ImmutableArray<string>.Builder issues, CancellationToken ct)
    {
        try
        {
            using var artifact = evidence.OpenArtifact(info, ct);
            if (InspectStorageEvidence(artifact.Stream, () => artifact.Identity, ct) != expected) throw new IOException("The obsolete artifact changed; it was retained.");
            evidence.DeleteArtifact(artifact, expected, ct); return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { AddStorageIssue(issues, "An obsolete storage review artifact was retained: " + error.Message); return false; }
    }
    private static string TextHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void AddStorageIssue(ImmutableArray<string>.Builder issues, string message)
    { if (issues.Count < 64) issues.Add(message.Length <= 2000 ? message : message[..2000]); }
    private static void RequireStorageOwner(StorageReviewEvidence evidence)
    {
        RequireStorageArtifacts(evidence);
        if (
            !StorageJournalPolicy.IsCandidate(evidence.JournalLeaf) || evidence.JournalLeaf.Length > 255 ||
            evidence.JournalLeaf.IndexOfAny(['\\', '/', ':', '\0']) >= 0)
            throw new IOException("An inactive, object-bound storage journal owner is required.");
    }
    private static void RequireStorageArtifacts(StorageReviewArtifacts evidence)
    {
        if (ProcessAuthority.IsElevatedOrRoot) throw new IOException("Storage history must be reviewed in the normal user process.");
        if (!evidence.IsOwned || string.IsNullOrEmpty(evidence.DirectoryIdentity) || evidence.DirectoryIdentity.Length > 1024)
            throw new IOException("Owned private storage artifact access is required.");
    }

    // Only ambiguous publication failures need a process-local latch: after a restart, a complete published pair that
    // survived is reverified from disk. A partial/temporary pair never validates. The latch cannot grow without bound.
    private static class StorageAcknowledgmentFailures
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<(string Directory, string Journal, Guid Nonce), StorageEvidenceVersion> Failed = [];
        internal static void RequireCapacity()
        { lock (Gate) if (Failed.Count >= StorageReviewNames.ArtifactCount) throw new IOException("Failed storage acknowledgment publications require reconciliation before another save."); }
        internal static void Record(StorageReviewEvidence evidence, Guid nonce, StorageEvidenceVersion version)
        { lock (Gate) Failed[(evidence.DirectoryIdentity, evidence.JournalIdentity, nonce)] = version; }
        internal static StorageEvidenceVersion? Find(StorageReviewEvidence evidence, Guid nonce)
        { lock (Gate) return Failed.GetValueOrDefault((evidence.DirectoryIdentity, evidence.JournalIdentity, nonce)); }
        internal static StorageEvidenceVersion? Find(string directory, string journal, Guid nonce)
        { lock (Gate) return Failed.GetValueOrDefault((directory, journal, nonce)); }
        internal static void Forget(StorageReviewEvidence evidence, Guid nonce)
        { lock (Gate) Failed.Remove((evidence.DirectoryIdentity, evidence.JournalIdentity, nonce)); }
    }
}
