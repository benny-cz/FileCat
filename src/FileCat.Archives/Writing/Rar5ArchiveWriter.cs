using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

internal sealed record Rar5InputDescriptor(int Id, long Length, string Sha256);
internal sealed record Rar5WriteEntry(string Name, ArchiveMemberType Type, ArchiveTimestamp Modified, int Mode, int? InputId = null);
internal sealed record Rar5WrittenMember(Rar5WriteEntry Entry, Rar5EncodedContent Content);
internal sealed record Rar5WriteManifest(IReadOnlyList<Rar5WrittenMember> Members, long PhysicalBytes,
    bool Encrypted = false, bool HiddenNames = false);

/// <summary>
/// Stream-only RAR5 creation foundation. A counting pass establishes exact packed length/CRC before the file header;
/// a fresh lease is encoded again and must match the captured SHA and every counting-pass result. Constant codec
/// memory avoids whole-file buffering and noncanonical padded/backpatched variable integers. This has no source
/// pathname, update, publication, recovery or deletion authority and is not registered as a production profile.
/// </summary>
internal static class Rar5ArchiveWriter
{
    private const long FileTimeEpochSeconds = -11644473600;
    private static readonly System.Text.UTF8Encoding Utf8 = new(false, true);

    internal static Rar5WriteManifest Create(Stream stage, IReadOnlyList<Rar5WriteEntry> entries,
        IReadOnlyDictionary<int, Rar5InputDescriptor> inputs, Func<int, Stream> openInput,
        Rar5Compression compression, CancellationToken ct, Rar5EncryptionSession? encryption = null)
    {
        if (!stage.CanWrite || !stage.CanSeek || stage.Position != 0 || stage.Length != 0 ||
            !Enum.IsDefined(compression) || entries.Count > ArchiveWritePolicy.MaxMembers || inputs.Count > ArchiveWritePolicy.MaxMembers)
            throw Invalid("The RAR creation request or stage is invalid.");
        ct.ThrowIfCancellationRequested();
        var budget = new ArchiveWriteBudget();
        budget.AddMetadata(0, checked((long)entries.Count * 256 + (long)inputs.Count * 128));
        // Freeze request collections before opening input grants or touching the stage.
        var members = entries.ToArray();
        var descriptors = inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (id, input) in descriptors)
        {
            if (input is null || id < 0 || id != input.Id || input.Length < 0 || input.Length > ArchiveWritePolicy.MaxPayloadBytes)
                throw Invalid("A RAR input grant has an invalid identity or length.");
            ArchiveWritePolicy.ValidateDigest(input.Sha256);
        }
        var names = new string[members.Length];
        var files = new List<string>();
        for (int index = 0; index < members.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = members[index];
            if (entry is null || !Enum.IsDefined(entry.Type) || (entry.Mode & ~0x1ff) != 0)
                throw Invalid("A RAR member has an unsupported kind or mode.");
            ArchiveWritePolicy.ValidateName(entry.Name, entry.Type);
            int nameBytes = Utf8.GetByteCount(entry.Name);
            if (nameBytes > FileCat.Archives.Rar.RarLimits.MaxNameBytes)
                throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature,
                    "The RAR member name exceeds FileCat's 64 KiB UTF-8 read/write profile. Use a shorter name.");
            ValidateTime(entry.Modified);
            budget.AddMember(); budget.AddNameChars(entry.Name.Length);
            budget.AddMetadata(nameBytes + (encryption is null ? 128L : 192L), entry.Name.Length * 6L + 128);
            names[index] = entry.Type == ArchiveMemberType.Directory && entry.Name.EndsWith('/') ? entry.Name[..^1] : entry.Name;
            if (entry.Type == ArchiveMemberType.File)
            {
                if (entry.InputId is not { } id || !descriptors.TryGetValue(id, out var input))
                    throw Invalid("A RAR regular member needs a captured numeric input grant.");
                budget.AddLogicalBytes(input.Length); files.Add(entry.Name);
            }
            else if (entry.InputId is not null) throw Invalid("A RAR directory cannot carry a payload grant.");
        }
        Array.Sort(names, StringComparer.Ordinal);
        for (int index = 1; index < names.Length; index++)
            if (names[index] == names[index - 1]) throw Invalid("The RAR creation contains duplicate member names or file/folder collisions.");
        foreach (string file in files)
        {
            ct.ThrowIfCancellationRequested();
            string prefix = file + "/";
            int index = Array.BinarySearch(names, prefix, StringComparer.Ordinal);
            if (index < 0) index = ~index;
            if (index < names.Length && names[index].StartsWith(prefix, StringComparison.Ordinal))
                throw Invalid("A regular RAR file would also be used as a folder.");
        }

        if (encryption is { HideNames: false } && !members.Any(member => member.Type == ArchiveMemberType.File))
            throw Invalid("Use encrypted file names to password-protect an archive with no regular members.");
        encryption?.ClaimArchive();
        byte[] header = new byte[ArchiveWritePolicy.MaxNameBytes + 256];
        try
        {
        var sink = new StageSink(stage, ct);
        sink.Write("Rar!\x1a\x07\x01\x00"u8);
        if (encryption?.HideNames == true)
        {
            var crypt = new RecordBuilder(header); crypt.VarInt(0); crypt.VarInt(1); crypt.Byte((byte)encryption.CountPower);
            crypt.Bytes(encryption.HeaderSalt); crypt.Bytes(encryption.HeaderCheck);
            WriteHeader(sink, header, 4, crypt.Length, null, 0);
        }
        var main = new RecordBuilder(header); main.VarInt(0);
        WriteRecord(sink, header, 1, main.Length, null, 0, encryption, ct);
        var manifest = new List<Rar5WrittenMember>(members.Length);
        foreach (var entry in members)
        {
            ct.ThrowIfCancellationRequested();
            Rar5EncodedContent counted;
            Rar5InputDescriptor? input = entry.InputId is { } id ? descriptors[id] : null;
            if (input is not null)
            {
                using var source = Open(input);
                counted = Rar5Encoder.Encode(source, input.Length, Stream.Null, compression, ct);
                if (counted.Sha256 != input.Sha256) throw Changed();
            }
            else counted = new(0, 0, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([])), 0, 0, 0, 0);
            bool encryptedMember = encryption is not null && entry.Type == ArchiveMemberType.File;
            var cipherTarget = encryptedMember ? encryption!.CreateCipher(sink, header: false, counted.PackedLength, ct) : ((ArchiveAesCbcWriteStream Cipher, byte[] Iv)?)null;
            using ArchiveAesCbcWriteStream? cipher = cipherTarget?.Cipher;
            var body = new RecordBuilder(header);
            body.VarInt((ulong)(4 | (entry.Type == ArchiveMemberType.Directory ? 1 : 0)));
            body.VarInt((ulong)counted.Length);
            body.VarInt((ulong)(entry.Mode | (entry.Type == ArchiveMemberType.Directory ? 0x4000 : 0x8000)));
            body.UInt32(encryptedMember ? encryption!.ProtectCrc32(counted.Crc32) : counted.Crc32);
            body.VarInt((ulong)counted.Method << 7); // algorithm version 0, 128 KiB dictionary, non-solid
            body.VarInt(1); // Unix attributes
            int nameBytes = Utf8.GetByteCount(entry.Name);
            body.VarInt((ulong)nameBytes); body.Name(entry.Name, nameBytes);
            int extraStart = body.Length;
            bool unix = entry.Modified.Seconds is >= 0 and <= uint.MaxValue;
            // Exactly one authoritative time record: older readers can overwrite high precision with basic mtime.
            body.VarInt(10); body.VarInt(3); body.VarInt(unix ? 0x13UL : 2UL);
            if (unix)
            {
                body.UInt32((uint)entry.Modified.Seconds); body.UInt32((uint)entry.Modified.Nanoseconds);
            }
            else body.UInt64(checked((ulong)((entry.Modified.Seconds - FileTimeEpochSeconds) * TimeSpan.TicksPerSecond + entry.Modified.Nanoseconds / 100)));
            if (cipherTarget is { } encrypted)
            {
                body.VarInt(48); body.VarInt(1); body.VarInt(0); body.VarInt(3); // encryption extra, version 0, password check + tweaked CRC
                body.Byte((byte)encryption!.CountPower); body.Bytes(encryption.DataSalt); body.Bytes(encrypted.Iv); body.Bytes(encryption.DataCheck);
            }
            long packedLength = encryptedMember ? checked((counted.PackedLength + 15) & ~15L) : counted.PackedLength;
            WriteRecord(sink, header, 2, body.Length, packedLength, body.Length - extraStart, encryption, ct);
            if (input is not null)
            {
                using var source = Open(input);
                var emitted = Rar5Encoder.Encode(source, input.Length, cipher is null ? sink : cipher, compression, ct);
                if (emitted != counted || emitted.Sha256 != input.Sha256) throw Changed();
            }
            if (cipher is not null)
            {
                cipher.Finish();
                if (cipher.PlaintextBytes != counted.PackedLength || cipher.CiphertextBytes != packedLength)
                    throw new ArchiveWriteException(ArchiveWriteReason.VerificationFailed, "The RAR5 cipher changed its declared extent.");
            }
            manifest.Add(new(entry, counted));
        }
        var end = new RecordBuilder(header); end.VarInt(0);
        WriteRecord(sink, header, 5, end.Length, null, 0, encryption, ct);
        return new(manifest.AsReadOnly(), sink.Bytes, encryption is not null, encryption?.HideNames == true);

        Stream Open(Rar5InputDescriptor input)
        {
            ct.ThrowIfCancellationRequested();
            Stream source = openInput(input.Id) ?? throw Invalid("A captured RAR input grant did not open.");
            if (ReferenceEquals(source, stage)) throw Invalid("The RAR stage cannot also be a source grant.");
            try
            {
                if (!source.CanRead || !source.CanSeek || source.Position != 0 || source.Length != input.Length) throw Changed();
                return source;
            }
            catch { source.Dispose(); throw; }
        }
        }
        finally { CryptographicOperations.ZeroMemory(header); }
    }

    private static void ValidateTime(ArchiveTimestamp time)
    {
        if (time.Seconds is >= 0 and <= uint.MaxValue) return;
        if (time.Seconds < FileTimeEpochSeconds || !time.FitsTicks)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This RAR5 timestamp is not exactly representable as Unix nanoseconds or FILETIME.");
    }

    private static void WriteRecord(StageSink sink, byte[] body, int kind, int bodyLength, long? packedLength, int extraLength,
        Rar5EncryptionSession? encryption, CancellationToken ct)
    {
        if (encryption?.HideNames != true) { WriteHeader(sink, body, kind, bodyLength, packedLength, extraLength); return; }
        var target = encryption.CreateCipher(sink, header: true, body.Length + 40L, ct);
        using (target.Cipher)
        {
            sink.Write(target.Iv); WriteHeader(target.Cipher, body, kind, bodyLength, packedLength, extraLength);
            target.Cipher.Finish();
        }
    }

    private static void WriteHeader(Stream sink, byte[] body, int kind, int bodyLength, long? packedLength, int extraLength)
    {
        Span<byte> prefix = stackalloc byte[40];
        int count = 7;
        count += VarInt(prefix[count..], (ulong)kind);
        count += VarInt(prefix[count..], (ulong)((packedLength is null ? 0 : 2) | (extraLength == 0 ? 0 : 1)));
        if (extraLength != 0) count += VarInt(prefix[count..], (ulong)extraLength);
        if (packedLength is { } packed) count += VarInt(prefix[count..], (ulong)packed);
        int payloadPrefix = count - 7, headerLength = checked(payloadPrefix + bodyLength);
        Span<byte> size = stackalloc byte[3]; int sizeCount = VarInt(size, (ulong)headerLength);
        var crc = new ArchiveCrc32(); crc.Append(size[..sizeCount]); crc.Append(prefix.Slice(7, payloadPrefix)); crc.Append(body.AsSpan(0, bodyLength));
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, crc.Value);
        size[..sizeCount].CopyTo(prefix[4..]);
        sink.Write(prefix[..(4 + sizeCount)]); sink.Write(prefix.Slice(7, payloadPrefix)); sink.Write(body.AsSpan(0, bodyLength));

    }

    private static int VarInt(Span<byte> target, ulong value)
    {
        int count = 0;
        do
        {
            if (count >= target.Length) throw Invalid("The RAR header integer exceeded its bound.");
            byte item = (byte)(value & 127); value >>= 7;
            target[count++] = (byte)(item | (value == 0 ? 0 : 128));
        } while (value != 0);
        return count;
    }

    private ref struct RecordBuilder(byte[] buffer)
    {
        private readonly Span<byte> _buffer = buffer;
        internal int Length { get; private set; }
        internal void Byte(byte value) => _buffer[Length++] = value;
        internal void Bytes(ReadOnlySpan<byte> value) { value.CopyTo(_buffer[Length..]); Length += value.Length; }
        internal void VarInt(ulong value) => Length += Rar5ArchiveWriter.VarInt(_buffer[Length..], value);
        internal void UInt32(uint value) { BinaryPrimitives.WriteUInt32LittleEndian(_buffer[Length..], value); Length += 4; }
        internal void UInt64(ulong value) { BinaryPrimitives.WriteUInt64LittleEndian(_buffer[Length..], value); Length += 8; }
        internal void Name(string value, int count)
        {
            if (Utf8.GetBytes(value, _buffer.Slice(Length, count)) != count) throw Invalid("The RAR name length changed.");
            Length += count;
        }
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
                throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The RAR stage exceeds its physical-byte budget.");
            while (!buffer.IsEmpty)
            {
                ct.ThrowIfCancellationRequested();
                int count = Math.Min(buffer.Length, ArchiveIoAuthority.MaximumReadBytes);
                output.Write(buffer[..count]); Bytes += count; buffer = buffer[count..];
            }
        }
        public override void Flush() => output.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidRequest, message);
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The captured RAR input changed between review, counting and encoding.");
}
