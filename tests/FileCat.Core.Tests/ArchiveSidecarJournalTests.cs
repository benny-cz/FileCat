using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class ArchiveSidecarJournalTests
{
    [Fact]
    public void A_phase_change_cannot_hide_missing_identity_and_version_transitions()
    {
        using var f = new Fixture(); using var journal = f.Journal(); var first = f.Facts(); journal.ArchiveTransaction(first);
        var sidecars = first.Sidecars.Select(s => s.Role == ArchiveSidecarRole.VerifiedOutput ? s :
            s.BindIdentity(s.Role.ToString()).BindVersion(new(s.Role.ToString(), s.Role == ArchiveSidecarRole.OriginEvidence ? 52 : 512, new string('A', 64)))).ToArray();
        // Constructor-valid observations are not a valid history: their separate durable creation/seal steps are missing.
        var combined = f.Facts(sidecars, 2).AdvancePreparation(ArchiveTransactionPhase.Rebuilding);
        var node = combined.ToJson(); node["sequence"] = 2;
        Assert.Throws<ArgumentException>(() => journal.ArchiveTransaction(ArchiveTransactionFacts.Parse(node)));
        Assert.Equal(1, Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions).Sequence);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("length")]
    [InlineData("verified-bytes")]
    public void Finalized_stage_evidence_must_match_its_reserved_bound_and_verified_bytes(string mutation)
    {
        using var f = new Fixture();
        var finalized = new ArchiveObjectVersion(mutation == "identity" ? "other" : f.Stage.Identity, mutation == "length" ? 4097 : f.Stage.Length,
            mutation == "verified-bytes" ? new string('A', 64) : f.Stage.Sha256);
        Assert.Throws<ArgumentException>(() => new ArchiveTransactionFacts(f.Nonce, 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved,
            Path.Combine(f.Root.Path, "output.tar"), "parent", null, f.StagePath, f.Stage.Identity,
            verifiedStage: mutation == "verified-bytes" ? f.Stage : null, stageMaximumLength: 4096, finalizedStage: finalized));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Provenance_reservation_counts_match_creation_or_update_scope(bool update)
    {
        using var f = new Fixture(); var original = update ? new ArchiveObjectVersion("original", 1, new string('B', 64)) : null;
        Assert.Throws<ArgumentException>(() => new ArchiveTransactionFacts(f.Nonce, 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved,
            Path.Combine(f.Root.Path, "output.tar"), "parent", original, f.StagePath, f.Stage.Identity,
            sidecars: [new(ArchiveSidecarRole.OriginEvidence, Path.Combine(f.Root.Path, ".origin"), ArchiveWritePolicy.MaxMetadataBytes,
                originSources: update ? 0 : ArchiveWritePolicy.MaxMembers + 1)]));
    }

    [Theory]
    [InlineData("unknown-field")]
    [InlineData("missing-bound")]
    [InlineData("text-bound")]
    [InlineData("negative-bound")]
    [InlineData("missing-list")]
    [InlineData("unknown-role")]
    [InlineData("version-without-identity")]
    public void Malformed_sidecar_json_is_not_accepted_as_recovery_evidence(string mutation)
    {
        using var f = new Fixture(); var node = f.Facts().ToJson(); var sidecar = (System.Text.Json.Nodes.JsonObject)node["sidecars"]![0]!;
        switch (mutation)
        {
            case "unknown-field": sidecar["extra"] = true; break;
            case "missing-bound": sidecar.Remove("maximum"); break;
            case "text-bound": sidecar["maximum"] = "1024"; break;
            case "negative-bound": sidecar["maximum"] = -1; break;
            case "missing-list": node.Remove("sidecars"); break;
            case "unknown-role": sidecar["role"] = 99; break;
            case "version-without-identity": sidecar["version"] = new System.Text.Json.Nodes.JsonObject { ["identity"] = "different", ["length"] = 1, ["sha256"] = new string('A', 64) }; break;
        }
        Assert.Throws<ArgumentException>(() => ArchiveTransactionFacts.Parse(node));
    }

    [Fact]
    public void Reserved_sidecars_bind_identity_then_version_and_keep_verified_preparation_unresolved()
    {
        using var f = new Fixture(); using var journal = f.Journal(); var facts = f.Facts(); journal.ArchiveTransaction(facts);
        Assert.Same(facts, journal.CurrentArchiveTransaction(facts.Nonce));
        facts = Seal(facts, ArchiveSidecarRole.ExpectedOutput); facts = Seal(facts, ArchiveSidecarRole.OriginEvidence);
        facts = facts.AdvancePreparation(ArchiveTransactionPhase.Rebuilding); journal.ArchiveTransaction(facts);
        facts = facts.AdvancePreparation(ArchiveTransactionPhase.Finalizing); journal.ArchiveTransaction(facts);
        Assert.Throws<ArgumentException>(() => facts.AdvancePreparation(ArchiveTransactionPhase.Verifying));
        facts = facts.AdvancePreparation(ArchiveTransactionPhase.Verifying, finalizedStage: f.Stage); journal.ArchiveTransaction(facts);
        facts = Seal(facts, ArchiveSidecarRole.VerifiedOutput);
        facts = facts.AdvancePreparation(ArchiveTransactionPhase.Verifying, f.Stage); journal.ArchiveTransaction(facts);
        var recovered = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
        Assert.Equal(ArchiveTransactionPhase.Verifying, recovered.Phase); Assert.Equal(f.Stage, recovered.VerifiedStage);
        Assert.All(recovered.Sidecars, s => Assert.NotNull(s.Version)); Assert.Equal(3, recovered.Sidecars.Count);
        Assert.True(journal.NeedsRecovery); Assert.Throws<IOException>(() => journal.Finish(JobState.Completed, "not published"));
        ArchiveTransactionFacts Seal(ArchiveTransactionFacts current, ArchiveSidecarRole role)
        {
            var sidecar = current.Sidecars.Single(s => s.Role == role).BindIdentity(role.ToString());
            current = current.BindSidecar(sidecar); journal.ArchiveTransaction(current);
            current = current.BindSidecar(sidecar.BindVersion(new(sidecar.Identity!, role == ArchiveSidecarRole.OriginEvidence ? 52 : 512, new string('A', 64))));
            journal.ArchiveTransaction(current); return current;
        }
    }

    [Fact]
    public void Initial_reservation_collection_is_frozen_and_an_unsealed_sidecar_cannot_advance_to_rebuilding()
    {
        using var f = new Fixture(); var sidecars = f.Sidecars(); var first = sidecars[0]; var facts = f.Facts(sidecars); sidecars[0] = sidecars[1];
        Assert.Same(first, facts.Sidecars[0]); Assert.Throws<ArgumentException>(() => facts.AdvancePreparation(ArchiveTransactionPhase.Rebuilding));
        using var journal = f.Journal(); journal.ArchiveTransaction(facts);
        Assert.Throws<ArgumentException>(() => journal.ArchiveTransaction(f.Facts([], sequence: 2)));
    }

    [Fact]
    public void Creation_and_seal_cannot_be_collapsed_into_one_reserved_transition()
    {
        using var f = new Fixture(); using var journal = f.Journal(); var facts = f.Facts(); journal.ArchiveTransaction(facts);
        var sidecars = facts.Sidecars.ToArray(); sidecars[0] = sidecars[0].BindIdentity("manifest").BindVersion(new("manifest", 512, new string('B', 64)));
        Assert.Throws<ArgumentException>(() => journal.ArchiveTransaction(f.Facts(sidecars, 2)));
        Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("path")]
    [InlineData("bound")]
    [InlineData("role")]
    [InlineData("count")]
    public void Recorded_sidecar_creation_cannot_change_or_disappear(string mutation)
    {
        using var f = new Fixture(); using var journal = f.Journal(); var facts = f.Facts(); journal.ArchiveTransaction(facts);
        var first = facts.Sidecars[0].BindIdentity("manifest"); facts = facts.BindSidecar(first); journal.ArchiveTransaction(facts);
        var sidecars = facts.Sidecars.ToArray();
        sidecars[0] = mutation switch
        {
            "identity" => new(first.Role, first.Path, first.MaximumLength, "other"),
            "path" => new(first.Role, Path.Combine(f.Root.Path, "other-manifest"), first.MaximumLength, first.Identity),
            "bound" => new(first.Role, first.Path, first.MaximumLength + 1, first.Identity),
            "role" => new(ArchiveSidecarRole.AssessedSource, first.Path, first.MaximumLength, first.Identity),
            _ => first,
        };
        if (mutation == "count") sidecars = sidecars[..^1];
        Assert.Throws<ArgumentException>(() => journal.ArchiveTransaction(f.Facts(sidecars, facts.Sequence + 1)));
        Assert.Equal(first, Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions).Sidecars[0]);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("path")]
    [InlineData("identity")]
    [InlineData("bound")]
    [InlineData("sources")]
    public void Invalid_or_aliased_sidecar_facts_are_rejected(string mutation)
    {
        using var f = new Fixture();
        Assert.Throws<ArgumentException>(() =>
        {
            var sidecars = f.Sidecars();
            sidecars[0] = mutation switch
            {
                "role" => new((ArchiveSidecarRole)99, sidecars[0].Path, 1024),
                "path" => new(sidecars[0].Role, f.StagePath, 1024),
                "identity" => new(sidecars[0].Role, sidecars[0].Path, 1024, f.Stage.Identity),
                "bound" => new(sidecars[0].Role, sidecars[0].Path, ArchiveWritePolicy.MaxMetadataBytes + 1),
                _ => new(sidecars[0].Role, sidecars[0].Path, 1024, originSources: 1),
            };
            return f.Facts(sidecars);
        });
    }

    [Fact]
    public void Only_current_flushed_parent_facts_can_authorize_private_sidecar_creation()
    {
        using var f = new Fixture(); using var journal = f.Journal(); var facts = f.Facts();
        Assert.Throws<InvalidOperationException>(() => journal.RequireCurrentArchiveTransaction(facts));
        journal.ArchiveTransaction(facts); journal.RequireCurrentArchiveTransaction(facts);
        Assert.Throws<InvalidOperationException>(() => journal.RequireCurrentArchiveTransaction(ArchiveTransactionFacts.Parse(facts.ToJson())));
        journal.RequireRecovery(); Assert.Throws<InvalidOperationException>(() => journal.RequireCurrentArchiveTransaction(facts));
    }

    [Fact]
    public void Schema_three_reservations_remain_readable_with_unknown_identity_but_are_read_only()
    {
        using var f = new Fixture(); var node = new ArchiveTransactionFacts(f.Nonce, 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved,
            Path.Combine(f.Root.Path, "output.tar"), "parent", null, f.StagePath, null, stageMaximumLength: 4096).ToJson();
        node["v"] = 3; node.Remove("sidecars"); node.Remove("finalized"); var old = ArchiveTransactionFacts.Parse(node);
        Assert.Equal(3, old.RecordVersion); Assert.Null(old.StageIdentity); Assert.Empty(old.Sidecars);
        using var journal = f.Journal(); Assert.Throws<InvalidOperationException>(() => journal.ArchiveTransaction(old));
    }

    private sealed class Fixture : IDisposable
    {
        internal TempDir Root { get; } = new(); internal Guid Nonce { get; } = Guid.NewGuid();
        internal string StagePath => Path.Combine(Root.Path, ".stage");
        internal ArchiveObjectVersion Stage { get; } = new("stage", 2048, new string('F', 64));
        internal ArchiveSidecarFacts[] Sidecars() => [new(ArchiveSidecarRole.ExpectedOutput, Path.Combine(Root.Path, ".expected"), 1024),
            new(ArchiveSidecarRole.OriginEvidence, Path.Combine(Root.Path, ".origin"), 1024, originSources: 0),
            new(ArchiveSidecarRole.VerifiedOutput, Path.Combine(Root.Path, ".verified"), 1024)];
        internal ArchiveTransactionFacts Facts(IReadOnlyList<ArchiveSidecarFacts>? sidecars = null, int sequence = 1)
        {
            sidecars ??= Sidecars(); var expected = sidecars.SingleOrDefault(s => s.Role == ArchiveSidecarRole.ExpectedOutput); var origin = sidecars.SingleOrDefault(s => s.Role == ArchiveSidecarRole.OriginEvidence);
            return new(Nonce, sequence, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved, Path.Combine(Root.Path, "output.tar"), "parent", null,
                StagePath, Stage.Identity, manifestPath: expected?.Version is null ? null : expected.Path, manifest: expected?.Version,
                originEvidencePath: origin?.Identity is null ? null : origin.Path, originEvidenceIdentity: origin?.Identity,
                originEvidenceSources: origin?.Identity is null ? null : origin.OriginSources, originEvidence: origin?.Version, stageMaximumLength: 4096, sidecars: sidecars);
        }
        internal JobJournal Journal() => JobJournal.Create(Root.Dir("journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
            Archive = new(Path.Combine(Root.Path, "output.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned sidecar journal", "owned", [], []));
        public void Dispose() => Root.Dispose();
    }
}
