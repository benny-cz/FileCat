using System.Formats.Tar;
using System.Buffers.Binary;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using FileCat.Core.Jobs;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    [Theory]
    [InlineData(false, ArchiveWriteFormat.Tar)] [InlineData(true, ArchiveWriteFormat.Tar)]
    [InlineData(false, ArchiveWriteFormat.TarGzip)] [InlineData(true, ArchiveWriteFormat.TarGzip)]
    [InlineData(false, ArchiveWriteFormat.TarBzip2)] [InlineData(true, ArchiveWriteFormat.TarBzip2)]
    [InlineData(false, ArchiveWriteFormat.TarZstd)] [InlineData(true, ArchiveWriteFormat.TarZstd)]
    public async Task Marked_NTFS_creation_applies_all_input_provenance_before_fresh_confined_verification_and_publication(bool application, ArchiveWriteFormat format)
    {
        string executable = Probe(); var ct = TestContext.Current.CancellationToken;
        if (application)
        {
            string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
            Assert.True(assets is not null && Path.IsPathFullyQualified(assets)); ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);
            executable = Path.Combine(assets!, "FileCat.exe");
        }
        string root = Directory.CreateTempSubdirectory("filecat-marked-creation-").FullName;
        var leases = new List<WindowsArchiveFile>();
        try
        {
            using var directory = WindowsArchiveDirectory.Open(root);
            var captured = new Dictionary<int, ArchiveRetainedBinding>();
            foreach (int id in new[] { 3, 1, 4, 2 })
            {
                string leaf = "input" + id, path = Path.Combine(root, leaf); File.WriteAllBytes(path, [(byte)id]);
                if (id != 4) File.WriteAllText(path + ":Zone.Identifier", $"[ZoneTransfer]\r\nZoneId={(id == 1 ? 4 : id == 2 ? 3 : 0)}\r\nHostUrl=https://example.test/source{id}\r\n");
                var input = directory.OpenSource(leaf); leases.Add(input); captured.Add(id, new(input, ((IArchiveRetainedFile)input).Observe(ct)));
            }
            var profile = new ArchiveWriteProfile(format);
            var plan = ArchiveTarTransformPlan.Create(profile, null, captured.Select(p => p.Value.Reviewed.Input(p.Key)).ToArray(), [],
                captured.Keys.Select(id => new ArchiveTransformAddition("file" + id, ArchiveMemberType.File, id)).ToArray(), ct);
            string target = Path.Combine(root, "result.tar");
            using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                Archive = new(target, null, [], Profile: profile) }, "marked creation", "owned", [], []));
            using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, target, plan, null, captured,
                (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
                new((nonce, cpu) => application ? ArchiveAppContainer.StartTransform(executable, nonce, cpu) : ArchiveAppContainer.Start(executable, "archive-transform " + nonce, cpu)),
                new((nonce, cpu) =>
                {
                    ArchiveJournaledTarAssertions.Phase(journal, ArchiveTransactionPhase.Verifying);
                    var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
                    Assert.Equal(ArchiveWindowsOriginZone.ZoneOnly(4).Bytes.ToArray(), ReadShared(facts.StagePath + ":Zone.Identifier"));
                    return application ? ArchiveAppContainer.StartVerification(executable, nonce, cpu) : ArchiveAppContainer.Start(executable, "archive-verify " + nonce, cpu);
                }), ct);
            ArchiveJournaledTarAssertions.Complete(prepared, journal, plan, ct);
            Assert.Equal(ArchiveWindowsOriginZone.ZoneOnly(4).Digest, prepared.Verified.Stage.Origin.Digest);
            await ArchiveJournaledTarAssertions.PublishNew(prepared, journal, captured, directory.CreateArtifact, ct);
            Assert.Equal(ArchiveWindowsOriginZone.ZoneOnly(4).Bytes.ToArray(), ReadShared(target + ":Zone.Identifier"));
            var metadata = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions).Sidecars.Single(s => s.Role == ArchiveSidecarRole.CreationMetadata);
            Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(ReadShared(metadata.Path).AsSpan(8, 2)));
            foreach (var binding in captured.Values) binding.File.Review(binding.Reviewed, ct);
        }
        finally { foreach (var input in leases) input.Dispose(); Directory.Delete(root, true); }

        static byte[] ReadShared(string path)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Assert.InRange(file.Length, 0, ArchiveCreationMetadata.MaximumBytes);
            byte[] bytes = new byte[(int)file.Length]; file.ReadExactly(bytes); Assert.Equal(-1, file.ReadByte()); return bytes;
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_huge_Zstandard_window_refuses_before_decoder_allocation_inside_LPAC(bool application)
    {
        string executable = Probe();
        if (application)
        {
            string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
            Assert.True(assets is not null && Path.IsPathFullyQualified(assets)); ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!); executable = Path.Combine(assets!, "FileCat.exe");
        }
        byte[] source = [0x28, 0xb5, 0x2f, 0xfd, 0, 0xf8, 1, 0, 0]; ArchiveAppContainer? worker = null;
        var client = new ArchiveAssessmentClient((nonce, cpu) => worker = application ? ArchiveAppContainer.StartAssessment(executable, nonce, cpu) : ArchiveAppContainer.Start(executable, "archive-assess " + nonce, cpu));
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.AssessAsync(new(new(ArchiveWriteFormat.TarZstd), source.Length,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source))), () => new MemoryStream(source, false), TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, error.Reason); Assert.NotNull(worker); Assert.True(worker.HasExited); Assert.Equal(0u, worker.ExitCode); Assert.False(worker.ErrorLimitExceeded);
    }
    [Theory]
    [InlineData("existing-target")]
    [InlineData("changed-input")]
    [InlineData("metadata-change")]
    [InlineData("cancelled")]
    [InlineData("after-claim")]
    public async Task Confined_new_private_archive_publication_failures_retain_exact_evidence(string failure)
    {
        string executable = Probe(); string root = Directory.CreateTempSubdirectory("filecat-publication-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(root, "input"), [1]); using var directory = WindowsArchiveDirectory.Open(root); using var input = directory.OpenSource("input");
            var state = ((IArchiveRetainedFile)input).Observe(TestContext.Current.CancellationToken);
            await ArchiveCreationPublicationCases.Failure(failure, root, input, state,
                (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
                new((nonce, cpu) => ArchiveAppContainer.Start(executable, "archive-transform " + nonce, cpu)),
                new((nonce, cpu) => ArchiveAppContainer.Start(executable, "archive-verify " + nonce, cpu)), TestContext.Current.CancellationToken);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(false, false, ArchiveWriteFormat.Tar)]
    [InlineData(false, true, ArchiveWriteFormat.Tar)]
    [InlineData(true, false, ArchiveWriteFormat.Tar)]
    [InlineData(true, true, ArchiveWriteFormat.Tar)]
    [InlineData(false, false, ArchiveWriteFormat.TarGzip)]
    [InlineData(false, true, ArchiveWriteFormat.TarGzip)]
    [InlineData(true, false, ArchiveWriteFormat.TarGzip)]
    [InlineData(true, true, ArchiveWriteFormat.TarGzip)]
    [InlineData(false, false, ArchiveWriteFormat.TarBzip2)]
    [InlineData(false, true, ArchiveWriteFormat.TarBzip2)]
    [InlineData(true, false, ArchiveWriteFormat.TarBzip2)]
    [InlineData(true, true, ArchiveWriteFormat.TarBzip2)]
    [InlineData(false, false, ArchiveWriteFormat.TarZstd)]
    [InlineData(false, true, ArchiveWriteFormat.TarZstd)]
    [InlineData(true, false, ArchiveWriteFormat.TarZstd)]
    [InlineData(true, true, ArchiveWriteFormat.TarZstd)]
    public async Task Retained_NTFS_inputs_rebuild_and_verify_a_real_private_stage_without_publication(bool application, bool update, ArchiveWriteFormat format)
    {
        string executable = Probe(); var ct = TestContext.Current.CancellationToken;
        var profile = new ArchiveWriteProfile(format);
        if (application)
        {
            string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
            Assert.True(assets is not null && Path.IsPathFullyQualified(assets)); ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);
            executable = Path.Combine(assets!, "FileCat.exe");
        }
        string root = Directory.CreateTempSubdirectory("filecat-retained-stage-").FullName;
        try
        {
            byte[] payload = "retained native NTFS input"u8.ToArray(); string inputPath = Path.Combine(root, "input");
            File.WriteAllBytes(inputPath, payload); File.SetLastWriteTimeUtc(inputPath, DateTime.UnixEpoch.AddSeconds(1700000000));
            if (!update) File.SetAttributes(inputPath, FileAttributes.ReadOnly);
            using var directory = WindowsArchiveDirectory.Open(root); using var input = directory.OpenSource("input");
            var state = ((IArchiveRetainedFile)input).Observe(ct); WindowsArchiveFile? original = null;
            try
            {
                ArchiveRetainedBinding? source = null; ArchiveAssessment? assessment = null;
                if (update)
                {
                    using (var data = File.Create(Path.Combine(root, "original.tar")))
                    using (Stream? gzip = format switch { ArchiveWriteFormat.TarGzip => new System.IO.Compression.GZipStream(data, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true),
                        ArchiveWriteFormat.TarBzip2 => SharpCompress.Compressors.BZip2.BZip2Stream.Create(data, SharpCompress.Compressors.CompressionMode.Compress, false, leaveOpen: true),
                        ArchiveWriteFormat.TarZstd => new SharpCompress.Compressors.ZStandard.CompressionStream(data, leaveOpen: true), _ => null })
                    using (var writer = new TarWriter(gzip ?? (Stream)data, TarEntryFormat.Pax))
                    { using var before = new MemoryStream("before"u8.ToArray()); writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "old") { DataStream = before }); }
                    original = directory.OpenSource("original.tar"); var originalState = ((IArchiveRetainedFile)original).Observe(ct); source = new(original, originalState);
                    assessment = await new ArchiveAssessmentClient((nonce, cpu) => application ? ArchiveAppContainer.StartAssessment(executable, nonce, cpu) : ArchiveAppContainer.Start(executable, "archive-assess " + nonce, cpu))
                        .AssessAsync(new(profile, originalState.Version.Length, originalState.Version.Sha256), () => ((IArchiveRetainedFile)original).OpenReadView(originalState, ct), ct);
                }
                var plan = ArchiveTarTransformPlan.Create(profile, assessment, [state.Input(3)], source is null ? [] :
                    [new(ArchiveChangeKind.Replace, new(assessment!.CatalogRevision, "old", ArchiveMemberType.File, 0), inputId: 3)], [new("added", ArchiveMemberType.File, 3)], ct);
                using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                    Archive = new(Path.Combine(root, "result.tar"), null, [], Profile: profile) }, "owned journaled TAR preparation", "owned", [], []));
                ArchiveAppContainer? transform = null, verifier = null;
                using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, Path.Combine(root, update ? "original.tar" : "result.tar"), plan, source,
                    new Dictionary<int, ArchiveRetainedBinding> { [3] = new(input, state) },
                    (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum),
                    r => { ArchiveJournaledTarAssertions.BeforeCreate(journal, r); return directory.CreateArtifact(r); },
                    new((nonce, cpu) => { ArchiveJournaledTarAssertions.Phase(journal, ArchiveTransactionPhase.Rebuilding); return transform = application ? ArchiveAppContainer.StartTransform(executable, nonce, cpu) : ArchiveAppContainer.Start(executable, "archive-transform " + nonce, cpu); }),
                    new((nonce, cpu) => { ArchiveJournaledTarAssertions.Phase(journal, ArchiveTransactionPhase.Verifying); return verifier = application ? ArchiveAppContainer.StartVerification(executable, nonce, cpu) : ArchiveAppContainer.Start(executable, "archive-verify " + nonce, cpu); }), ct);
                var stage = prepared.Stage; var result = prepared.Verified; ArchiveJournaledTarAssertions.Complete(prepared, journal, plan, ct);
                Assert.Equal(0x1a4, state.Input(3).Mode);
                Assert.Equal(0x1a4, BinaryPrimitives.ReadInt32LittleEndian(result.Verification.ObservedManifest.Members.Single(m => m.Name == "added").Metadata[2..]));
                Assert.Equal(result.Stage.Version, stage.CaptureVersion(ct)); Assert.Equal(result.Stage.Version, result.Verification.Stage);
                Assert.Equal(plan.Expected.Digest, result.Verification.ManifestDigest); Assert.False(stage.CanWrite); Assert.Throws<NotSupportedException>(() => stage.WriteByte(1));
                Assert.True(input.CanRead); Assert.Equal(state.Version, ((IArchiveRetainedFile)input).Observe(ct).Version);
                Assert.NotNull(transform); Assert.NotNull(verifier); Assert.NotEqual(transform.ProcessId, verifier.ProcessId);
                Assert.True(transform.HasExited); Assert.True(verifier.HasExited); Assert.Equal(0u, transform.ExitCode); Assert.Equal(0u, verifier.ExitCode);
                Assert.True(File.Exists(stage.DisplayPath)); Assert.False(File.Exists(Path.Combine(root, "result.tar")));
                if (!update) await ArchiveJournaledTarAssertions.PublishNew(prepared, journal,
                    new Dictionary<int, ArchiveRetainedBinding> { [3] = new(input, state) },
                    r => { ArchiveJournaledTarAssertions.BeforeCreate(journal, r); return directory.CreateArtifact(r); }, ct);
            }
            finally { original?.Dispose(); }
        }
        finally { if (File.Exists(Path.Combine(root, "input"))) File.SetAttributes(Path.Combine(root, "input"), FileAttributes.Normal); Directory.Delete(root, recursive: true); }
    }
}
