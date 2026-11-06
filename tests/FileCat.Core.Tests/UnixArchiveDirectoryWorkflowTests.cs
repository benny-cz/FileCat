using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Directory_workflow_publishes_a_folder_only_archive_with_no_payload_grants(bool capturedFolder)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0); Directory.CreateDirectory(Path.Combine(root, "source"));
        using var directory = UnixArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
        if (capturedFolder) directory.CaptureSourceDirectory(captured, 1, "source", ct); captured.Seal(ct);
        var addition = capturedFolder ? captured.DirectoryAddition("empty/", 1) : new ArchiveTransformAddition("empty/", new ArchiveTransformDirectoryMetadata(new(-1, 999999999), 0x1c0));
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar); var plan = captured.CreatePlan(profile, null, [], [addition], ct);
        var invocation = ArchiveTransformWire.Prepare(plan, ct); Assert.Empty(invocation.Request.Inputs); Assert.Empty(invocation.CapturedInputIds);
        using var journal = ReservationJournal(root, profile);
        using var prepared = await ArchiveJournaledTarPreparation.PrepareAsync(journal, Path.Combine(root, "result.tar"), plan, null, captured.Bindings,
            (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
            new((nonce, _) => Child.Start(nonce, "archive-transform")), new((nonce, _) => Child.Start(nonce, "archive-verify")), ct);
        Assert.Equal(capturedFolder ? 1 : 0, prepared.Facts.Sidecars.Single(s => s.Role == ArchiveSidecarRole.OriginEvidence).OriginSources);
        Assert.Equal(ArchiveMemberType.Directory, Assert.Single(prepared.Verified.Verification.ObservedManifest.Members).Type);
        await ArchiveJournaledTarAssertions.PublishNew(prepared, journal, captured.Bindings, directory.CreateArtifact, ct);
    }

    [Theory]
    [InlineData(ArchiveWriteFormat.Tar, false)]
    [InlineData(ArchiveWriteFormat.TarGzip, false)]
    [InlineData(ArchiveWriteFormat.TarBzip2, false)]
    [InlineData(ArchiveWriteFormat.TarZstd, false)]
    [InlineData(ArchiveWriteFormat.Tar, true)]
    public async Task Directory_workflow_creates_and_publishes_verified_folders_with_native_metadata(ArchiveWriteFormat format, bool changed)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string tree = Directory.CreateDirectory(Path.Combine(root, "source-tree")).FullName;
        string empty = Directory.CreateDirectory(Path.Combine(root, "source-empty")).FullName;
        Payload(Path.Combine(tree, "payload"), [7, 0, 255, 9]); File.SetUnixFileMode(tree, (UnixFileMode)0x1d9); File.SetUnixFileMode(empty, (UnixFileMode)0x1c0);
        ArchiveNativeTimestampFixture.SetModified(tree, -1, 999999999); ArchiveNativeTimestampFixture.SetModified(empty, 1700000000, 123456789);
        using var directory = UnixArchiveDirectory.Open(root); using var source = UnixArchiveDirectory.Open(tree);
        using var captured = new ArchiveCapturedInputs(); directory.CaptureSourceDirectory(captured, 1, "source-tree", ct);
        directory.CaptureSourceDirectory(captured, 2, "source-empty", ct); source.CaptureSource(captured, 3, "payload", ct); captured.Seal(ct);
        var profile = new ArchiveWriteProfile(format); var plan = captured.CreatePlan(profile, null, [],
            [captured.DirectoryAddition("tree/", 1), captured.DirectoryAddition("empty/", 2), new("tree/file.bin", ArchiveMemberType.File, 3)], ct);
        using var journal = ReservationJournal(root, profile); var workers = new List<Child>();
        Child Launch(Guid nonce, string role) { var worker = Child.Start(nonce, role); workers.Add(worker); return worker; }
        using var prepared = await ArchiveJournaledTarPreparation.PrepareAsync(journal, Path.Combine(root, "result.tar"), plan, null, captured.Bindings,
            (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
            new((nonce, _) => Launch(nonce, "archive-transform")), new((nonce, _) => Launch(nonce, "archive-verify")), ct);
        Assert.Equal(2, prepared.Verified.Directories.Count); Assert.Equal(3, prepared.Facts.Sidecars.Single(s => s.Role == ArchiveSidecarRole.OriginEvidence).OriginSources);
        Assert.Equal(plan.Expected.Digest, prepared.Verified.Verification.ManifestDigest);
        Assert.Equal(2, workers.Count); Assert.All(workers, worker => Assert.True(worker.Reaped));
        if (changed)
        {
            ArchiveNativeTimestampFixture.SetModified(tree, -1, 999999998);
            await Assert.ThrowsAnyAsync<IOException>(() => ArchiveNewFilePublication.PublishAsync(journal, prepared, captured.Bindings, directory.CreateArtifact, ct));
            Assert.False(File.Exists(Path.Combine(root, "result.tar"))); Assert.True(journal.NeedsRecovery); return;
        }
        await ArchiveJournaledTarAssertions.PublishNew(prepared, journal, captured.Bindings, directory.CreateArtifact, ct);
        string? oracle = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_DIRECTORY_ORACLES");
        if (oracle is not null)
        {
            Assert.True(Path.IsPathFullyQualified(oracle) && Directory.Exists(oracle));
            File.Copy(Path.Combine(root, "result.tar"), Path.Combine(oracle, "unix-" + format + ".archive"));
            File.WriteAllText(Path.Combine(oracle, "unix-" + format + ".json"), JsonSerializer.Serialize(new { format = format.ToString(),
                directories = new[] { new { name = "tree/", mode = 0x1d9, seconds = -1L, nanos = 999999999 }, new { name = "empty/", mode = 0x1c0, seconds = 1700000000L, nanos = 123456789 } },
                file = "tree/file.bin", payload = new byte[] { 7, 0, 255, 9 }, manifest = plan.Expected.Digest }));
        }
    }

    [Fact]
    public async Task Directory_workflow_refuses_staging_inside_the_captured_source_before_artifact_creation()
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path);
        string tree = Directory.CreateDirectory(Path.Combine(root, "tree")).FullName;
        using var parent = UnixArchiveDirectory.Open(root); using var destination = UnixArchiveDirectory.Open(tree); using var captured = new ArchiveCapturedInputs();
        parent.CaptureSourceDirectory(captured, 1, "tree", ct); captured.Seal(ct);
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar); var plan = captured.CreatePlan(profile, null, [], [captured.DirectoryAddition("tree/", 1)], ct);
        using var journal = ReservationJournal(root, profile); int effects = 0;
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveJournaledTarPreparation.PrepareAsync<UnixArchiveFile>(journal,
            Path.Combine(tree, "result.tar"), plan, null, captured.Bindings,
            (nonce, kind, maximum) => destination.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum),
            _ => { effects++; throw new InvalidOperationException(); }, new((_, _) => { effects++; throw new InvalidOperationException(); }), new((_, _) => throw new InvalidOperationException()), ct));
        Assert.Equal(ArchiveWriteReason.InvalidRequest, failure.Reason); Assert.Equal(0, effects); Assert.Empty(Directory.GetFileSystemEntries(tree));
        Assert.Empty(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
    }
}
