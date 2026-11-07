using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Archives;
using SharpCompress.Compressors.LZMA;

namespace FileCat.Archives.Writing;

internal sealed record SevenZipInputDescriptor(int Id, long Length, string Sha256);
internal sealed record SevenZipWriteEntry(string Name, ArchiveMemberType Type, ArchiveTimestamp Modified, int Mode,
    int? InputId = null, ArchiveTimestamp? Created = null, ArchiveTimestamp? Accessed = null);
internal sealed record SevenZipWrittenMember(SevenZipWriteEntry Entry, SevenZipEncodedContent? Content);
internal enum SevenZipHeaderMode { Raw, Lzma2 }
internal sealed record SevenZipWriteManifest(IReadOnlyList<SevenZipWrittenMember> Members, long PhysicalBytes,
    SevenZipHeaderMode HeaderMode = SevenZipHeaderMode.Raw);

/// <summary>
/// Bounded stream-only non-solid 7z creation foundation. Headers are streamed in fixed buffers, then the fixed-size
/// signature is backpatched through the owned seekable stage. No whole archive/header buffer, pathname or publication
/// authority. Raw/LZMA2 headers and the current coder graphs are implementation steps in the required 7z profile.
/// </summary>
internal static class SevenZipArchiveWriter
{
    private const long FileTimeEpochSeconds = -11644473600;
    private static readonly UnicodeEncoding Utf16 = new(false, false, true);

    internal static SevenZipWriteManifest Create(Stream stage, IReadOnlyList<SevenZipWriteEntry> entries,
        IReadOnlyDictionary<int, SevenZipInputDescriptor> inputs, Func<int, Stream> openInput,
        SevenZipCodecOptions options, CancellationToken ct, SevenZipHeaderMode headerMode = SevenZipHeaderMode.Raw)
    {
        SevenZipCodecEncoder.Validate(options);
        if (!Enum.IsDefined(headerMode) || !stage.CanWrite || !stage.CanSeek || stage.Position != 0 || stage.Length != 0 ||
            entries.Count > ArchiveWritePolicy.MaxMembers || inputs.Count > ArchiveWritePolicy.MaxMembers)
            throw Invalid("The 7z creation request or stage is invalid.");
        ct.ThrowIfCancellationRequested();
        var budget = new ArchiveWriteBudget();
        budget.AddMetadata(0, checked(entries.Count * 320L + inputs.Count * 128L));
        var members = entries.ToArray();
        var descriptors = inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (id, input) in descriptors)
        {
            if (input is null || id < 0 || id != input.Id || input.Length < 0 || input.Length > ArchiveWritePolicy.MaxPayloadBytes)
                throw Invalid("A 7z input grant has an invalid identity or length.");
            ArchiveWritePolicy.ValidateDigest(input.Sha256);
        }
        var names = new string[members.Length]; var files = new List<string>();
        for (int index = 0; index < members.Length; index++)
        {
            ct.ThrowIfCancellationRequested(); var entry = members[index];
            if (entry is null || !Enum.IsDefined(entry.Type) || (entry.Mode & ~0x1ff) != 0)
                throw Invalid("A 7z member has an unsupported kind or mode.");
            ArchiveWritePolicy.ValidateName(entry.Name, entry.Type);
            FileTime(entry.Modified);
            if (entry.Created is { } created) FileTime(created);
            if (entry.Accessed is { } accessed) FileTime(accessed);
            budget.AddMember(); budget.AddNameChars(entry.Name.Length);
            budget.AddMetadata(entry.Name.Length * 2L + 256, entry.Name.Length * 6L + 256);
            names[index] = entry.Type == ArchiveMemberType.Directory && entry.Name.EndsWith('/') ? entry.Name[..^1] : entry.Name;
            if (entry.Type == ArchiveMemberType.File)
            {
                if (entry.InputId is not { } id || !descriptors.TryGetValue(id, out var input))
                    throw Invalid("A 7z regular member needs a captured numeric input grant.");
                budget.AddLogicalBytes(input.Length); files.Add(entry.Name);
            }
            else if (entry.InputId is not null) throw Invalid("A 7z directory cannot carry a payload grant.");
        }
        Array.Sort(names, StringComparer.Ordinal);
        for (int index = 1; index < names.Length; index++)
            if (names[index] == names[index - 1]) throw Invalid("The 7z creation contains duplicate member names or file/folder collisions.");
        foreach (string name in files)
        {
            ct.ThrowIfCancellationRequested(); string prefix = name + "/";
            int index = Array.BinarySearch(names, prefix, StringComparer.Ordinal); if (index < 0) index = ~index;
            if (index < names.Length && names[index].StartsWith(prefix, StringComparison.Ordinal))
                throw Invalid("A regular 7z file would also be used as a folder.");
        }

