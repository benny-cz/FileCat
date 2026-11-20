using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Creation_review_refuses_a_different_name_or_bound_and_does_not_seal_the_stage(bool boundChanged)
    {
        RequireWindows(); using var f = new Fixture(); using var directory = WindowsArchiveDirectory.Open(f.Root);
        var reservation = directory.ReserveArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Stage, 1024);
        using var stage = directory.CreateArtifact(reservation);
        var other = directory.ReserveArtifact(reservation.Nonce, WindowsArchiveArtifactKind.Stage, boundChanged ? 1025 : 1024);
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
        RequireWindows(); using var f = new Fixture();
        var recovered = await FileCat.Tests.ArchiveStageCrashAssertions.KillAndRecover(f.Root, point, typeof(WindowsArchiveDirectory).Assembly.Location);
        using var directory = WindowsArchiveDirectory.Open(f.Root); Assert.Equal(directory.Identity.ToString(), recovered.ParentIdentity);
        if (recovered.StageIdentity is { } identity)
        {
            var native = new FileCat.Core.FileSystem.FileIdentity(Convert.ToUInt64(identity[..16], 16), identity[17..]);
            using var file = directory.OpenRecoveryArtifact(Path.GetFileName(recovered.StagePath), native, WindowsArchiveArtifactKind.Stage, recovered.StageMaximumLength);
            Assert.Equal(0, file.Length); Assert.Equal(identity, file.CaptureVersion(TestContext.Current.CancellationToken).Identity); Assert.False(file.CanWrite);
        }
    }

    [Fact]
    public void Identity_journal_failure_closes_the_new_stage_and_retains_its_durable_reservation()
    {
        RequireWindows(); using var f = new Fixture(); using var directory = WindowsArchiveDirectory.Open(f.Root);
        var r = directory.ReserveArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Stage, 1024); using var journal = ReservationJournal(f.Root); WindowsArchiveFile? created = null;
        var facts = new ArchiveTransactionFacts(r.Nonce, 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved,
            f.PathOf("result.tar"), r.ParentIdentity, null, r.Path, null, stageMaximumLength: r.MaximumLength);
        Assert.Throws<ObjectDisposedException>(() => ArchiveJournaledStage.Create(journal, facts, r, reservation =>
        { created = directory.CreateArtifact(reservation); journal.Dispose(); return created; }, TestContext.Current.CancellationToken));
        Assert.NotNull(created); Assert.False(created.CanRead); Assert.False(created.CanWrite); Assert.True(File.Exists(r.Path));
        Assert.Null(Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions).StageIdentity);
    }

    [Fact]
    public void Reserved_native_name_is_created_only_after_its_exact_intent_is_durable()
    {
        RequireWindows(); using var f = new Fixture(); using var directory = WindowsArchiveDirectory.Open(f.Root);
        var reservation = directory.ReserveArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Stage, 1024);
        Assert.Empty(Directory.EnumerateFileSystemEntries(f.Root)); using var journal = ReservationJournal(f.Root);
        var facts = new ArchiveTransactionFacts(reservation.Nonce, 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved,
            f.PathOf("result.tar"), directory.Identity.ToString(), null, reservation.Path, null, stageMaximumLength: 1024);
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
        RequireWindows(); using var f = new Fixture(); using var directory = WindowsArchiveDirectory.Open(f.Root);
        var reservation = directory.ReserveArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Stage, 1024); File.WriteAllText(reservation.Path, "other object");
        using var journal = ReservationJournal(f.Root);
        var facts = new ArchiveTransactionFacts(reservation.Nonce, 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved,
            f.PathOf("result.tar"), directory.Identity.ToString(), null, reservation.Path, null, stageMaximumLength: 1024);
        Assert.ThrowsAny<IOException>(() => ArchiveJournaledStage.Create(journal, facts, reservation, directory.CreateArtifact, TestContext.Current.CancellationToken));
        Assert.Equal("other object", File.ReadAllText(reservation.Path)); Assert.Single(Directory.GetFiles(f.Root, ".filecat-archive-*"));
        Assert.Null(Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions).StageIdentity);
        Assert.Throws<InvalidOperationException>(() => directory.CreateArtifact(reservation));
    }

    [Fact]
    public void A_second_directory_handle_cannot_consume_another_instances_reservation()
    {
        RequireWindows(); using var f = new Fixture(); using var first = WindowsArchiveDirectory.Open(f.Root); using var second = WindowsArchiveDirectory.Open(f.Root);
        var reservation = first.ReserveArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Manifest, 64);
        Assert.Throws<ArgumentException>(() => second.CreateArtifact(reservation)); Assert.False(File.Exists(reservation.Path));
        using var created = first.CreateArtifact(reservation); Assert.Equal(reservation.Path, created.DisplayPath);
    }

    private static JobJournal ReservationJournal(string root) => JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
        Archive = new(Path.Combine(root, "result.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned native reservation", "owned", [], []));
}
