using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FileCat.Archives.Rar;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

internal sealed record Rar5RawOwner(ulong Flags, byte[]? UserName, byte[]? GroupName, ulong? UserId, ulong? GroupId);
internal sealed record Rar5RawTimes(ulong Flags, ArchiveTimestamp? Modified, ArchiveTimestamp? Created, ArchiveTimestamp? Accessed);
internal sealed record Rar5PhysicalMember(string Name, ArchiveMemberType Type, int Occurrence, long HeaderOffset,
    long DataOffset, long PackedLength, long Length, uint Attributes, int Host, ulong Compression,
    ArchiveTimestamp? BasicModified, uint? Crc32, Rar5RawTimes? Times, byte[]? Blake2sp,
    Rar5RawOwner? Owner, Rar5CryptoParameters? Encryption)
{
    internal int Method => (int)((Compression >> 7) & 7);
    internal int AlgorithmVersion => (int)(Compression & 63);
    internal long DictionaryBytes
    {
        get
        {
            if (Method == 0) return 0;
            long dictionary = 0x20000L << (int)((Compression >> 10) & 31);
            return AlgorithmVersion == 1 ? dictionary + dictionary / 32 * (long)((Compression >> 15) & 31) : dictionary;
        }
    }
    internal ArchiveTimestamp? Modified => Times?.Modified ?? BasicModified;
}

/// <summary>
/// Complete admitted physical framing, not a content assessment or permission to rewrite. Every payload byte has
/// been read into the physical revision hash, but its plaintext SHA/CRC, compressed end and filter features still
/// require a separate strict decoder pass. Salt/IV/check records stay job-local; this object is never a journal DTO.
/// </summary>
internal sealed record Rar5PhysicalCatalog(IReadOnlyList<Rar5PhysicalMember> Members, string Revision, long Length,
    bool HiddenNames, int? HeaderKdfPower)
{
    internal long RetainedHeapBytes { get; init; }
}

