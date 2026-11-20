using System.Formats.Tar;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;
using Scripted = FileCat.Core.Tests.ArchiveAssessmentClientTests.Scripted;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchiveVerificationClientTests
{
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static ArchiveObjectVersion Version(byte[] bytes) => new("parent-held-stage-identity", bytes.Length, Hash(bytes));
    private static byte[] Tar()
    {
        using var bytes = new MemoryStream();
        using (var writer = new TarWriter(bytes, TarEntryFormat.Pax, leaveOpen: true))
            foreach (string name in new[] { "first", "second" })
            { using var data = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(name)); writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = data, ModificationTime = DateTimeOffset.UnixEpoch, Mode = (UnixFileMode)0x1a4 }); }
        return bytes.ToArray();
    }
    private static Task<ArchiveAssessment> Assess(byte[] bytes, Func<Guid, TimeSpan, IArchiveWorkerProcess>? launch = null) =>
        new ArchiveAssessmentClient(launch ?? ((nonce, _) => Child.Start(nonce))).AssessAsync(new(new(ArchiveWriteFormat.Tar), bytes.Length, Hash(bytes)),
            () => new MemoryStream(bytes, writable: false), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_fresh_child_derives_all_members_without_receiving_expected_manifest_and_is_reaped_before_acceptance()
    {
        byte[] bytes = Tar(); Child? analysis = null, verification = null;
        var expected = await Assess(bytes, (nonce, _) => analysis = Child.Start(nonce));
        Assert.True(analysis!.Reaped); int opens = 0;
        var client = new ArchiveVerificationClient((nonce, _) => verification = Child.Start(nonce, "archive-verify"));
        var result = await client.VerifyAsync(Version(bytes), expected.Manifest, () => { opens++; return new MemoryStream(bytes, writable: false); }, TestContext.Current.CancellationToken);
        Assert.Equal(1, opens); Assert.True(verification!.Reaped); Assert.True(verification.Disposed);
        Assert.NotEqual(analysis.ProcessId, verification.ProcessId); Assert.Equal(Version(bytes), result.Stage);
        Assert.Equal(expected.Manifest.Digest, result.ManifestDigest); Assert.Equal(expected.Manifest.ContentDigest, result.ContentDigest); Assert.Equal(expected.Manifest.MetadataDigest, result.MetadataDigest);
    }
    [Theory]
    [InlineData("name")]
    [InlineData("content")]
    [InlineData("metadata")]
    [InlineData("order")]
    [InlineData("features")]
    public async Task A_complete_valid_stage_must_match_every_parent_expected_field(string field)
    {
        byte[] bytes = Tar(); var assessed = await Assess(bytes); var first = assessed.Manifest.Members[0]; var second = assessed.Manifest.Members[1];
        byte[] metadata = first.Metadata.ToArray(); if (field == "metadata") metadata[2] ^= 1;
        var changed = new ArchiveManifestMember(field == "name" ? "different" : first.Name, first.Type, first.Occurrence, first.Length,
            field == "content" ? Hash("other"u8.ToArray()) : first.Sha256, metadata);
        var expected = new ArchiveWriteManifest(assessed.Manifest.Profile, field == "order" ? [second, changed] : [changed, second], field == "features" ? [1, 1] : assessed.Manifest.Features);
        Child? child = null; var client = new ArchiveVerificationClient((nonce, _) => child = Child.Start(nonce, "archive-verify"));
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.VerifyAsync(Version(bytes), expected, () => new MemoryStream(bytes, writable: false), TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveWriteReason.VerificationFailed, failure.Reason); Assert.True(child!.Reaped); Assert.True(child.Disposed);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assessment_and_verification_workers_cannot_substitute_for_each_others_roles(bool verify)
    {
        byte[] bytes = Tar(); var expected = await Assess(bytes); Child? child = null; int opens = 0;
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(async () =>
        {
            if (verify)
                await new ArchiveVerificationClient((nonce, _) => child = Child.Start(nonce)).VerifyAsync(Version(bytes), expected.Manifest,
                    () => { opens++; return new MemoryStream(bytes, writable: false); }, TestContext.Current.CancellationToken);
            else await Assess(bytes, (nonce, _) => child = Child.Start(nonce, "archive-verify"));
        });
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, failure.Reason); Assert.Equal(0, opens); Assert.True(child!.Reaped);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_physical_stage_or_invalid_archive_never_becomes_a_verification_result(bool changed)
    {
        byte[] bytes = Tar(); var expected = await Assess(bytes); var version = Version(bytes); bytes[0] ^= 1;
        if (!changed) version = Version(bytes); Child? child = null;
        var client = new ArchiveVerificationClient((nonce, _) => child = Child.Start(nonce, "archive-verify"));
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.VerifyAsync(version, expected.Manifest, () => new MemoryStream(bytes, writable: false), TestContext.Current.CancellationToken));
        Assert.Equal(changed ? ArchiveWriteReason.SourceChanged : ArchiveWriteReason.InvalidArchive, failure.Reason); Assert.True(child!.Reaped);
    }
    [Fact]
    public async Task A_matching_catalog_without_complete_stage_reads_is_not_accepted_as_fresh_verification()
    {
        byte[] bytes = Tar(); var expected = await Assess(bytes); int opens = 0; Scripted? child = null;
        var client = new ArchiveVerificationClient((nonce, _) =>
        {
            using var output = new MemoryStream(); var wire = new ArchiveWorkerProtocol(nonce, ArchiveWritePolicy.MaxWorkerWireBytes);
            wire.Write(output, ArchiveWorkerMessage.Hello, ArchiveAssessmentWire.VerificationHello);
            var records = new ArchiveWorkerRecords(wire, output, writing: true);
            records.Write(ArchiveControlRecord.Catalog, ArchiveAssessmentWire.Catalog(expected));
            foreach (var member in expected.Manifest.Members) records.Write(ArchiveControlRecord.Member, ArchiveAssessmentWire.Member(member));
            wire.Write(output, ArchiveWorkerMessage.Completed, Convert.FromHexString(expected.Manifest.Digest));
            return child = new Scripted(output.ToArray());
        });
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.VerifyAsync(Version(bytes), expected.Manifest,
            () => { opens++; return new MemoryStream(bytes, writable: false); }, TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, failure.Reason); Assert.Equal(0, opens); Assert.True(child!.Disposed);
    }
    [Fact]
    public async Task Unsupported_profile_refuses_before_launch_or_stage_access()
    {
        var expected = new ArchiveWriteManifest(new(ArchiveWriteFormat.TarBzip2), [], [1, 2]); int starts = 0, opens = 0;
        var client = new ArchiveVerificationClient((_, _) => { starts++; throw new InvalidOperationException(); });
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.VerifyAsync(Version(Tar()), expected,
            () => { opens++; throw new InvalidOperationException(); }, TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, failure.Reason); Assert.Equal(0, starts); Assert.Equal(0, opens);
    }
}
