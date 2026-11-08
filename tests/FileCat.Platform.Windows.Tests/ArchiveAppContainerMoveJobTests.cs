using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    [Theory, MemberData(nameof(ArchiveNativeJobCases.Profiles), MemberType = typeof(ArchiveNativeJobCases))]
    public async Task Actual_application_native_move_jobs_remove_only_verified_sources(ArchiveWriteFormat format, int mode)
    {
        _ = Probe(); string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
        Assert.True(assets is not null && Path.IsPathFullyQualified(assets)); ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);
        string root = Directory.CreateTempSubdirectory("filecat-native-move-job-").FullName; var ct = TestContext.Current.CancellationToken;
        try
        {
            string a = Path.Combine(root, "a"), b = Path.Combine(root, "b"), folder = Path.Combine(root, "tree");
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            byte[] first = [1, 2, 3], second = [7, 8, 9], nested = "nested move bytes"u8.ToArray();
            File.WriteAllBytes(a, first); File.WriteAllBytes(b, second); File.WriteAllBytes(Path.Combine(folder, "sub/file"), nested);
            File.WriteAllText(a + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            File.WriteAllText(b + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=4\r\n");
            File.WriteAllText(folder + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=2\r\n");
            foreach (string path in new[] { a, b, Path.Combine(folder, "sub/file") }) File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddSeconds(1700000000).AddTicks(7));
            var profile = ArchiveNativeJobCases.Profile(format, mode); string target = Path.Combine(root, "output.zip"), journals = Path.Combine(root, "journals");
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals,
                new([WindowsArchiveJobBackend.Create(format, Path.Combine(assets!, "FileCat.exe"), retainArtifacts: true)]));
            manager.DecisionRequested += pending => Assert.Fail(pending.Request.Message);
            using var password = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
            var job = manager.SubmitArchive(new(target, null,
                [new(ArchiveChangeKind.AddFile, "first", a), new(ArchiveChangeKind.AddFile, "second", b), new(ArchiveChangeKind.AddFolder, "moved", folder)], Profile: profile) { MoveSources = true }, password);
            await ArchiveNativeJobCases.Finished(job);
            Assert.Equal(new[] { 0, 1, 2 }, job.CompletedRootIndices); Assert.Equal(0, job.FailedRootCount);
            Assert.False(File.Exists(a)); Assert.False(File.Exists(b)); Assert.False(Directory.Exists(folder));
            using (var directory = WindowsArchiveDirectory.Open(root))
            using (var file = directory.OpenSource(Path.GetFileName(target)))
                Assert.Equal(4, ArchiveWindowsOriginZone.Parse(((IArchiveRetainedFile)file).Observe(ct).Origin).DeclaredZone);
            var expected = ArchiveNativeJobCases.Expected(Assert.Single(Directory.GetFiles(journals)), profile, ct);
            ArchiveNativeJobCases.Check(expected, new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["first"] = first, ["second"] = second, ["moved/sub/file"] = nested });
            byte[] saved = File.ReadAllBytes(target);
            using var readingPassword = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
            var observed = await new ArchiveAssessmentClient((nonce, cpu) => ArchiveAppContainer.StartAssessment(Path.Combine(assets!, "FileCat.exe"), nonce, cpu, 2), protocolVersion: 2)
                .AssessAsync(new(profile, saved.Length, Convert.ToHexString(SHA256.HashData(saved))), () => new MemoryStream(saved, false), ct, readingPassword?.CreateGrant());
            Assert.Equal(expected.Digest, observed.Manifest.Digest);
            ArchiveNativeJobCases.Export(profile, "moved-created", saved, expected, "FILECAT_ARCHIVE_MOVE_EXPORT_ROOT");
            string firstJournal = Assert.Single(Directory.GetFiles(journals));
            ArchiveFileObservation baseline;
            using (var directory = WindowsArchiveDirectory.Open(root))
            using (var file = directory.OpenSource(Path.GetFileName(target))) baseline = ((IArchiveRetainedFile)file).Observe(ct);
            string added = Path.Combine(root, "added"); byte[] addedBytes = "another moved source"u8.ToArray();
            File.WriteAllBytes(added, addedBytes); File.SetLastWriteTimeUtc(added, DateTime.UnixEpoch.AddSeconds(1700000001));
            using var updatePassword = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
            var update = manager.SubmitArchive(new(target, ArchiveBaseline.Of(target),
                [new(ArchiveChangeKind.AddFile, "added", added)], Profile: profile) { MoveSources = true, NativeBaseline = baseline }, updatePassword);
            try { await ArchiveNativeJobCases.Finished(update); }
            catch
            {
                TestContext.Current.TestOutputHelper?.WriteLine("Move update: " + update.State + " " + update.Summary + " " + JobIssue.Summarize(update.Issues));
                throw;
            }
            Assert.Equal(new[] { 0 }, update.CompletedRootIndices); Assert.Equal(0, update.FailedRootCount); Assert.False(File.Exists(added));
            string updateJournal = Assert.Single(Directory.GetFiles(journals).Except([firstJournal]));
            var updated = ArchiveNativeJobCases.Expected(updateJournal, profile, ct);
            ArchiveNativeJobCases.Check(updated, new Dictionary<string, byte[]>(StringComparer.Ordinal)
                { ["first"] = first, ["second"] = second, ["moved/sub/file"] = nested, ["added"] = addedBytes });
            var transaction = Assert.Single(JournalRecovery.ReadArchiveTransactions(updateJournal).ContentUpdates);
            Assert.Equal(saved, File.ReadAllBytes(transaction.SnapshotPath));
            using (var directory = WindowsArchiveDirectory.Open(root))
            using (var file = directory.OpenSource(Path.GetFileName(target)))
                Assert.Equal(baseline.Version.Identity, ((IArchiveRetainedFile)file).ObjectIdentity);
            byte[] updatedBytes = File.ReadAllBytes(target);
            using var verifyPassword = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
            var updatedReview = await new ArchiveAssessmentClient((nonce, cpu) => ArchiveAppContainer.StartAssessment(Path.Combine(assets!, "FileCat.exe"), nonce, cpu, 2), protocolVersion: 2)
                .AssessAsync(new(profile, updatedBytes.Length, Convert.ToHexString(SHA256.HashData(updatedBytes))), () => new MemoryStream(updatedBytes, false), ct, verifyPassword?.CreateGrant());
            Assert.Equal(updated.Digest, updatedReview.Manifest.Digest);
            ArchiveNativeJobCases.Export(profile, "moved-updated", updatedBytes, updated, "FILECAT_ARCHIVE_MOVE_EXPORT_ROOT");
        }
        finally { TestCleanup.DeleteTree(root); }
    }
}
