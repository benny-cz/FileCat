using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class ArchiveStageReservationJournalTests
{
    [Fact]
    public void Precreation_name_and_bound_survive_scan_pruning_and_generic_closure()
    {
        using var f = new Fixture(); var reserved = f.Facts();
        using (var journal = f.Journal())
        {
            journal.ArchiveTransaction(reserved);
            Assert.True(journal.NeedsRecovery);
            Assert.Throws<IOException>(() => journal.Finish(JobState.Completed, "cannot hide reservation"));
        }
        var interrupted = Assert.Single(JournalRecovery.Scan(f.Journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        var recovered = Assert.Single(interrupted.ArchiveTransactions.Transactions);
        Assert.Null(recovered.StageIdentity); Assert.Equal(f.Reservation.Path, recovered.StagePath);
        Assert.Equal(1024, recovered.StageMaximumLength); Assert.True(interrupted.ArchiveTransactions.RequiresReview);
        Assert.False(JournalRecovery.TryClose(interrupted, "no identity is not cleanup permission"));
        Assert.False(File.Exists(f.Reservation.Path));
    }

    [Theory]
    [InlineData("Rebuilding")]
    [InlineData("NotCommitted")]
    public void An_unbound_stage_cannot_advance_or_claim_a_resolved_outcome(string phase)
    {
        using var f = new Fixture();
        Assert.Throws<ArgumentException>(() => f.Facts(phase: Enum.Parse<ArchiveTransactionPhase>(phase)));
    }

    [Fact]
    public void Identity_binding_is_monotonic_and_the_physical_bound_cannot_change()
    {
        using var f = new Fixture(); using var journal = f.Journal(); var reserved = f.Facts();
        journal.ArchiveTransaction(reserved); var created = reserved.BindCreatedStage("native-stage"); journal.ArchiveTransaction(created);
        Assert.Throws<InvalidOperationException>(() => created.BindCreatedStage("other"));
        Assert.Throws<ArgumentException>(() => journal.ArchiveTransaction(f.Facts(3, ArchiveTransactionPhase.Rebuilding, "other")));
        Assert.Throws<ArgumentException>(() => journal.ArchiveTransaction(f.Facts(3, ArchiveTransactionPhase.Rebuilding, "native-stage", 1025)));
        Assert.Throws<ArgumentException>(() => journal.ArchiveTransaction(f.Facts(3, ArchiveTransactionPhase.Reserved, "native-stage")));
        journal.ArchiveTransaction(f.Facts(3, ArchiveTransactionPhase.Rebuilding, "native-stage"));
        Assert.Equal(3, Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions).Sequence);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("negative")]
    [InlineData("oversize")]
    [InlineData("fraction")]
    [InlineData("text")]
    public void Invalid_reserved_bounds_cannot_be_parsed(string mutation)
    {
        using var f = new Fixture(); var node = f.Facts().ToJson();
        switch (mutation)
        {
            case "missing": node.Remove("stageMaximum"); break;
            case "negative": node["stageMaximum"] = -1; break;
            case "oversize": node["stageMaximum"] = ArchiveWritePolicy.MaxPhysicalBytes + 1; break;
            case "fraction": node["stageMaximum"] = 1.5; break;
            case "text": node["stageMaximum"] = "1024"; break;
        }
        Assert.Throws<ArgumentException>(() => ArchiveTransactionFacts.Parse(node));
    }

    [Fact]
    public void Schema_two_identity_facts_remain_readable_but_cannot_be_written_as_new_facts()
    {
        using var f = new Fixture(); var node = f.Facts(identity: "native-stage").ToJson(); node["v"] = 2; node.Remove("stageMaximum"); node.Remove("sidecars"); node.Remove("finalized");
        var old = ArchiveTransactionFacts.Parse(node); Assert.Equal(2, old.RecordVersion); Assert.Equal("native-stage", old.StageIdentity);
        using var journal = f.Journal(); Assert.Throws<InvalidOperationException>(() => journal.ArchiveTransaction(old));
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("parent")]
    [InlineData("bound")]
    [InlineData("identity")]
    public void A_mismatched_reservation_is_refused_before_journaling_or_creating(string mutation)
    {
        using var f = new Fixture(); using var journal = f.Journal(); int calls = 0;
        var facts = new ArchiveTransactionFacts(mutation == "nonce" ? Guid.NewGuid() : f.Reservation.Nonce, 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved,
            Path.Combine(f.Root.Path, "output.tar"), mutation == "parent" ? "other-parent" : "parent", null, f.Reservation.Path,
            mutation == "identity" ? "already-created" : null, stageMaximumLength: mutation == "bound" ? 2048 : 1024);
        Assert.Throws<ArgumentException>(() => ArchiveJournaledStage.Create<NeverStage>(journal, facts, f.Reservation,
            _ => { calls++; throw new InvalidOperationException(); }, CancellationToken.None));
        Assert.Equal(0, calls); Assert.Empty(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
    }

    [Fact]
    public void Creation_failure_leaves_the_exact_precreation_fact_durable()
    {
        using var f = new Fixture(); using var journal = f.Journal();
        Assert.Throws<IOException>(() => ArchiveJournaledStage.Create<NeverStage>(journal, f.Facts(), f.Reservation, _ =>
        {
            Assert.Null(Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions).StageIdentity);
            throw new IOException("native create refused");
        }, CancellationToken.None));
        Assert.True(journal.NeedsRecovery); Assert.Null(Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions).StageIdentity);
    }

    private sealed class Fixture : IDisposable
    {
        internal TempDir Root { get; } = new();
        internal string Journals => Root.Dir("journals");
        internal ArchiveArtifactReservation Reservation { get; }
        internal Fixture() => Reservation = new(this, Guid.NewGuid(), ArchiveArtifactKind.Stage, Root.Path, "parent", 1024);
        internal JobJournal Journal() => JobJournal.Create(Journals, new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
            Archive = new(Path.Combine(Root.Path, "output.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned reservation", "owned", [], []));
        internal ArchiveTransactionFacts Facts(int sequence = 1, ArchiveTransactionPhase phase = ArchiveTransactionPhase.Reserved, string? identity = null, long bound = 1024) =>
            new(Reservation.Nonce, sequence, new(ArchiveWriteFormat.Tar), phase, Path.Combine(Root.Path, "output.tar"), "parent", null, Reservation.Path, identity, stageMaximumLength: bound);
        public void Dispose() => Root.Dispose();
    }

    private sealed class NeverStage : MemoryStream, IArchiveRetainedStage
    {
        public string ObjectIdentity => throw new NotSupportedException();
        public string ReviewCreation(ArchiveArtifactReservation reservation, CancellationToken ct) => throw new NotSupportedException();
        public Stream BeginEvidence(ArchiveArtifactReservation reservation, CancellationToken ct) => throw new NotSupportedException();
        public ArchiveFileObservation SealEvidence(CancellationToken ct) => throw new NotSupportedException();
        public ArchiveFileObservation Observe(CancellationToken ct) => throw new NotSupportedException();
        public void Review(ArchiveFileObservation expected, CancellationToken ct) => throw new NotSupportedException();
        public Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct) => throw new NotSupportedException();
        public Stream BeginRebuild(long exactMaximumLength, CancellationToken ct) => throw new NotSupportedException();
        public ArchiveFileObservation SealForVerification(CancellationToken ct) => throw new NotSupportedException();
        public void StopWrites() { }
    }
}
