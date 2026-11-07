using System.Buffers.Binary;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveSevenZipMetadataTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ArchiveSevenZipCoder Copy => new(0, []);
    private static ArchiveSevenZipGraph Graph => new([Copy], [], [0], [true], 0, true, true);

    [Theory] [InlineData(0UL)] [InlineData(1UL)] [InlineData(ulong.MaxValue)]
    public void Full_unsigned_FILETIME_and_undefined_fields_roundtrip_without_DateTime_projection(ulong time)
    {
        var metadata = new ArchiveSevenZipMetadata(ArchiveMemberType.File, 0x81a48020, time, null, time, Graph);
        var decoded = ArchiveSevenZipMetadata.Decode(metadata.Encode());
        Assert.Equal(time, decoded.CreatedFileTime); Assert.Null(decoded.AccessedFileTime); Assert.Equal(time, decoded.ModifiedFileTime);
        Assert.Equal(metadata.Encode(), decoded.Encode());
        Assert.NotEqual(metadata.Encode(), new ArchiveSevenZipMetadata(ArchiveMemberType.File, 0x81a48020, time, 0, time, Graph).Encode());
    }

    [Fact]
    public void Semantic_packets_refuse_every_truncation_trailing_bytes_unknown_versions_and_noncanonical_flags()
    {
        byte[] metadata = new ArchiveSevenZipMetadata(ArchiveMemberType.File, 0x81a48020, 1, 2, 3, Graph).Encode();
        byte[] features = new ArchiveSevenZipFeatures(4, true, 1u << 17, 0, true, false, false, false, Graph, true).Encode();
        for (int i = 0; i < metadata.Length; i++) Assert.Throws<ArchiveWriteException>(() => ArchiveSevenZipMetadata.Decode(metadata.AsSpan(0, i)));
        for (int i = 0; i < features.Length; i++) Assert.Throws<ArchiveWriteException>(() => ArchiveSevenZipFeatures.Decode(features.AsSpan(0, i)));
        Assert.Throws<ArchiveWriteException>(() => ArchiveSevenZipMetadata.Decode([.. metadata, 0]));
        Assert.Throws<ArchiveWriteException>(() => ArchiveSevenZipFeatures.Decode([.. features, 0]));
        metadata[0] = 2; Assert.Throws<ArchiveWriteException>(() => ArchiveSevenZipMetadata.Decode(metadata));
        metadata[0] = 1; metadata[6] = 2; Assert.Throws<ArchiveWriteException>(() => ArchiveSevenZipMetadata.Decode(metadata));
        features[3] = 2; Assert.Throws<ArchiveWriteException>(() => ArchiveSevenZipFeatures.Decode(features));
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Ownership_cycles_disconnection_bad_properties_and_sizes_are_not_semantic_graphs(int damage)
    {
        Assert.Throws<ArchiveWriteException>(() =>
        {
            _ = damage switch
            {
                0 => new ArchiveSevenZipGraph([Copy, Copy], [new(0, 0)], [1], [true], 1, true, true),
                1 => new ArchiveSevenZipGraph([Copy, Copy], [new(1, 1)], [0], [true], 0, true, true),
                2 => new ArchiveSevenZipGraph([Copy], [], [1], [true], 0, true, true),
                3 => new ArchiveSevenZipGraph([new(10, [1, 0, 0, 0])], [], [0], [true], 0, true, true),
                _ => new ArchiveSevenZipGraph([Copy], [], [0], [], 0, true, true),
            };
        });
    }

    [Fact]
    public void Owned_graph_inputs_are_frozen_and_AES_public_properties_cannot_accept_native_crypto_blobs()
    {
        byte[] properties = [19, 16, 16]; var coder = new ArchiveSevenZipCoder(0x06f10701, properties);
        ArchiveSevenZipCoder[] coders = [coder]; int[] packs = [0]; bool[] crcs = [true];
        var graph = new ArchiveSevenZipGraph(coders, [], packs, crcs, 0, true, true);
        byte[] original = new ArchiveSevenZipMetadata(ArchiveMemberType.File, graph: graph).Encode();
        properties[0] = 1; coders[0] = Copy; packs[0] = 1; crcs[0] = false;
        Assert.Equal(original, new ArchiveSevenZipMetadata(ArchiveMemberType.File, graph: graph).Encode());
        Assert.Throws<ArchiveWriteException>(() => new ArchiveSevenZipCoder(0x06f10701, new byte[34]));
    }

    [Fact]
    public void Random_AES_salts_and_IVs_never_enter_the_semantic_projection()
    {
        byte[] native = new byte[34]; native[0] = 0xd3; native[1] = 0xff;
        var folder = new SevenZipRawFolder([new([6, 0xf1, 7, 1], native)], [], [0], [16], 0, 0, 0);
        var streams = new SevenZipRawStreams([new(32, 16, 0)], [folder], [0], true, false, false, false);
        byte[] before = new ArchiveSevenZipMetadata(ArchiveMemberType.File, graph: SevenZipSemanticCatalog.Graph(folder, streams, true)).Encode();
        native.AsSpan(2).Fill(0xa5);
        byte[] after = new ArchiveSevenZipMetadata(ArchiveMemberType.File, graph: SevenZipSemanticCatalog.Graph(folder, streams, true)).Encode();
        Assert.Equal(before, after);
    }

    [Fact]
    public void Whole_container_and_member_integrity_presence_changes_the_manifest()
    {
        var left = new ArchiveSevenZipGraph([Copy], [], [0], [false], 0, false, true);
        var right = new ArchiveSevenZipGraph([Copy], [], [0], [true], 0, false, true);
        Assert.NotEqual(new ArchiveSevenZipMetadata(ArchiveMemberType.File, graph: left).Encode(), new ArchiveSevenZipMetadata(ArchiveMemberType.File, graph: right).Encode());
        var features = new ArchiveSevenZipFeatures(4, true, 1u << 17, 0, true, true, false, true, Graph, true, true);
        Assert.Equal(features.Encode(), ArchiveSevenZipFeatures.Decode(features.Encode()).Encode());
        Assert.Throws<ArchiveWriteException>(() => new ArchiveSevenZipFeatures(4, false, 1u << 17, 0, true, false, false, false));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveSevenZipFeatures(4, true, 1u << 24, 0, true, false, false, false));
    }

    [Fact]
    public void Claimed_coder_property_size_is_bounded_before_any_blob_allocation()
    {
        byte[] metadata = new ArchiveSevenZipMetadata(ArchiveMemberType.File, graph: Graph).Encode();
        // Version/type, four undefined native fields and present graph, one coder, method and input count.
        BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(27), int.MaxValue);
        Assert.Throws<ArchiveWriteException>(() => ArchiveSevenZipMetadata.Decode(metadata));
    }
}
