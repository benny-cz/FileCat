using System.Formats.Tar;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Linux;

namespace FileCat.Core.Tests;

public sealed partial class LinuxArchiveProcessTests
{
    [Fact]
    public async Task Fresh_TAR_verification_uses_a_second_confined_process_and_reaps_both_controllers()
    {
        string probe = Probe(); using var bytes = new MemoryStream();
        using (var writer = new TarWriter(bytes, TarEntryFormat.Pax, leaveOpen: true))
        { using var data = new MemoryStream("fresh verification"u8.ToArray()); writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "owned") { DataStream = data }); }
        byte[] source = bytes.ToArray(); string hash = Convert.ToHexString(SHA256.HashData(source)); LinuxArchiveProcess? assessment = null, verification = null;
        var first = new ArchiveAssessmentClient((nonce, cpu) => assessment = LinuxArchiveProcess.Start(probe, nonce, cpu, ["archive-assess", nonce.ToString("D")]));
        var expected = await first.AssessAsync(new(new(ArchiveWriteFormat.Tar), source.Length, hash), () => new MemoryStream(source, writable: false), TestContext.Current.CancellationToken);
        var second = new ArchiveVerificationClient((nonce, cpu) => verification = LinuxArchiveProcess.Start(probe, nonce, cpu, ["archive-verify", nonce.ToString("D")]));
        var version = new ArchiveObjectVersion("parent-owned-stage", source.Length, hash);
        var verified = await second.VerifyAsync(version, expected.Manifest, () => new MemoryStream(source, writable: false), TestContext.Current.CancellationToken);
        Assert.Equal(version, verified.Stage); Assert.Equal(expected.Manifest.Digest, verified.ManifestDigest);
        Assert.NotNull(assessment); Assert.NotNull(verification); Assert.NotEqual(assessment.ProcessId, verification.ProcessId);
        Assert.True(assessment.HasExited); Assert.True(verification.HasExited); Assert.Equal(0u, verification.ExitCode);
        Assert.False(Directory.Exists(assessment.ControlPath)); Assert.False(Directory.Exists(verification.ControlPath));
    }
}