        var sink = new StageSink(stage, ct);
        Span<byte> signature = stackalloc byte[32]; signature.Clear(); sink.Write(signature);
        var manifest = new List<SevenZipWrittenMember>(members.Length);
        foreach (var entry in members)
        {
            ct.ThrowIfCancellationRequested(); SevenZipEncodedContent? content = null;
            if (entry.InputId is { } id)
            {
                var input = descriptors[id];
                Stream lease = openInput(id) ?? throw Invalid("A captured 7z input grant did not open.");
                if (ReferenceEquals(lease, stage)) throw Invalid("The 7z stage cannot also be a source grant.");
                using (lease)
                {
                    if (!lease.CanRead || !lease.CanSeek || lease.Position != 0 || lease.Length != input.Length) throw Changed();
                    if (input.Length == 0)
                    {
                        if (lease.ReadByte() != -1 || input.Sha256 != Convert.ToHexString(SHA256.HashData([]))) throw Changed();
                    }
                    else
                    {
                        long start = stage.Position;
                        content = SevenZipCodecEncoder.Encode(lease, input.Length, sink, options, ct);
                        if (content.Sha256 != input.Sha256 || stage.Position - start != content.PackedLength) throw Changed();
                    }
                }
            }
            manifest.Add(new(entry, content));
        }
        var header = WriteHeader(sink, manifest, headerMode, ct);
        long physical = sink.Bytes;
        ((ReadOnlySpan<byte>)[0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c]).CopyTo(signature); signature[6] = 0; signature[7] = 4;
        BinaryPrimitives.WriteUInt64LittleEndian(signature[12..], checked((ulong)(header.Start - 32)));
        BinaryPrimitives.WriteUInt64LittleEndian(signature[20..], checked((ulong)header.Bytes));
        BinaryPrimitives.WriteUInt32LittleEndian(signature[28..], header.Crc32);
        BinaryPrimitives.WriteUInt32LittleEndian(signature[8..], ArchiveCrc32.Compute(signature[12..]));
        ct.ThrowIfCancellationRequested(); stage.Position = 0; stage.Write(signature); stage.Position = physical;
        if (stage.Length != physical) throw Invalid("The 7z stage extent changed during finalization.");
        return new(manifest.AsReadOnly(), physical, headerMode);
    }

    private static (long Start, long Bytes, uint Crc32) WriteHeader(StageSink sink, List<SevenZipWrittenMember> members,
        SevenZipHeaderMode mode, CancellationToken ct)
    {
        var streams = members.Where(member => member.Content is not null).Select(member => member.Content!).ToArray();
        long packedStart = sink.Bytes;
        if (mode == SevenZipHeaderMode.Raw)
        {
            using var raw = new HeaderWriter(sink, ct); WriteRaw(raw);
            return (packedStart, raw.Bytes, raw.Crc32);
        }
        // Push the frozen header directly through a bounded encoder. No full plaintext/header array or scratch path.
        var packed = new SevenZipCodecEncoder.PackedSink(sink, ct);
        byte[] properties; long rawBytes; uint rawCrc; string rawSha;
        using (var encoder = new Lzma2EncoderStream(packed, 1 << 20, 32))
        using (var raw = new HeaderWriter(encoder, ct))
        {
            properties = (byte[])encoder.Properties.Clone(); WriteRaw(raw);
            rawBytes = raw.Bytes; rawCrc = raw.Crc32; rawSha = raw.FinishDigest();
        }
        packed.Finish();
        if (rawBytes > 64L << 20 && rawBytes / Math.Max(1, packed.Bytes) > 1000)
            throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The compressed 7z header exceeds the reader's expansion policy.");
        var content = new SevenZipEncodedContent(SevenZipCodec.Lzma2, [new([0x21], properties)], [], [rawBytes],
            [new(packed.Bytes, packed.Crc32)], rawBytes, rawCrc, rawSha);
        long start = sink.Bytes;
        using var outer = new HeaderWriter(sink, ct);
        outer.Byte(0x17); // EncodedHeader, followed directly by StreamsInfo (no MainStreamsInfo tag).
        WriteStreams(outer, [content], checked((ulong)(packedStart - 32)));
        outer.Finish();
        return (start, outer.Bytes, outer.Crc32);

        void WriteRaw(HeaderWriter h)
        {
            h.Byte(1);
            if (streams.Length > 0) { h.Byte(4); WriteStreams(h, streams, 0); }
            WriteFiles(h, members); h.Byte(0); h.Finish();
        }
    }

    private static void WriteStreams(HeaderWriter h, IReadOnlyList<SevenZipEncodedContent> contents, ulong packedPosition)
    {
        int packedCount = contents.Sum(content => content.PackedStreams.Count);
        h.Byte(6); h.Number(packedPosition); h.Number((ulong)packedCount); h.Byte(9); // PackInfo, PackPos, count, Size
        foreach (var c in contents) foreach (var stream in c.PackedStreams) h.Number((ulong)stream.Length);
        h.Byte(10); h.Byte(1);
        foreach (var c in contents) foreach (var stream in c.PackedStreams) h.UInt32(stream.Crc32);
        h.Byte(0);
        h.Byte(7); h.Byte(11); h.Number((ulong)contents.Count); h.Byte(0); // UnpackInfo, Folder, count, external=false
        foreach (var c in contents)
        {
            h.Number((ulong)c.Coders.Count);
            foreach (var coder in c.Coders)
            {
                bool complex = coder.Inputs != 1 || coder.Outputs != 1;
                h.Byte((byte)(coder.MethodId.Length | (coder.Properties.Length == 0 ? 0 : 0x20) | (complex ? 0x10 : 0)));
                h.Write(coder.MethodId);
                if (complex) { h.Number((ulong)coder.Inputs); h.Number((ulong)coder.Outputs); }
                if (coder.Properties.Length != 0) { h.Number((ulong)coder.Properties.Length); h.Write(coder.Properties); }
            }
            foreach (var binding in c.Bindings) { h.Number((ulong)binding.Input); h.Number((ulong)binding.Output); }
            if (c.PackedStreams.Count > 1)
                for (int input = 0; input < c.Coders.Sum(coder => coder.Inputs); input++)
                    if (!c.Bindings.Any(binding => binding.Input == input)) h.Number((ulong)input);
        }
        h.Byte(12);
        foreach (var c in contents) foreach (long length in c.UnpackedSizes) h.Number((ulong)length);
        h.Byte(10); h.Byte(1);
        foreach (var c in contents) h.UInt32(c.Crc32);
        h.Byte(0); h.Byte(8); h.Byte(0); h.Byte(0); // Unpack end, default single substream folders, streams end
    }

    private static void WriteFiles(HeaderWriter h, List<SevenZipWrittenMember> members)
    {
        h.Byte(5); h.Number((ulong)members.Count);
        if (members.Count == 0) { h.Byte(0); return; }
        int empty = members.Count(member => member.Content is null);
        if (empty > 0)
        {
            h.Byte(14); h.Number((ulong)((members.Count + 7) / 8)); h.Bits(members.Select(m => m.Content is null));
            h.Byte(15); h.Number((ulong)((empty + 7) / 8));
            h.Bits(members.Where(m => m.Content is null).Select(m => m.Entry.Type == ArchiveMemberType.File));
        }
        long nameBytes = 1;
        foreach (var member in members) nameBytes = checked(nameBytes + member.Entry.Name.Length * 2L + 2);
        h.Byte(17); h.Number((ulong)nameBytes); h.Byte(0);
        foreach (var member in members) { h.Write(Utf16.GetBytes(member.Entry.Name)); h.Byte(0); h.Byte(0); }
        Times(18, member => member.Entry.Created);
        Times(19, member => member.Entry.Accessed);
        Times(20, member => member.Entry.Modified);
        h.Byte(21); h.Number(checked((ulong)members.Count * 4 + 2)); h.Byte(1); h.Byte(0);
        foreach (var member in members)
        {
            bool directory = member.Entry.Type == ArchiveMemberType.Directory;
            uint unix = (uint)(member.Entry.Mode | (directory ? 0x4000 : 0x8000));
            h.UInt32((unix << 16) | 0x8000u | (directory ? 0x10u : 0x20u));
        }
        h.Byte(0);

        void Times(byte kind, Func<SevenZipWrittenMember, ArchiveTimestamp?> select)
        {
            int defined = members.Count(member => select(member) is not null);
            if (defined == 0) return;
            bool all = defined == members.Count;
            long size = checked(2 + defined * 8L + (all ? 0 : (members.Count + 7) / 8));
            h.Byte(kind); h.Number((ulong)size); h.Byte(all ? (byte)1 : (byte)0);
            if (!all) h.Bits(members.Select(member => select(member) is not null));
            h.Byte(0);
            foreach (var member in members) if (select(member) is { } time) h.UInt64(FileTime(time));
        }
    }

    private static ulong FileTime(ArchiveTimestamp time)
    {
        if (time.Seconds < FileTimeEpochSeconds || !time.FitsTicks)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This 7z timestamp cannot be represented exactly as 100 ns FILETIME.");
        return checked((ulong)((time.Seconds - FileTimeEpochSeconds) * TimeSpan.TicksPerSecond + time.Nanoseconds / 100));
    }

    private sealed class HeaderWriter(Stream output, CancellationToken ct) : IDisposable
    {
        private readonly byte[] _pending = new byte[ArchiveIoAuthority.MaximumReadBytes];
        private int _count;
        private ArchiveCrc32 _crc;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        internal long Bytes { get; private set; }
        internal uint Crc32 => _crc.Value;
        internal void Write(ReadOnlySpan<byte> bytes)
        {
            ct.ThrowIfCancellationRequested();
            if (bytes.Length > ArchiveWritePolicy.MaxMetadataBytes - Bytes)
                throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The 7z header exceeded its metadata budget.");
            _crc.Append(bytes); _hash.AppendData(bytes); Bytes += bytes.Length;
            while (!bytes.IsEmpty)
            {
                int count = Math.Min(_pending.Length - _count, bytes.Length);
                bytes[..count].CopyTo(_pending.AsSpan(_count)); _count += count; bytes = bytes[count..];
                if (_count == _pending.Length) Finish();
            }
        }
        internal void Byte(byte value) { Span<byte> bytes = stackalloc byte[1]; bytes[0] = value; Write(bytes); }
        internal void UInt32(uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); Write(bytes); }
        internal void UInt64(ulong value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(bytes, value); Write(bytes); }
        internal void Number(ulong value)
        {
            Span<byte> bytes = stackalloc byte[9]; byte first = 0, mask = 128; int extra = 0;
            for (; extra < 8; extra++)
            {
                if (value < 1UL << (7 * (extra + 1))) { first |= (byte)(value >> (extra * 8)); break; }
                first |= mask; mask >>= 1;
            }
            bytes[0] = first;
            for (int index = 0; index < extra; index++) bytes[index + 1] = (byte)(value >> (index * 8));
            Write(bytes[..(extra + 1)]);
        }
        internal void Bits(IEnumerable<bool> values)
        {
            byte value = 0, mask = 128;
            foreach (bool item in values)
            {
                if (item) value |= mask; mask >>= 1;
                if (mask == 0) { Byte(value); value = 0; mask = 128; }
            }
            if (mask != 128) Byte(value);
        }
        internal void Finish() { ct.ThrowIfCancellationRequested(); if (_count > 0) { output.Write(_pending.AsSpan(0, _count)); _count = 0; } }
        internal string FinishDigest() => Convert.ToHexString(_hash.GetHashAndReset());
        public void Dispose() { _hash.Dispose(); CryptographicOperations.ZeroMemory(_pending); }
    }

    private sealed class StageSink(Stream output, CancellationToken ct) : Stream
    {
        internal long Bytes { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Bytes; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ct.ThrowIfCancellationRequested();
            if (buffer.Length > ArchiveWritePolicy.MaxPhysicalBytes - Bytes)
                throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The 7z stage exceeded its physical-byte budget.");
            output.Write(buffer); Bytes += buffer.Length;
        }
        public override void Flush() => output.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidRequest, message);
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The captured 7z input changed after review.");
}
