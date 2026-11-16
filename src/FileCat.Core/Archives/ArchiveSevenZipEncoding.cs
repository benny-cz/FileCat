using System.Buffers.Binary;

namespace FileCat.Core.Archives;

/// <summary>Parent-owned public encoding expectations. No codec execution, stream access or random key material.</summary>
internal static class ArchiveSevenZipEncoding
{
    internal static ArchiveSevenZipGraph? Payload(ArchiveWriteProfile profile, long length)
    {
        Validate(profile); if (length == 0) return null;
        var options = profile.SevenZip!; var coders = new List<ArchiveSevenZipCoder>(); var bindings = new List<ArchiveSevenZipBinding>();
        if (options.Filter == ArchiveSevenZipFilter.Bcj2)
        {
            var leaves = new int[4]; Array.Fill(leaves, -1);
            for (int branch = 0; branch < 4; branch++)
            {
                int previous = -1;
                if (profile.Encryption.Mode != ArchiveEncryptionMode.None) { previous = coders.Count; coders.Add(Aes(profile)); }
                if (branch != 3)
                { int index = coders.Count; coders.Add(Codec(profile)); if (previous >= 0) bindings.Add(new(index, previous)); previous = index; }
                leaves[branch] = previous;
            }
            int bcj2 = coders.Count; coders.Add(new(0x0303011b, [], 4));
            for (int branch = 0; branch < 4; branch++) if (leaves[branch] >= 0) bindings.Add(new(bcj2 + branch, leaves[branch]));
        }
        else
        {
            if (profile.Encryption.Mode != ArchiveEncryptionMode.None) coders.Add(Aes(profile));
            int codec = coders.Count; coders.Add(Codec(profile)); if (codec != 0) bindings.Add(new(codec, 0));
            if (options.Filter != ArchiveSevenZipFilter.None)
            {
                (ulong Method, byte[] Properties) filterCoder = options.Filter switch
                {
                    ArchiveSevenZipFilter.Delta => (3UL, [(byte)(options.DeltaDistance - 1)]),
                    ArchiveSevenZipFilter.X86 => (0x03030103UL, []), ArchiveSevenZipFilter.Arm => (0x03030501UL, []),
                    ArchiveSevenZipFilter.ArmThumb => (0x03030701UL, []), ArchiveSevenZipFilter.Arm64 => (10UL, []),
                    ArchiveSevenZipFilter.Ppc => (0x03030205UL, []), ArchiveSevenZipFilter.Ia64 => (0x03030401UL, []),
                    ArchiveSevenZipFilter.Sparc => (0x03030805UL, []), ArchiveSevenZipFilter.RiscV => (11UL, []),
                    ArchiveSevenZipFilter.Swap2 => (0x020302UL, []), ArchiveSevenZipFilter.Swap4 => (0x020304UL, []),
                    _ => throw ArchiveSevenZipMetadata.Invalid(),
                };
                int filter = coders.Count; coders.Add(new(filterCoder.Method, filterCoder.Properties)); bindings.Add(new(filter, codec));
            }
        }
        return Graph(coders, bindings);
    }
    internal static ArchiveSevenZipGraph? Header(ArchiveWriteProfile profile)
    {
        Validate(profile); var coders = new List<ArchiveSevenZipCoder>(); var bindings = new List<ArchiveSevenZipBinding>();
        if (profile.Encryption.Mode == ArchiveEncryptionMode.DataAndNames) coders.Add(Aes(profile));
        if (profile.SevenZip!.HeaderCompression == ArchiveSevenZipHeaderCompression.Lzma2)
        { int index = coders.Count; coders.Add(new(0x21, [16])); if (index != 0) bindings.Add(new(index, 0)); }
        return coders.Count == 0 ? null : Graph(coders, bindings);
    }
    internal static int[] ValidateHeader(ArchiveSevenZipGraph graph)
    {
        // The admitted write route covers the interoperable single compression coder with optional outer AES.
        // More involved decoded-header chains remain readable by the strict catalog, pending their encoder route.
        if (graph.Coders.Count > 2 || graph.PackedInputs.Count != 1 || graph.Coders.Any(c => c.Inputs != 1)) throw HeaderUnsupported();
        var reverse = new List<int>(); int current = graph.FinalOutput;
        while (true)
        {
            reverse.Add(current); var binding = graph.Bindings.FirstOrDefault(b => b.Input == current);
            if (binding is null) break; current = binding.Output;
        }
        reverse.Reverse(); int[] order = reverse.ToArray();
        if (order.Length != graph.Coders.Count || graph.PackedInputs[0] != order[0]) throw HeaderUnsupported();
        for (int i = 0; i < order.Length; i++)
        {
            var coder = graph.Coders[order[i]]; var p = coder.Properties;
            if (coder.Method == 0x06f10701)
            { if (i != 0 || p[0] is < 19 or > 24 || p[2] != 16) throw HeaderUnsupported(); }
            else
            {
                if (i != order.Length - 1 || coder.Method is not (0 or 0x21 or 0x030101)) throw HeaderUnsupported();
                if (coder.Method == 0x21 && p[0] > 24 || coder.Method == 0x030101 && BinaryPrimitives.ReadUInt32LittleEndian(p[1..]) > 16 << 20) throw HeaderUnsupported();
            }
        }
        return order;
    }
    internal static ArchiveSevenZipFeatures Features(ArchiveWriteProfile profile, IReadOnlyList<ArchiveManifestMember> members,
        ArchiveSevenZipFeatures? source)
    {
        Validate(profile); bool streams = source?.MainStreamsPresent == true; uint mask = source?.FilePropertyMask ?? 0;
        if (members.Count != 0) mask |= 1u << 17;
        foreach (var member in members)
        {
            var metadata = ArchiveSevenZipMetadata.Decode(member.Metadata); streams |= metadata.Graph is not null;
            if (metadata.Graph is null) { mask |= 1u << 14; if (member.Type == ArchiveMemberType.File) mask |= 1u << 15; }
            if (metadata.Attributes is not null) mask |= 1u << 21;
            if (metadata.CreatedFileTime is not null) mask |= 1u << 18;
            if (metadata.AccessedFileTime is not null) mask |= 1u << 19;
            if (metadata.ModifiedFileTime is not null) mask |= 1u << 20;
        }
        var header = source is null ? Header(profile) : source.HeaderGraph;
        bool newlyStreaming = streams && source?.MainStreamsPresent != true;
        return new(source?.MinorVersion ?? 4, streams, mask, source?.DummyBytes ?? 0,
            newlyStreaming || source?.SubstreamsPresent == true, source?.SubstreamCountsPresent == true,
            source?.SubstreamSizesPresent == true, source?.SubstreamCrcPresent == true, header,
            source is null ? header is not null : source.HeaderSubstreamsPresent, source?.HeaderSubstreamCountsPresent == true,
            source?.HeaderSubstreamSizesPresent == true, source?.HeaderSubstreamCrcPresent == true);
    }
    internal static void ValidateFeatures(IReadOnlyList<ArchiveManifestMember> members, ArchiveSevenZipFeatures features)
    {
        uint mask = features.FilePropertyMask;
        if (members.Count != 0 && (mask & (1u << 17)) == 0) throw ArchiveSevenZipMetadata.Invalid();
        foreach (var member in members)
        {
            var metadata = ArchiveSevenZipMetadata.Decode(member.Metadata);
            if (metadata.Type != member.Type || metadata.Attributes is not null && (mask & (1u << 21)) == 0 ||
                metadata.CreatedFileTime is not null && (mask & (1u << 18)) == 0 || metadata.AccessedFileTime is not null && (mask & (1u << 19)) == 0 ||
                metadata.ModifiedFileTime is not null && (mask & (1u << 20)) == 0 || metadata.Graph is null &&
                ((mask & (1u << 14)) == 0 || member.Length != 0 || member.Type == ArchiveMemberType.File && (mask & (1u << 15)) == 0) ||
                metadata.Graph is { } graph && (!features.MainStreamsPresent || graph.FolderCrcPresent && !graph.MemberCrcPresent ||
                    graph.MemberCrcPresent && !graph.FolderCrcPresent && !features.SubstreamCrcPresent)) throw ArchiveSevenZipMetadata.Invalid();
        }
        if (features.HeaderGraph is { } h && (h.FolderCrcPresent && !h.MemberCrcPresent || h.MemberCrcPresent && !h.FolderCrcPresent && !features.HeaderSubstreamCrcPresent)) throw ArchiveSevenZipMetadata.Invalid();
    }
    private static ArchiveWriteException HeaderUnsupported() => new(ArchiveWriteReason.UnsupportedFeature,
        "This 7z encoded-header chain, dictionary or legacy AES parameter set needs a separately qualified preservation encoder.");
    private static ArchiveSevenZipGraph Graph(List<ArchiveSevenZipCoder> coders, List<ArchiveSevenZipBinding> bindings)
    {
        int count = coders.Sum(c => c.Inputs); var bound = bindings.Select(b => b.Input).ToHashSet();
        int[] packs = Enumerable.Range(0, count).Where(i => !bound.Contains(i)).ToArray();
        return new(coders, bindings, packs, Enumerable.Repeat(true, packs.Length).ToArray(), coders.Count - 1, true, true);
    }
    private static ArchiveSevenZipCoder Aes(ArchiveWriteProfile profile) => new(0x06f10701, [(byte)profile.Encryption.CountPower, 16, 16]);
    private static ArchiveSevenZipCoder Codec(ArchiveWriteProfile profile)
    {
        var options = profile.SevenZip!; int effort = profile.CompressionLevel == 0 ? 2 : profile.CompressionLevel;
        byte[] props = [];
        ulong method = options.Codec switch
        {
            ArchiveSevenZipCodec.Copy => 0, ArchiveSevenZipCodec.Lzma => 0x030101, ArchiveSevenZipCodec.Lzma2 => 0x21,
            ArchiveSevenZipCodec.Bzip2 => 0x040202, ArchiveSevenZipCodec.Deflate => 0x040108,
            ArchiveSevenZipCodec.Zstandard => 0x04f71101, ArchiveSevenZipCodec.Ppmd7 => 0x030401, _ => throw ArchiveSevenZipMetadata.Invalid(),
        };
        if (options.Codec == ArchiveSevenZipCodec.Lzma)
        { props = new byte[5]; props[0] = 93; BinaryPrimitives.WriteUInt32LittleEndian(props.AsSpan(1), (uint)profile.DictionaryBytes); }
        if (options.Codec == ArchiveSevenZipCodec.Lzma2)
        { int power = System.Numerics.BitOperations.Log2((uint)profile.DictionaryBytes); props = [(byte)(2 * (power - 12))]; }
        if (options.Codec == ArchiveSevenZipCodec.Zstandard) props = [1, 5, (byte)(effort switch { 1 => 1, 2 => 3, _ => 9 }), 0, 0];
        if (options.Codec == ArchiveSevenZipCodec.Ppmd7)
        {
            (int order, int memory) = options.ModelOrder != 0 ? (options.ModelOrder, options.ModelMemoryBytes) : profile.CompressionLevel switch { 1 => (4, 4 << 20), 3 => (8, 16 << 20), _ => (6, 8 << 20) };
            props = new byte[5]; props[0] = (byte)order; BinaryPrimitives.WriteUInt32LittleEndian(props.AsSpan(1), (uint)memory);
        }
        return new(method, props);
    }
    internal static ArchiveSevenZipMetadata Captured(ArchiveMemberType type, long length, int mode, ArchiveTimestamp modified, ArchiveWriteProfile profile)
        => new(type, (uint)(mode | (type == ArchiveMemberType.Directory ? 0x4000 : 0x8000)) << 16 | 0x8000u | (type == ArchiveMemberType.Directory ? 0x10u : 0x20u),
            modifiedFileTime: CapturedFileTime(modified), graph: type == ArchiveMemberType.File ? Payload(profile, length) : null);
    // Version 2 is still unregistered: new inputs use the selected format's 100 ns precision, toward the earlier
    // instant. The source descriptor/held observation retains every nanosecond for native rereviews. Existing
    // archive metadata never passes through this conversion; its unsigned FILETIME values remain exact.
    internal static ulong CapturedFileTime(ArchiveTimestamp time)
        => FileTime(new(time.Seconds, time.Nanoseconds - time.Nanoseconds % 100));
    internal static ulong FileTime(ArchiveTimestamp time)
    {
        const long epoch = -11644473600;
        if (time.Seconds < epoch || !time.FitsTicks) throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "7z cannot represent this timestamp exactly as a 100 ns FILETIME.");
        try { return checked((ulong)(time.Seconds - epoch) * 10000000UL + (uint)(time.Nanoseconds / 100)); }
        catch (OverflowException ex) { throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The timestamp exceeds the unsigned 7z FILETIME range.", ex); }
    }
    private static void Validate(ArchiveWriteProfile profile)
    { if (profile.Version != 2 || profile.Format != ArchiveWriteFormat.SevenZip || profile.SevenZip is null) throw ArchiveSevenZipMetadata.Invalid(); }
}
