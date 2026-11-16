using System.Globalization;
using System.Text;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.App.Services;

internal static class InterruptedRecoveryReport
{
    private const int DetailLimit = 10_000;

    internal static string ArchiveSummary(InterruptedJob job)
    {
        var facts = job.ArchiveTransactions;
        if (facts.Problem is not null)
            return "Archive recovery records are incomplete or unsupported. Keep the current archive and retained files; Review report lists the recorded locations.";
        var exchanges = facts.ExchangeUpdates.Where(t => !t.IsResolved).ToArray();
        if (exchanges.Any(t => t.Phase == ArchiveExchangeUpdatePhase.NeedsReview))
            return "FileCat could not confirm which version of the archive is current. Both available versions were kept; open Review report for their locations.";
        if (exchanges.Any(t => t.Phase is ArchiveExchangeUpdatePhase.Reserved or ArchiveExchangeUpdatePhase.MetadataApplied or ArchiveExchangeUpdatePhase.ExchangeIntent))
            return "An archive update was interrupted before its outcome was recorded. Review checks which version is current, keeps it and removes only that update's private files.";
        if (exchanges.Length != 0)
            return exchanges.Any(t => t.WasPublished)
                ? "The archive was updated. The previous version and this update's private files are still kept; review removes them exactly."
                : "The archive was not changed. This update's unpublished private files are still kept; review removes them exactly.";
        if (ArchiveDiscardReview.CanOffer(job))
            return facts.Discards.Any(t => t.Phase == ArchiveDiscardPhase.NeedsReview)
                ? "An archive change stopped before it was saved, and removing its unfinished files needs review. The archive was not changed by it; review checks the files again."
                : "An archive change stopped before it was saved, so the archive was not changed by it. FileCat's unfinished files for it are still kept; review removes them exactly.";
        var restorations = facts.ContentRestorations.Where(t => !facts.IsContentResolved(t.Nonce)).ToArray();
        if (restorations.Any(t => t.Phase != ArchiveContentRestorationPhase.Committed))
            return "An archive restoration was interrupted. Keep the current archive and retained versions; open Review report for the linked restore job and recorded locations.";
        if (restorations.Length != 0)
            return "A retained archive version was recorded as restored. Temporary files still need review; open Review report for the linked restore job and retained versions.";
        if (facts.ContentUpdates.Any(t => !facts.IsContentResolved(t.Nonce) && t.Phase != ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts) ||
            facts.Transactions.Any(t => !t.IsResolved && !facts.IsContentResolved(t.Nonce) && t.Phase != ArchiveTransactionPhase.CommittedWithRetainedArtifacts &&
                !facts.ContentUpdates.Any(c => c.Nonce == t.Nonce && c.Phase == ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts)))
            return "The archive update's final outcome has not been confirmed. Keep the current archive and retained files; open Review report for recorded locations.";
        if (facts.ContentUpdates.Any(t => !facts.IsContentResolved(t.Nonce)) || facts.Transactions.Any(t => !facts.IsContentResolved(t.Nonce) && t.Phase == ArchiveTransactionPhase.CommittedWithRetainedArtifacts))
            return "An archive update was recorded as committed, with retained files still needing review. Keep those files and open Review report.";
        return "No unresolved archive transaction was recorded. Open Review report before closing this recovery record; the files have not been inspected.";
    }

    internal static bool HasUnreviewedRemoteUpload(InterruptedJob job)
    {
        if (job.OpenIntents.Any(intent => intent.Operation is "upload" or "upload-move" or "remote-replace-backup")) return true;
        if (job.Kind is not (nameof(JobKind.Copy) or nameof(JobKind.Move))) return false;
        var destination = Location.Deserialize(job.Destination);
        // An upload intent can be lost in an interrupted batched journal even
        // after the server received a temporary file. The recorded provider
        // still requires remote inspection before local tracking can close.
        return destination?.Scheme is Schemes.Sftp or Schemes.Ftp;
    }

    internal const string RemoteUploadWarning =
        "An interrupted upload may have left a hidden temporary file on the remote server. " +
        "This local recovery review cannot inspect or delete server files. Inspect the remote destination " +
        "and its temporary files before marking this recovery reviewed.";

    // The remote uploader names its stage from the journal's eight-digit job ID.
    // This is a search hint, never proof that a server item still belongs to us.
    internal static string? ExpectedRemoteStagePrefix(InterruptedJob job)
    {
        if (!HasUnreviewedRemoteUpload(job)) return null;
        string stem = Path.GetFileNameWithoutExtension(job.JournalPath);
        if (stem.Length != 27 || !stem.StartsWith("job-", StringComparison.Ordinal) || stem[18] != '-' ||
            !stem.AsSpan(4, 14).ToArray().All(char.IsAsciiDigit) ||
            !stem.AsSpan(19, 8).ToArray().All(char.IsAsciiHexDigit)) return null;
        return JournalRecovery.StagedPrefix + stem[19..] + "-";
    }

