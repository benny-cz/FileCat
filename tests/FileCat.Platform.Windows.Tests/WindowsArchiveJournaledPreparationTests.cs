using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
    [Fact]
    public async Task Creation_cannot_include_its_own_destination_and_refuses_before_creating_any_artifact()
    {
        RequireWindows(); using var f = new Fixture(); File.WriteAllBytes(f.PathOf("result.tar"), "owned input"u8.ToArray());
        using var directory = WindowsArchiveDirectory.Open(f.Root); using var input = directory.OpenSource("result.tar"); var ct = TestContext.Current.CancellationToken;
        var state = ((IArchiveRetainedFile)input).Observe(ct); using var journal = ReservationJournal(f.Root);
        var plan = ArchiveTarTransformPlan.Create(null, [state.Input(1)], [], [new("payload", ArchiveMemberType.File, 1)], ct); int creates = 0;
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveJournaledContainerPreparation.PrepareAsync(journal, f.PathOf("result.tar"), plan, null,
            new Dictionary<int, ArchiveRetainedBinding> { [1] = new(input, state) }, (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum),
            r => { creates++; return directory.CreateArtifact(r); }, new((_, _) => throw new InvalidOperationException()), new((_, _) => throw new InvalidOperationException()), ct));
        Assert.Equal(ArchiveWriteReason.InvalidRequest, failure.Reason); Assert.Equal(0, creates); Assert.Empty(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
        Assert.Empty(Directory.GetFiles(f.Root, ".filecat-archive-*")); Assert.Equal("owned input", File.ReadAllText(f.PathOf("result.tar")));
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
        RequireWindows(); using var f = new Fixture();
        var sidecar = await FileCat.Tests.ArchiveSidecarCrashAssertions.KillAndRecover(f.Root, role, point, typeof(WindowsArchiveDirectory).Assembly.Location);
        if (sidecar.Identity is { } identity)
        {
            using var directory = WindowsArchiveDirectory.Open(f.Root);
            var native = new FileCat.Core.FileSystem.FileIdentity(Convert.ToUInt64(identity[..16], 16), identity[17..]);
            using var file = directory.OpenRecoveryArtifact(Path.GetFileName(sidecar.Path), native,
                role == "manifest" ? WindowsArchiveArtifactKind.Manifest : WindowsArchiveArtifactKind.Provenance, sidecar.MaximumLength);
            Assert.False(file.CanWrite); var version = file.CaptureVersion(TestContext.Current.CancellationToken);
            Assert.Equal(identity, version.Identity); if (sidecar.Version is not null) Assert.Equal(sidecar.Version, version);
        }
    }

    [Fact]
    public async Task Worker_launch_failure_retains_sealed_evidence_and_closes_every_created_native_handle()
    {
        RequireWindows(); using var f = new Fixture(); using var directory = WindowsArchiveDirectory.Open(f.Root);
        using var journal = ReservationJournal(f.Root); var ct = TestContext.Current.CancellationToken; var plan = ArchiveTarTransformPlan.Create(null, [], [], [], ct);
        var created = new List<WindowsArchiveFile>(); int launches = 0;
        await Assert.ThrowsAsync<IOException>(() => ArchiveJournaledContainerPreparation.PrepareAsync(journal, f.PathOf("result.tar"), plan, null,
            new Dictionary<int, ArchiveRetainedBinding>(), (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum),
            r => { var file = directory.CreateArtifact(r); created.Add(file); return file; },
            new((_, _) => { launches++; throw new IOException("owned launch failure"); }), new((_, _) => throw new InvalidOperationException()), ct));
        Assert.Equal(1, launches); Assert.Equal(3, created.Count); Assert.All(created, file => { Assert.False(file.CanRead); Assert.False(file.CanWrite); Assert.True(File.Exists(file.DisplayPath)); });
        var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
        Assert.Equal(ArchiveTransactionPhase.Rebuilding, facts.Phase); Assert.NotNull(facts.Manifest); Assert.NotNull(facts.OriginEvidence);
        Assert.Null(facts.VerifiedStage); Assert.Null(facts.Sidecars.Single(s => s.Role == ArchiveSidecarRole.VerifiedOutput).Identity); Assert.True(journal.NeedsRecovery);
    }
}
