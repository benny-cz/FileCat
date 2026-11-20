using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_fresh_verifier_matches_the_complete_TAR_manifest_in_a_second_confined_process(bool application)
    {
        string executable = Probe();
        if (application)
        {
            string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
            Assert.True(assets is not null && Path.IsPathFullyQualified(assets)); ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);
            executable = Path.Combine(assets!, "FileCat.exe");
        }
        using var bytes = new MemoryStream();
        using (var writer = new System.Formats.Tar.TarWriter(bytes, leaveOpen: true))
        { using var data = new MemoryStream("fresh native verifier"u8.ToArray()); writer.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "owned") { DataStream = data }); }
        byte[] source = bytes.ToArray(); string hash = Convert.ToHexString(SHA256.HashData(source)); ArchiveAppContainer? analysis = null, verification = null;
        var first = new ArchiveAssessmentClient((nonce, cpu) => analysis = application ? ArchiveAppContainer.StartAssessment(executable, nonce, cpu) : ArchiveAppContainer.Start(executable, "archive-assess " + nonce, cpu));
        var expected = await first.AssessAsync(new(new(ArchiveWriteFormat.Tar), source.Length, hash), () => new MemoryStream(source, writable: false), TestContext.Current.CancellationToken);
        var second = new ArchiveVerificationClient((nonce, cpu) => verification = application ? ArchiveAppContainer.StartVerification(executable, nonce, cpu) : ArchiveAppContainer.Start(executable, "archive-verify " + nonce, cpu));
        var version = new ArchiveObjectVersion("parent-owned-stage", source.Length, hash);
        var result = await second.VerifyAsync(version, expected.Manifest, () => new MemoryStream(source, writable: false), TestContext.Current.CancellationToken);
        Assert.Equal(version, result.Stage); Assert.Equal(expected.Manifest.Digest, result.ManifestDigest);
        Assert.NotNull(analysis); Assert.NotNull(verification); Assert.NotEqual(analysis.ProcessId, verification.ProcessId);
        Assert.True(analysis.HasExited); Assert.True(verification.HasExited); Assert.Equal(0u, verification.ExitCode);
    }
}
