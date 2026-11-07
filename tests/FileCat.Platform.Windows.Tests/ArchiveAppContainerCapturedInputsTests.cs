using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    [Theory]
    [InlineData(ArchiveWriteFormat.Tar, 256)]
    [InlineData(ArchiveWriteFormat.TarGzip, 64)]
    [InlineData(ArchiveWriteFormat.TarBzip2, 64)]
    [InlineData(ArchiveWriteFormat.TarZstd, 64)]
    public async Task Captured_native_sources_beyond_sixteen_handles_complete_fresh_LPAC_verification(ArchiveWriteFormat format, int count)
    {
        string executable = Probe(); var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-captured-lpac-").FullName;
        try
        {
            using var directory = WindowsArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
            for (int id = 1; id <= count; id++)
            {
                string leaf = "source-" + id; File.WriteAllBytes(Path.Combine(root, leaf), [(byte)id, 0, 255, (byte)(id >> 8)]);
                directory.CaptureSource(captured, id, leaf, ct);
            }
            captured.Seal(ct); var profile = new ArchiveWriteProfile(format);
            var plan = captured.CreatePlan(profile, null, [], Enumerable.Range(1, count).Reverse()
                .Select(id => new ArchiveTransformAddition("界-" + id + ".bin", ArchiveMemberType.File, id)).ToArray(), ct);
            using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                Archive = new(Path.Combine(root, "result.tar"), null, [], Profile: profile) }, "captured LPAC inputs", "owned", [], []));
            var workers = new List<ArchiveAppContainer>();
            try
            {
                using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, Path.Combine(root, "result.tar"), plan, null, captured.Bindings,
                    (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
                    new((nonce, cpu) => { var worker = ArchiveAppContainer.Start(executable, "archive-transform " + nonce, cpu); workers.Add(worker); return worker; }),
                    new((nonce, cpu) => { var worker = ArchiveAppContainer.Start(executable, "archive-verify " + nonce, cpu); workers.Add(worker); return worker; }), ct);
                ArchiveJournaledTarAssertions.Complete(prepared, journal, plan, ct);
                Assert.Equal(count, prepared.Verified.Verification.ObservedManifest.Members.Count);
                Assert.Equal(2, workers.Count); Assert.All(workers, worker => { Assert.True(worker.HasExited); Assert.Equal(0u, worker.ExitCode); });
                string? oracle = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_CAPTURE_ORACLES");
                if (oracle is not null)
                {
                    Assert.True(Path.IsPathFullyQualified(oracle) && Directory.Exists(oracle));
                    File.Copy(prepared.Stage.DisplayPath, Path.Combine(oracle, "windows-" + format + ".archive"));
                    File.WriteAllText(Path.Combine(oracle, "windows-" + format + ".json"), System.Text.Json.JsonSerializer.Serialize(new
                    { format = format.ToString(), count, manifest = plan.Expected.Digest, prefix = "界-", suffix = ".bin", reversed = true }));
                }
            }
            finally { foreach (var worker in workers) await worker.DisposeAsync(); }
        }
        finally { Directory.Delete(root, true); }
    }
}
