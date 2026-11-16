using System.Formats.Tar;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    [Theory]
    [InlineData(ArchiveWriteFormat.Tar, "restore", false)]
    [InlineData(ArchiveWriteFormat.TarGzip, "restore", false)]
    [InlineData(ArchiveWriteFormat.TarBzip2, "restore", false)]
    [InlineData(ArchiveWriteFormat.TarZstd, "restore", false)]
    [InlineData(ArchiveWriteFormat.Tar, "restore", true)]
    [InlineData(ArchiveWriteFormat.TarGzip, "restore", true)]
    [InlineData(ArchiveWriteFormat.TarBzip2, "restore", true)]
    [InlineData(ArchiveWriteFormat.TarZstd, "restore", true)]
    [InlineData(ArchiveWriteFormat.Tar, "changed-target", false)]
    [InlineData(ArchiveWriteFormat.Tar, "changed-snapshot", false)]
    [InlineData(ArchiveWriteFormat.Tar, "stale-journal", false)]
    [InlineData(ArchiveWriteFormat.Tar, "busy-journal", false)]
    public async Task Native_fresh_restoration_keeps_a_current_version_rollback_copy_and_restores_exact_retained_bytes_with_strongest_origin(
        ArchiveWriteFormat format, string mode, bool application)
    {
        string executable = Probe();
        if (application)
        {
            string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
            Assert.True(assets is not null && Path.IsPathFullyQualified(assets), "Supply the complete dedicated application bundle.");
            ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!); executable = Path.Combine(assets!, "FileCat.exe");
        }
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Run explicit NTFS restoration qualification.");
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-archive-restoration-").FullName;
        var workers = new List<ArchiveAppContainer>();
        try
        {
            string path = Path.Combine(root, "archive.tar"), journals = Path.Combine(root, "journals"), journalPath;
            using (var data = File.Create(path))
            using (var wrapper = ContentWrapper(data, format, writing: true))
            using (var writer = new TarWriter(wrapper, TarEntryFormat.Pax, leaveOpen: true))
            using (var bytes = new MemoryStream("retained original"u8.ToArray()))
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "old") { DataStream = bytes, ModificationTime = DateTimeOffset.UnixEpoch });
            File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddSeconds(1700000000));
            File.WriteAllBytes(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://original.invalid/\r\n"u8.ToArray());
            byte[] originalBytes = File.ReadAllBytes(path);
            string inputPath = Path.Combine(root, "input"); File.WriteAllBytes(inputPath, "current replacement"u8.ToArray());
            File.WriteAllBytes(inputPath + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=4\r\n"u8.ToArray());
            File.WriteAllText(Path.Combine(root, "sentinel"), "unrelated retained sentinel");
            var profile = new ArchiveWriteProfile(format); ArchiveFileObservation published;
            using (var directory = WindowsArchiveDirectory.Open(root))
            using (var target = directory.OpenSource("archive.tar"))
            using (var input = directory.OpenSource("input"))
            {
                var before = ((IArchiveRetainedFile)target).Observe(ct); var captured = ((IArchiveRetainedFile)input).Observe(ct);
                var assessed = await new ArchiveAssessmentClient((nonce, cpu) => Start("archive-assess", nonce, cpu)).AssessAsync(
                    new(profile, before.Version.Length, before.Version.Sha256), () => ((IArchiveRetainedFile)target).OpenReadView(before, ct), ct);
                var plan = ArchiveTarTransformPlan.Create(profile, assessed, [captured.Input(3)],
                    [new(ArchiveChangeKind.Replace, new(assessed.CatalogRevision, "old", ArchiveMemberType.File, 0), inputId: 3)], [], ct);
                using var journal = NewJournal("initial qualified update"); journalPath = journal.Path;
                using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, path, plan, new(target, before),
                    new Dictionary<int, ArchiveRetainedBinding> { [3] = new(input, captured) },
                    (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
                    new((nonce, cpu) => Start("archive-transform", nonce, cpu)), new((nonce, cpu) => Start("archive-verify", nonce, cpu)), ct);
                using var committed = WindowsArchiveContentUpdate.Publish(journal, prepared, target,
                    new Dictionary<int, ArchiveRetainedBinding> { [3] = new(input, captured) }, TimeSpan.FromMinutes(2), ct);
                published = committed.Published;
            }
            var old = JournalRecovery.ReadArchiveTransactions(journalPath); var content = Assert.Single(old.ContentUpdates);
            var catalog = Assert.Single(old.ContentCleanups); byte[] currentBytes = File.ReadAllBytes(path);
            Assert.NotEqual(originalBytes, currentBytes); string expected = Hash(journalPath);
            if (mode == "changed-target") File.WriteAllBytes(path, "a later user version"u8.ToArray());
            if (mode == "changed-snapshot") File.SetLastWriteTimeUtc(content.SnapshotPath, DateTime.UnixEpoch.AddSeconds(1700000123));
            if (mode == "stale-journal") File.AppendAllText(journalPath, "a later torn record");
            string[] beforeRefusal = Tree();
            if (mode != "restore")
            {
                if (mode == "busy-journal") { using var busy = new FileStream(journalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read); Refused(); }
                else Refused();
                Assert.Equal(beforeRefusal, Tree()); return;
                void Refused()
                {
                    var error = Record.Exception(() => { using var rejected = WindowsArchiveRestorationLease.Open(journalPath, expected, content.Nonce, ct); });
                    Assert.True(error is ArchiveWriteException or IOException or System.ComponentModel.Win32Exception, error?.ToString());
                }
            }
            string newJournalPath;
            using (var lease = WindowsArchiveRestorationLease.Open(journalPath, expected, content.Nonce, ct))
            using (var fresh = NewJournal("restore retained archive"))
            {
                newJournalPath = fresh.Path;
                var currentMetadata = ((WindowsArchiveFile)lease.Target.File).CaptureOuterMetadata(ct);
                using var restored = await ArchiveContentRestoration.RestoreAsync(lease, fresh,
                    new((nonce, cpu) => Start("archive-assess", nonce, cpu)), new((nonce, cpu) => Start("archive-verify", nonce, cpu)), TimeSpan.FromMinutes(2), ct);
                Assert.Equal(originalBytes, ReadContentShared(path));
                Assert.Equal(currentBytes, ReadContentShared(restored.Update.Facts.SnapshotPath));
                Assert.Equal(originalBytes, ReadContentShared(content.SnapshotPath));
                Assert.Equal(published.Version.Identity, restored.Update.Published.Version.Identity);
                Assert.Equal(published.Origin.Digest, restored.Update.Published.Origin.Digest);
                using (var currentDirectory = WindowsArchiveDirectory.Open(root))
                using (var currentTarget = currentDirectory.OpenSource("archive.tar"))
                {
                    var actualMetadata = currentTarget.CaptureOuterMetadata(ct);
                    Assert.Equal(currentMetadata.CreationTime, actualMetadata.CreationTime);
                    Assert.Equal(currentMetadata.SecurityDescriptor.ToArray(), actualMetadata.SecurityDescriptor.ToArray());
                    Assert.Equal(currentMetadata.Attributes, actualMetadata.Attributes);
                    // Restoring is a new content write; the archive modification time is allowed to advance.
                }
                Assert.Equal(4, ArchiveWindowsOriginZone.Parse(restored.Update.Published.Origin).DeclaredZone);
                Assert.Equal(ArchiveContentRestorationPhase.Committed, restored.Facts.Phase);
                Assert.Equal(fresh.Path, restored.Facts.RestoreJournalPath);
                Assert.True(JournalRecovery.ReadArchiveTransactions(journalPath).RequiresReview);
                Assert.Throws<ArchiveWriteException>(() => lease.Claim(ct));
                // Explicit caller-approved cleanup of this new job; the source journal/snapshot remain separate.
                Assert.True(restored.Update.Cleanup(fresh, ct).IsResolved); fresh.Finish(JobState.Completed, "fresh restoration and its private cleanup completed");
                Assert.True(File.Exists(content.SnapshotPath));
            }
            using (var cleanup = WindowsArchiveCleanupRecovery.Open(journalPath, Hash(journalPath), content.Nonce, ct))
            {
                var outcome = cleanup.ResumeCleanup(ct);
                Assert.Equal(ArchiveContentRecoveryTarget.RestoredIntact, outcome.TargetState);
                Assert.Equal(2, outcome.RecordVersion); Assert.NotNull(outcome.RestorationSha256);
                cleanup.Finish("restored archive freshly matched; retained source artifacts were explicitly cleaned");
            }
            Assert.False(JournalRecovery.ReadArchiveTransactions(journalPath).RequiresReview);
            Assert.False(JournalRecovery.ReadArchiveTransactions(newJournalPath).RequiresReview);
            Assert.All(catalog.Artifacts, row => Assert.False(File.Exists(row.Path)));
            Assert.Equal(originalBytes, File.ReadAllBytes(path)); Assert.Equal("unrelated retained sentinel", File.ReadAllText(Path.Combine(root, "sentinel")));
            Assert.Equal(5, workers.Count); Assert.Equal(5, workers.Select(worker => worker.ProcessId).Distinct().Count());
            Assert.All(workers, worker => { Assert.True(worker.HasExited); Assert.Equal(0u, worker.ExitCode); });
            JobJournal NewJournal(string title) => JobJournal.Create(journals, new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                Archive = new(path, null, [], Profile: profile) }, title, "owned", [], []));
        }
        finally { Directory.Delete(root, recursive: true); }
        ArchiveAppContainer Start(string role, Guid nonce, TimeSpan cpu)
        {
            var worker = application ? role switch
            {
                "archive-assess" => ArchiveAppContainer.StartAssessment(executable, nonce, cpu),
                "archive-transform" => ArchiveAppContainer.StartTransform(executable, nonce, cpu),
                "archive-verify" => ArchiveAppContainer.StartVerification(executable, nonce, cpu),
                _ => throw new ArgumentException("Unknown test role.", nameof(role)),
            } : ArchiveAppContainer.Start(executable, role + " " + nonce, cpu);
            workers.Add(worker); return worker;
        }
        static string Hash(string path) => Convert.ToHexString(SHA256.HashData(ReadContentShared(path)));
        string[] Tree() => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path) + ":" + Hash(path)).ToArray();
    }
}
