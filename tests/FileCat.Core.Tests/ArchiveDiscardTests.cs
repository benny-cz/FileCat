using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Tests;

namespace FileCat.Core.Tests;

/// <summary>
/// Plan §7.1: a transaction that never reached publication ends in "safe owned cleanup or NeedsReview". The discard is
/// recorded before each destruction, resolves the journal only when every recorded object is gone and every unrecorded
/// reserved name is absent, and freezes the preparation it was derived from.
/// </summary>
public sealed class ArchiveDiscardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Fixture : IDisposable
    {
        private readonly TempDir temp = new();
        internal readonly JobJournal Journal;
        internal ArchiveTransactionFacts Facts;
        internal readonly string Folder, Archive, Stage, Expected, Origin, Verified, Metadata;
        internal Fixture()
        {
            var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
            Folder = Path.Combine(temp.Path, "archives"); Archive = Path.Combine(Folder, "new.tar");
            Stage = Path.Combine(Folder, ".fc-stage"); Expected = Path.Combine(Folder, ".fc-expected"); Origin = Path.Combine(Folder, ".fc-origin");
            Verified = Path.Combine(Folder, ".fc-verified"); Metadata = Path.Combine(Folder, ".fc-metadata");
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = new(Archive, null, [], Profile: profile) }, "discard", "owned", [], []);
            Journal = JobJournal.Create(Path.Combine(temp.Path, "journals"), job);
            Facts = new(Guid.NewGuid(), 1, profile, ArchiveTransactionPhase.Reserved, Archive, "parent-object", null, Stage, null, stageMaximumLength: 1 << 20,
                sidecars: [new(ArchiveSidecarRole.ExpectedOutput, Expected, 4096), new(ArchiveSidecarRole.OriginEvidence, Origin, 4096, originSources: 1),
                    new(ArchiveSidecarRole.VerifiedOutput, Verified, 4096), new(ArchiveSidecarRole.CreationMetadata, Metadata, 4096)]);
            Append(Facts); Append(Facts.BindCreatedStage("stage-object"));
            Seal(ArchiveSidecarRole.ExpectedOutput, "expected-object", 'E'); Seal(ArchiveSidecarRole.OriginEvidence, "origin-object", 'F');
            Append(Facts.AdvancePreparation(ArchiveTransactionPhase.Rebuilding));
        }
        internal void Append(ArchiveTransactionFacts next) { Journal.ArchiveTransaction(next); Facts = next; }
        internal void Seal(ArchiveSidecarRole role, string identity, char digest)
        {
            var reserved = Facts.Sidecars.Single(sidecar => sidecar.Role == role);
            Append(Facts.BindSidecar(reserved.BindIdentity(identity)));
            Append(Facts.BindSidecar(Facts.Sidecars.Single(sidecar => sidecar.Role == role).BindVersion(new(identity, 1024, new string(digest, 64)))));
        }
        /// <summary>Finishes preparation (finalized, verified, observed manifest) so publication can start.</summary>
        internal void Verify()
        {
            var stage = new ArchiveObjectVersion("stage-object", 2048, new string('B', 64));
            Append(Facts.AdvancePreparation(ArchiveTransactionPhase.Finalizing));
            Append(Facts.AdvancePreparation(ArchiveTransactionPhase.Verifying, finalizedStage: stage));
            Seal(ArchiveSidecarRole.VerifiedOutput, "verified-object", 'C');
            Append(Facts.AdvancePreparation(ArchiveTransactionPhase.Verifying, verifiedStage: stage));
        }
        internal IReadOnlyList<string> DiscardPhases() => JobJournal.ReadAll(Journal.Path)
            .Where(record => record.Node["t"]?.GetValue<string>() == ArchiveDiscardFacts.RecordType)
            .Select(record => record.Node["phase"]!.GetValue<string>() + ":" + record.Node["reviewed"]!.GetValue<int>()).ToArray();
        public void Dispose() { Journal.Dispose(); temp.Dispose(); }
    }

    private sealed class Lease(JobJournal journal, ArchiveTransactionFacts preparation, IEnumerable<string> present, string? failOn = null) : IArchiveDiscardLease
    {
        private readonly HashSet<string> names = new(present, StringComparer.Ordinal);
        internal readonly List<string> Destroyed = [];
        internal bool Finished;
        public JobJournal Journal => journal;
        public ArchiveTransactionFacts Preparation => preparation;
        public string? FolderToRemove => null;
        public bool HasArtifact(ArchiveDiscardArtifact artifact) => names.Contains(artifact.Path);
        public void DestroyArtifact(ArchiveDiscardArtifact artifact, bool resumingIntent, CancellationToken ct)
        {
            if (artifact.Path == failOn) throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The recorded object changed.");
            Assert.True(names.Remove(artifact.Path)); Destroyed.Add(artifact.Path);
        }
        public void RequireAbsent(string path)
        { if (names.Contains(path)) throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "A reserved name is occupied."); }
        public void FinishArtifacts(CancellationToken ct) => Finished = true;
        public void Dispose() { }
    }

    [Fact]
    public void A_failed_preparation_is_discarded_in_recorded_order_and_its_journal_can_close()
    {
        using var f = new Fixture();
        f.Journal.RequirePreparationReview(); // What a failing preparation records.
        Assert.True(f.Journal.NeedsRecovery);
        Assert.Same(f.Facts, f.Journal.DiscardableArchiveTransaction());
        var lease = new Lease(f.Journal, f.Facts, [f.Stage, f.Expected, f.Origin]);
        using (var discard = new ArchiveTransactionDiscard(lease))
        {
            // Review writes nothing; the catalog is recorded only when the discard runs.
            Assert.Empty(f.DiscardPhases());
            Assert.Equal([f.Stage, f.Expected, f.Origin], discard.Observe().Present);
            Assert.Equal([f.Verified, f.Metadata], discard.Facts.Unrecorded);
            Assert.True(discard.Execute(Ct).IsResolved);
        }
        Assert.Equal([f.Stage, f.Expected, f.Origin], lease.Destroyed); Assert.True(lease.Finished);
        Assert.Equal(["Cataloged:0", "DeleteIntent:0", "Deleted:1", "DeleteIntent:1", "Deleted:2", "DeleteIntent:2", "Deleted:3", "Completed:3"], f.DiscardPhases());
        Assert.False(f.Journal.NeedsRecovery);
        f.Journal.Finish(JobState.Canceled, "Stopped; FileCat's unfinished files were removed.");
        var recovery = JournalRecovery.ReadArchiveTransactions(f.Journal.Path);
        Assert.Null(recovery.Problem); Assert.False(recovery.RequiresReview);
        Assert.True(Assert.Single(recovery.Discards).IsResolved);
    }

    [Fact]
    public void An_already_absent_object_is_recorded_absent_without_a_destruction()
    {
        using var f = new Fixture(); f.Journal.RequirePreparationReview();
        var lease = new Lease(f.Journal, f.Facts, [f.Expected, f.Origin]);
        using (var discard = new ArchiveTransactionDiscard(lease)) Assert.True(discard.Execute(Ct).IsResolved);
        Assert.Equal([f.Expected, f.Origin], lease.Destroyed);
        Assert.Equal("Absent:1", f.DiscardPhases()[1]);
        Assert.False(f.Journal.NeedsRecovery);
    }

    [Fact]
    public void An_occupied_reserved_name_keeps_every_object_for_review()
    {
        using var f = new Fixture(); f.Journal.RequirePreparationReview();
        var lease = new Lease(f.Journal, f.Facts, [f.Stage, f.Expected, f.Origin, f.Verified]);
        using (var discard = new ArchiveTransactionDiscard(lease))
            Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() => discard.Execute(Ct)).Reason);
        Assert.Empty(lease.Destroyed); Assert.False(lease.Finished);
        Assert.Equal(["Cataloged:0", "NeedsReview:0"], f.DiscardPhases());
        Assert.True(f.Journal.NeedsRecovery);
        Assert.Throws<IOException>(() => f.Journal.Finish(JobState.Canceled, "must stay open"));
        Assert.True(JournalRecovery.ReadArchiveTransactions(f.Journal.Path).RequiresReview);
    }

    [Fact]
    public void A_changed_object_records_needs_review_and_keeps_the_rest()
    {
        using var f = new Fixture(); f.Journal.RequirePreparationReview();
        var lease = new Lease(f.Journal, f.Facts, [f.Stage, f.Expected, f.Origin], failOn: f.Expected);
        using (var discard = new ArchiveTransactionDiscard(lease)) Assert.Throws<ArchiveWriteException>(() => discard.Execute(Ct));
        Assert.Equal([f.Stage], lease.Destroyed);
        Assert.Equal(["Cataloged:0", "DeleteIntent:0", "Deleted:1", "DeleteIntent:1", "NeedsReview:1"], f.DiscardPhases());
        var recovery = JournalRecovery.ReadArchiveTransactions(f.Journal.Path);
        Assert.Null(recovery.Problem); Assert.True(recovery.RequiresReview);
        Assert.Equal(ArchiveDiscardPhase.NeedsReview, Assert.Single(recovery.Discards).Phase);
    }

    [Fact]
    public void A_discard_freezes_the_preparation_against_any_later_record()
    {
        using var f = new Fixture(); f.Journal.RequirePreparationReview();
        var preparation = f.Facts;
        using (var discard = new ArchiveTransactionDiscard(new Lease(f.Journal, preparation, [f.Stage, f.Expected, f.Origin])))
            discard.Execute(Ct);
        Assert.Throws<InvalidOperationException>(() => f.Journal.ArchiveTransaction(preparation.AdvancePreparation(ArchiveTransactionPhase.Finalizing)));
        Assert.Null(f.Journal.DiscardableArchiveTransaction());
    }

    [Fact]
    public void Only_facts_that_prove_publication_never_started_are_discardable()
    {
        using var f = new Fixture();
        Assert.True(ArchiveDiscardFacts.IsDiscardable(f.Facts)); // Rebuilding with sealed sidecars mirrored into the legacy fields.
        Assert.NotNull(f.Facts.ManifestPath); Assert.NotNull(f.Facts.OriginEvidencePath);
        f.Verify(); Assert.True(ArchiveDiscardFacts.IsDiscardable(f.Facts));
        // A creation that stopped before its claim lists only FileCat's own objects: still discardable, with the soft hold.
        using var unclaimed = new Fixture(); unclaimed.Verify();
        var review = unclaimed.Journal.RecordArchiveCreationReview(unclaimed.Facts.Nonce, claimAttempted: false);
        Assert.Equal(ArchiveTransactionPhase.NeedsReview, review.Phase);
        Assert.DoesNotContain(review.ArchivePath, review.UnresolvedArtifacts);
        Assert.True(ArchiveDiscardFacts.IsDiscardable(review));
        Assert.Same(review, unclaimed.Journal.DiscardableArchiveTransaction());
        // Once the claim may have run, the archive name is unresolved: never discardable, and the hold is the hard one.
        using var claimed = new Fixture(); claimed.Verify();
        var uncertain = claimed.Journal.RecordArchiveCreationReview(claimed.Facts.Nonce, claimAttempted: true);
        Assert.Contains(uncertain.ArchivePath, uncertain.UnresolvedArtifacts);
        Assert.False(ArchiveDiscardFacts.IsDiscardable(uncertain));
        Assert.Null(claimed.Journal.DiscardableArchiveTransaction());
    }

    [Fact]
    public void Discard_records_round_trip_and_a_tampered_catalog_is_refused_on_reading()
    {
        using var f = new Fixture(); f.Journal.RequirePreparationReview();
        var facts = ArchiveDiscardFacts.Begin(f.Facts);
        Assert.Equal(facts.ToJson().ToJsonString(), ArchiveDiscardFacts.Parse(facts.ToJson()).ToJson().ToJsonString());
        f.Journal.Dispose();
        // An extra catalog entry no longer matches the frozen preparation: the reader reports a problem, never a scope.
        var tampered = facts.ToJson(); ((JsonArray)tampered["artifacts"]!).Add(new JsonObject { ["path"] = Path.Combine(f.Folder, ".other"), ["identity"] = "other", ["version"] = null });
        string json = tampered.ToJsonString();
        File.AppendAllText(f.Journal.Path, $"{Crc32.HashToUInt32(Encoding.UTF8.GetBytes(json)):x8} {json}\n");
        var recovery = JournalRecovery.ReadArchiveTransactions(f.Journal.Path);
        Assert.NotNull(recovery.Problem); Assert.True(recovery.RequiresReview); Assert.Empty(recovery.Discards);
    }
}