/// <summary>
/// Bounded stream-only observer of RAR5 raw headers, independent from tolerant browsing. Exact optional metadata,
/// occurrences and extents survive observation; unknown/unsupported structures refuse instead of disappearing.
/// No pathname, file opening, publication, deletion or implicit producer-based admission.
/// </summary>
internal static class StrictRar5Framing
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const long FileTimeEpochSeconds = -11644473600;

    internal static Rar5PhysicalCatalog Read(Stream input, CancellationToken ct, Rar5ReadPasswordSession? password = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek || input.Position != 0 || input.Length < 8)
            throw Invalid("The RAR framing source grant is invalid.");
        if (input.Length > ArchiveWritePolicy.MaxPhysicalBytes) throw Limit();
        ct.ThrowIfCancellationRequested();
        using var source = new Forward(input, ct);
        var budget = new ArchiveWriteBudget();
        // Reserve the peak owned raw/decrypted/cipher header arrays and the fixed streaming buffer up front.
        budget.AddMetadata(0, 3L * ArchiveWritePolicy.MaxEntryMetadataBytes + (128 << 10));
        var members = new List<Rar5PhysicalMember>();
        var occurrences = new Dictionary<(string, ArchiveMemberType), int>();
        Rar5CryptoParameters? headers = null;
        bool main = false; int records = 0;
        try
        {
            Span<byte> signature = stackalloc byte[8]; source.ReadExactly(signature);
            if (!signature.SequenceEqual((ReadOnlySpan<byte>)[0x52, 0x61, 0x72, 0x21, 0x1a, 7, 1, 0]))
                throw Unsupported("This source is not an unprefixed RAR5 container.");
            while (source.Position < source.Length)
            {
                ct.ThrowIfCancellationRequested();
                if (++records > ArchiveWritePolicy.MaxMembers + 3) throw Limit();
                long position = source.Position; byte[]? raw = null;
                try
                {
                    int sizeBytes, size;
                    if (headers is null) raw = PlainHeader(source, budget, out sizeBytes, out size);
                    else
                    {
                        raw = password!.ReadHeader(source, position, headers, out sizeBytes, out size, out long end, ct,
                            ArchiveWritePolicy.MaxEntryMetadataBytes, bytes => budget.AddMetadata(bytes, 0));
                        if (end != source.Position) throw Invalid("The encrypted RAR header changed its physical extent.");
                    }
                    var fields = new RarHeaderReader(raw.AsSpan(4 + sizeBytes, size));
                    int kind = fields.Int(int.MaxValue); ulong flags = fields.VarInt();
                    if ((flags & ~7UL) != 0) throw Unsupported("RAR volume, dependent or unknown header flags cannot be preserved.");
                    long extraLength = (flags & 1) == 0 ? 0 : fields.Size();
                    long packed = (flags & 2) == 0 ? 0 : fields.Size();
                    if (extraLength > fields.Remaining || packed > source.Length - source.Position)
                        throw Invalid("The RAR header or payload extends beyond its source grant.");
                    var body = new RarHeaderReader(fields.Bytes(fields.Remaining - extraLength));
                    var extras = fields.Rest();
                    if (kind == 4)
                    {
                        if (main || headers is not null || records != 1 || packed != 0 || extraLength != 0 || (flags & 3) != 0)
                            throw Invalid("The RAR encryption header is misplaced or has unsupported extents.");
                        headers = Rar5CryptoParameters.Parse(body.Rest(), header: true);
                        if (password is null) throw Unsupported("A password is required to assess encrypted RAR headers.");
                        password.Keys(headers);
                    }
                    else if (kind == 1)
                    {
                        if (main || members.Count != 0 || packed != 0 || extraLength != 0 || (flags & 3) != 0)
                            throw Unsupported("Repeated RAR main headers or archive metadata cannot be preserved by this profile.");
                        if (body.VarInt() != 0) throw Unsupported("RAR volume, solid, recovery or locked archive features cannot be preserved.");
                        End(body); main = true;
                    }
                    else if (kind == 2)
                    {
                        if (!main) throw Invalid("A RAR member precedes the main header.");
                        budget.AddMember();
                        var member = Member(ref body, extras, position, source.Position, packed, budget, occurrences);
                        members.Add(member); source.Drain(packed);
                    }
                    else if (kind == 5)
                    {
                        if (!main || packed != 0 || extraLength != 0 || (flags & 3) != 0)
                            throw Invalid("The RAR end header is misplaced or has unexpected data.");
                        if (body.VarInt() != 0) throw Unsupported("A continuing RAR volume cannot be rewritten by this profile.");
                        End(body); source.End();
                        return new(members.AsReadOnly(), source.Digest(), source.Length, headers is not null, headers?.CountPower)
                            { RetainedHeapBytes = budget.RetainedHeapBytes };
                    }
                    else throw Unsupported("RAR service, recovery or unknown records require a preservation implementation.");
                }
                finally { if (raw is not null) CryptographicOperations.ZeroMemory(raw); }
            }
            throw Invalid("The RAR container is missing its end header.");
        }
        catch (ArchiveWriteException) { throw; }
        catch (InvalidDataException ex) when (ex.InnerException is ArchiveWriteException) { throw (ArchiveWriteException)ex.InnerException; }
        catch (InvalidDataException ex) when (RarData.IsUnsupported(ex)) { throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The RAR framing uses an unsupported feature.", ex); }
        catch (InvalidDataException ex) { throw new ArchiveWriteException(ArchiveWriteReason.InvalidArchive, "The RAR framing or password check is invalid.", ex); }
        catch (EndOfStreamException ex) { throw new ArchiveWriteException(ArchiveWriteReason.InvalidArchive, "The RAR framing ends unexpectedly.", ex); }
    }

    private static byte[] PlainHeader(Forward source, ArchiveWriteBudget budget, out int sizeBytes, out int size)
    {
        Span<byte> prefix = stackalloc byte[7]; source.ReadExactly(prefix[..4]); size = 0; sizeBytes = 0;
        while (true)
        {
            if (sizeBytes == 3) throw Invalid("The RAR header size exceeds its encoded field bound.");
            int value = source.ReadByte(); if (value < 0) throw Invalid("The RAR header ends inside its size field.");
            prefix[4 + sizeBytes] = (byte)value; size |= (value & 127) << (7 * sizeBytes++);
            if ((value & 128) == 0) break;
        }
        if (size < 2) throw Invalid("The RAR header size is invalid.");
        if (size > ArchiveWritePolicy.MaxEntryMetadataBytes) throw Limit();
        int length = checked(4 + sizeBytes + size);
        if (size > source.Length - source.Position) throw Invalid("The RAR header extends beyond its source grant.");
        budget.AddMetadata(length, 0); byte[] raw = new byte[length];
        try
        {
            prefix[..(4 + sizeBytes)].CopyTo(raw); source.ReadExactly(raw.AsSpan(4 + sizeBytes));
            if (ArchiveCrc32.Compute(raw.AsSpan(4)) != BinaryPrimitives.ReadUInt32LittleEndian(raw))
                throw Invalid("The RAR header checksum does not match.");
            return raw;
        }
        catch { CryptographicOperations.ZeroMemory(raw); throw; }
    }

    private static Rar5PhysicalMember Member(ref RarHeaderReader body, ReadOnlySpan<byte> extra, long headerOffset,
        long dataOffset, long packed, ArchiveWriteBudget budget, Dictionary<(string, ArchiveMemberType), int> occurrences)
    {
        ulong flags = body.VarInt();
        if ((flags & ~15UL) != 0 || (flags & 8) != 0) throw Unsupported("Unknown RAR member flags or unknown unpacked sizes are not admitted.");
        long length = body.Size();
        if (length > ArchiveWritePolicy.MaxPayloadBytes) throw Limit();
        var type = (flags & 1) != 0 ? ArchiveMemberType.Directory : ArchiveMemberType.File;
        if (type == ArchiveMemberType.Directory && (length != 0 || packed != 0)) throw Invalid("A RAR directory carries unexpected payload bytes.");
        budget.AddLogicalBytes(length);
        ulong attr = body.VarInt(); if (attr > uint.MaxValue) throw Unsupported("The RAR attributes exceed the understood field width.");
        ArchiveTimestamp? basic = (flags & 2) == 0 ? null : new(body.UInt32(), 0);
        uint? crc = (flags & 4) == 0 ? null : body.UInt32();
        ulong compression = body.VarInt(); int algorithm = (int)(compression & 63), method = (int)((compression >> 7) & 7);
        int dictionary = (int)((compression >> 10) & 31);
        if (algorithm > 1 || method > 5 || (compression & 64) != 0 ||
            algorithm == 0 && ((compression & ~0x7fffUL) != 0 || dictionary > 15) ||
            algorithm == 1 && ((compression & ~0x1fffffUL) != 0 || dictionary > 23))
            throw Unsupported("The RAR algorithm, solid state or compression properties are not understood by this profile.");
        int host = body.Int(int.MaxValue);
        if (host is not (0 or 1)) throw Unsupported("The RAR host attribute layout is unknown.");
        if (host == 1 && ((attr >> 12) & 15) is not (0 or 4 or 8)) throw Unsupported("RAR special-file attributes are not admitted.");
        if (host == 1 && (attr & 0xf000) != 0 && (attr & 0xf000) != (type == ArchiveMemberType.Directory ? 0x4000UL : 0x8000UL))
            throw Invalid("The RAR member kind and Unix attributes disagree.");
        int nameBytes = body.Int(Math.Min(RarLimits.MaxNameBytes, ArchiveWritePolicy.MaxNameBytes));
        var encodedName = body.Bytes(nameBytes); int characters;
        try { characters = Utf8.GetCharCount(encodedName); }
        catch (DecoderFallbackException) { throw Invalid("The RAR member name is not exact UTF-8."); }
        budget.AddNameChars(characters); budget.AddMetadata(0, checked(2L * nameBytes + 768));
        if (characters > ArchiveWritePolicy.MaxNameChars) throw Limit();
        string name = Utf8.GetString(encodedName);
        try { ArchiveWritePolicy.ValidateName(name, type); }
        catch (ArchiveWriteException ex) when (ex.Reason == ArchiveWriteReason.InvalidRequest)
        { throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The RAR member name cannot be preserved by the portable write profile.", ex); }
        if (name.Contains('\ufffe')) throw Unsupported("Mapped native RAR names are not portable across the supported write platforms.");
        End(body);
        Rar5CryptoParameters? crypto = null; Rar5RawTimes? times = null; Rar5RawOwner? owner = null; byte[]? blake = null;
        var extras = new RarHeaderReader(extra); int seen = 0;
        while (extras.Remaining != 0)
        {
            long count = extras.Size(); if (count is < 1 or > ArchiveWritePolicy.MaxEntryMetadataBytes) throw Invalid("The RAR extra-record extent is invalid.");
            var record = new RarHeaderReader(extras.Bytes(count)); int kind = record.Int(int.MaxValue);
            if (kind is not (1 or 2 or 3 or 6)) throw Unsupported("A RAR extra record needs additional preservation support.");
            if ((seen & (1 << kind)) != 0) throw Invalid("A RAR extra record is repeated ambiguously.");
            seen |= 1 << kind;
            if (kind == 1)
            {
                if (type != ArchiveMemberType.File) throw Unsupported("Encrypted RAR directory records are not admitted.");
                budget.AddMetadata(0, 256); crypto = Rar5CryptoParameters.Parse(record.Rest(), header: false);
                if ((packed & 15) != 0) throw Invalid("Encrypted RAR member data is not block aligned.");
            }
            else if (kind == 2)
            {
                if (record.VarInt() != 0) throw Unsupported("The RAR extra hash type is unknown.");
                budget.AddMetadata(0, 64); blake = record.Bytes(32).ToArray(); End(record);
            }
            else if (kind == 3) times = Times(ref record);
            else owner = Owner(ref record, budget);
        }
        if (method == 0 && (crypto is null ? packed != length : packed < length || packed - length > 15))
            throw Invalid("Stored RAR member extents disagree.");
        int occurrence = occurrences.GetValueOrDefault((name, type)); occurrences[(name, type)] = occurrence + 1;
        return new(name, type, occurrence, headerOffset, dataOffset, packed, length, (uint)attr, host, compression, basic,
            crc, times, blake, owner, crypto);
    }

    private static Rar5RawTimes Times(ref RarHeaderReader record)
    {
        ulong flags = record.VarInt();
        if ((flags & ~31UL) != 0 || (flags & 16) != 0 && (flags & 1) == 0) throw Unsupported("The RAR time flags are not understood.");
        bool unix = (flags & 1) != 0;
        ArchiveTimestamp? modified = (flags & 2) == 0 ? null : Time(ref record, unix);
        ArchiveTimestamp? created = (flags & 4) == 0 ? null : Time(ref record, unix);
        ArchiveTimestamp? accessed = (flags & 8) == 0 ? null : Time(ref record, unix);
        // All whole-second fields precede all fractional fields in the RAR layout.
        if ((flags & 16) != 0)
        {
            if (modified is { } m) modified = new(m.Seconds, Nanos(ref record));
            if (created is { } c) created = new(c.Seconds, Nanos(ref record));
            if (accessed is { } a) accessed = new(a.Seconds, Nanos(ref record));
        }
        End(record); return new(flags, modified, created, accessed);
    }

    private static ArchiveTimestamp Time(ref RarHeaderReader reader, bool unix)
    {
        if (unix) return new(reader.UInt32(), 0);
        ulong ticks = reader.UInt64(); return new(checked((long)(ticks / 10_000_000) + FileTimeEpochSeconds), (int)(ticks % 10_000_000) * 100);
    }
    private static int Nanos(ref RarHeaderReader reader)
    { uint value = reader.UInt32(); if (value >= 1_000_000_000) throw Invalid("The RAR nanosecond field is invalid."); return (int)value; }

    private static Rar5RawOwner Owner(ref RarHeaderReader record, ArchiveWriteBudget budget)
    {
        ulong flags = record.VarInt(); if ((flags & ~15UL) != 0) throw Unsupported("The RAR owner flags are unknown.");
        byte[]? user = (flags & 1) == 0 ? null : NativeName(ref record, budget);
        byte[]? group = (flags & 2) == 0 ? null : NativeName(ref record, budget);
        ulong? uid = (flags & 4) == 0 ? null : record.VarInt(), gid = (flags & 8) == 0 ? null : record.VarInt();
        End(record); return new(flags, user, group, uid, gid);
    }
    private static byte[] NativeName(ref RarHeaderReader record, ArchiveWriteBudget budget)
    {
        int length = record.Int(ArchiveWritePolicy.MaxEntryMetadataBytes); budget.AddMetadata(0, checked(length + 32L));
        return record.Bytes(length).ToArray(); // Native encoding is unspecified: preserve exact bytes and presence.
    }
    private static void End(RarHeaderReader reader)
    { if (reader.Remaining != 0) throw Unsupported("The RAR record has trailing fields without a preservation contract."); }

    internal sealed class Forward : Stream
    {
        private readonly Stream input;
        private readonly CancellationToken ct;
        private readonly long length;
        private readonly IncrementalHash hash;
        private readonly byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
        private long position;
        internal Forward(Stream input, CancellationToken ct)
        { this.input = input; this.ct = ct; length = input.Length; hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); }
        public override int Read(Span<byte> bytes)
        {
            ct.ThrowIfCancellationRequested(); int wanted = (int)Math.Min(Math.Min(bytes.Length, ArchiveIoAuthority.MaximumReadBytes), length - position);
            if (wanted == 0) return 0;
            int count = input.Read(bytes[..wanted]); if (count < 0 || count > wanted) throw Invalid("The RAR source violated its read grant.");
            hash.AppendData(bytes[..count]); position += count; return count;
        }
        public override int Read(byte[] bytes, int offset, int count) => Read(bytes.AsSpan(offset, count));
        public override int ReadByte() { Span<byte> value = stackalloc byte[1]; return Read(value) == 0 ? -1 : value[0]; }
        internal void Drain(long count)
        {
            while (count != 0) { int read = Read(buffer.AsSpan(0, (int)Math.Min(count, buffer.Length))); if (read == 0) throw Invalid("The RAR payload ends early."); count -= read; }
        }
        internal void End()
        {
            ct.ThrowIfCancellationRequested(); if (position != length) throw Unsupported("Trailing RAR data cannot be preserved by this profile.");
            Span<byte> extra = stackalloc byte[1];
            try
            {
                if (input.Read(extra) != 0 || input.Length != length)
                    throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The RAR source changed during its framing pass.");
            }
            finally { CryptographicOperations.ZeroMemory(extra); }
        }
        internal string Digest() => Convert.ToHexString(hash.GetHashAndReset());
        protected override void Dispose(bool disposing)
        { if (disposing) { hash.Dispose(); CryptographicOperations.ZeroMemory(buffer); } base.Dispose(disposing); }
        public override long Length => length;
        public override long Position { get => position; set { if (value != position) throw Invalid("The RAR framing reader cannot seek outside its forward grant."); } }
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] bytes, int offset, int count) => throw new NotSupportedException();
    }

    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidArchive, message);
    private static ArchiveWriteException Unsupported(string message) => new(ArchiveWriteReason.UnsupportedFeature, message);
    private static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The RAR framing metadata exceeds the write profile budget.");
}
