using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
using System.Security.Cryptography;

namespace FileCat.Core.Archives;

/// <summary>Worker request only. Expected manifests and native object/publication identities never cross this wire.</summary>
public sealed class ArchiveTransformRequest
{
    public ArchiveWriteProfile Profile { get; }
    public ArchiveAssessmentRequest? Source { get; }
    public IReadOnlyDictionary<int, ArchiveTransformInput> Inputs { get; }
    public IReadOnlyList<ArchiveTransformEdit> Edits { get; }
    public IReadOnlyList<ArchiveTransformAddition> Additions { get; }
    public long OutputLengthBound { get; }
    internal ArchiveTransformRequest(ArchiveAssessmentRequest? source, Dictionary<int, ArchiveTransformInput> inputs,
        ArchiveTransformEdit[] edits, ArchiveTransformAddition[] additions, long outputLengthBound, ArchiveWriteProfile? profile = null)
    {
        Profile = profile ?? source?.Profile ?? new(ArchiveWriteFormat.Tar); Source = source; Inputs = new ReadOnlyDictionary<int, ArchiveTransformInput>(inputs);
        Edits = Array.AsReadOnly(edits); Additions = Array.AsReadOnly(additions); OutputLengthBound = outputLengthBound;
    }
}

/// <summary>Parent-only alias map and expectation. Every payload reference has its own fixed numeric read grant.</summary>
public sealed class ArchiveTransformInvocation
{
    public ArchiveTransformRequest Request { get; }
    public ArchiveWriteManifest Expected { get; }
    public IReadOnlyDictionary<int, int> CapturedInputIds { get; }
    internal ArchiveTransformInvocation(ArchiveTransformRequest request, ArchiveWriteManifest expected, Dictionary<int, int> captured)
    { Request = request; Expected = expected; CapturedInputIds = new ReadOnlyDictionary<int, int>(captured); }
}

public sealed record ArchiveTransformReceipt(long Length, string ManifestDigest, string ContentDigest, string MetadataDigest);

/// <summary>Bounded canonical container transform records. No paths, expected-output digests or worker-selected grants.</summary>
public static class ArchiveTransformWire
{
    public static ReadOnlySpan<byte> Hello => [1, 0, 4, 0, 2, 0, 0, 0];
    public static ReadOnlySpan<byte> HelloFor(ushort protocolVersion) => protocolVersion switch
    { ArchiveWorkerProtocol.Version => Hello, ArchiveWorkerProtocol.SecretVersion => [2, 0, 4, 0, 2, 0, 0, 0], _ => throw Fault() };

    public static ArchiveTransformInvocation Prepare(ArchiveContainerTransformPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Prepare(plan.Source, plan.Inputs, plan.Edits, plan.Additions, plan.Expected, OutputBound(plan, ct), ct);
    }

    internal static long OutputBound(ArchiveContainerTransformPlan plan, CancellationToken ct) => plan switch
    {
        ArchiveTarTransformPlan => OutputBound(plan.Expected, ct),
        ArchiveRar5TransformPlan rar => Rar5OutputBound(rar, ct),
        ArchiveSevenZipTransformPlan seven => SevenZipOutputBound(seven, ct),
        ArchiveStandaloneTransformPlan standalone => ArchiveStandaloneTransformPlan.OutputBound(standalone),
        _ => throw Fault(),
    };

