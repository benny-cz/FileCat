using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Archives;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
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
        RequireWindows(); using var temp = new Fixture();
        var facts = await ArchiveCreationCrashAssertions.KillAndRecover(temp.Root, point, typeof(WindowsArchiveDirectory).Assembly.Location);
        using var directory = WindowsArchiveDirectory.Open(temp.Root); Assert.Equal(directory.Identity.ToString(), facts.ParentIdentity);
        var expected = facts.VerifiedStage!; var identity = new FileIdentity(Convert.ToUInt64(expected.Identity[..16], 16), expected.Identity[17..]);
        string path = File.Exists(facts.StagePath) ? facts.StagePath : facts.ArchivePath;
        using var file = directory.OpenRecoveryArtifact(Path.GetFileName(path), identity, WindowsArchiveArtifactKind.Stage, facts.StageMaximumLength);
        Assert.Equal(expected, file.CaptureVersion(TestContext.Current.CancellationToken)); Assert.False(file.CanWrite);
        Assert.ThrowsAny<IOException>(() => ((IArchiveCreatePublicationStage)file).ReviewCreate(((IArchiveRetainedFile)file).Observe(TestContext.Current.CancellationToken), "another.tar", TestContext.Current.CancellationToken));
        foreach (var evidence in facts.Sidecars.Where(s => s.Identity is not null))
        {
            var id = new FileIdentity(Convert.ToUInt64(evidence.Identity![..16], 16), evidence.Identity[17..]);
            using var held = directory.OpenRecoveryArtifact(Path.GetFileName(evidence.Path), id,
                evidence.Role == ArchiveSidecarRole.OriginEvidence ? WindowsArchiveArtifactKind.Provenance : WindowsArchiveArtifactKind.Manifest, evidence.MaximumLength);
            Assert.False(held.CanWrite); var version = held.CaptureVersion(TestContext.Current.CancellationToken); Assert.Equal(evidence.Identity, version.Identity);
            if (evidence.Version is not null) Assert.Equal(evidence.Version, version);
        }
    }
}
