using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveManifestWireTests
{
    private static string Empty => Convert.ToHexString(SHA256.HashData([]));
    private static ArchiveManifestMember Member(string name = "é/name", int occurrence = 0, byte[]? metadata = null, string? digest = null, long length = 0) =>
        new(name, ArchiveMemberType.File, occurrence, length, digest ?? Empty, metadata ?? [1, 0, 7]);
    private static ArchiveWriteManifest Manifest(params ArchiveManifestMember[] members) => new(new(ArchiveWriteFormat.Tar), members, [1, 2]);

    [Fact]
    public void Canonical_manifest_freezes_caller_arrays_and_distinguishes_metadata_content_order_and_options()
    {
        byte[] metadata = [1, 0, 7];
        var first = Member(metadata: metadata);
        var entries = new[] { first, Member("other") };
        byte[] features = [1, 2];
        var manifest = new ArchiveWriteManifest(new(ArchiveWriteFormat.Tar), entries, features);
        string digest = manifest.Digest;
        metadata[2]++; features[1]++; entries[0] = Member("changed");
        Assert.Equal(digest, Manifest(first, Member("other")).Digest);
        Assert.Equal("é/name", manifest.Members[0].Name);
        Assert.Equal(new byte[] { 1, 0, 7 }, first.Metadata.ToArray());
        Assert.NotEqual(digest, Manifest(Member("other"), first).Digest);
        var differentMetadata = Manifest(Member(metadata: [1, 0, 8]), Member("other"));
        Assert.Equal(manifest.ContentDigest, differentMetadata.ContentDigest);
        Assert.NotEqual(manifest.MetadataDigest, differentMetadata.MetadataDigest);
        var differentContent = Manifest(Member(digest: new string('A', 64), length: 1), Member("other"));
        Assert.Equal(manifest.MetadataDigest, differentContent.MetadataDigest);
        Assert.NotEqual(manifest.ContentDigest, differentContent.ContentDigest);
        Assert.NotEqual(manifest.Digest, new ArchiveWriteManifest(new(ArchiveWriteFormat.TarGzip), manifest.Members, [1, 2]).Digest);
    }

    [Fact]
    public void Impossible_empty_content_and_directory_digests_are_refused()
    {
        Assert.Throws<ArchiveWriteException>(() => Member(digest: new string('A', 64)));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveManifestMember("dir/", ArchiveMemberType.Directory, 0, 0, new string('A', 64), []));
    }

    [Fact]
    public void Duplicate_occurrences_are_contiguous_per_raw_name_and_type_without_folding_case_or_Unicode()
    {
        var valid = Manifest(Member("same"), Member("Same"), Member("same", 1), Member("é"), Member("é"));
        Assert.Equal(5, valid.Members.Count);
        Assert.Throws<ArchiveWriteException>(() => Manifest(Member("same", 1)));
        Assert.Throws<ArchiveWriteException>(() => Manifest(Member("same"), Member("same")));
    }

    [Fact]
    public void Manifest_aggregate_heap_and_logical_limits_apply_even_when_individual_records_fit()
    {
        string name = new('a', 32000);
        var many = Enumerable.Range(0, 3300).Select(i => Member(name, i)).ToArray();
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => Manifest(many)).Reason);
        var payloads = Enumerable.Range(0, 9).Select(i => new ArchiveManifestMember("f" + i, ArchiveMemberType.File, 0,
            ArchiveWritePolicy.MaxPayloadBytes, Empty, [])).ToArray();
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => Manifest(payloads)).Reason);
        Assert.Throws<ArchiveWriteException>(() => Manifest(Enumerable.Repeat(Member(), ArchiveWritePolicy.MaxMembers + 1).ToArray()));
    }

    [Fact]
    public void Exact_wire_roundtrip_reconstructs_the_complete_manifest_and_rejects_extra_bytes()
    {
        var request = new ArchiveAssessmentRequest(new(ArchiveWriteFormat.Tar), 1024, Empty);
        Assert.Equal(request, ArchiveAssessmentWire.ReadRequest(ArchiveAssessmentWire.Request(request)));
        var expected = Manifest(Member(metadata: new byte[900000]));
        var assessment = new ArchiveAssessment(expected, Empty, 1024, Empty);
        var budget = new ArchiveWriteBudget();
        var header = ArchiveAssessmentWire.ReadCatalog(ArchiveAssessmentWire.Catalog(assessment), request, budget);
        var decoded = ArchiveAssessmentWire.ReadMember(ArchiveAssessmentWire.Member(expected.Members[0]), budget);
        var observed = new ArchiveWriteManifest(header.Profile, [decoded], header.Features);
        ArchiveAssessmentWire.VerifyComplete(header, observed);
        Assert.Equal(expected.Digest, observed.Digest);
        Assert.Throws<ArchiveWriteException>(() => ArchiveAssessmentWire.ReadMember([.. ArchiveAssessmentWire.Member(decoded), 0], new()));
        Assert.Throws<ArchiveWriteException>(() => ArchiveAssessmentWire.ReadRequest([.. ArchiveAssessmentWire.Request(request), 0]));
    }

    [Fact]
    public void Malicious_string_length_is_rejected_before_large_allocation_and_unknown_profile_is_not_selected()
    {
        byte[] record = ArchiveAssessmentWire.Member(Member());
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(2), int.MaxValue);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(ArchiveWriteReason.ProtocolViolation,
            Assert.Throws<ArchiveWriteException>(() => ArchiveAssessmentWire.ReadMember(record, new())).Reason);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1 << 20);
        byte[] request = ArchiveAssessmentWire.Request(new(new(ArchiveWriteFormat.Tar), 1024, Empty));
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(2), int.MaxValue);
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, Assert.Throws<ArchiveWriteException>(() => ArchiveAssessmentWire.ReadRequest(request)).Reason);
    }

    [Fact]
    public void Fragmented_control_records_preserve_large_bounded_records_and_reject_wrong_offsets_and_resume()
    {
        Guid nonce = Guid.NewGuid();
        byte[] content = new byte[900000]; new Random(71).NextBytes(content);
        using var wire = new MemoryStream();
        new ArchiveWorkerRecords(new(nonce, 2 << 20), wire, writing: true).Write(ArchiveControlRecord.Member, content);
        wire.Position = 0;
        Assert.Equal(content, new ArchiveWorkerRecords(new(nonce, 2 << 20), wire, writing: false).Read(ArchiveControlRecord.Member));
        byte[] broken = wire.ToArray();
        int second = ArchiveWorkerProtocol.HeaderBytes + ArchiveWritePolicy.MaxDataFrameBytes;
        BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(second + ArchiveWorkerProtocol.HeaderBytes + 5), 0);
        using var bad = new MemoryStream(broken);
        var reader = new ArchiveWorkerRecords(new(nonce, 2 << 20), bad, writing: false);
        Assert.Throws<ArchiveWriteException>(() => reader.Read(ArchiveControlRecord.Member));
        Assert.Throws<ArchiveWriteException>(() => reader.Read(ArchiveControlRecord.Catalog));
    }

    [Fact]
    public void Wire_traffic_allowance_can_cover_multiple_bounded_passes_without_raising_the_file_size_policy()
    {
        _ = new ArchiveWorkerProtocol(Guid.NewGuid(), 4 * ArchiveWritePolicy.MaxPhysicalBytes + ArchiveWritePolicy.MaxMetadataBytes);
        Assert.Equal(80L << 30, ArchiveWritePolicy.MaxPhysicalBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveWorkerProtocol(Guid.NewGuid(), ArchiveWritePolicy.MaxWorkerWireBytes + 1));
    }
}