    private static ArchiveTransformInvocation Prepare(ArchiveAssessment? original, IReadOnlyDictionary<int, ArchiveTransformInput> captures,
        IReadOnlyList<ArchiveTransformEdit> sourceEdits, IReadOnlyList<ArchiveTransformAddition> sourceAdditions,
        ArchiveWriteManifest expected, long outputBound, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var inputs = new Dictionary<int, ArchiveTransformInput>(); var captured = new Dictionary<int, int>();
        var edits = new ArchiveTransformEdit[sourceEdits.Count]; var additions = new ArchiveTransformAddition[sourceAdditions.Count];
        for (int i = 0; i < edits.Length; i++)
        { ct.ThrowIfCancellationRequested(); var edit = sourceEdits[i]; edits[i] = new(edit.Kind, edit.Key, edit.NewName, Alias(edit.InputId)); }
        for (int i = 0; i < additions.Length; i++)
        { ct.ThrowIfCancellationRequested(); var addition = sourceAdditions[i]; additions[i] = addition.DirectoryMetadata is { } metadata
            ? new(addition.Name, metadata) : new(addition.Name, addition.Type, Alias(addition.InputId)); }
        var source = original is { } old ? new ArchiveAssessmentRequest(old.Manifest.Profile, old.SourceLength, old.SourceSha256) : null;
        return new(new(source, inputs, edits, additions, outputBound, expected.Profile), expected, captured);

        int? Alias(int? id)
        {
            if (id is null) return null;
            if (inputs.Count == ArchiveWritePolicy.MaxMembers) throw ArchiveWriteBudget.Limit();
            var input = captures[id.Value]; int alias = inputs.Count + 1;
            inputs.Add(alias, new(alias, input.Length, input.Sha256, input.ModifiedTimestamp, input.Mode)); captured.Add(alias, id.Value); return alias;
        }
    }

    private static long Rar5OutputBound(ArchiveRar5TransformPlan plan, CancellationToken ct)
    {
        // Source physical bytes bound every retained packed extent together. Headers are charged separately so
        // growing names/metadata are covered. Directories and stored empty files carry no retained packed bytes.
        long length = 512;
        foreach (var member in plan.Members)
        {
            ct.ThrowIfCancellationRequested(); var expected = member.Expected;
            var metadata = ArchiveRar5Metadata.Decode(expected.Metadata);
            if (member.SourceIndex is not null && expected.Type == ArchiveMemberType.File &&
                (expected.Length != 0 || ((metadata.Compression >> 7) & 7) != 0))
            { length = checked(length + plan.Source!.SourceLength); break; }
        }
        foreach (var member in plan.Members)
        {
            ct.ThrowIfCancellationRequested(); var expected = member.Expected;
            length = checked(length + ArchiveWritePolicy.Utf8.GetByteCount(expected.Name) + 2L * expected.Metadata.Length + 1024);
            if (member.InputId is { } id)
            {
                long input = plan.Inputs[id].Length;
                // Independently reset 32 KiB blocks: <=9 bits/input byte plus <=229 bytes tables/framing/block.
                // 256 bytes/block and the per-member framing allowance also cover the final AES block rounding.
                length = checked(length + input + (input + 7) / 8 + ((input + 32767) / 32768) * 256);
            }
            if (length > ArchiveWritePolicy.MaxPhysicalBytes) throw ArchiveWriteBudget.Limit();
        }
        if (length > ArchiveWritePolicy.MaxPhysicalBytes) throw ArchiveWriteBudget.Limit(); return length;
    }

