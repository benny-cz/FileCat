using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;

internal static class NativeRecursiveSelection
{
    internal static async Task<int> Run(string native, string kernel, string initrd, string output)
    {
        if (!OperatingSystem.IsMacOS() || !Path.IsPathFullyQualified(output) || Directory.Exists(output)) return 65;
        Directory.CreateDirectory(output); string root = Directory.CreateDirectory(Path.Combine(output, "sources")).FullName;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string tree = Directory.CreateDirectory(Path.Combine(root, "tree")).FullName;
        Directory.CreateDirectory(Path.Combine(tree, "nested", "empty"));
        File.WriteAllBytes(Path.Combine(tree, "nested", "界.bin"), [7, 0, 255, 9]); File.WriteAllBytes(Path.Combine(root, "loose"), [2, 3]);
        File.CreateSymbolicLink(Path.Combine(tree, "link"), Path.Combine(root, "loose"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10)); var ct = deadline.Token;
        var workers = new List<VmProcess>(); var outcomes = new List<object>();
        try
        {
            using var destination = UnixArchiveDirectory.Open(root);
            foreach (var format in new[] { ArchiveWriteFormat.Tar, ArchiveWriteFormat.TarGzip, ArchiveWriteFormat.TarBzip2, ArchiveWriteFormat.TarZstd })
            {
                using var selection = UnixArchiveSelectionTree.Capture(root, ["tree", "loose"], ct);
                if (selection.ExcludedCount != 1 || selection.Inputs.Directories.Count != 3 || selection.Inputs.Bindings.Count != 2) throw new InvalidDataException("Recursive source scope differs.");
                var profile = new ArchiveWriteProfile(format); var plan = selection.CreatePlan(profile, null, [], true, ct);
                string target = Path.Combine(root, "result-" + format);
                using var journal = JobJournal.Create(Path.Combine(output, "journals-" + format), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                    Archive = new(target, null, [], Profile: profile) }, "owned VM recursive selection", "owned", [], []));
                VmProcess Launch(byte role, Guid nonce)
                { var worker = new VmProcess(native, kernel, initrd, nonce, role, Path.Combine(output, format + "-" + role + ".log")); workers.Add(worker); return worker; }
                using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, target, plan, null, selection.Inputs.Bindings,
                    (nonce, kind, maximum) => destination.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), destination.CreateArtifact,
                    new((nonce, _) => Launch(2, nonce)), new((nonce, _) => Launch(3, nonce)), ct);
                if (prepared.Verified.Verification.ManifestDigest != plan.Expected.Digest || prepared.Verified.Directories.Count != 3) throw new InvalidDataException("Recursive verification differs.");
                var published = await ArchiveNewFilePublication.PublishAsync(journal, prepared, selection.Inputs.Bindings, destination.CreateArtifact, ct);
                if (published.Facts.Phase != ArchiveTransactionPhase.CommittedWithRetainedArtifacts || published.Published.Version != prepared.Verified.Stage.Version) throw new InvalidDataException("Native publication differs.");
                File.Copy(target, Path.Combine(output, "native-vm-" + format + ".archive"));
                File.WriteAllText(Path.Combine(output, "native-vm-" + format + ".json"), JsonSerializer.Serialize(new { format = format.ToString(), names = plan.Additions.Select(a => a.Name), manifest = plan.Expected.Digest }));
                outcomes.Add(new { format = format.ToString(), directories = selection.Inputs.Directories.Count, files = selection.Inputs.Inputs.Count,
                    exclusions = selection.ExcludedCount, manifest = plan.Expected.Digest, phase = published.Facts.Phase.ToString() });
            }
            if (workers.Count != 8 || workers.Select(w => w.Id).Distinct().Count() != 8 || workers.Any(w => !w.Reaped || w.ExitCode != 0 || w.ErrorLimitExceeded)) throw new InvalidDataException("Eight fresh successful reaped helpers are required.");
            File.WriteAllText(Path.Combine(output, "facts.json"), JsonSerializer.Serialize(new { componentChecksPassed = true, productionBackendQualified = false,
                outcomes, workers = workers.Select(w => new { pid = w.Id, reaped = w.Reaped, exit = w.ExitCode }) }, new JsonSerializerOptions { WriteIndented = true })); return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); return 1; }
        finally { foreach (var worker in workers) await worker.DisposeAsync(); }
    }
}
