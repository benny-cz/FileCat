using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using SharpCompress.Compressors.LZMA;

namespace FileCat.Archives.Writing;

internal sealed record SevenZipAssemblyResult(ArchiveWriteManifest Manifest, long PhysicalBytes);

internal static partial class SevenZipArchiveWriter
{
    /// <summary>Stream-only exact metadata assembly. Retained packs are copied with their verified witnesses.</summary>
    internal static SevenZipAssemblyResult Assemble(Stream stage, ArchiveSevenZipTransformPlan plan, SevenZipLogicalCatalog? source,
        Func<int, Stream> openCaptured, Func<int, int, Stream> openPacked, CancellationToken ct, SevenZipEncryptionSession? encryption = null)
    {
        var profile = plan.Expected.Profile; var options = ArchiveWriterProfileAdapter.SevenZip(profile);
        var features = ArchiveSevenZipFeatures.Decode(plan.Expected.Features);
        ArchiveSevenZipEncoding.ValidateFeatures(plan.Expected.Members, features);
        if (!stage.CanWrite || !stage.CanSeek || stage.Length != 0 || stage.Position != 0 ||
            (plan.Source is null) != (source is null) || (encryption is null) != (profile.Encryption.Mode == ArchiveEncryptionMode.None) ||
            (encryption?.HideNames == true) != features.HiddenNames || encryption is not null && encryption.CountPower != profile.Encryption.CountPower)
            throw Invalid("The 7z assembly stage, verified source or password session does not match its plan.");
        if (features.HeaderGraph is { } hg)
        {
            ArchiveSevenZipEncoding.ValidateHeader(hg);
            foreach (var cipher in hg.Coders.Where(c => c.Method == 0x06f10701))
                if (encryption is null || cipher.Properties[0] != encryption.HeaderCountPower || cipher.Properties[1] != encryption.HeaderSaltLength)
                    throw Invalid("The header cipher does not match the preserved public parameters.");
        }
        var budget = new ArchiveWriteBudget(); budget.AddMetadata(0, checked(plan.RetainedHeapBytes + plan.Members.Count * 384L + (128 << 10)));
        long totalPacks = 0;
        foreach (var member in plan.Members)
        {
            ct.ThrowIfCancellationRequested(); var m = ArchiveSevenZipMetadata.Decode(member.Expected.Metadata);
            budget.AddMetadata(0, checked(member.Expected.Name.Length * 4L + member.Expected.Metadata.Length * 4L + 512));
            totalPacks += m.Graph?.PackedInputs.Count ?? 0; if (totalPacks > SevenZipStrictMetadata.MaximumPackedStreams) throw ArchiveWriteBudget.Limit();
        }
        var allMetadata = plan.Expected.Members.Select(m => ArchiveSevenZipMetadata.Decode(m.Metadata)).ToArray();
        encryption?.ClaimArchive(); var sink = new StageSink(stage, ct); sink.Write(new byte[32]);
        var contents = new SevenZipEncodedContent?[plan.Members.Count]; byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
        try
        {
            for (int index = 0; index < contents.Length; index++)
            {
                ct.ThrowIfCancellationRequested(); var planned = plan.Members[index]; var expected = planned.Expected;
                var metadata = allMetadata[index]; SevenZipEncodedContent? content = null;
                if (planned.SourceIndex is { } original)
                {
                    if (source is null || original < 0 || original >= source.Members.Count) throw Changed();
                    var retained = source.Members[original]; var physical = retained.Physical;
                    if (physical.Type != expected.Type || physical.Length != expected.Length || retained.Sha256 != expected.Sha256 ||
                        !SevenZipSemanticCatalog.Metadata(retained, source.Physical).Encode().AsSpan().SequenceEqual(expected.Metadata)) throw Changed();
                    if (physical.Folder is { } number)
                    {
                        var folder = source.Physical.Streams.Folders[number];
                        for (int branch = 0; branch < retained.Packed.Count; branch++)
                        {
                            using var lease = openPacked(original, branch); if (ReferenceEquals(lease, stage)) throw Invalid("A retained source cannot also be the 7z stage.");
                            var witness = retained.Packed[branch]; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long left = witness.Physical.Length;
                            while (left > 0)
                            {
                                ct.ThrowIfCancellationRequested(); int wanted = (int)Math.Min(left, buffer.Length), n = lease.Read(buffer.AsSpan(0, wanted));
                                if (n <= 0 || n > wanted) throw Changed(); hash.AppendData(buffer, 0, n); sink.Write(buffer.AsSpan(0, n)); left -= n;
                            }
                            if (lease.ReadByte() != -1 || Convert.ToHexString(hash.GetHashAndReset()) != witness.Sha256) throw Changed();
                        }
                        content = new(null, folder.Coders, folder.Bindings, folder.UnpackedSizes,
                            retained.Packed.Select(p => new SevenZipPackedStream(p.Physical.Length, p.Physical.Crc32 ?? 0)).ToArray(),
                            expected.Length, physical.Crc32 ?? folder.Crc32 ?? 0, expected.Sha256);
                    }
                }
                else if (planned.InputId is { } id)
                {
                    var input = plan.Inputs[id];
                    if (input.Length == 0) { using var lease = Fresh(); if (lease.ReadByte() != -1) throw Changed(); }
                    else if (options.Filter == SevenZipFilter.Bcj2) content = SevenZipBcj2Packing.Encode(Fresh, input.Length, input.Sha256, sink, options, ct, encryption);
                    else { using var lease = Fresh(); content = SevenZipCodecEncoder.Encode(lease, input.Length, sink, options, ct, encryption); }
                    Stream Fresh()
                    {
                        ct.ThrowIfCancellationRequested(); var lease = openCaptured(id) ?? throw Changed();
                        if (ReferenceEquals(lease, stage)) throw Invalid("The 7z stage cannot also be a captured source.");
                        try { if (!lease.CanRead || !lease.CanSeek || lease.Position != 0 || lease.Length != input.Length) throw Changed(); return lease; }
                        catch { lease.Dispose(); throw; }
                    }
                }
                if ((content is null) != (metadata.Graph is null) || content is not null && (content.Length != expected.Length || content.Sha256 != expected.Sha256))
                    throw new ArchiveWriteException(ArchiveWriteReason.VerificationFailed, "7z assembly disagrees with the independently planned member content.");
                if (content is not null)
                {
                    var graph = metadata.Graph!; var consumed = content.Bindings.Select(b => b.Output).ToHashSet();
                    int final = Enumerable.Range(0, content.Coders.Count).Single(i => !consumed.Contains(i));
                    var actual = new ArchiveSevenZipGraph(content.Coders.Select(SevenZipSemanticCatalog.PublicCoder).ToArray(),
                        content.Bindings.Select(b => new ArchiveSevenZipBinding(b.Input, b.Output)).ToArray(), graph.PackedInputs,
                        graph.PackedCrcPresent, final, graph.FolderCrcPresent, graph.MemberCrcPresent);
                    if (!new ArchiveSevenZipMetadata(metadata.Type, metadata.Attributes, metadata.CreatedFileTime, metadata.AccessedFileTime,
                        metadata.ModifiedFileTime, actual).Encode().AsSpan().SequenceEqual(expected.Metadata))
                        throw new ArchiveWriteException(ArchiveWriteReason.VerificationFailed, "7z encoding disagrees with the parent-predicted public coder graph.");
                }
                contents[index] = content;
            }
            var header = AssemblyHeader(sink, plan, allMetadata, contents, features, ct, encryption); long physicalBytes = sink.Bytes;
            Span<byte> signature = stackalloc byte[32]; signature.Clear(); ((ReadOnlySpan<byte>)[0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c]).CopyTo(signature);
            signature[7] = features.MinorVersion; BinaryPrimitives.WriteUInt64LittleEndian(signature[12..], (ulong)(header.Start - 32));
            BinaryPrimitives.WriteUInt64LittleEndian(signature[20..], (ulong)header.Bytes); BinaryPrimitives.WriteUInt32LittleEndian(signature[28..], header.Crc32);
            BinaryPrimitives.WriteUInt32LittleEndian(signature[8..], ArchiveCrc32.Compute(signature[12..]));
            ct.ThrowIfCancellationRequested(); stage.Position = 0; stage.Write(signature); stage.Position = physicalBytes;
            if (stage.Length != physicalBytes) throw Changed(); return new(plan.Expected, physicalBytes);
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    private static (long Start, long Bytes, uint Crc32) AssemblyHeader(StageSink sink, ArchiveSevenZipTransformPlan plan,
        ArchiveSevenZipMetadata[] metadata, SevenZipEncodedContent?[] contents, ArchiveSevenZipFeatures features, CancellationToken ct, SevenZipEncryptionSession? encryption)
    {
        var rows = new List<(SevenZipEncodedContent Content, ArchiveSevenZipGraph Graph)>();
        for (int i = 0; i < contents.Length; i++) if (contents[i] is { } content) rows.Add((content, metadata[i].Graph!));
        long packedStart = sink.Bytes;
        if (features.HeaderGraph is null)
        { using var header = new HeaderWriter(sink, ct); Raw(header); return (packedStart, header.Bytes, header.Crc32); }
        var graph = features.HeaderGraph; int[] order = ArchiveSevenZipEncoding.ValidateHeader(graph);
        var packed = new SevenZipCodecEncoder.PackedSink(sink, ct);
        var cipherTarget = graph.Encrypted ? encryption!.CreateCipher(packed, true, ArchiveWritePolicy.MaxMetadataBytes, ct) : ((ArchiveAesCbcWriteStream Cipher, byte[] Properties)?)null;
        using var cipher = cipherTarget?.Cipher;
        var compressionOutput = cipher is null ? packed : new SevenZipCodecEncoder.PackedSink(cipher, ct);
        var codec = graph.Coders[order[^1]]; Stream encoded = compressionOutput;
        if (codec.Method == 0x21)
        {
            int dictionary = (2 | codec.Properties[0] & 1) << ((codec.Properties[0] >> 1) + 11);
            encoded = new Lzma2EncoderStream(compressionOutput, dictionary, 32);
        }
        else if (codec.Method == 0x030101)
        {
            int bits = codec.Properties[0], lc = bits % 9, lp = bits / 9 % 5, pb = bits / 45;
            int dictionary = (int)Math.Max(4096u, BinaryPrimitives.ReadUInt32LittleEndian(codec.Properties[1..]));
            encoded = LzmaStream.Create(new LzmaEncoderProperties(true, dictionary, 32, pb, lc, lp), false, compressionOutput);
        }
        long rawBytes; uint rawCrc; string rawSha;
        using (encoded)
        {
            using var raw = new HeaderWriter(encoded, ct); Raw(raw); rawBytes = raw.Bytes; rawCrc = raw.Crc32; rawSha = raw.FinishDigest();
        }
        compressionOutput.Finish(); if (cipher is not null) { cipher.Finish(); packed.Finish(); }
        if (rawBytes > 64L << 20 && rawBytes > Math.Max(1, packed.Bytes) * 1000) throw ArchiveWriteBudget.Limit();
        var native = graph.Coders.Select(c => new SevenZipCoder(MethodBytes(c.Method), c.Properties.ToArray(), c.Inputs)).ToArray();
        var sizes = Enumerable.Repeat(rawBytes, native.Length).ToArray();
        if (cipherTarget is { } encrypted)
        { int index = order[0]; native[index] = new([6, 0xf1, 7, 1], encrypted.Properties); sizes[index] = compressionOutput.Bytes; }
        var headerContent = new SevenZipEncodedContent(null, native, graph.Bindings.Select(b => new SevenZipBinding(b.Input, b.Output)).ToArray(),
            sizes, [new(packed.Bytes, packed.Crc32)], rawBytes, rawCrc, rawSha);
        long start = sink.Bytes; using var outer = new HeaderWriter(sink, ct); outer.Byte(0x17);
        AssemblyStreams(outer, [(headerContent, graph)], (ulong)(packedStart - 32), features.HeaderSubstreamsPresent,
            features.HeaderSubstreamCountsPresent, features.HeaderSubstreamSizesPresent, features.HeaderSubstreamCrcPresent); outer.Finish();
        return (start, outer.Bytes, outer.Crc32);

        void Raw(HeaderWriter writer)
        {
            writer.Byte(1);
            if (features.MainStreamsPresent)
            { writer.Byte(4); AssemblyStreams(writer, rows, 0, features.SubstreamsPresent, features.SubstreamCountsPresent, features.SubstreamSizesPresent, features.SubstreamCrcPresent); }
            AssemblyFiles(writer, plan.Expected.Members, metadata, features, ct); writer.Byte(0); writer.Finish();
        }
    }

    private static byte[] MethodBytes(ulong value)
    { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); int start = 0; while (start < 7 && bytes[start] == 0) start++; return bytes[start..].ToArray(); }

    private static void AssemblyStreams(HeaderWriter h, IReadOnlyList<(SevenZipEncodedContent Content, ArchiveSevenZipGraph Graph)> rows,
        ulong packedPosition, bool substreams, bool counts, bool sizes, bool crcs)
    {
        h.Byte(6); h.Number(packedPosition); h.Number((ulong)rows.Sum(r => r.Content.PackedStreams.Count)); h.Byte(9);
        foreach (var row in rows) foreach (var pack in row.Content.PackedStreams) h.Number((ulong)pack.Length);
        var packs = rows.SelectMany(r => r.Content.PackedStreams.Select((p, i) => (Defined: r.Graph.PackedCrcPresent[i], Value: p.Crc32))).ToArray();
        if (packs.Any(p => p.Defined)) { h.Byte(10); Digests(packs); } h.Byte(0);
        h.Byte(7); h.Byte(11); h.Number((ulong)rows.Count); h.Byte(0);
        foreach (var row in rows)
        {
            h.Number((ulong)row.Content.Coders.Count);
            foreach (var coder in row.Content.Coders)
            {
                bool complex = coder.Inputs != 1 || coder.Outputs != 1;
                h.Byte((byte)(coder.MethodId.Length | (complex ? 16 : 0) | (coder.Properties.Length == 0 ? 0 : 32))); h.Write(coder.MethodId);
                if (complex) { h.Number((ulong)coder.Inputs); h.Number((ulong)coder.Outputs); }
                if (coder.Properties.Length != 0) { h.Number((ulong)coder.Properties.Length); h.Write(coder.Properties); }
            }
            foreach (var binding in row.Content.Bindings) { h.Number((ulong)binding.Input); h.Number((ulong)binding.Output); }
            if (row.Graph.PackedInputs.Count > 1) foreach (int input in row.Graph.PackedInputs) h.Number((ulong)input);
        }
        h.Byte(12); foreach (var row in rows) foreach (long size in row.Content.UnpackedSizes) h.Number((ulong)size);
        var folders = rows.Select(r => (Defined: r.Graph.FolderCrcPresent, Value: r.Content.Crc32)).ToArray();
        if (folders.Any(f => f.Defined)) { h.Byte(10); Digests(folders); } h.Byte(0);
        if (substreams)
        {
            h.Byte(8); if (counts) { h.Byte(13); foreach (var row in rows) h.Number(1); } if (sizes) h.Byte(9);
            if (crcs) { h.Byte(10); Digests(rows.Where(r => !r.Graph.FolderCrcPresent).Select(r => (r.Graph.MemberCrcPresent, r.Content.Crc32)).ToArray()); }
            h.Byte(0);
        }
        h.Byte(0);
        void Digests((bool Defined, uint Value)[] values)
        { bool all = values.All(v => v.Defined); h.Byte(all ? (byte)1 : (byte)0); if (!all) h.Bits(values.Select(v => v.Defined)); foreach (var v in values) if (v.Defined) h.UInt32(v.Value); }
    }

    private static void AssemblyFiles(HeaderWriter h, IReadOnlyList<ArchiveManifestMember> members, ArchiveSevenZipMetadata[] metadata, ArchiveSevenZipFeatures features, CancellationToken ct)
    {
        uint mask = features.FilePropertyMask;
        h.Byte(5); h.Number((ulong)members.Count); int empty = metadata.Count(m => m.Graph is null);
        if (Present(14)) { h.Byte(14); h.Number((ulong)((members.Count + 7) / 8)); h.Bits(metadata.Select(m => m.Graph is null)); }
        if (Present(15)) { h.Byte(15); h.Number((ulong)((empty + 7) / 8)); h.Bits(metadata.Where(m => m.Graph is null).Select(m => m.Type == ArchiveMemberType.File)); }
        if (Present(16)) { h.Byte(16); h.Number((ulong)((empty + 7) / 8)); h.Bits(Enumerable.Repeat(false, empty)); }
        if (Present(17))
        {
            long bytes = 1 + members.Sum(m => m.Name.Length * 2L + 2); h.Byte(17); h.Number((ulong)bytes); h.Byte(0);
            foreach (var member in members) { ct.ThrowIfCancellationRequested(); h.Write(Utf16.GetBytes(member.Name)); h.Byte(0); h.Byte(0); }
        }
        Times(18, m => m.CreatedFileTime); Times(19, m => m.AccessedFileTime); Times(20, m => m.ModifiedFileTime);
        if (Present(21))
        {
            bool[] defined = metadata.Select(m => m.Attributes is not null).ToArray(); int count = defined.Count(v => v); bool all = count == members.Count;
            h.Byte(21); h.Number((ulong)(2L + count * 4L + (all ? 0 : (members.Count + 7) / 8))); h.Byte(all ? (byte)1 : (byte)0);
            if (!all) h.Bits(defined); h.Byte(0); foreach (var m in metadata) if (m.Attributes is { } value) h.UInt32(value);
        }
        if (Present(25))
        {
            h.Byte(25); h.Number((ulong)features.DummyBytes); byte[] zeros = new byte[ArchiveIoAuthority.MaximumReadBytes]; long left = features.DummyBytes;
            while (left > 0) { int n = (int)Math.Min(left, zeros.Length); h.Write(zeros.AsSpan(0, n)); left -= n; }
        }
        h.Byte(0);
        bool Present(int tag) => (mask & (1u << tag)) != 0;
        void Times(int tag, Func<ArchiveSevenZipMetadata, ulong?> select)
        {
            if (!Present(tag)) return; bool[] defined = metadata.Select(m => select(m) is not null).ToArray(); int count = defined.Count(v => v); bool all = count == members.Count;
            h.Byte((byte)tag); h.Number((ulong)(2L + count * 8L + (all ? 0 : (members.Count + 7) / 8))); h.Byte(all ? (byte)1 : (byte)0);
            if (!all) h.Bits(defined); h.Byte(0); foreach (var m in metadata) if (select(m) is { } time) h.UInt64(time);
        }
    }
}