    private static long SevenZipOutputBound(ArchiveSevenZipTransformPlan plan, CancellationToken ct)
    {
        var profile = plan.Expected.Profile; var options = profile.SevenZip!;
        var features = ArchiveSevenZipFeatures.Decode(plan.Expected.Features); long payload = 0, header = checked(4096 + features.DummyBytes);
        if (plan.Members.Any(m => m.SourceIndex is not null && ArchiveSevenZipMetadata.Decode(m.Expected.Metadata).Graph is not null)) payload = plan.Source!.SourceLength;
        foreach (var member in plan.Members)
        {
            ct.ThrowIfCancellationRequested(); var expected = member.Expected;
            header = checked(header + expected.Name.Length * 2L + expected.Metadata.Length * 4L + 128);
            if (member.InputId is not { } id || plan.Inputs[id].Length == 0) continue;
            long length = plan.Inputs[id].Length;
            payload = checked(payload + CodecBound(length, (int)options.Codec));
            if (options.Filter == ArchiveSevenZipFilter.Bcj2) payload = checked(payload + length + (128 << 10));
            if (profile.Encryption.Mode != ArchiveEncryptionMode.None) payload = checked(payload + 64);
        }
        if (features.HeaderGraph is { } graph)
        {
            int[] order = ArchiveSevenZipEncoding.ValidateHeader(graph); ulong method = graph.Coders[order[^1]].Method;
            header = method == 0x030101 ? CodecBound(header, (int)ArchiveSevenZipCodec.Lzma) :
                method == 0x21 ? CodecBound(header, (int)ArchiveSevenZipCodec.Lzma2) : header;
            header = checked(header + 4096 + 16);
        }
        // This is a preflight space/IO ceiling. The parent still enforces the absolute stage limit before writes.
        return Math.Min(ArchiveWritePolicy.MaxPhysicalBytes, checked(32 + payload + header));

        long CodecBound(long length, int codec) => (ArchiveSevenZipCodec)codec switch
        {
            ArchiveSevenZipCodec.Copy => length,
            ArchiveSevenZipCodec.Lzma => checked(9 * length + (64 << 10)),
            ArchiveSevenZipCodec.Lzma2 => checked(length + 6 * ((length + 65535) / 65536) + (64 << 10)),
            ArchiveSevenZipCodec.Deflate => checked(2 * length + (64 << 10)),
            ArchiveSevenZipCodec.Zstandard => checked(length + (length + 7) / 8 + (64 << 10)),
            ArchiveSevenZipCodec.Bzip2 => checked(4 * length + ((5 * length / 4 + 99979) / 99980 + 1) * (64 << 10) + 32),
            // A symbol can escape at most the configured context depth; each 32-bit range normalization is finite.
            ArchiveSevenZipCodec.Ppmd7 => checked(4L * ((options.ModelOrder != 0 ? options.ModelOrder : profile.CompressionLevel switch { 1 => 4, 3 => 8, _ => 6 }) + 2) * length + (64 << 10)),
            _ => throw Fault(),
        };
    }

