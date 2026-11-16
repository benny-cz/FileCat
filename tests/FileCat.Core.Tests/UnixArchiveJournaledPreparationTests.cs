using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Fact]
    public async Task Creation_cannot_include_its_own_destination_and_refuses_before_creating_any_artifact()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); Payload(Path.Combine(root, "result.tar"), "owned input"u8.ToArray());
        using var directory = UnixArchiveDirectory.Open(root); using var input = directory.OpenSource("result.tar"); var state = Observe(input);
        using var journal = ReservationJournal(root); var ct = TestContext.Current.CancellationToken;
        var plan = ArchiveTarTransformPlan.Create(null, [state.Input(1)], [], [new("payload", ArchiveMemberType.File, 1)], ct); int creates = 0;
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveJournaledContainerPreparation.PrepareAsync(journal, Path.Combine(root, "result.tar"), plan, null,
            new Dictionary<int, ArchiveRetainedBinding> { [1] = new(input, state) }, (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum),
            r => { creates++; return directory.CreateArtifact(r); }, new((_, _) => throw new InvalidOperationException()), new((_, _) => throw new InvalidOperationException()), ct));
        Assert.Equal(ArchiveWriteReason.InvalidRequest, failure.Reason); Assert.Equal(0, creates);
        Assert.Empty(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions); Assert.Empty(Directory.GetFiles(root, ".filecat-archive-*"));
        Assert.Equal("owned input", File.ReadAllText(Path.Combine(root, "result.tar")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_requires_the_retained_originals_exact_destination_name_and_native_parent(bool wrongParent)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path), sourceRoot = wrongParent ? Path.Combine(root, "source") : root;
        Directory.CreateDirectory(sourceRoot); string originalPath = Path.Combine(sourceRoot, "original.tar");
        File.WriteAllBytes(originalPath, new byte[1024]); // A canonical empty TAR, including both physical end blocks.
        using var sourceDirectory = UnixArchiveDirectory.Open(sourceRoot); using var original = sourceDirectory.OpenSource("original.tar"); var state = Observe(original);
        var ct = TestContext.Current.CancellationToken;
        var assessment = await new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce)).AssessAsync(new(new(ArchiveWriteFormat.Tar), state.Version.Length, state.Version.Sha256),
            () => ((IArchiveRetainedFile)original).OpenReadView(state, ct), ct);
        var plan = ArchiveTarTransformPlan.Create(assessment, [], [], [], ct); using var directory = UnixArchiveDirectory.Open(root); using var journal = ReservationJournal(root); int creates = 0;
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveJournaledContainerPreparation.PrepareAsync(journal, Path.Combine(root, wrongParent ? "original.tar" : "other.tar"),
            plan, new(original, state), new Dictionary<int, ArchiveRetainedBinding>(), (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum),
            r => { creates++; return directory.CreateArtifact(r); }, new((_, _) => throw new InvalidOperationException()), new((_, _) => throw new InvalidOperationException()), ct));
        Assert.Equal(ArchiveWriteReason.InvalidRequest, failure.Reason); Assert.Equal(0, creates); Assert.Empty(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
        Assert.Equal(state.Version, Observe(original).Version); Assert.Empty(Directory.GetFiles(root, ".filecat-archive-*"));
    }

    [Theory]
    [InlineData("manifest", "reserved")]
    [InlineData("manifest", "identified")]
    [InlineData("manifest", "partial")]
    [InlineData("manifest", "sealed")]
    [InlineData("manifest", "recorded")]
    [InlineData("origin", "reserved")]
    [InlineData("origin", "identified")]
    [InlineData("origin", "partial")]
    [InlineData("origin", "sealed")]
    [InlineData("origin", "recorded")]
    public async Task Real_sidecar_process_crashes_preserve_reserved_partial_and_sealed_evidence(string role, string point)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        var sidecar = await ArchiveSidecarCrashAssertions.KillAndRecover(root, role, point, "-");
        if (sidecar.Identity is { } identity)
        {
            using var directory = UnixArchiveDirectory.Open(root);
            var native = new FileCat.Core.FileSystem.FileIdentity(Convert.ToUInt64(identity[..16], 16), identity[17..]);
            using var file = directory.OpenRecoveryArtifact(Path.GetFileName(sidecar.Path), native,
                role == "manifest" ? UnixArchiveArtifactKind.Manifest : UnixArchiveArtifactKind.Provenance, sidecar.MaximumLength);
            Assert.False(file.CanWrite); var version = file.CaptureVersion(TestContext.Current.CancellationToken);
            Assert.Equal(identity, version.Identity); if (sidecar.Version is not null) Assert.Equal(sidecar.Version, version);
        }
    }

    [Fact]
    public async Task Empty_native_preparation_records_zero_source_provenance_and_complete_observed_manifest()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); using var directory = UnixArchiveDirectory.Open(root);
        using var journal = ReservationJournal(root); var ct = TestContext.Current.CancellationToken; var plan = ArchiveTarTransformPlan.Create(null, [], [], [], ct);
        using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, Path.Combine(root, "result.tar"), plan, null,
            new Dictionary<int, ArchiveRetainedBinding>(), (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum),
            r => { ArchiveJournaledTarAssertions.BeforeCreate(journal, r); return directory.CreateArtifact(r); },
            new((nonce, _) => Child.Start(nonce, "archive-transform")), new((nonce, _) => Child.Start(nonce, "archive-verify")), ct);
        ArchiveJournaledTarAssertions.Complete(prepared, journal, plan, ct); Assert.Equal(0, prepared.Facts.OriginEvidenceSources);
        Assert.Empty(prepared.Verified.Inputs); Assert.Equal(52, prepared.Facts.OriginEvidence!.Length); Assert.False(File.Exists(Path.Combine(root, "result.tar")));
    }

    [Fact]
    public async Task Worker_launch_failure_retains_sealed_evidence_and_closes_every_created_native_handle()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); using var directory = UnixArchiveDirectory.Open(root);
        using var journal = ReservationJournal(root); var ct = TestContext.Current.CancellationToken; var plan = ArchiveTarTransformPlan.Create(null, [], [], [], ct);
        var created = new List<UnixArchiveFile>(); int launches = 0;
        await Assert.ThrowsAsync<IOException>(() => ArchiveJournaledContainerPreparation.PrepareAsync(journal, Path.Combine(root, "result.tar"), plan, null,
            new Dictionary<int, ArchiveRetainedBinding>(), (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum),
            r => { var file = directory.CreateArtifact(r); created.Add(file); return file; },
            new((_, _) => { launches++; throw new IOException("owned launch failure"); }), new((_, _) => throw new InvalidOperationException()), ct));
        Assert.Equal(1, launches); Assert.Equal(3, created.Count); Assert.All(created, f => { Assert.False(f.CanRead); Assert.False(f.CanWrite); Assert.True(File.Exists(f.DisplayPath)); });
        var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
        Assert.Equal(ArchiveTransactionPhase.Rebuilding, facts.Phase); Assert.NotNull(facts.Manifest); Assert.NotNull(facts.OriginEvidence);
        Assert.Null(facts.VerifiedStage); Assert.Null(facts.Sidecars.Single(s => s.Role == ArchiveSidecarRole.VerifiedOutput).Identity); Assert.True(journal.NeedsRecovery);
    }

    [Fact]
    public async Task Cancellation_at_fresh_verifier_launch_retains_real_finalized_stage_and_does_not_record_verification()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); using var directory = UnixArchiveDirectory.Open(root);
        using var journal = ReservationJournal(root); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var plan = ArchiveTarTransformPlan.Create(null, [], [], [], cancel.Token); Child? transform = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArchiveJournaledContainerPreparation.PrepareAsync(journal, Path.Combine(root, "result.tar"), plan, null,
            new Dictionary<int, ArchiveRetainedBinding>(), (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
            new((nonce, _) => transform = Child.Start(nonce, "archive-transform")),
            new((_, _) => { cancel.Cancel(); throw new OperationCanceledException(cancel.Token); }), cancel.Token));
        Assert.NotNull(transform); Assert.True(transform.Reaped);
        var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
        Assert.Equal(ArchiveTransactionPhase.Verifying, facts.Phase); Assert.Null(facts.VerifiedStage);
        Assert.True(new FileInfo(facts.StagePath).Length >= 1024); Assert.Null(facts.Sidecars.Single(s => s.Role == ArchiveSidecarRole.VerifiedOutput).Identity);
        Assert.True(journal.NeedsRecovery); Assert.False(File.Exists(Path.Combine(root, "result.tar")));
    }

    [Fact]
    public async Task A_changed_completed_sidecar_is_detected_before_transform_launch()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); using var directory = UnixArchiveDirectory.Open(root);
        using var journal = ReservationJournal(root); var ct = TestContext.Current.CancellationToken; var plan = ArchiveTarTransformPlan.Create(null, [], [], [], ct); int launches = 0;
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveJournaledContainerPreparation.PrepareAsync(journal, Path.Combine(root, "result.tar"), plan, null,
            new Dictionary<int, ArchiveRetainedBinding>(), (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), r =>
            {
                if (r.Kind == ArchiveArtifactKind.Provenance)
                {
                    var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
                    Assert.NotNull(facts.Manifest); File.WriteAllBytes(facts.ManifestPath!, [1]);
                }
                return directory.CreateArtifact(r);
            }, new((_, _) => { launches++; throw new InvalidOperationException(); }), new((_, _) => throw new InvalidOperationException()), ct));
        Assert.Equal(ArchiveWriteReason.SourceChanged, failure.Reason); Assert.Equal(0, launches);
        var retained = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
        Assert.Equal(ArchiveTransactionPhase.Reserved, retained.Phase); Assert.True(File.Exists(retained.StagePath));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(retained.ManifestPath!)); Assert.True(journal.NeedsRecovery);
    }
}