    internal static string RemoteUploadReviewWarning(InterruptedJob job) =>
        RemoteUploadWarning + (job.OpenIntents.Any(i => i.Operation == "remote-replace-backup")
            ? " A hidden name may hold the previous destination version, while another may hold the incoming copy. " +
              "Inspect the recorded backup and stage paths; do not delete either until their contents and the public destination are reconciled."
            : string.Empty) + (ExpectedRemoteStagePrefix(job) is { } prefix
            ? " Candidate temporary names begin " + prefix + "; verify the server item before changing it."
            : string.Empty);

    internal static IReadOnlyList<string> RemoteUploadTargets(InterruptedJob job)
    {
        var targets = job.OpenIntents.Where(i => i.Operation is "upload" or "upload-move" or "remote-replace-backup")
            .Select(i => i.Operation == "remote-replace-backup" ? i.Path : i.Target)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!).Distinct(StringComparer.Ordinal).Take(4).ToArray();
        if (targets.Length > 0) return targets;
        return Location.Deserialize(job.Destination) is { } recorded ? [recorded.Path] : [];
    }

    internal static string Format(InterruptedJob job, string action,
        IReadOnlyList<string> reviewedStaged, IReadOnlyList<string> unreviewedStaged, CopyReview copies,
        bool stagedLimit, IReadOnlyList<string>? deleted = null, IReadOnlyList<string>? kept = null,
        IReadOnlyList<string>? renameEffects = null, IReadOnlyList<string>? allSources = null)
    {
        var text = new StringBuilder();
        var sources = allSources ?? job.Sources;
        int shown = 0;
        long known = 0;
        static string Count(long number) => number.ToString("N0", CultureInfo.InvariantCulture);
        void Line(string value = "")
        {
            text.AppendLine(value);
            if (text.Length > RecoveryReviewStore.MaxReportCharacters)
                throw new InvalidDataException("The recovery report exceeds the retained-detail budget. The journal remains open; use Hide for now and inspect the journal directly.");
        }
        void Detail(string label, string path, string? target = null)
        {
            known++;
            if (shown >= DetailLimit) return;
            Line(label + ": " + JsonSerializer.Serialize(path) +
                 (target is null ? "" : " → " + JsonSerializer.Serialize(target)));
            shown++;
        }

        Line("FileCat interrupted-operation review");
        Line("Title: " + JsonSerializer.Serialize(job.Title));
        Line("Kind: " + job.Kind);
        Line("Action: " + action);
        Line("Captured (UTC): " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Line("Journal: " + JsonSerializer.Serialize(job.JournalPath));
        Line("Destination: " + (job.Destination is null ? "not recorded" : JsonSerializer.Serialize(job.Destination)));
        Line($"Completed journal steps: {Count(job.CompletedSteps)}; open intents: {Count(job.OpenIntents.Count)}");
        Line($"Recorded source roots: {Count(sources.Count)} retained in this report of " +
             (job.SourceCount >= 0 ? Count(job.SourceCount) + " declared" : "an unknown declared total") + "; " +
             (job.SourceCount == sources.Count ? "all declared roots are represented" : "additional roots may be unavailable"));
        Line("Quoted paths use JSON escaping. Decoding the quoted value recovers its exact characters.");
        Line("This is a review-time snapshot. Files may change afterward. Closing tracking does not delete or repair files.");
        if (HasUnreviewedRemoteUpload(job))
            Line("Remote upload still requires review: " + RemoteUploadReviewWarning(job));
        if (job.ArchiveTransactions.HasRecords)
        {
            Line();
            Line("Archive recovery facts (read-only)");
            Line(ArchiveSummary(job));
            Line("These are recorded paths and object versions. Their current existence, bytes, identities and security metadata have not been inspected.");
            Line("Generic cleanup and transfer retry are unavailable for archive artifacts. Keep every needed version until the archive outcome is reconciled.");
            if (job.ArchiveTransactions.Problem is { } problem) Line("Journal review warning: " + JsonSerializer.Serialize(problem));
            foreach (var transaction in job.ArchiveTransactions.Transactions)
            {
                Line($"Transaction {transaction.Nonce:N}, facts v{transaction.RecordVersion}, sequence {transaction.Sequence}, profile {transaction.Profile.Format} v{transaction.Profile.Version}, recorded phase {transaction.Phase}");
                Detail("Archive path (not inspected)", transaction.ArchivePath);
                Detail("Rebuilt archive stage (not inspected)", transaction.StagePath);
                if (transaction.RetainedPath is { } retained) Detail("Reserved retained-original path (not inspected)", retained);
                if (transaction.ManifestPath is { } manifest) Detail("Verification manifest (not inspected)", manifest);
                if (transaction.OriginEvidencePath is { } originEvidence) Detail("Captured provenance evidence (not inspected)", originEvidence);
                foreach (string artifact in transaction.UnresolvedArtifacts) Detail("Recorded retained artifact (not inspected)", artifact);
                void Version(string label, ArchiveObjectVersion? version)
                {
                    if (version is not null) Line(label + ": identity " + JsonSerializer.Serialize(version.Identity) +
                        ", length " + version.Length.ToString(CultureInfo.InvariantCulture) + ", SHA-256 " + version.Sha256);
                }
                Line("Recorded parent identity: " + JsonSerializer.Serialize(transaction.ParentIdentity));
                Line(transaction.StageIdentity is { } stageIdentity ? "Recorded stage identity: " + JsonSerializer.Serialize(stageIdentity) :
                    "Stage creation identity was not recorded; its reserved path is evidence for review only.");
                if (transaction.RecordVersion >= 3) Line("Reserved stage byte limit: " + transaction.StageMaximumLength.ToString(CultureInfo.InvariantCulture));
                if (transaction.OriginEvidenceIdentity is { } originIdentity) Line("Recorded provenance-evidence identity: " + JsonSerializer.Serialize(originIdentity));
                if (transaction.OriginEvidenceSources is { } originSources) Line("Declared captured origin sources: " + originSources.ToString(CultureInfo.InvariantCulture));
                Version("Original object", transaction.Original);
                Version("Verified stage object", transaction.VerifiedStage);
                Version("Finalized stage object", transaction.FinalizedStage);
                Version("Manifest object", transaction.Manifest);
                Version("Finalized provenance evidence", transaction.OriginEvidence);
                foreach (var sidecar in transaction.Sidecars)
                {
                    Detail("Reserved sidecar " + sidecar.Role + " (not inspected)", sidecar.Path);
                    Line("Sidecar byte limit: " + sidecar.MaximumLength.ToString(CultureInfo.InvariantCulture));
                    Line(sidecar.Identity is { } sidecarIdentity ? "Recorded sidecar identity: " + JsonSerializer.Serialize(sidecarIdentity) :
                        "Sidecar creation identity was not recorded; its reserved path is evidence for review only.");
                    Version("Sealed sidecar " + sidecar.Role, sidecar.Version);
                }
                Version("Observed publication object", transaction.Published);
                Version("Observed retired object", transaction.Retired);
                if (transaction.SecuritySnapshotSha256 is { } digest) Line("Security/provenance snapshot SHA-256: " + digest);
            }
        }
        foreach (var content in job.ArchiveTransactions.ContentUpdates)
        {
            Line($"Same-object archive update {content.Nonce:N}, facts v{ArchiveContentUpdateFacts.SchemaVersion}, sequence {content.Sequence}, recorded phase {content.Phase}");
            Detail("Retained original-byte snapshot (not inspected)", content.SnapshotPath);
            Line("This recipe updates the existing file's data. Its snapshot is a separate file; no recorded path grants restore or deletion authority.");
            Line("Snapshot byte limit: " + content.SnapshotMaximumLength.ToString(CultureInfo.InvariantCulture));
            if (content.SnapshotIdentity is { } identity) Line("Recorded snapshot identity: " + JsonSerializer.Serialize(identity));
            if (content.Snapshot is { } snapshot) Line("Retained original SHA-256: " + snapshot.Sha256);
            if (content.NativeTransactionId is { } transactionId) Line("Recorded native transaction: " + transactionId.ToString("D"));
            if (content.Published is { } published) Line("Recorded committed object: " + JsonSerializer.Serialize(published.Identity) + ", SHA-256 " + published.Sha256);
        }
        foreach (var cleanup in job.ArchiveTransactions.ContentCleanups)
        {
            Line($"Private archive artifact cleanup {cleanup.Nonce:N}, sequence {cleanup.Sequence}, recorded phase {cleanup.Phase}, confirmed deletions {cleanup.DeletedCount} of {cleanup.Artifacts.Count}");
            for (int i = 0; i < cleanup.Artifacts.Count; i++)
            {
                var artifact = cleanup.Artifacts[i];
                Detail(i < cleanup.DeletedCount ? "Recorded deleted artifact (not inspected)" : "Recorded remaining artifact (not inspected)", artifact.Path);
                Line("Recorded artifact identity: " + JsonSerializer.Serialize(artifact.Version.Identity) + ", SHA-256 " + artifact.Version.Sha256);
                Line("Sealed artifact metadata SHA-256: " + artifact.MetadataSha256);
            }
        }
        foreach (var recovery in job.ArchiveTransactions.ContentRecoveries)
        {
            Line($"Archive recovery {recovery.Nonce:N}, sequence {recovery.Sequence}, recorded phase {recovery.Phase}, verified target {recovery.TargetState}");
            Line("Reviewed archive identity: " + JsonSerializer.Serialize(recovery.Target.Identity) + ", SHA-256 " + recovery.Target.Sha256);
            var catalog = job.ArchiveTransactions.ContentCleanups.SingleOrDefault(c => c.Nonce == recovery.Nonce);
            if (catalog is not null)
                for (int i = recovery.InitialCount; i < recovery.ReviewedCount; i++)
                    Detail((recovery.AbsentMask & (1u << i)) != 0 ? "Artifact recorded absent during recovery (not inspected)" :
                        "Artifact recorded deleted during recovery (not inspected)", catalog.Artifacts[i].Path);
        }
        foreach (var restoration in job.ArchiveTransactions.ContentRestorations)
        {
            Line($"Archive restoration {restoration.Nonce:N}, facts v{ArchiveContentRestorationFacts.SchemaVersion}, sequence {restoration.Sequence}, attempt {restoration.Attempt}, recorded phase {restoration.Phase}");
            Detail("Linked fresh restore journal (not inspected)", restoration.RestoreJournalPath);
            Line("Fresh restore transaction: " + restoration.RestoreNonce.ToString("N") + ", native transaction: " + restoration.NativeTransactionId.ToString("D"));
            Line("Archive before restoration: identity " + JsonSerializer.Serialize(restoration.Before.Identity) + ", SHA-256 " + restoration.Before.Sha256);
            Line("Expected restored archive: identity " + JsonSerializer.Serialize(restoration.Restored.Identity) + ", SHA-256 " + restoration.Restored.Sha256);
            Line("Restored origin evidence SHA-256: " + restoration.RestoredOriginSha256);
            Line("The fresh restore job has its own rollback snapshot of the version being replaced. A restore intent alone does not confirm publication; no recorded path grants replay authority.");
        }
        Line();
        if (deleted is not null)
            foreach (string path in deleted) Detail("Deleted by cleanup", path);
        if (kept is not null)
            foreach (string path in kept) Detail("Kept after cleanup", path);
        if (renameEffects is not null)
            foreach (string effect in renameEffects) Detail("Rename review outcome", effect);
        foreach (string path in reviewedStaged)
            Detail(deleted?.Contains(path, PathUtil.SafetyComparer) == true
                ? "Reviewed staged file deleted by cleanup" : "Reviewed staged file kept at this snapshot", path);
        foreach (string path in unreviewedStaged) Detail("Staged file not completely reviewed; kept", path);
        foreach (var copy in copies.Incomplete) Detail(deleted?.Contains(copy.Path, PathUtil.SafetyComparer) == true
            ? "Incomplete copy deleted by cleanup" : "Incomplete copy kept", copy.Path, copy.Source);
        foreach (string path in copies.Differing) Detail("Differing copy kept; compare with its source", path);
        foreach (var intent in job.OpenIntents)
        {
            Detail($"Open step {Count(intent.Step)} ({intent.Operation}) source", intent.Path, intent.Target);
            if (intent.Staged is { } staged) Detail("  staged path", staged);
            if (intent.Via is { } via) Detail("  temporary rename path", via);
            if (intent.RecoveryPath is { } recovery)
                Detail("  retained replaced destination; inspect and restore manually if present", recovery);
        }
        foreach (string path in job.StagingDirectories) Detail("Staging directory", path);
        foreach (string path in sources) Detail("Recorded source", path);
        foreach (var fill in job.FillDirectories) Detail("Direct-copy mapping", fill.Source, fill.Destination);
        Line();
        Line($"Known detail rows: {Count(known)}; retained here: {Count(shown)}; omitted by this report's limit: {Count(known - shown)}.");
        if (job.SourceCount > sources.Count)
            Line($"{Count(job.SourceCount - sources.Count)} source roots are not retained in this report; their paths may no longer be available after tracking closes.");
        else if (job.SourceCount < 0)
            Line("The complete source-root count could not be reconstructed from the journal.");
        if (stagedLimit || copies.LimitReached || job.FillDirectoriesCut)
            Line("Further files or directories may exist beyond the recovery review limits. Their count and paths are unknown; no completeness claim is made.");
        return text.ToString();
    }
}
