using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Creation_review_refuses_a_different_name_or_bound_and_does_not_seal_the_stage(bool boundChanged)
    {
        Native(); using var temp = new TempDir(); using var directory = UnixArchiveDirectory.Open(Canonical(temp.Path));
        var reservation = directory.ReserveArtifact(Guid.NewGuid(), UnixArchiveArtifactKind.Stage, 1024);
        using var stage = directory.CreateArtifact(reservation);
        var other = directory.ReserveArtifact(reservation.Nonce, UnixArchiveArtifactKind.Stage, boundChanged ? 1025 : 1024);
        Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() => ((IArchiveRetainedStage)stage).ReviewCreation(other, TestContext.Current.CancellationToken)).Reason);
        Assert.Equal(stage.Identity.ToString(), ((IArchiveRetainedStage)stage).ReviewCreation(reservation, TestContext.Current.CancellationToken));
        Assert.True(stage.CanWrite); Assert.Throws<InvalidOperationException>(() => ((IArchiveRetainedFile)stage).Observe(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("reserved")]
    [InlineData("created")]
    [InlineData("recorded")]
    public async Task External_process_termination_preserves_precreation_and_created_identity_evidence(string point)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        var recovered = await FileCat.Tests.ArchiveStageCrashAssertions.KillAndRecover(root, point, "-");
        using var directory = UnixArchiveDirectory.Open(root); Assert.Equal(directory.Identity.ToString(), recovered.ParentIdentity);
        if (recovered.StageIdentity is { } identity)
        {
            var native = new FileCat.Core.FileSystem.FileIdentity(Convert.ToUInt64(identity[..16], 16), identity[17..]);
            using var file = directory.OpenRecoveryArtifact(Path.GetFileName(recovered.StagePath), native, UnixArchiveArtifactKind.Stage, recovered.StageMaximumLength);
            Assert.Equal(0, file.Length); Assert.Equal(identity, Version(file).Identity); Assert.False(file.CanWrite);
        }
    }

    [Fact]
    public void Identity_journal_failure_closes_the_new_stage_and_retains_its_durable_reservation()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); using var directory = UnixArchiveDirectory.Open(root);
        var r = directory.ReserveArtifact(Guid.NewGuid(), UnixArchiveArtifactKind.Stage, 1024); using var journal = ReservationJournal(root); UnixArchiveFile? created = null;
        var facts = new ArchiveTransactionFacts(r.Nonce, 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved,
            Path.Combine(root, "result.tar"), r.ParentIdentity, null, r.Path, null, stageMaximumLength: r.MaximumLength);
        Assert.Throws<ObjectDisposedException>(() => ArchiveJournaledStage.Create(journal, facts, r, reservation =>
        { created = directory.CreateArtifact(reservation); journal.Dispose(); return created; }, TestContext.Current.CancellationToken));
        Assert.NotNull(created); Assert.False(created.CanRead); Assert.False(created.CanWrite); Assert.True(File.Exists(r.Path));
        Assert.Null(Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions).StageIdentity);
    }

    [Fact]
    public void Reserved_native_name_is_created_only_after_its_exact_intent_is_durable()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); using var directory = UnixArchiveDirectory.Open(root);
        var reservation = directory.ReserveArtifact(Guid.NewGuid(), UnixArchiveArtifactKind.Stage, 1024);
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        var facts = new ArchiveTransactionFacts(reservation.Nonce, 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved,
            Path.Combine(root, "result.tar"), directory.Identity.ToString(), null, reservation.Path, null, stageMaximumLength: 1024);
        using var journal = ReservationJournal(root);
        using var stage = ArchiveJournaledStage.Create(journal, facts, reservation, r =>
        {
            var before = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
            Assert.Null(before.StageIdentity); Assert.False(File.Exists(r.Path)); Assert.Equal(r.Path, before.StagePath);
            return directory.CreateArtifact(r);
        }, TestContext.Current.CancellationToken);
        var created = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
        Assert.Equal(stage.Identity.ToString(), created.StageIdentity); Assert.Equal(2, created.Sequence); Assert.Equal(1024, created.StageMaximumLength);
        Assert.Equal(reservation.Path, stage.DisplayPath); Assert.Throws<ArchiveWriteException>(() => stage.SetLength(1025));
        Assert.Throws<InvalidOperationException>(() => directory.CreateArtifact(reservation)); Assert.True(journal.NeedsRecovery);
    }

    [Fact]
    public void Reserved_name_collision_preserves_the_other_object_and_does_not_choose_an_unrecorded_name()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); using var directory = UnixArchiveDirectory.Open(root);
        var reservation = directory.ReserveArtifact(Guid.NewGuid(), UnixArchiveArtifactKind.Stage, 1024); File.WriteAllText(reservation.Path, "other object");
        using var journal = ReservationJournal(root);
        var facts = new ArchiveTransactionFacts(reservation.Nonce, 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved,
            Path.Combine(root, "result.tar"), directory.Identity.ToString(), null, reservation.Path, null, stageMaximumLength: 1024);
        Assert.ThrowsAny<IOException>(() => ArchiveJournaledStage.Create(journal, facts, reservation, directory.CreateArtifact, TestContext.Current.CancellationToken));
        Assert.Equal("other object", File.ReadAllText(reservation.Path)); Assert.Single(Directory.GetFiles(root, ".filecat-archive-*"));
        Assert.Null(Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions).StageIdentity);
        Assert.Throws<InvalidOperationException>(() => directory.CreateArtifact(reservation));
    }

    [Fact]
    public void A_second_directory_handle_cannot_consume_another_instances_reservation()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); using var first = UnixArchiveDirectory.Open(root); using var second = UnixArchiveDirectory.Open(root);
        var reservation = first.ReserveArtifact(Guid.NewGuid(), UnixArchiveArtifactKind.Manifest, 64);
        Assert.Throws<ArgumentException>(() => second.CreateArtifact(reservation)); Assert.False(File.Exists(reservation.Path));
        using var created = first.CreateArtifact(reservation); Assert.Equal(reservation.Path, created.DisplayPath);
    }

    private static JobJournal ReservationJournal(string root, ArchiveWriteProfile? profile = null) => JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
        Archive = new(Path.Combine(root, "result.tar"), null, [], Profile: profile ?? new(ArchiveWriteFormat.Tar)) }, "owned native reservation", "owned", [], []));
}
