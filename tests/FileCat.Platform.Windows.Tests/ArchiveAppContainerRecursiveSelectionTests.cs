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
    [InlineData(ArchiveWriteFormat.Tar)]
    [InlineData(ArchiveWriteFormat.TarGzip)]
    [InlineData(ArchiveWriteFormat.TarBzip2)]
    [InlineData(ArchiveWriteFormat.TarZstd)]
    public async Task Recursive_selection_publishes_exact_native_tree_and_preserves_empty_folders(ArchiveWriteFormat format)
    {
        string executable = Probe(); var ct = TestContext.Current.CancellationToken; string root = Directory.CreateTempSubdirectory("filecat-recursive-lpac-").FullName;
        try
        {
            string tree = Directory.CreateDirectory(Path.Combine(root, "tree")).FullName;
            Directory.CreateDirectory(Path.Combine(tree, "nested", "empty")); Directory.CreateDirectory(Path.Combine(root, "outside"));
            File.WriteAllBytes(Path.Combine(tree, "nested", "界.bin"), [7, 0, 255, 9]); File.WriteAllBytes(Path.Combine(root, "loose"), [2, 3]);
            File.WriteAllText(Path.Combine(root, "outside", "secret"), "not selected");
            File.WriteAllText(tree + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            using var selection = WindowsArchiveSelectionTree.Capture(root, ["tree", "loose"], ct);
            Assert.Equal(3, selection.Inputs.Directories.Count); Assert.Equal(2, selection.Inputs.Bindings.Count); var profile = new ArchiveWriteProfile(format);
            var plan = selection.CreatePlan(profile, null, [], false, ct);
            Assert.Equal(new[] { "loose", "tree", "tree/nested", "tree/nested/empty", "tree/nested/界.bin" }, plan.Additions.Select(a => a.Name));
            using var destination = WindowsArchiveDirectory.Open(root);
            using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                Archive = new(Path.Combine(root, "result.tar"), null, [], Profile: profile) }, "owned recursive selection", "owned", [], []));
            var workers = new List<ArchiveAppContainer>();
            try
            {
                using var prepared = await ArchiveJournaledTarPreparation.PrepareAsync(journal, Path.Combine(root, "result.tar"), plan, null, selection.Inputs.Bindings,
                    (nonce, kind, maximum) => destination.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum), destination.CreateArtifact,
                    new((nonce, cpu) => { var worker = ArchiveAppContainer.Start(executable, "archive-transform " + nonce, cpu); workers.Add(worker); return worker; }),
                    new((nonce, cpu) => { var worker = ArchiveAppContainer.Start(executable, "archive-verify " + nonce, cpu); workers.Add(worker); return worker; }), ct);
                Assert.Equal(5, prepared.Facts.Sidecars.Single(s => s.Role == ArchiveSidecarRole.OriginEvidence).OriginSources);
                Assert.Equal(ArchiveWindowsOriginZone.ZoneOnly(3).Digest, prepared.Verified.Stage.Origin.Digest);
                Assert.All(workers, worker => { Assert.True(worker.HasExited); Assert.Equal(0u, worker.ExitCode); });
                await ArchiveJournaledTarAssertions.PublishNew(prepared, journal, selection.Inputs.Bindings, destination.CreateArtifact, ct);
                string? oracle = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_RECURSIVE_ORACLES");
                if (oracle is not null)
                {
                    File.Copy(Path.Combine(root, "result.tar"), Path.Combine(oracle, "windows-" + format + ".archive"));
                    File.WriteAllText(Path.Combine(oracle, "windows-" + format + ".json"), JsonSerializer.Serialize(new { format = format.ToString(), names = plan.Additions.Select(a => a.Name), manifest = plan.Expected.Digest }));
                }
            }
            finally { foreach (var worker in workers) await worker.DisposeAsync(); }
        }
        finally { Directory.Delete(root, true); }
    }
}
