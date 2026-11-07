using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Actual_application_7z_updates_and_fresh_snapshot_restoration_preserve_identity_bytes_and_origin(int mode)
    {
        _ = Probe();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Run explicit NTFS restoration qualification.");
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
        Assert.True(assets is not null && Path.IsPathFullyQualified(assets)); ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);
        string executable = Path.Combine(assets!, "FileCat.exe"), root = Directory.CreateTempSubdirectory("filecat-sevenzip-update-").FullName;
        var ct = TestContext.Current.CancellationToken; var workers = new List<ArchiveAppContainer>(); var profile = ArchiveSevenZipNativeCases.Profile(2, mode);
        using var password = mode == 0 ? null : new ArchiveJobPassword(ArchiveSevenZipNativeCases.Password);
        try
        {
            string path = Path.Combine(root, "archive.7z"), journalPath;
            byte[] payload = "retained original"u8.ToArray();
            var originalPlan = ArchiveSevenZipTransformPlan.Create(profile, null,
                [new(1, payload.Length, Convert.ToHexString(SHA256.HashData(payload)), new ArchiveTimestamp(1700000000, 0))], [], [new("old", ArchiveMemberType.File, 1)], ct);
            using var initial = new MemoryStream();
            await new ArchiveTransformClient((nonce, cpu) => Start(ArchiveWorkerRole.Transform, nonce, cpu), protocolVersion: 2).RebuildAsync(
                ArchiveTransformWire.Prepare(originalPlan, ct), initial, () => throw new InvalidOperationException(), _ => new MemoryStream(payload, false), ct, password?.CreateGrant());
            byte[] originalBytes = initial.ToArray(); File.WriteAllBytes(path, originalBytes);
            File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://original.invalid/\r\n");
            string inputPath = Path.Combine(root, "input"), folder = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
            File.WriteAllBytes(inputPath, "current replacement"u8.ToArray());
            File.WriteAllText(inputPath + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=4\r\n");
            File.SetLastWriteTimeUtc(inputPath, DateTime.UnixEpoch.AddSeconds(1700000001));
            Directory.SetLastWriteTimeUtc(folder, DateTime.UnixEpoch.AddSeconds(1700000002));
            ArchiveFileObservation published; byte[] updatedBytes; ArchiveWriteManifest updatedManifest;
            using (var directory = WindowsArchiveDirectory.Open(root))
            using (var target = directory.OpenSource("archive.7z"))
            using (var captured = new ArchiveCapturedInputs())
            {
                var before = ((IArchiveRetainedFile)target).Observe(ct);
                directory.CaptureSource(captured, 1, "input", ct); directory.CaptureSourceDirectory(captured, 2, "folder", ct); captured.Seal(ct);
                var assessed = await Assessor().AssessAsync(new(profile, before.Version.Length, before.Version.Sha256),
                    () => ((IArchiveRetainedFile)target).OpenReadView(before, ct), ct, password?.CreateGrant());
                Assert.Equal(originalPlan.Expected.Digest, assessed.Manifest.Digest);
                var plan = captured.CreatePlan(profile, assessed,
                    [new(ArchiveChangeKind.Replace, new(assessed.CatalogRevision, "old", ArchiveMemberType.File, 0), inputId: 1)],
                    [captured.DirectoryAddition("tree/", 2), new("tree/new", ArchiveMemberType.File, 1)], ct);
                updatedManifest = plan.Expected;
                using var journal = NewJournal("7z native content update"); journalPath = journal.Path;
                using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, path, plan, new(target, before), captured.Bindings,
                    (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
                    new((nonce, cpu) => Start(ArchiveWorkerRole.Transform, nonce, cpu), protocolVersion: 2), Verifier(), ct, password: password);
                using var committed = WindowsArchiveContentUpdate.Publish(journal, prepared, target, captured.Bindings, TimeSpan.FromMinutes(2), ct);
                published = committed.Published; Assert.Equal(before.Version.Identity, published.Version.Identity);
                Assert.Equal(prepared.Verified.Stage.Version.Sha256, published.Version.Sha256);
                Assert.Equal(4, ArchiveWindowsOriginZone.Parse(published.Origin).DeclaredZone);
                updatedBytes = ReadContentShared(path); Assert.Equal(originalBytes, ReadContentShared(committed.Facts.SnapshotPath));
            }
            var content = Assert.Single(JournalRecovery.ReadArchiveTransactions(journalPath).ContentUpdates);
            using (var lease = WindowsArchiveRestorationLease.Open(journalPath, Hash(journalPath), content.Nonce, ct))
            using (var fresh = NewJournal("7z fresh encrypted restoration"))
            using (var restored = await ArchiveContentRestoration.RestoreAsync(lease, fresh, Assessor(), Verifier(), TimeSpan.FromMinutes(2), ct, password))
            {
                Assert.Equal(originalBytes, ReadContentShared(path)); Assert.Equal(updatedBytes, ReadContentShared(restored.Update.Facts.SnapshotPath));
                Assert.Equal(published.Version.Identity, restored.Update.Published.Version.Identity);
                Assert.Equal(published.Origin.Digest, restored.Update.Published.Origin.Digest);
                Assert.Equal(ArchiveContentRestorationPhase.Committed, restored.Facts.Phase);
                Assert.True(restored.Update.Cleanup(fresh, ct).IsResolved); fresh.Finish(JobState.Completed, "fresh 7z restore and cleanup completed");
            }
            using (var cleanup = WindowsArchiveCleanupRecovery.Open(journalPath, Hash(journalPath), content.Nonce, ct))
            {
                Assert.Equal(ArchiveContentRecoveryTarget.RestoredIntact, cleanup.ResumeCleanup(ct).TargetState);
                cleanup.Finish("7z source snapshot explicitly cleaned after restoration");
            }
            Assert.False(JournalRecovery.ReadArchiveTransactions(journalPath).RequiresReview);
            Assert.DoesNotContain(ArchiveSevenZipNativeCases.Password, File.ReadAllText(journalPath));
            Assert.Equal(6, workers.Count); Assert.All(workers, worker => { Assert.True(worker.HasExited); Assert.Equal(0u, worker.ExitCode); });
            if (Environment.GetEnvironmentVariable("FILECAT_7Z_NATIVE_EXPORT_ROOT") is { } export)
            {
                string output = Path.Combine(export, "windows-update-mode-" + mode);
                ArchiveSevenZipNativeCases.Export(output, "source", originalBytes, originalPlan.Expected);
                ArchiveSevenZipNativeCases.Export(output, "updated", updatedBytes, updatedManifest);
                ArchiveSevenZipNativeCases.Export(output, "restored", File.ReadAllBytes(path), originalPlan.Expected);
            }
            JobJournal NewJournal(string title) => JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest
            { Kind = JobKind.ArchiveUpdate, Archive = new(path, null, [], Profile: profile) }, title, "owned", [], []));
        }
        finally { Directory.Delete(root, true); }
        ArchiveAssessmentClient Assessor() => new((nonce, cpu) => Start(ArchiveWorkerRole.Assess, nonce, cpu), protocolVersion: 2);
        ArchiveVerificationClient Verifier() => new((nonce, cpu) => Start(ArchiveWorkerRole.Verify, nonce, cpu), protocolVersion: 2);
        ArchiveAppContainer Start(ArchiveWorkerRole role, Guid nonce, TimeSpan cpu)
        {
            var worker = role switch
            {
                ArchiveWorkerRole.Assess => ArchiveAppContainer.StartAssessment(executable, nonce, cpu, 2),
                ArchiveWorkerRole.Transform => ArchiveAppContainer.StartTransform(executable, nonce, cpu, 2),
                _ => ArchiveAppContainer.StartVerification(executable, nonce, cpu, 2)
            };
            workers.Add(worker); return worker;
        }
        static string Hash(string path) => Convert.ToHexString(SHA256.HashData(ReadContentShared(path)));
    }
}
