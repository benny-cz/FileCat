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
    public async Task Actual_application_native_jobs_create_update_and_retain_exact_recovery_snapshots(ArchiveWriteFormat format, int mode)
    {
        _ = Probe();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Explicit native transactional job lane.");
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS"); Assert.True(assets is not null && Path.IsPathFullyQualified(assets));
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!); string executable = Path.Combine(assets!, "FileCat.exe");
        string root = Directory.CreateTempSubdirectory("filecat-native-jobs-").FullName; var ct = TestContext.Current.CancellationToken;
        try
        {
            var changes = ArchiveNativeJobCases.Fixture(root); var payloads = ArchiveNativeJobCases.Payloads(root);
            File.WriteAllText(Path.Combine(root, "a/payload") + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            File.WriteAllText(Path.Combine(root, "b/payload") + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=4\r\n");
            File.WriteAllText(Path.Combine(root, "a/tree") + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=2\r\n");
            var profile = ArchiveNativeJobCases.Profile(format, mode);
            string target = Path.Combine(root, "typed-output.zip"), journals = Path.Combine(root, "journals");
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([WindowsArchiveJobBackend.Create(format, executable, retainArtifacts: true)]));
            manager.DecisionRequested += pending => Assert.Fail("Fixture has no conflict or exclusion: " + pending.Request.Message);
            using var password = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
            var created = manager.SubmitArchive(new(target, null, changes, Profile: profile), password); await ArchiveNativeJobCases.Finished(created);
            if (password is not null) Assert.Throws<ObjectDisposedException>(() => password.CreateGrant());
            string firstJournal = Assert.Single(Directory.GetFiles(journals));
            var originalManifest = ArchiveNativeJobCases.Expected(firstJournal, profile, ct); ArchiveNativeJobCases.Check(originalManifest, payloads);
            byte[] originalBytes = File.ReadAllBytes(target); await Fresh(originalBytes, originalManifest);
            ArchiveNativeJobCases.Export(profile, "created", originalBytes, originalManifest);
            ArchiveFileObservation original;
            using (var directory = WindowsArchiveDirectory.Open(root))
            using (var file = directory.OpenSource(Path.GetFileName(target))) original = ((IArchiveRetainedFile)file).Observe(ct);
            Assert.Equal(4, ArchiveWindowsOriginZone.Parse(original.Origin).DeclaredZone);
            File.WriteAllBytes(Path.Combine(root, "a/payload"), "new explicit replacement"u8.ToArray());
            File.SetLastWriteTimeUtc(Path.Combine(root, "a/payload"), DateTime.UnixEpoch.AddSeconds(1700000003).AddTicks(9));
            ArchiveChange[] updates = [new(ArchiveChangeKind.Replace, "Case", Path.Combine(root, "a/payload"), Ordinal: 0),
                new(ArchiveChangeKind.Delete, "case", Ordinal: 0), new(ArchiveChangeKind.Rename, "renamed", NewMemberPath: "moved"),
                new(ArchiveChangeKind.AddFile, "added", Path.Combine(root, "b/payload")), new(ArchiveChangeKind.AddFolder, "empty", Path.Combine(root, "a/tree")),
                new(ArchiveChangeKind.CreateFolderEntry, "new-empty/")];
            using var secondPassword = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
            var pathsBeforeReview = Directory.GetFiles(root).Order(StringComparer.Ordinal).ToArray();
            var reviewed = await manager.ReviewArchiveUpdateAsync(target, profile, secondPassword, ct);
            Assert.True(original.Matches(reviewed.Native)); Assert.Equal(originalManifest.Digest, reviewed.Assessment.Manifest.Digest);
            Assert.Equal(pathsBeforeReview, Directory.GetFiles(root).Order(StringComparer.Ordinal).ToArray());
            var update = reviewed.CreatePlan(updates);
            Assert.Equal(originalManifest.Digest, update.ReviewedManifestDigest);
            using var stalePassword = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
            var stale = manager.SubmitArchive(update with { ReviewedManifestDigest = new string('0', 64) }, stalePassword);
            await ArchiveNativeJobCases.Finished(stale); Assert.Equal(JobState.Failed, stale.State);
            string staleJournal = Assert.Single(Directory.GetFiles(journals).Except([firstJournal]));
            Assert.Equal(originalBytes, File.ReadAllBytes(target));
            Assert.Equal(pathsBeforeReview, Directory.GetFiles(root).Order(StringComparer.Ordinal).ToArray());
            var updated = manager.SubmitArchive(update, secondPassword); await ArchiveNativeJobCases.Finished(updated);
            if (secondPassword is not null) Assert.Throws<ObjectDisposedException>(() => secondPassword.CreateGrant());
            string updateJournal = Assert.Single(Directory.GetFiles(journals).Except([firstJournal, staleJournal]));
            var manifest = ArchiveNativeJobCases.Expected(updateJournal, profile, ct);
            payloads["Case"] = File.ReadAllBytes(Path.Combine(root, "a/payload")); payloads.Remove("case");
            payloads["moved/sub/file"] = payloads["renamed/sub/file"]; payloads.Remove("renamed/sub/file");
            payloads["added"] = File.ReadAllBytes(Path.Combine(root, "b/payload")); payloads["empty/sub/file"] = File.ReadAllBytes(Path.Combine(root, "a/tree/sub/file"));
            ArchiveNativeJobCases.Check(manifest, payloads);
            Assert.Equal(originalManifest.Members.Single(member => member.Name.TrimEnd('/') == "empty").Metadata.ToArray(),
                manifest.Members.Single(member => member.Name.TrimEnd('/') == "empty").Metadata.ToArray());
            byte[] updatedBytes = File.ReadAllBytes(target); await Fresh(updatedBytes, manifest);
            ArchiveNativeJobCases.Export(profile, "updated", updatedBytes, manifest);
            var content = Assert.Single(JournalRecovery.ReadArchiveTransactions(updateJournal).ContentUpdates);
            Assert.Equal(ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts, content.Phase);
            Assert.Equal(originalBytes, File.ReadAllBytes(content.SnapshotPath));
            using (var directory = WindowsArchiveDirectory.Open(root))
            using (var file = directory.OpenSource(Path.GetFileName(target)))
            {
                var after = ((IArchiveRetainedFile)file).Observe(ct); Assert.Equal(original.Version.Identity, after.Version.Identity);
                Assert.Equal(4, ArchiveWindowsOriginZone.Parse(after.Origin).DeclaredZone);
            }
            foreach (string journal in Directory.GetFiles(journals)) Assert.DoesNotContain(ArchiveNativeJobCases.Password, File.ReadAllText(journal));

            async Task Fresh(byte[] bytes, ArchiveWriteManifest expected)
            {
                using var readingPassword = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
                var observed = await new ArchiveAssessmentClient((nonce, cpu) => ArchiveAppContainer.StartAssessment(executable, nonce, cpu, 2), protocolVersion: 2)
                    .AssessAsync(new(profile, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))), () => new MemoryStream(bytes, false), ct, readingPassword?.CreateGrant());
                Assert.Equal(expected.Digest, observed.Manifest.Digest);
            }
        }
        finally { TestCleanup.DeleteTree(root); }
    }

    [Fact]
    public void Actual_native_forest_uses_source_ancestor_paths_for_archive_aliases()
    {
        _ = Probe(); string root = Directory.CreateTempSubdirectory("filecat-forest-alias-").FullName;
        try
        {
            ArchiveNativeJobCases.Fixture(root); var ct = TestContext.Current.CancellationToken;
            using var selection = ArchiveSelectionForest.Capture([new(Path.Combine(root, "a/tree"), "archive/alias", ArchiveSelectionKind.Directory)], WindowsArchiveSelectionTree.Open, ct);
            Assert.Contains(selection.Members, member => member.Name == "archive/alias/sub/file");
            var binding = Assert.Single(selection.Inputs.Bindings).Value;
            using var stream = binding.File.OpenReadView(binding.Reviewed, ct); using var bytes = new MemoryStream(); stream.CopyTo(bytes);
            Assert.Equal(File.ReadAllBytes(Path.Combine(root, "a/tree/sub/file")), bytes.ToArray());
        }
        finally { TestCleanup.DeleteTree(root); }
    }

    [Theory]
    [InlineData("identity")] [InlineData("origin")] [InlineData("missing")]
    public async Task Actual_native_jobs_refuse_stale_or_missing_native_baselines_before_parsing(string changed)
    {
        _ = Probe(); string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
        Assert.True(assets is not null && Path.IsPathFullyQualified(assets)); ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);
        string root = Directory.CreateTempSubdirectory("filecat-stale-native-job-").FullName; var ct = TestContext.Current.CancellationToken;
        try
        {
            string target = Path.Combine(root, "sentinel.rar"); byte[] sentinel = "not an archive: this must never reach a parser"u8.ToArray(); File.WriteAllBytes(target, sentinel);
            File.WriteAllText(target + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            var baseline = ArchiveBaseline.Of(target); ArchiveFileObservation original;
            using (var directory = WindowsArchiveDirectory.Open(root))
            using (var file = directory.OpenSource(Path.GetFileName(target))) original = ((IArchiveRetainedFile)file).Observe(ct);
            if (changed == "identity") { File.Move(target, target + ".old"); File.Copy(target + ".old", target); }
            if (changed == "origin") File.WriteAllText(target + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=4\r\n");
            File.SetLastWriteTimeUtc(target, new DateTime(baseline.LastWriteUtcTicks, DateTimeKind.Utc));
            var profile = ArchiveWriteProfile.ForRar5(encryption: new(ArchiveEncryptionMode.DataAndNames));
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(root, "journals"),
                new([WindowsArchiveJobBackend.Create(profile.Format, Path.Combine(assets!, "FileCat.exe"))]));
            using var password = new ArchiveJobPassword(ArchiveNativeJobCases.Password);
            var job = manager.SubmitArchive(new(target, baseline, [], Profile: profile) { NativeBaseline = changed == "missing" ? null : original }, password);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
            Assert.Equal(JobState.Failed, job.State); Assert.Equal(sentinel, File.ReadAllBytes(target));
            Assert.Contains(job.Issues, issue => issue.Message.Contains("native identity", StringComparison.Ordinal));
            Assert.Throws<ObjectDisposedException>(() => password.CreateGrant());
            Assert.DoesNotContain(Directory.GetFiles(root), path => Path.GetFileName(path).StartsWith(".filecat-", StringComparison.Ordinal));
        }
        finally { TestCleanup.DeleteTree(root); }
    }
}
