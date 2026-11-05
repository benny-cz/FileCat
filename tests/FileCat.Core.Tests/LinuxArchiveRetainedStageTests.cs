using System.Formats.Tar;
using System.Buffers.Binary;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Linux;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Tests;

namespace FileCat.Core.Tests;

public sealed partial class LinuxArchiveProcessTests
{
    [Theory]
    [InlineData("existing-target")]
    [InlineData("changed-input")]
    [InlineData("metadata-change")]
    [InlineData("cancelled")]
    [InlineData("after-claim")]
    public async Task Confined_new_private_archive_publication_failures_retain_exact_evidence(string failure)
    {
        string probe = Probe(); using var temp = new TempDir(); string root = UnixFiles.RealPath(temp.Path)!; File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        File.WriteAllBytes(Path.Combine(root, "input"), [1]);
        using var directory = UnixArchiveDirectory.Open(root); using var input = directory.OpenSource("input"); var state = ((IArchiveRetainedFile)input).Observe(TestContext.Current.CancellationToken);
        await ArchiveCreationPublicationCases.Failure(failure, root, input, state,
            (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
            new((nonce, cpu) => LinuxArchiveProcess.Start(probe, nonce, cpu, ["archive-transform", nonce.ToString("D")])),
            new((nonce, cpu) => LinuxArchiveProcess.Start(probe, nonce, cpu, ["archive-verify", nonce.ToString("D")])), TestContext.Current.CancellationToken);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retained_ext4_inputs_produce_a_real_private_sealed_stage_before_fresh_verification(bool update)
    {
        string probe = Probe(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = UnixFiles.RealPath(temp.Path)!;
        if (!update) File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string inputPath = Path.Combine(root, "input"); File.WriteAllBytes(inputPath, "retained ext4 input"u8.ToArray());
        File.SetLastWriteTimeUtc(inputPath, DateTime.UnixEpoch.AddSeconds(1700000000)); File.SetUnixFileMode(inputPath, (UnixFileMode)0x1a4);
        if (!update) File.SetUnixFileMode(inputPath, (UnixFileMode)0xfed);
        ArchiveNativeTimestampFixture.SetModified(inputPath);
        using var directory = UnixArchiveDirectory.Open(root); using var input = directory.OpenSource("input"); var state = ((IArchiveRetainedFile)input).Observe(ct);
        UnixArchiveFile? original = null; var workers = new List<LinuxArchiveProcess>();
        try
        {
            ArchiveRetainedBinding? source = null; ArchiveAssessment? assessment = null;
            if (update)
            {
                using (var data = File.Create(Path.Combine(root, "original.tar")))
                using (var writer = new TarWriter(data, TarEntryFormat.Pax))
                { using var content = new MemoryStream("before"u8.ToArray()); writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "old") { DataStream = content }); }
                original = directory.OpenSource("original.tar"); var originalState = ((IArchiveRetainedFile)original).Observe(ct); source = new(original, originalState);
                assessment = await new ArchiveAssessmentClient((nonce, cpu) => Start(nonce, cpu, "archive-assess"))
                    .AssessAsync(new(new(ArchiveWriteFormat.Tar), originalState.Version.Length, originalState.Version.Sha256), () => ((IArchiveRetainedFile)original).OpenReadView(originalState, ct), ct);
            }
            var plan = ArchiveTarTransformPlan.Create(assessment, [state.Input(5)], source is null ? [] :
                [new(ArchiveChangeKind.Replace, new(assessment!.CatalogRevision, "old", ArchiveMemberType.File, 0), inputId: 5)], [new("added", ArchiveMemberType.File, 5)], ct);
            using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                Archive = new(Path.Combine(root, "result.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned journaled TAR preparation", "owned", [], []));
            using var prepared = await ArchiveJournaledTarPreparation.PrepareAsync(journal, Path.Combine(root, update ? "original.tar" : "result.tar"), plan, source,
                new Dictionary<int, ArchiveRetainedBinding> { [5] = new(input, state) },
                (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum),
                r => { ArchiveJournaledTarAssertions.BeforeCreate(journal, r); return directory.CreateArtifact(r); },
                new((nonce, cpu) => { ArchiveJournaledTarAssertions.Phase(journal, ArchiveTransactionPhase.Rebuilding); return Start(nonce, cpu, "archive-transform"); }),
                new((nonce, cpu) => { ArchiveJournaledTarAssertions.Phase(journal, ArchiveTransactionPhase.Verifying); return Start(nonce, cpu, "archive-verify"); }), ct);
            var stage = prepared.Stage; var result = prepared.Verified; ArchiveJournaledTarAssertions.Complete(prepared, journal, plan, ct);
            Assert.Equal(update ? 0x1a4 : 0x1ed, state.Input(5).Mode);
            Assert.Equal(new ArchiveTimestamp(1700000000, 123456789), state.Input(5).ModifiedTimestamp);
            var addedMetadata = result.Verification.ObservedManifest.Members.Single(m => m.Name == "added").Metadata.ToArray();
            Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(addedMetadata));
            Assert.Equal(1700000000, BinaryPrimitives.ReadInt64LittleEndian(addedMetadata.AsSpan(22)));
            Assert.Equal(123456789, BinaryPrimitives.ReadInt32LittleEndian(addedMetadata.AsSpan(30)));
            Assert.Equal(state.Input(5).Mode, BinaryPrimitives.ReadInt32LittleEndian(result.Verification.ObservedManifest.Members.Single(m => m.Name == "added").Metadata[2..]));
            Assert.Equal(result.Stage.Version, stage.CaptureVersion(ct)); Assert.Equal(result.Stage.Version, result.Verification.Stage);
            Assert.Equal(plan.Expected.Digest, result.Verification.ManifestDigest); Assert.False(stage.CanWrite); Assert.Throws<NotSupportedException>(() => stage.WriteByte(1));
            Assert.True(input.CanRead); Assert.Equal(state.Version, ((IArchiveRetainedFile)input).Observe(ct).Version);
            Assert.True(File.Exists(stage.DisplayPath)); Assert.False(File.Exists(Path.Combine(root, "result.tar")));
            Assert.Equal(update ? 3 : 2, workers.Select(w => w.ProcessId).Distinct().Count());
            foreach (var worker in workers) { Assert.True(worker.HasExited); Assert.Equal(0u, worker.ExitCode); Assert.False(Directory.Exists(worker.ControlPath)); }
            if (!update) await ArchiveJournaledTarAssertions.PublishNew(prepared, journal,
                new Dictionary<int, ArchiveRetainedBinding> { [5] = new(input, state) },
                r => { ArchiveJournaledTarAssertions.BeforeCreate(journal, r); return directory.CreateArtifact(r); }, ct);
        }
        finally { original?.Dispose(); }
        LinuxArchiveProcess Start(Guid nonce, TimeSpan cpu, string role)
        { var worker = LinuxArchiveProcess.Start(probe, nonce, cpu, [role, nonce.ToString("D")]); workers.Add(worker); return worker; }
    }
}
