using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;

/// <summary>Owned development workflow: native Mac sources/stages, actual separate Linux VM transform/verifier roles.</summary>
internal static class NativeCapturedInputs
{
    internal static async Task<int> Run(string native, string kernel, string initrd, string output)
    {
        if (!OperatingSystem.IsMacOS() || !Path.IsPathFullyQualified(output) || Directory.Exists(output)) return 65;
        Directory.CreateDirectory(output); string sources = Path.Combine(output, "sources"); Directory.CreateDirectory(sources);
        File.SetUnixFileMode(sources, (UnixFileMode)0x1c0);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10)); var ct = deadline.Token;
        var workers = new List<VmProcess>(); var outcomes = new List<object>(); const int count = 128;
        try
        {
            using var directory = UnixArchiveDirectory.Open(sources);
            for (int id = 1; id <= count; id++)
            {
                string path = Path.Combine(sources, "source-" + id);
                File.WriteAllBytes(path, [(byte)id, 0, 255, (byte)(id >> 8)]);
                File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddSeconds(1700000000)); File.SetUnixFileMode(path, (UnixFileMode)0x1a4);
            }
            foreach (var format in new[] { ArchiveWriteFormat.Tar, ArchiveWriteFormat.TarGzip, ArchiveWriteFormat.TarBzip2, ArchiveWriteFormat.TarZstd })
            {
                using var captured = new ArchiveCapturedInputs();
                for (int id = 1; id <= count; id++) directory.CaptureSource(captured, id, "source-" + id, ct);
                captured.Seal(ct); var profile = new ArchiveWriteProfile(format);
                var plan = captured.CreatePlan(profile, null, [], Enumerable.Range(1, count).Reverse()
                    .Select(id => new ArchiveTransformAddition("界-" + id + ".bin", ArchiveMemberType.File, id)).ToArray(), ct);
                string target = Path.Combine(sources, "result-" + format);
                using var journal = JobJournal.Create(Path.Combine(output, "journals-" + format), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                    Archive = new(target, null, [], Profile: profile) }, "owned native capture", "owned", [], []));
                VmProcess Launch(byte role, Guid nonce, TimeSpan cpu)
                {
                    var worker = new VmProcess(native, kernel, initrd, nonce, role, Path.Combine(output, format + "-" + role + ".log"));
                    workers.Add(worker); return worker;
                }
                using var prepared = await ArchiveJournaledTarPreparation.PrepareAsync(journal, target, plan, null, captured.Bindings,
                    (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
                    new((nonce, cpu) => Launch(2, nonce, cpu)), new((nonce, cpu) => Launch(3, nonce, cpu)), ct);
                if (prepared.Verified.Verification.ManifestDigest != plan.Expected.Digest || prepared.Stage.CanWrite || File.Exists(target) ||
                    prepared.Verified.Verification.ObservedManifest.Members.Count != count) throw new InvalidOperationException("Native capture verification differs.");
                foreach (var binding in captured.Bindings.Values) binding.File.Review(binding.Reviewed, ct);
                File.Copy(prepared.Stage.DisplayPath, Path.Combine(output, "native-vm-" + format + ".archive"));
                File.WriteAllText(Path.Combine(output, "native-vm-" + format + ".json"), JsonSerializer.Serialize(new
                { format = format.ToString(), count, manifest = plan.Expected.Digest, prefix = "界-", suffix = ".bin", reversed = true }));
                outcomes.Add(new { format = format.ToString(), captured = count, cost = captured.Cost, manifest = plan.Expected.Digest });
            }
            if (workers.Count != 8 || workers.Select(worker => worker.Id).Distinct().Count() != 8 || workers.Any(worker => !worker.Reaped || worker.ExitCode != 0 || worker.ErrorLimitExceeded))
                throw new InvalidOperationException("Each transform and fresh verification needs its own successful reaped VM.");
            File.WriteAllText(Path.Combine(output, "facts.json"), JsonSerializer.Serialize(new { componentChecksPassed = true, productionBackendQualified = false,
                outcomes, workers = workers.Select(worker => new { pid = worker.Id, reaped = worker.Reaped, exit = worker.ExitCode }) }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); return 1; }
        finally { foreach (var worker in workers) await worker.DisposeAsync(); }
    }
}
