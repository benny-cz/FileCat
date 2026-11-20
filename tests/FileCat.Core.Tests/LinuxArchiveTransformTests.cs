using System.Formats.Tar;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Linux;

namespace FileCat.Core.Tests;

public sealed partial class LinuxArchiveProcessTests
{
    [Fact]
    public async Task TAR_assessment_transform_and_fresh_verification_use_separate_confined_processes_and_reap_every_controller()
    {
        string probe = Probe(); var ct = TestContext.Current.CancellationToken; using var original = new MemoryStream();
        using (var writer = new TarWriter(original, TarEntryFormat.Pax, leaveOpen: true))
        { using var content = new MemoryStream("before"u8.ToArray()); writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "file") { DataStream = content }); }
        byte[] source = original.ToArray(), payload = "after native transform"u8.ToArray(); string hash = Convert.ToHexString(SHA256.HashData(source));
        LinuxArchiveProcess? assessor = null, transform = null, verifier = null;
        var assessed = await new ArchiveAssessmentClient((nonce, cpu) => assessor = LinuxArchiveProcess.Start(probe, nonce, cpu, ["archive-assess", nonce.ToString("D")]))
            .AssessAsync(new(new(ArchiveWriteFormat.Tar), source.Length, hash), () => new MemoryStream(source, writable: false), ct);
        var invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(assessed,
            [new(19, payload.Length, Convert.ToHexString(SHA256.HashData(payload)), DateTimeOffset.UnixEpoch)],
            [new(ArchiveChangeKind.Replace, new(assessed.CatalogRevision, "file", ArchiveMemberType.File, 0), inputId: 19)], [new(new string('界', ArchiveWritePolicy.MaxNameChars), ArchiveMemberType.File, 19)], ct), ct);
        using var stage = new MemoryStream();
        await new ArchiveTransformClient((nonce, cpu) => transform = LinuxArchiveProcess.Start(probe, nonce, cpu, ["archive-transform", nonce.ToString("D")]))
            .RebuildAsync(invocation, stage, () => new MemoryStream(source, writable: false), id => { Assert.Equal(19, id); return new MemoryStream(payload, writable: false); }, ct);
        byte[] output = stage.ToArray();
        var verified = await new ArchiveVerificationClient((nonce, cpu) => verifier = LinuxArchiveProcess.Start(probe, nonce, cpu, ["archive-verify", nonce.ToString("D")]))
            .VerifyAsync(new("parent-stage", output.Length, Convert.ToHexString(SHA256.HashData(output))), invocation.Expected, () => new MemoryStream(output, writable: false), ct);
        Assert.Equal(invocation.Expected.Digest, verified.ManifestDigest); Assert.NotNull(assessor); Assert.NotNull(transform); Assert.NotNull(verifier);
        Assert.Equal(3, new[] { assessor.ProcessId, transform.ProcessId, verifier.ProcessId }.Distinct().Count());
        foreach (var worker in new[] { assessor, transform, verifier })
        { Assert.True(worker.HasExited); Assert.Equal(0u, worker.ExitCode); Assert.False(Directory.Exists(worker.ControlPath)); }
    }
}