    /// <summary>Canonical public request binding, independent of the task nonce and without retaining a whole packet.</summary>
    public static string RequestDigest(ArchiveTransformRequest request, CancellationToken ct = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("FileCat archive transform request binding 1"u8);
        using var sink = new DigestSink(hash);
        var protocol = new ArchiveWorkerProtocol(new Guid("00000000-0000-0000-0000-000000000001"), ArchiveWritePolicy.MaxMetadataBytes);
        WriteRequest(new(protocol, sink, writing: true), request, ct); return Convert.ToHexString(hash.GetHashAndReset());
    }
    private sealed class DigestSink(IncrementalHash hash) : Stream
    {
        public override void Write(ReadOnlySpan<byte> bytes) => hash.AppendData(bytes);
        public override void Write(byte[] bytes, int offset, int count) => hash.AppendData(bytes, offset, count);
        public override void Flush() { } public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] bytes, int offset, int count) => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    public static long OutputBound(ArchiveWriteManifest expected, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArchiveTarFeatures.Validate(expected.Profile, expected.Features);
        long length = 1024;
        foreach (var member in expected.Members)
        {
            ct.ThrowIfCancellationRequested();
            length = checked(length + 512 + Align(member.Length));
            // One PAX extension per member. Include both variable UTF-8 text bodies and generous fixed room for
            // every admitted numeric/time/key/length field, then their complete headers and 512-byte padding.
            // This is a parent ceiling; a runtime emitting more is refused before extending the native stage.
            if (expected.Features[1] == 2)
                length = checked(length + 512 + Align(ArchiveWritePolicy.Utf8.GetByteCount(member.Name) + member.Metadata.Length + 4096L));
            if (length > ArchiveWritePolicy.MaxPhysicalBytes) throw ArchiveWriteBudget.Limit();
        }
        // A conservative streaming-encoder ceiling, still enforced by the parent before every write. Its fixed
        // room and 12.5% expansion allowance exceed the supported DEFLATE encoder's ordinary block overhead.
        if (expected.Profile.Format == ArchiveWriteFormat.TarGzip) length = checked(length + (length + 7) / 8 + (64 << 10) + 18);
        if (expected.Profile.Format == ArchiveWriteFormat.TarZstd) length = checked(length + (length + 7) / 8 + (64 << 10) + 22);
        if (expected.Profile.Format == ArchiveWriteFormat.TarBzip2)
        {
            // First RLE grows at most 5/4; MTF/second RLE do not increase that symbol count. Encoder Huffman
            // lengths are at most 20 bits. Four bytes/input byte plus 64 KiB per minimum-size block cover that
            // payload and all selectors/tables/framing. Actual stage IO still cannot exceed the absolute ceiling.
            long rle = checked((5 * length + 3) / 4);
            long blocks = checked((rle + 99_979) / 99_980 + 1);
            length = Math.Min(ArchiveWritePolicy.MaxPhysicalBytes, checked(4 * length + blocks * (64 << 10) + 32));
        }
        if (length > ArchiveWritePolicy.MaxPhysicalBytes) throw ArchiveWriteBudget.Limit();
        return length;
        static long Align(long value) => checked((value + 511) / 512 * 512);
    }

    public static void WriteRequest(ArchiveWorkerRecords records, ArchiveTransformRequest request, CancellationToken ct = default, Action? progress = null)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(request); ct.ThrowIfCancellationRequested();
        if (request.Source is not null && request.Source.Profile != request.Profile) throw Fault();
        records.Write(ArchiveControlRecord.TransformRequest, Encode(w =>
        {
            w.Write((ushort)3); ArchiveWriteProfileEncoding.Write(w, request.Profile); w.Write(request.Source is not null);
            if (request.Source is { } source) { w.Write(source.SourceLength); Digest(w, source.SourceSha256); }
            w.Write(request.Inputs.Count); w.Write(request.Edits.Count); w.Write(request.Additions.Count); w.Write(request.OutputLengthBound);
        }));
        progress?.Invoke();
        foreach (var input in request.Inputs.Values.OrderBy(i => i.Id))
        {
            ct.ThrowIfCancellationRequested();
            records.Write(ArchiveControlRecord.TransformInput, Encode(w =>
            { w.Write((ushort)2); w.Write(input.Id); w.Write(input.Length); Digest(w, input.Sha256); w.Write(input.ModifiedTimestamp.Seconds); w.Write(input.ModifiedTimestamp.Nanoseconds); w.Write(input.Mode); }));
            progress?.Invoke();
        }
        foreach (var edit in request.Edits)
        {
            ct.ThrowIfCancellationRequested();
            records.Write(ArchiveControlRecord.TransformEdit, Encode(w =>
            {
                w.Write((ushort)1); w.Write((int)edit.Kind); Digest(w, edit.Key.CatalogRevision); Text(w, edit.Key.RawPath);
                w.Write((int)edit.Key.Type); w.Write(edit.Key.Occurrence); w.Write(edit.NewName is not null);
                if (edit.NewName is not null) Text(w, edit.NewName); w.Write(edit.InputId ?? 0);
            }));
            progress?.Invoke();
        }
        foreach (var addition in request.Additions)
        {
            ct.ThrowIfCancellationRequested();
            records.Write(ArchiveControlRecord.TransformAddition, Encode(w =>
            {
                w.Write((ushort)2); Text(w, addition.Name); w.Write((int)addition.Type); w.Write(addition.InputId ?? 0);
                w.Write(addition.DirectoryMetadata is not null);
                if (addition.DirectoryMetadata is { } metadata) { w.Write(metadata.Modified.Seconds); w.Write(metadata.Modified.Nanoseconds); w.Write(metadata.Mode); }
            }));
            progress?.Invoke();
        }
    }

    public static ArchiveTransformRequest ReadRequest(ArchiveWorkerRecords records, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested(); var reader = new Packet(records.Read(ArchiveControlRecord.TransformRequest)); reader.Schema(3);
            var profile = reader.Profile();
            if (profile.Format is not (ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarGzip or ArchiveWriteFormat.TarBzip2 or ArchiveWriteFormat.TarZstd or ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip or ArchiveWriteFormat.Gzip or ArchiveWriteFormat.Bzip2 or ArchiveWriteFormat.Zstd)) throw Fault();
            ArchiveAssessmentRequest? source = reader.Flag() ? new(profile, reader.Long(), reader.Digest()) : null;
            if (source is not null) ArchiveAssessmentWire.Validate(source);
            int inputCount = reader.Count(), editCount = reader.Count(), additionCount = reader.Count(); long bound = reader.Long(); reader.End();
            if (source is null && editCount != 0 || bound < MinimumOutputLength(profile.Format) || bound > ArchiveWritePolicy.MaxPhysicalBytes) throw Fault();
            var budget = new ArchiveWriteBudget(); budget.AddMetadata(0, 512L * inputCount + 256L * editCount + 192L * additionCount);
            var inputs = new Dictionary<int, ArchiveTransformInput>(); long inputBytes = 0;
            for (int i = 0; i < inputCount; i++)
            {
                ct.ThrowIfCancellationRequested(); byte[] data = records.Read(ArchiveControlRecord.TransformInput); budget.AddMetadata(data.Length, 0);
                var packet = new Packet(data); packet.Schema(2); int id = packet.Integer(); long length = packet.Long(); string hash = packet.Digest();
                var modified = new ArchiveTimestamp(packet.Long(), packet.Integer()); int mode = packet.Integer(); packet.End();
                if (id != i + 1 || length is < 0 or > ArchiveWritePolicy.MaxPayloadBytes) throw Fault();
                ArchiveWriteBudget.Reserve(inputBytes, length, ArchiveWritePolicy.MaxLogicalBytes); inputBytes += length;
                inputs.Add(id, new(id, length, hash, modified, mode));
            }
            var used = new HashSet<int>(); var keys = new HashSet<ArchiveMemberKey>(); var edits = new List<ArchiveTransformEdit>();
            for (int i = 0; i < editCount; i++)
            {
                ct.ThrowIfCancellationRequested(); byte[] data = records.Read(ArchiveControlRecord.TransformEdit); budget.AddMetadata(data.Length, 0);
                var packet = new Packet(data); packet.Schema(); var kind = (ArchiveChangeKind)packet.Integer(); string revision = packet.Digest();
                string path = packet.Text(budget); var type = (ArchiveMemberType)packet.Integer(); int occurrence = packet.Integer();
                string? name = packet.Flag() ? packet.Text(budget) : null; int? id = Input(packet.Integer()); packet.End();
                var key = new ArchiveMemberKey(revision, path, type, occurrence);
                if (source is null || key.CatalogRevision != source.SourceSha256 || !keys.Add(key)) throw Fault();
                edits.Add(new(kind, key, name, id));
            }
            var additions = new List<ArchiveTransformAddition>();
            for (int i = 0; i < additionCount; i++)
            {
                ct.ThrowIfCancellationRequested(); byte[] data = records.Read(ArchiveControlRecord.TransformAddition); budget.AddMetadata(data.Length, 0);
                var packet = new Packet(data); packet.Schema(2); string name = packet.Text(budget); var type = (ArchiveMemberType)packet.Integer();
                int? id = Input(packet.Integer()); bool directory = packet.Flag();
                if ((type == ArchiveMemberType.Directory) != directory || directory && id is not null) throw Fault();
                ArchiveTransformDirectoryMetadata? metadata = directory ? new(new(packet.Long(), packet.Integer()), packet.Integer()) : null;
                packet.End(); additions.Add(metadata is not null ? new(name, metadata) : new(name, type, id));
            }
            if (used.Count != inputs.Count) throw Fault();
            return new(source, inputs, edits.ToArray(), additions.ToArray(), bound, profile);

            int? Input(int id)
            { if (id == 0) return null; if (!inputs.ContainsKey(id) || !used.Add(id)) throw Fault(); return id; }
        }
        catch (ArchiveWriteException ex) when (ex.Reason != ArchiveWriteReason.BudgetExceeded) { throw Fault(); }
    }

    public static byte[] Receipt(ArchiveTransformReceipt receipt)
    {
        Validate(receipt);
        return Encode(w => { w.Write((ushort)1); w.Write(receipt.Length); Digest(w, receipt.ManifestDigest); Digest(w, receipt.ContentDigest); Digest(w, receipt.MetadataDigest); });
    }
    public static ArchiveTransformReceipt ReadReceipt(ReadOnlySpan<byte> data)
    {
        var packet = new Packet(data); packet.Schema(); var result = new ArchiveTransformReceipt(packet.Long(), packet.Digest(), packet.Digest(), packet.Digest()); packet.End();
        try { Validate(result); } catch (ArchiveWriteException) { throw Fault(); } return result;
    }
    private static void Validate(ArchiveTransformReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt); if (receipt.Length is < 9 or > ArchiveWritePolicy.MaxPhysicalBytes) throw Fault();
        ArchiveWritePolicy.ValidateDigest(receipt.ManifestDigest); ArchiveWritePolicy.ValidateDigest(receipt.ContentDigest); ArchiveWritePolicy.ValidateDigest(receipt.MetadataDigest);
    }
    private static byte[] Encode(Action<BinaryWriter> encode)
    { using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) encode(writer); return stream.ToArray(); }
    private static void Text(BinaryWriter writer, string value)
    { byte[] data = ArchiveWritePolicy.Utf8.GetBytes(value); writer.Write(data.Length); writer.Write(data); }
    private static void Digest(BinaryWriter writer, string digest) => writer.Write(Convert.FromHexString(digest));
    private static ArchiveWriteException Fault() => new(ArchiveWriteReason.ProtocolViolation, "The archive worker's transform contract is invalid.");
    internal static int MinimumOutputLength(ArchiveWriteFormat format) => format switch
    { ArchiveWriteFormat.Tar => 1024, ArchiveWriteFormat.TarGzip => 20, ArchiveWriteFormat.TarBzip2 => 14, ArchiveWriteFormat.TarZstd => 9, ArchiveWriteFormat.Rar5 => 24, ArchiveWriteFormat.SevenZip => 32,
      ArchiveWriteFormat.Gzip => 20, ArchiveWriteFormat.Bzip2 => 14, ArchiveWriteFormat.Zstd => 9, _ => throw Fault() };

    private ref struct Packet(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> _data = data;
        private ReadOnlySpan<byte> Take(int count)
        { if (count < 0 || count > _data.Length) throw Fault(); var result = _data[..count]; _data = _data[count..]; return result; }
        public int Integer() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public long Long() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        public ArchiveWriteProfile Profile() => ArchiveWriteProfileEncoding.Read(ref _data);
        public string Digest() => Convert.ToHexString(Take(32));
        public bool Flag() { byte flag = Take(1)[0]; if (flag > 1) throw Fault(); return flag == 1; }
        public void Schema(ushort expected = 1) { if (BinaryPrimitives.ReadUInt16LittleEndian(Take(2)) != expected) throw Fault(); }
        public int Count() { int count = Integer(); if (count is < 0 or > ArchiveWritePolicy.MaxMembers) throw Fault(); return count; }
        public string Text(ArchiveWriteBudget budget)
        {
            int length = Integer(); if (length is <= 0 or > ArchiveWritePolicy.MaxNameBytes) throw Fault(); var bytes = Take(length); int chars;
            try { chars = ArchiveWritePolicy.Utf8.GetCharCount(bytes); } catch (DecoderFallbackException) { throw Fault(); }
            if (chars > ArchiveWritePolicy.MaxNameChars) throw Fault(); budget.AddNameChars(chars); budget.AddMetadata(0, 2L * chars + 128);
            return ArchiveWritePolicy.Utf8.GetString(bytes);
        }
        public void End() { if (_data.Length != 0) throw Fault(); }
    }
}
