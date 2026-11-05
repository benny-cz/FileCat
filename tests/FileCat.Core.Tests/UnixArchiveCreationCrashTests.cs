using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using FileCat.Tests;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Theory]
    [InlineData("metadata-reserved")]
    [InlineData("metadata-identified")]
    [InlineData("metadata-partial")]
    [InlineData("metadata-sealed")]
    [InlineData("metadata-recorded")]
    [InlineData("ready")]
    [InlineData("publishing")]
    [InlineData("claimed")]
    [InlineData("reconciling")]
    [InlineData("committed-warning")]
    public async Task Real_creation_process_crashes_preserve_metadata_and_actual_namespace_evidence(string point)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        var facts = await ArchiveCreationCrashAssertions.KillAndRecover(root, point, "-");
        using var directory = UnixArchiveDirectory.Open(root); Assert.Equal(directory.Identity.ToString(), facts.ParentIdentity);
        var expected = facts.VerifiedStage!; var identity = new FileIdentity(Convert.ToUInt64(expected.Identity[..16], 16), expected.Identity[17..]);
        string path = File.Exists(facts.StagePath) ? facts.StagePath : facts.ArchivePath;
        using var file = directory.OpenRecoveryArtifact(Path.GetFileName(path), identity, UnixArchiveArtifactKind.Stage, facts.StageMaximumLength);
        Assert.Equal(expected, Version(file)); Assert.False(file.CanWrite);
        Assert.ThrowsAny<IOException>(() => ((IArchiveCreatePublicationStage)file).ReviewCreate(Observe(file), "another.tar", TestContext.Current.CancellationToken));
        foreach (var evidence in facts.Sidecars.Where(s => s.Identity is not null))
        {
            var id = new FileIdentity(Convert.ToUInt64(evidence.Identity![..16], 16), evidence.Identity[17..]);
            using var held = directory.OpenRecoveryArtifact(Path.GetFileName(evidence.Path), id,
                evidence.Role == ArchiveSidecarRole.OriginEvidence ? UnixArchiveArtifactKind.Provenance : UnixArchiveArtifactKind.Manifest, evidence.MaximumLength);
            Assert.False(held.CanWrite); var version = Version(held); Assert.Equal(evidence.Identity, version.Identity);
            if (evidence.Version is not null) Assert.Equal(evidence.Version, version);
        }
    }
}
