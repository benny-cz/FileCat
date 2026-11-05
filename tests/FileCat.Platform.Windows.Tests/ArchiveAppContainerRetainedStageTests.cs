using System.Formats.Tar;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using FileCat.Core.Jobs;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Retained_NTFS_inputs_rebuild_and_verify_a_real_private_stage_without_publication(bool application, bool update)
    {
        string executable = Probe(); var ct = TestContext.Current.CancellationToken;
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
            using var directory = WindowsArchiveDirectory.Open(root); using var input = directory.OpenSource("input");
            var state = ((IArchiveRetainedFile)input).Observe(ct); WindowsArchiveFile? original = null;
            try
            {
                ArchiveRetainedBinding? source = null; ArchiveAssessment? assessment = null;
                if (update)
                {
                    using (var data = File.Create(Path.Combine(root, "original.tar")))
                    using (var writer = new TarWriter(data, TarEntryFormat.Pax))
                    { using var before = new MemoryStream("before"u8.ToArray()); writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "old") { DataStream = before }); }
                    original = directory.OpenSource("original.tar"); var originalState = ((IArchiveRetainedFile)original).Observe(ct); source = new(original, originalState);
                    assessment = await new ArchiveAssessmentClient((nonce, cpu) => application ? ArchiveAppContainer.StartAssessment(executable, nonce, cpu) : ArchiveAppContainer.Start(executable, "archive-assess " + nonce, cpu))
                        .AssessAsync(new(new(ArchiveWriteFormat.Tar), originalState.Version.Length, originalState.Version.Sha256), () => ((IArchiveRetainedFile)original).OpenReadView(originalState, ct), ct);
                }
                var plan = ArchiveTarTransformPlan.Create(assessment, [state.Input(3)], source is null ? [] :
                    [new(ArchiveChangeKind.Replace, new(assessment!.CatalogRevision, "old", ArchiveMemberType.File, 0), inputId: 3)], [new("added", ArchiveMemberType.File, 3)], ct);
                using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                    Archive = new(Path.Combine(root, "result.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned journaled TAR preparation", "owned", [], []));
                ArchiveAppContainer? transform = null, verifier = null;
                using var prepared = await ArchiveJournaledTarPreparation.PrepareAsync(journal, Path.Combine(root, update ? "original.tar" : "result.tar"), plan, source,
                    new Dictionary<int, ArchiveRetainedBinding> { [3] = new(input, state) },
                    (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum),
                    r => { ArchiveJournaledTarAssertions.BeforeCreate(journal, r); return directory.CreateArtifact(r); },
                    new((nonce, cpu) => { ArchiveJournaledTarAssertions.Phase(journal, ArchiveTransactionPhase.Rebuilding); return transform = application ? ArchiveAppContainer.StartTransform(executable, nonce, cpu) : ArchiveAppContainer.Start(executable, "archive-transform " + nonce, cpu); }),
                    new((nonce, cpu) => { ArchiveJournaledTarAssertions.Phase(journal, ArchiveTransactionPhase.Verifying); return verifier = application ? ArchiveAppContainer.StartVerification(executable, nonce, cpu) : ArchiveAppContainer.Start(executable, "archive-verify " + nonce, cpu); }), ct);
                var stage = prepared.Stage; var result = prepared.Verified; ArchiveJournaledTarAssertions.Complete(prepared, journal, plan, ct);
                Assert.Equal(result.Stage.Version, stage.CaptureVersion(ct)); Assert.Equal(result.Stage.Version, result.Verification.Stage);
                Assert.Equal(plan.Expected.Digest, result.Verification.ManifestDigest); Assert.False(stage.CanWrite); Assert.Throws<NotSupportedException>(() => stage.WriteByte(1));
                Assert.True(input.CanRead); Assert.Equal(state.Version, ((IArchiveRetainedFile)input).Observe(ct).Version);
                Assert.NotNull(transform); Assert.NotNull(verifier); Assert.NotEqual(transform.ProcessId, verifier.ProcessId);
                Assert.True(transform.HasExited); Assert.True(verifier.HasExited); Assert.Equal(0u, transform.ExitCode); Assert.Equal(0u, verifier.ExitCode);
                Assert.True(File.Exists(stage.DisplayPath)); Assert.False(File.Exists(Path.Combine(root, "result.tar")));
            }
            finally { original?.Dispose(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
