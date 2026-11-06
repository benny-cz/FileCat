using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    [Theory]
    [InlineData(ArchiveWriteFormat.Tar, false)]
    [InlineData(ArchiveWriteFormat.TarGzip, false)]
    [InlineData(ArchiveWriteFormat.TarBzip2, false)]
    [InlineData(ArchiveWriteFormat.TarZstd, false)]
    [InlineData(ArchiveWriteFormat.Tar, true)]
    public async Task Directory_workflow_updates_bind_folder_provenance_through_the_native_commit(ArchiveWriteFormat format, bool changed)
    {
        string executable = Probe(); if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Explicit native transactional lane.");
        var ct = TestContext.Current.CancellationToken; string root = Directory.CreateTempSubdirectory("filecat-dir-update-").FullName;
        try
        {
            string path = Path.Combine(root, "original.tar");
            using (var data = File.Create(path))
            using (var wrapper = ContentWrapper(data, format, writing: true))
            using (var writer = new System.Formats.Tar.TarWriter(wrapper, System.Formats.Tar.TarEntryFormat.Pax, leaveOpen: true))
            using (var before = new MemoryStream("before"u8.ToArray()))
                writer.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "old") { DataStream = before, ModificationTime = DateTimeOffset.UnixEpoch });
            File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://original.invalid/\r\n");
            byte[] oldBytes = File.ReadAllBytes(path);
            string tree = Directory.CreateDirectory(Path.Combine(root, "source-tree")).FullName;
            File.WriteAllText(tree + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=4\r\n");
            var modified = DateTime.UnixEpoch.AddSeconds(1700000000).AddTicks(1234567); Directory.SetLastWriteTimeUtc(tree, modified);
            File.WriteAllBytes(Path.Combine(root, "payload"), [7, 0, 255, 9]);
            using var directory = WindowsArchiveDirectory.Open(root); using var original = directory.OpenSource("original.tar");
            using var captured = new ArchiveCapturedInputs(); directory.CaptureSourceDirectory(captured, 1, "source-tree", ct);
            directory.CaptureSource(captured, 2, "payload", ct); captured.Seal(ct);
            var baseline = ((IArchiveRetainedFile)original).Observe(ct); var workers = new List<ArchiveAppContainer>();
            ArchiveAppContainer Launch(Guid nonce, TimeSpan cpu, string role) { var worker = ArchiveAppContainer.Start(executable, role + " " + nonce, cpu); workers.Add(worker); return worker; }
            try
            {
                var profile = new ArchiveWriteProfile(format);
                var assessment = await new ArchiveAssessmentClient((nonce, cpu) => Launch(nonce, cpu, "archive-assess"))
                    .AssessAsync(new(profile, baseline.Version.Length, baseline.Version.Sha256), () => ((IArchiveRetainedFile)original).OpenReadView(baseline, ct), ct);
                var plan = captured.CreatePlan(profile, assessment, [], [captured.DirectoryAddition("tree/", 1), new("tree/file.bin", ArchiveMemberType.File, 2)], ct);
                using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                    Archive = new(path, null, [], Profile: profile) }, "owned directory content update", "owned", [], []));
                using var prepared = await ArchiveJournaledTarPreparation.PrepareAsync(journal, path, plan, new(original, baseline), captured.Bindings,
                    (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
                    new((nonce, cpu) => Launch(nonce, cpu, "archive-transform")), new((nonce, cpu) => Launch(nonce, cpu, "archive-verify")), ct);
                Assert.Single(prepared.Verified.Directories); Assert.Equal(3, prepared.Facts.Sidecars.Single(s => s.Role == ArchiveSidecarRole.OriginEvidence).OriginSources);
                if (changed)
                {
                    Directory.SetLastWriteTimeUtc(tree, modified.AddTicks(1));
                    Assert.ThrowsAny<IOException>(() => WindowsArchiveContentUpdate.Publish(journal, prepared, original, captured.Bindings, TimeSpan.FromSeconds(30), ct));
                    Assert.Equal(oldBytes, File.ReadAllBytes(path)); Assert.True(journal.NeedsRecovery); return;
                }
                using var committed = WindowsArchiveContentUpdate.Publish(journal, prepared, original, captured.Bindings, TimeSpan.FromSeconds(30), ct);
                Assert.Equal(baseline.Version.Identity, committed.Published.Version.Identity);
                Assert.Equal(4, ArchiveWindowsOriginZone.Parse(committed.Published.Origin).DeclaredZone);
                Assert.Contains("https://original.invalid/", ArchiveWritePolicy.Utf8.GetString(committed.Published.Origin.Bytes));
                Assert.Equal(ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts, committed.Facts.Phase);
                string? oracle = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_DIRECTORY_ORACLES");
                if (oracle is not null)
                {
                    File.Copy(path, Path.Combine(oracle, "windows-update-" + format + ".archive"));
                    File.WriteAllText(Path.Combine(oracle, "windows-update-" + format + ".json"), JsonSerializer.Serialize(new { format = format.ToString(),
                        directories = new[] { new { name = "tree/", mode = 0x1ed, seconds = 1700000000L, nanos = 123456700 } },
                        file = "tree/file.bin", payload = new byte[] { 7, 0, 255, 9 }, old = true, manifest = plan.Expected.Digest }));
                }
                Assert.Equal(3, workers.Count); Assert.All(workers, worker => { Assert.True(worker.HasExited); Assert.Equal(0u, worker.ExitCode); });
            }
            finally { foreach (var worker in workers) await worker.DisposeAsync(); }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(ArchiveWriteFormat.Tar, false)]
    [InlineData(ArchiveWriteFormat.TarGzip, false)]
    [InlineData(ArchiveWriteFormat.TarBzip2, false)]
    [InlineData(ArchiveWriteFormat.TarZstd, false)]
    [InlineData(ArchiveWriteFormat.Tar, true)]
    public async Task Directory_workflow_publishes_exact_verified_folders_and_unions_their_origin(ArchiveWriteFormat format, bool changed)
    {
        string executable = Probe(); var ct = TestContext.Current.CancellationToken; string root = Directory.CreateTempSubdirectory("filecat-dir-lpac-").FullName;
        try
        {
            string tree = Directory.CreateDirectory(Path.Combine(root, "source-tree")).FullName;
            string empty = Directory.CreateDirectory(Path.Combine(root, "source-empty")).FullName;
            File.WriteAllBytes(Path.Combine(tree, "payload"), [7, 0, 255, 9]);
            File.WriteAllText(tree + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            var modified = DateTime.UnixEpoch.AddSeconds(1700000000).AddTicks(1234567);
            Directory.SetLastWriteTimeUtc(tree, modified); Directory.SetLastWriteTimeUtc(empty, modified);
            using var directory = WindowsArchiveDirectory.Open(root); using var source = WindowsArchiveDirectory.Open(tree);
            using var captured = new ArchiveCapturedInputs(); directory.CaptureSourceDirectory(captured, 1, "source-tree", ct);
            directory.CaptureSourceDirectory(captured, 2, "source-empty", ct); source.CaptureSource(captured, 3, "payload", ct); captured.Seal(ct);
            var profile = new ArchiveWriteProfile(format); var plan = captured.CreatePlan(profile, null, [],
                [captured.DirectoryAddition("tree/", 1), captured.DirectoryAddition("empty/", 2), new("tree/file.bin", ArchiveMemberType.File, 3)], ct);
            using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                Archive = new(Path.Combine(root, "result.tar"), null, [], Profile: profile) }, "owned directory workflow", "owned", [], []));
            var workers = new List<ArchiveAppContainer>();
            try
            {
                using var prepared = await ArchiveJournaledTarPreparation.PrepareAsync(journal, Path.Combine(root, "result.tar"), plan, null, captured.Bindings,
                    (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
                    new((nonce, cpu) => { var worker = ArchiveAppContainer.Start(executable, "archive-transform " + nonce, cpu); workers.Add(worker); return worker; }),
                    new((nonce, cpu) => { var worker = ArchiveAppContainer.Start(executable, "archive-verify " + nonce, cpu); workers.Add(worker); return worker; }), ct);
                Assert.Equal(2, prepared.Verified.Directories.Count); Assert.Equal(3, prepared.Facts.Sidecars.Single(s => s.Role == ArchiveSidecarRole.OriginEvidence).OriginSources);
                Assert.Equal(ArchiveWindowsOriginZone.ZoneOnly(3).Digest, prepared.Verified.Stage.Origin.Digest);
                Assert.Equal(2, workers.Count); Assert.All(workers, worker => { Assert.True(worker.HasExited); Assert.Equal(0u, worker.ExitCode); });
                if (changed)
                {
                    Directory.SetLastWriteTimeUtc(tree, modified.AddTicks(1));
                    await Assert.ThrowsAnyAsync<IOException>(() => ArchiveNewFilePublication.PublishAsync(journal, prepared, captured.Bindings, directory.CreateArtifact, ct));
                    Assert.False(File.Exists(Path.Combine(root, "result.tar"))); Assert.True(journal.NeedsRecovery); return;
                }
                await ArchiveJournaledTarAssertions.PublishNew(prepared, journal, captured.Bindings, directory.CreateArtifact, ct);
                string? oracle = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_DIRECTORY_ORACLES");
                if (oracle is not null)
                {
                    Assert.True(Path.IsPathFullyQualified(oracle) && Directory.Exists(oracle));
                    File.Copy(Path.Combine(root, "result.tar"), Path.Combine(oracle, "windows-" + format + ".archive"));
                    File.WriteAllText(Path.Combine(oracle, "windows-" + format + ".json"), JsonSerializer.Serialize(new { format = format.ToString(),
                        directories = new[] { new { name = "tree/", mode = 0x1ed, seconds = 1700000000L, nanos = 123456700 }, new { name = "empty/", mode = 0x1ed, seconds = 1700000000L, nanos = 123456700 } },
                        file = "tree/file.bin", payload = new byte[] { 7, 0, 255, 9 }, manifest = plan.Expected.Digest }));
                }
            }
            finally { foreach (var worker in workers) await worker.DisposeAsync(); }
        }
        finally { Directory.Delete(root, true); }
    }
}
