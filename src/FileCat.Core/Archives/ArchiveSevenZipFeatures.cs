namespace FileCat.Core.Archives;

/// <summary>Complete admitted container/header property presence; physical placement and random crypto bytes stay local.</summary>
public sealed class ArchiveSevenZipFeatures
{
    public byte MinorVersion { get; }
    public bool MainStreamsPresent { get; }
    public uint FilePropertyMask { get; }
    public long DummyBytes { get; }
    public bool SubstreamsPresent { get; }
    public bool SubstreamCountsPresent { get; }
    public bool SubstreamSizesPresent { get; }
    public bool SubstreamCrcPresent { get; }
    public ArchiveSevenZipGraph? HeaderGraph { get; }
    public bool HeaderSubstreamsPresent { get; }
    public bool HeaderSubstreamCountsPresent { get; }
    public bool HeaderSubstreamSizesPresent { get; }
    public bool HeaderSubstreamCrcPresent { get; }
    public bool HiddenNames => HeaderGraph?.Encrypted == true;
    public ArchiveSevenZipFeatures(byte minorVersion, bool mainStreamsPresent, uint filePropertyMask, long dummyBytes,
        bool substreamsPresent, bool substreamCountsPresent, bool substreamSizesPresent, bool substreamCrcPresent,
        ArchiveSevenZipGraph? headerGraph = null, bool headerSubstreamsPresent = false,
        bool headerSubstreamCountsPresent = false, bool headerSubstreamSizesPresent = false, bool headerSubstreamCrcPresent = false)
    {
        const uint supported = (1u << 14) | (1u << 15) | (1u << 16) | (1u << 17) | (1u << 18) | (1u << 19) | (1u << 20) | (1u << 21) | (1u << 25);
        if (minorVersion > 4 || (filePropertyMask & ~supported) != 0 || dummyBytes < 0 || dummyBytes > ArchiveWritePolicy.MaxMetadataBytes ||
            (filePropertyMask & (1u << 25)) == 0 && dummyBytes != 0 ||
            (filePropertyMask & ((1u << 15) | (1u << 16))) != 0 && (filePropertyMask & (1u << 14)) == 0 ||
            !mainStreamsPresent && (substreamsPresent || substreamCountsPresent || substreamSizesPresent || substreamCrcPresent) ||
            !substreamsPresent && (substreamCountsPresent || substreamSizesPresent || substreamCrcPresent) ||
            headerGraph is null && (headerSubstreamsPresent || headerSubstreamCountsPresent || headerSubstreamSizesPresent || headerSubstreamCrcPresent) ||
            !headerSubstreamsPresent && (headerSubstreamCountsPresent || headerSubstreamSizesPresent || headerSubstreamCrcPresent)) throw ArchiveSevenZipMetadata.Invalid();
        MinorVersion = minorVersion; MainStreamsPresent = mainStreamsPresent; FilePropertyMask = filePropertyMask; DummyBytes = dummyBytes;
        SubstreamsPresent = substreamsPresent; SubstreamCountsPresent = substreamCountsPresent; SubstreamSizesPresent = substreamSizesPresent;
        SubstreamCrcPresent = substreamCrcPresent; HeaderGraph = headerGraph;
        HeaderSubstreamsPresent = headerSubstreamsPresent; HeaderSubstreamCountsPresent = headerSubstreamCountsPresent;
        HeaderSubstreamSizesPresent = headerSubstreamSizesPresent; HeaderSubstreamCrcPresent = headerSubstreamCrcPresent;
    }
    public byte[] Encode()
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write((ushort)1); writer.Write(MinorVersion); writer.Write(MainStreamsPresent); writer.Write(FilePropertyMask); writer.Write(DummyBytes);
        writer.Write(SubstreamsPresent); writer.Write(SubstreamCountsPresent); writer.Write(SubstreamSizesPresent); writer.Write(SubstreamCrcPresent);
        writer.Write(HeaderGraph is not null); HeaderGraph?.Write(writer);
        writer.Write(HeaderSubstreamsPresent); writer.Write(HeaderSubstreamCountsPresent); writer.Write(HeaderSubstreamSizesPresent); writer.Write(HeaderSubstreamCrcPresent);
        writer.Flush(); return output.ToArray();
    }
    public static ArchiveSevenZipFeatures Decode(ReadOnlySpan<byte> data)
    {
        var reader = new SevenZipMetadataPacket(data); if (reader.Short() != 1) throw ArchiveSevenZipMetadata.Invalid();
        byte minor = reader.Byte(); bool main = reader.Flag(); uint mask = reader.UInt(); long dummy = reader.Long();
        bool sub = reader.Flag(), counts = reader.Flag(), sizes = reader.Flag(), crc = reader.Flag(); var graph = reader.Flag() ? ArchiveSevenZipGraph.Read(ref reader) : null;
        bool headerSub = reader.Flag(), headerCounts = reader.Flag(), headerSizes = reader.Flag(), headerCrc = reader.Flag();
        reader.End(); return new(minor, main, mask, dummy, sub, counts, sizes, crc, graph, headerSub, headerCounts, headerSizes, headerCrc);
    }
}
