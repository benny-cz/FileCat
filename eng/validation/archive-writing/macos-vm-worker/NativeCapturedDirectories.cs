using System.Runtime.InteropServices;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;

internal static class NativeCapturedDirectories
{
    [StructLayout(LayoutKind.Sequential)]
    private struct TimeSpec { internal long Seconds, Nanos; }
    [DllImport("libc", EntryPoint = "utimensat", SetLastError = true)]
    private static extern int SetTimes(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [In] TimeSpec[] times, int flags);

    internal static async Task<int> Run(string native, string kernel, string initrd, string output)
    {
        if (!OperatingSystem.IsMacOS() || !Path.IsPathFullyQualified(output) || Directory.Exists(output)) return 65;
        Directory.CreateDirectory(output); string root = Path.Combine(output, "sources"); Directory.CreateDirectory(root); File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string tree = Directory.CreateDirectory(Path.Combine(root, "source-tree")).FullName, empty = Directory.CreateDirectory(Path.Combine(root, "source-empty")).FullName;
        File.WriteAllBytes(Path.Combine(tree, "payload"), [7, 0, 255, 9]); File.SetUnixFileMode(tree, (UnixFileMode)0x1d9); File.SetUnixFileMode(empty, (UnixFileMode)0x1c0);
        var precise = new[] { new TimeSpec { Seconds = -1, Nanos = 999999999 }, new TimeSpec { Seconds = -1, Nanos = 999999999 } };
        if (SetTimes(-2, tree, precise, 0) != 0 || SetTimes(-2, empty, precise, 0) != 0) return 66;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10)); var ct = deadline.Token;
        var workers = new List<VmProcess>(); var outcomes = new List<object>();
        try
        {
            using var directory = UnixArchiveDirectory.Open(root); using var source = UnixArchiveDirectory.Open(tree);
            foreach (var format in new[] { ArchiveWriteFormat.Tar, ArchiveWriteFormat.TarGzip, ArchiveWriteFormat.TarBzip2, ArchiveWriteFormat.TarZstd })
            {
                using var captured = new ArchiveCapturedInputs(); directory.CaptureSourceDirectory(captured, 1, "source-tree", ct);
                directory.CaptureSourceDirectory(captured, 2, "source-empty", ct); source.CaptureSource(captured, 3, "payload", ct); captured.Seal(ct);
                if (captured.Directories.Values.Any(d => d.Reviewed.Modified != new ArchiveTimestamp(-1, 999999999))) throw new InvalidDataException("Native directory precision differs.");
                var profile = new ArchiveWriteProfile(format); var plan = captured.CreatePlan(profile, null, [],
                    [captured.DirectoryAddition("tree/", 1), captured.DirectoryAddition("empty/", 2), new("tree/file.bin", ArchiveMemberType.File, 3)], ct);
                string target = Path.Combine(root, "result-" + format);
                using var journal = JobJournal.Create(Path.Combine(output, "journals-" + format), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                    Archive = new(target, null, [], Profile: profile) }, "owned VM directory workflow", "owned", [], []));
                VmProcess Launch(byte role, Guid nonce)
                { var worker = new VmProcess(native, kernel, initrd, nonce, role, Path.Combine(output, format + "-" + role + ".log")); workers.Add(worker); return worker; }
                using var prepared = await ArchiveJournaledTarPreparation.PrepareAsync(journal, target, plan, null, captured.Bindings,
                    (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
                    new((nonce, _) => Launch(2, nonce)), new((nonce, _) => Launch(3, nonce)), ct);
                if (prepared.Verified.Verification.ManifestDigest != plan.Expected.Digest || prepared.Verified.Directories.Count != 2) throw new InvalidDataException("Directory verification differs.");
                var published = await ArchiveNewFilePublication.PublishAsync(journal, prepared, captured.Bindings, directory.CreateArtifact, ct);
                if (published.Facts.Phase != ArchiveTransactionPhase.CommittedWithRetainedArtifacts || published.Published.Version != prepared.Verified.Stage.Version) throw new InvalidDataException("Native publication differs.");
                File.Copy(target, Path.Combine(output, "native-vm-" + format + ".archive"));
                File.WriteAllText(Path.Combine(output, "native-vm-" + format + ".json"), JsonSerializer.Serialize(new { format = format.ToString(),
                    directories = new[] { new { name = "tree/", mode = 0x1d9, seconds = -1L, nanos = 999999999 }, new { name = "empty/", mode = 0x1c0, seconds = -1L, nanos = 999999999 } },
                    file = "tree/file.bin", payload = new byte[] { 7, 0, 255, 9 }, manifest = plan.Expected.Digest }));
                outcomes.Add(new { format = format.ToString(), directories = captured.Directories.Count, files = captured.Inputs.Count, originSources = 3, manifest = plan.Expected.Digest });
            }
            if (workers.Count != 8 || workers.Select(w => w.Id).Distinct().Count() != 8 || workers.Any(w => !w.Reaped || w.ExitCode != 0 || w.ErrorLimitExceeded)) throw new InvalidDataException("Eight fresh reaped VM workers are required.");
            File.WriteAllText(Path.Combine(output, "facts.json"), JsonSerializer.Serialize(new { componentChecksPassed = true, productionBackendQualified = false,
                outcomes, workers = workers.Select(w => new { pid = w.Id, reaped = w.Reaped, exit = w.ExitCode }) }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); return 1; }
        finally { foreach (var worker in workers) await worker.DisposeAsync(); }
    }
}
