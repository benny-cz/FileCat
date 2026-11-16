using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TAR_creation_then_fresh_verification_uses_two_confined_roles_and_reaps_both(bool application)
    {
        string executable = Probe(); var ct = TestContext.Current.CancellationToken;
        if (application)
        {
            string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
            Assert.True(assets is not null && Path.IsPathFullyQualified(assets)); ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);
            executable = Path.Combine(assets!, "FileCat.exe");
        }
        byte[] payload = "native confined TAR creation"u8.ToArray();
        var invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(null,
            [new(71, payload.Length, Convert.ToHexString(SHA256.HashData(payload)), DateTimeOffset.UnixEpoch)], [],
            [new("dir/", ArchiveMemberType.Directory), new("dir/file", ArchiveMemberType.File, 71), new(new string('界', ArchiveWritePolicy.MaxNameChars), ArchiveMemberType.File, 71)], ct), ct);
        using var stage = new MemoryStream(); ArchiveAppContainer? transform = null, verifier = null;
        await new ArchiveTransformClient((nonce, cpu) => transform = application ? ArchiveAppContainer.StartTransform(executable, nonce, cpu) : ArchiveAppContainer.Start(executable, "archive-transform " + nonce, cpu))
            .RebuildAsync(invocation, stage, () => throw new InvalidOperationException(), id => { Assert.Equal(71, id); return new MemoryStream(payload, writable: false); }, ct);
        byte[] output = stage.ToArray();
        var verified = await new ArchiveVerificationClient((nonce, cpu) => verifier = application ? ArchiveAppContainer.StartVerification(executable, nonce, cpu) : ArchiveAppContainer.Start(executable, "archive-verify " + nonce, cpu))
            .VerifyAsync(new("parent-stage", output.Length, Convert.ToHexString(SHA256.HashData(output))), invocation.Expected, () => new MemoryStream(output, writable: false), ct);
        Assert.Equal(invocation.Expected.Digest, verified.ManifestDigest); Assert.NotNull(transform); Assert.NotNull(verifier);
        Assert.NotEqual(transform.ProcessId, verifier.ProcessId); Assert.True(transform.HasExited); Assert.True(verifier.HasExited);
        Assert.Equal(0u, transform.ExitCode); Assert.Equal(0u, verifier.ExitCode);
    }
}
