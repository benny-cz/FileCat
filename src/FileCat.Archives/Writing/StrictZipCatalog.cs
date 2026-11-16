using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>One admitted member with what the writer needs to copy its original compressed bytes.</summary>
internal sealed record ZipWriteMember(string Name, ArchiveMemberType Type, int Occurrence, long Length, string Sha256, byte[] Metadata,
    ushort Method, ushort Flags, uint Crc, long CompressedLength, long DataOffset);

internal sealed record ZipWriteCatalog(string Revision, long Length, byte[] Comment, IReadOnlyList<ZipWriteMember> Members);

/// <summary>
/// Strict assessment of a complete ZIP for the worker profile (ZIP_WORKER_PROFILE_DESIGN.md): one end record, optional ZIP64
/// records, contiguous local records in central-directory order from offset zero, agreeing local and central headers, only
/// stored or Deflate data, only the admitted extra fields, and every member decoded with its CRC checked. A single
/// sequential pass hashes the whole file. The result must also agree with .NET's own ZIP reader, which browsing uses.
/// </summary>
internal static class StrictZipCatalog
{
    internal const uint LocalSignature = 0x04034b50, CentralSignature = 0x02014b50, EndSignature = 0x06054b50,
        Zip64EndSignature = 0x06064b50, Zip64LocatorSignature = 0x07064b50, DescriptorSignature = 0x08074b50;
    internal const ushort Zip64Extra = 0x0001, NtfsExtra = 0x000a, TimeExtra = 0x5455, OwnerExtra = 0x7875;
    internal const ushort AllowedFlags = 0x0002 | 0x0004 | 0x0008 | 0x0800;

    private sealed record Central(byte[] NameBytes, string Name, ArchiveMemberType Type, ushort MadeBy, ushort Needed, ushort Flags, ushort Method,
        ushort Time, ushort Date, uint Crc, long Compressed, long Uncompressed, long LocalOffset, ushort Internal, uint External,
        Extras Extras, string Comment);

    private sealed record Extras(bool Zip64, byte TimeFlags, int Modified, int Accessed, int Created, ArchiveZipNtfsTimes? Ntfs,
        ArchiveZipOwner? Owner, bool OwnerPresent);

    internal static ZipWriteCatalog Read(Stream source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source); ct.ThrowIfCancellationRequested();
        if (!source.CanRead || !source.CanSeek) throw Invalid("A strict ZIP assessment needs a complete, seekable source.");
        long length = source.Length;
        if (length > ArchiveWritePolicy.MaxPhysicalBytes) throw ArchiveWriteBudget.Limit();
        if (length < 22) throw Invalid("The ZIP has no complete end record.");
        var budget = new ArchiveWriteBudget();

        // The end region, read once: the end record (exactly one candidate whose comment reaches the physical end) and the
        // 76 bytes a ZIP64 record and locator occupy right before it. Every byte of the source is fetched once in total.
        int regionLength = (int)Math.Min(length, 76 + 22 + ushort.MaxValue);
        long regionStart = length - regionLength;
        byte[] region = ReadAt(source, regionStart, regionLength, ct);
        int end = -1;
        for (int i = regionLength - 22; i >= Math.Max(0, regionLength - 22 - ushort.MaxValue); i--)
        {
            if (U32(region, i) != EndSignature || U16(region, i + 20) != regionLength - i - 22) continue;
            if (end >= 0) throw Unsupported("The ZIP has more than one possible end record.");
            end = i;
        }
        if (end < 0) throw Invalid("The ZIP end record is missing or does not reach the end of the file.");
        // Readers such as .NET's take the end signature nearest the end of the file without checking that its comment reaches
        // the end, so a decoy end record (with its own central directory) in the comment, or a signature formed by the end
        // record's own fields, would make them read different members than this catalog. No record signature may follow.
        for (int i = end + 1; i + 4 <= regionLength; i++)
        {
            uint signature = U32(region, i); bool inComment = i >= end + 22;
            if (signature == EndSignature && (inComment || i <= regionLength - 22) ||
                inComment && (signature == Zip64EndSignature || signature == Zip64LocatorSignature))
                throw Unsupported("Another ZIP record signature follows the end record, so ZIP readers could disagree about this archive.");
        }
        long endOffset = regionStart + end;
        ushort disk = U16(region, end + 4), directoryDisk = U16(region, end + 6), entriesHere = U16(region, end + 8), entriesTotal = U16(region, end + 10);
        uint size32 = U32(region, end + 12), offset32 = U32(region, end + 16);
        byte[] comment = region.AsSpan(end + 22).ToArray();
        if (disk != 0 || directoryDisk != 0) throw Unsupported("Split or spanned ZIP archives are not admitted.");
        long entries = entriesTotal, directorySize = size32, directoryOffset = offset32, directoryEnd = endOffset;
        if (end >= 20 && U32(region, end - 20) == Zip64LocatorSignature)
        {
            var locator = region.AsSpan(end - 20, 20);
            {
                if (U32(locator, 4) != 0 || U32(locator, 16) != 1) throw Unsupported("Split or spanned ZIP archives are not admitted.");
                ulong recordOffset = U64(locator, 8);
                // Version-1 records only: extensible data (or anything between the record and its locator) is not admitted.
                if (end < 76 || recordOffset != (ulong)(endOffset - 76)) throw Unsupported("The ZIP64 end record is misplaced or extended.");
                var record = region.AsSpan(end - 76, 56);
                ushort needed = U16(record, 14);
                if (U32(record, 0) != Zip64EndSignature || U64(record, 4) != 44 || needed >> 8 != 0 || (needed & 0xff) > 63 ||
                    U32(record, 16) != 0 || U32(record, 20) != 0 || U64(record, 24) != U64(record, 32))
                    throw Unsupported("The ZIP64 end record is outside this profile.");
                ulong entries64 = U64(record, 32), size64 = U64(record, 40), offset64 = U64(record, 48);
                if (entries64 > ArchiveWritePolicy.MaxMembers) throw ArchiveWriteBudget.Limit();
                if (size64 > (ulong)length || offset64 > (ulong)length) throw Invalid("The ZIP64 end record points outside the file.");
                if (entriesHere != 0xffff && entriesHere != entries64 || entriesTotal != 0xffff && entriesTotal != entries64 ||
                    size32 != uint.MaxValue && size32 != size64 || offset32 != uint.MaxValue && offset32 != offset64)
                    throw Invalid("The ZIP end records disagree.");
                entries = (long)entries64; directorySize = (long)size64; directoryOffset = (long)offset64; directoryEnd = (long)recordOffset;
            }
        }
        else if (entriesHere != entriesTotal) throw Unsupported("Split or spanned ZIP archives are not admitted.");
        if (entries > ArchiveWritePolicy.MaxMembers) throw ArchiveWriteBudget.Limit();
        if (directorySize > ArchiveWritePolicy.MaxMetadataBytes) throw ArchiveWriteBudget.Limit();
        if (directoryOffset > directoryEnd || directoryEnd - directoryOffset != directorySize)
            throw Unsupported("The ZIP central directory does not end where the end records begin.");
        // The whole directory is one archive-wide record set (the directory and the trailer copy), not one entry's metadata.
        budget.AddAggregateMetadata(directorySize, checked(2 * directorySize));
        // The directory's bytes already in the end region are reused; only the part before it is fetched.
        byte[] directory = new byte[directorySize];
        long fetched = Math.Max(0, Math.Min(directoryEnd, regionStart) - directoryOffset);
        if (fetched > 0) ReadAt(source, directoryOffset, (int)fetched, ct).CopyTo(directory, 0);
        region.AsSpan((int)(directoryOffset + fetched - regionStart), (int)(directorySize - fetched)).CopyTo(directory.AsSpan((int)fetched));

        var centrals = new List<Central>((int)Math.Min(entries, 4096));
        int at = 0;
        for (long i = 0; i < entries; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = ParseCentral(directory, ref at, budget);
            if (entry.LocalOffset >= directoryOffset) throw Invalid("A ZIP member starts inside or after the central directory.");
            centrals.Add(entry);
        }
        if (at != directory.Length) throw Unsupported("The ZIP central directory has bytes that belong to no member.");

        source.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var reader = new SequentialReader(source, hash, directoryOffset, ct);
        var members = new List<ZipWriteMember>(centrals.Count);
        var occurrences = new Dictionary<(string, ArchiveMemberType), int>();
        byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
        try
        {
            for (int i = 0; i < centrals.Count; i++)
            {
                ct.ThrowIfCancellationRequested(); var entry = centrals[i];
                if (reader.Position != entry.LocalOffset) throw Unsupported("ZIP member records are not contiguous in central-directory order.");
                var observed = ReadLocal(reader, entry);
                long dataOffset = reader.Position;
                var (sha, crc) = Decode(reader, entry, buffer, ct);
                if (crc != entry.Crc) throw Invalid("A ZIP member's checksum does not match its data.");
                if ((entry.Flags & 8) != 0) ReadDescriptor(reader, entry, observed.Zip64, i + 1 < centrals.Count ? centrals[i + 1].LocalOffset : directoryOffset);
                var key = (entry.Name, entry.Type); int occurrence = occurrences.GetValueOrDefault(key); occurrences[key] = occurrence + 1;
                ArchiveZipMetadata metadata;
                try
                {
                    metadata = new(entry.MadeBy, entry.Internal, entry.External, entry.Time, entry.Date, observed.TimeFlags,
                        observed.Modified, observed.Accessed, observed.Created, observed.Ntfs, observed.Owner, entry.Comment);
                }
                catch (ArchiveWriteException ex) when (ex.Reason == ArchiveWriteReason.UnsupportedFeature) { throw Unsupported("A ZIP member's host or attributes are outside this profile."); }
                members.Add(new(entry.Name, entry.Type, occurrence, entry.Uncompressed, sha, metadata.Encode(), entry.Method,
                    (ushort)(entry.Flags & 6), entry.Crc, entry.Compressed, dataOffset));
            }
            if (reader.Position != directoryOffset) throw Unsupported("The ZIP has bytes between its last member and its central directory.");
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
        // The revision covers the whole file in order: the walked records, then the directory and end records as parsed.
        hash.AppendData(directory); hash.AppendData(region, (int)(directoryEnd - regionStart), (int)(length - directoryEnd));
        string revision = Convert.ToHexString(hash.GetHashAndReset());
        byte[] trailer = new byte[checked(directorySize + (length - directoryEnd))];
        directory.CopyTo(trailer, 0); region.AsSpan((int)(directoryEnd - regionStart)).CopyTo(trailer.AsSpan((int)directorySize));
        CompareDecoder(new TrailerView(length, directoryOffset, trailer), members, ct);
        return new(revision, length, comment, members);
    }

    internal static ArchiveWriteManifest Manifest(ArchiveWriteProfile profile, ZipWriteCatalog catalog, CancellationToken ct)
    {
        _ = ArchiveZipFeatures.Level(profile);
        var members = new List<ArchiveManifestMember>(catalog.Members.Count);
        foreach (var member in catalog.Members)
        {
            ct.ThrowIfCancellationRequested();
            members.Add(new(member.Name, member.Type, member.Occurrence, member.Length, member.Sha256, member.Metadata));
        }
        return new(profile, members, ArchiveZipFeatures.Create(catalog.Comment));
    }

    private static Central ParseCentral(byte[] d, ref int at, ArchiveWriteBudget budget)
    {
        if (d.Length - at < 46 || U32(d, at) != CentralSignature) throw Invalid("A ZIP central-directory record is damaged.");
        ushort madeBy = U16(d, at + 4), needed = U16(d, at + 6), flags = U16(d, at + 8), method = U16(d, at + 10), time = U16(d, at + 12), date = U16(d, at + 14);
        uint crc = U32(d, at + 16), compressed32 = U32(d, at + 20), uncompressed32 = U32(d, at + 24);
        int nameLength = U16(d, at + 28), extraLength = U16(d, at + 30), commentLength = U16(d, at + 32);
        ushort diskStart = U16(d, at + 34), internalAttributes = U16(d, at + 36); uint external = U32(d, at + 38), offset32 = U32(d, at + 42);
        if (d.Length - at - 46 < nameLength + extraLength + commentLength) throw Invalid("A ZIP central-directory record is truncated.");
        var nameBytes = d.AsSpan(at + 46, nameLength); var extra = d.AsSpan(at + 46 + nameLength, extraLength);
        var commentBytes = d.AsSpan(at + 46 + nameLength + extraLength, commentLength);
        at += 46 + nameLength + extraLength + commentLength;
        if ((flags & 0x41) != 0) throw Unsupported("Encrypted ZIP members are not admitted.");
        if ((flags & ~AllowedFlags) != 0) throw Unsupported("A ZIP member uses a general-purpose flag outside this profile.");
        if (method is not (0 or 8)) throw Unsupported("A ZIP member uses a compression method other than stored or Deflate.");
        if (needed >> 8 != 0 || (needed & 0xff) > 63) throw Unsupported("A ZIP member needs a newer ZIP version than this profile.");
        if (nameLength == 0) throw Unsupported("A ZIP member has no name.");
        // A saturated disk number (0xFFFF with a ZIP64 disk field) is legal ZIP64 but .NET's reader then refuses the member as
        // spanned; this writer never needs it, so only disk 0 is admitted.
        if (diskStart != 0) throw Unsupported("Split or spanned ZIP archives are not admitted.");
        var extras = ParseExtras(extra, central: true);
        // ZIP64 fields appear exactly for the header fields that are saturated, in order.
        int expected = (uncompressed32 == uint.MaxValue ? 8 : 0) + (compressed32 == uint.MaxValue ? 8 : 0) + (offset32 == uint.MaxValue ? 8 : 0);
        long uncompressed = uncompressed32, compressed = compressed32, offset = offset32;
        if (expected == 0) { if (extras.Zip64) throw Unsupported("A ZIP64 extra field is present without a saturated header field."); }
        else
        {
            byte[] zip64 = Zip64Data(extra);
            if (zip64.Length != expected) throw Unsupported("A ZIP64 extra field does not match the saturated header fields.");
            int z = 0;
            if (uncompressed32 == uint.MaxValue) { uncompressed = Long(zip64, z); z += 8; }
            if (compressed32 == uint.MaxValue) { compressed = Long(zip64, z); z += 8; }
            if (offset32 == uint.MaxValue) { offset = Long(zip64, z); z += 8; }
        }
        string name = Text(nameBytes, (flags & 0x800) != 0, comment: false);
        string comment = Text(commentBytes, (flags & 0x800) != 0, comment: true);
        var type = name.EndsWith('/') ? ArchiveMemberType.Directory : ArchiveMemberType.File;
        try { ArchiveWritePolicy.ValidateName(name, type); }
        catch (ArchiveWriteException) { throw Unsupported("A ZIP member name is outside the archive name rules."); }
        if (uncompressed > ArchiveWritePolicy.MaxPayloadBytes) throw ArchiveWriteBudget.Limit();
        // A folder holds no data. Java writes an empty Deflate stream (two bytes) for folders; anything larger would be copied
        // forward as dead weight the plan's output bound does not count, so it is refused.
        if (type == ArchiveMemberType.Directory && (uncompressed != 0 || compressed > 8)) throw Unsupported("A ZIP folder entry holds data.");
        if (!ArchiveZipMetadata.AttributesFit(external, type))
            throw Unsupported("A ZIP member's attributes describe a link, device or other special file, or disagree with its name.");
        if (method == 0 && compressed != uncompressed || method == 8 && compressed < 2) throw Invalid("A ZIP member's sizes do not fit its method.");
        if (uncompressed > 64L << 20 && uncompressed > Math.Max(1, compressed) * 1000) throw ArchiveWriteBudget.Limit();
        budget.AddMember(); budget.AddLogicalBytes(uncompressed); budget.AddNameChars(name.Length);
        budget.AddMetadata(nameLength + commentLength + 96L, 2L * name.Length + 2L * comment.Length + 640L);
        return new(nameBytes.ToArray(), name, type, madeBy, needed, flags, method, time, date, crc, compressed, uncompressed, offset,
            internalAttributes, external, extras, comment);
    }

    /// <summary>
    /// Checks the local header against the central record and returns the local extra fields: the local extended timestamp can
    /// hold access and creation times, and Info-ZIP writes owner IDs only locally. A ZIP64 local field makes descriptor sizes
    /// eight bytes wide.
    /// </summary>
    private static Extras ReadLocal(SequentialReader reader, Central entry)
    {
        var header = reader.Take(30);
        if (U32(header, 0) != LocalSignature) throw Invalid("A ZIP local header is missing.");
        if (U16(header, 4) != entry.Needed || U16(header, 6) != entry.Flags || U16(header, 8) != entry.Method || U16(header, 10) != entry.Time ||
            U16(header, 12) != entry.Date) throw Invalid("A ZIP member's local and central headers disagree.");
        uint crc = U32(header, 14), compressed32 = U32(header, 18), uncompressed32 = U32(header, 22);
        int nameLength = U16(header, 26), extraLength = U16(header, 28);
        if (!reader.Take(nameLength).SequenceEqual(entry.NameBytes)) throw Invalid("A ZIP member's local and central names differ.");
        byte[] extra = reader.Take(extraLength).ToArray();
        var local = ParseExtras(extra, central: false);
        long? zipUncompressed = null, zipCompressed = null;
        if (local.Zip64)
        {
            byte[] zip64 = Zip64Data(extra);
            if (zip64.Length != 16 || compressed32 != uint.MaxValue && compressed32 != 0 || uncompressed32 != compressed32)
                throw Unsupported("A ZIP64 local header is outside this profile.");
            zipUncompressed = Long(zip64, 0); zipCompressed = Long(zip64, 8);
        }
        bool descriptor = (entry.Flags & 8) != 0;
        bool zeros = crc == 0 && (local.Zip64 ? zipUncompressed == 0 && zipCompressed == 0 : compressed32 == 0 && uncompressed32 == 0);
        bool exact = crc == entry.Crc && (local.Zip64
            ? compressed32 == uint.MaxValue && zipUncompressed == entry.Uncompressed && zipCompressed == entry.Compressed
            : compressed32 == entry.Compressed && uncompressed32 == entry.Uncompressed && entry.Compressed < uint.MaxValue && entry.Uncompressed < uint.MaxValue);
        if (!(exact || descriptor && zeros)) throw Invalid("A ZIP member's local sizes or checksum disagree with its central record.");
        // Every admitted field is the same observation in both headers; the local extended timestamp may add times and the
        // central owner field may be empty.
        if (local.TimeFlags != entry.Extras.TimeFlags || local.Modified != entry.Extras.Modified ||
            local.Ntfs != entry.Extras.Ntfs || local.OwnerPresent != entry.Extras.OwnerPresent ||
            entry.Extras.Owner is not null && entry.Extras.Owner != local.Owner)
            throw Invalid("A ZIP member's local and central extra fields disagree.");
        return local;
    }

    private static (string Sha256, uint Crc) Decode(SequentialReader reader, Central entry, byte[] buffer, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var crc = new ArchiveCrc32(); long produced = 0;
        using var view = new BoundedView(reader, entry.Compressed);
        using Stream data = entry.Method == 8 ? new StrictDeflateReadStream(view, entry.Compressed, entry.Uncompressed, ct) : view;
        for (int count; (count = data.Read(buffer)) != 0;)
        {
            ct.ThrowIfCancellationRequested(); produced = checked(produced + count);
            if (produced > entry.Uncompressed) throw Invalid("A ZIP member holds more data than recorded.");
            hash.AppendData(buffer, 0, count); crc.Append(buffer.AsSpan(0, count));
        }
        if (produced != entry.Uncompressed || view.Remaining != 0) throw Invalid("A ZIP member's data is truncated.");
        return (Convert.ToHexString(hash.GetHashAndReset()), crc.Value);
    }

    private static void ReadDescriptor(SequentialReader reader, Central entry, bool wide, long next)
    {
        int sizes = wide ? 16 : 8; long room = next - reader.Position;
        if (room < 4 + sizes) throw Invalid("A ZIP data descriptor is missing.");
        var ahead = reader.Peek((int)Math.Min(8 + sizes, room));
        bool signed = Matches(ahead, 4) && U32(ahead, 0) == DescriptorSignature, unsigned = Matches(ahead, 0);
        if (signed && unsigned) // A checksum equal to the signature: only the layout that reaches the next record is the descriptor.
        {
            signed = reader.Position + 8 + sizes == next; unsigned = reader.Position + 4 + sizes == next;
        }
        if (signed == unsigned) throw Invalid("A ZIP data descriptor is missing or ambiguous.");
        reader.Take(signed ? 8 + sizes : 4 + sizes);

        bool Matches(ReadOnlySpan<byte> bytes, int at)
        {
            if (bytes.Length < at + 4 + sizes || BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]) != entry.Crc) return false;
            long compressed = wide ? (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(at + 4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at + 4)..]);
            long uncompressed = wide ? (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(at + 12)..]) : BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at + 8)..]);
            return compressed == entry.Compressed && uncompressed == entry.Uncompressed;
        }
    }

    private static Extras ParseExtras(ReadOnlySpan<byte> extra, bool central)
    {
        bool zip64 = false, timeSeen = false, ntfsSeen = false, ownerSeen = false; byte timeFlags = 0; Span<int> times = stackalloc int[3];
        ArchiveZipNtfsTimes? ntfs = null; ArchiveZipOwner? owner = null;
        for (int at = 0; at < extra.Length;)
        {
            if (extra.Length - at < 4) throw Invalid("A ZIP extra field is truncated.");
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra[at..]); int size = BinaryPrimitives.ReadUInt16LittleEndian(extra[(at + 2)..]);
            if (extra.Length - at - 4 < size) throw Invalid("A ZIP extra field is truncated.");
            var data = extra.Slice(at + 4, size); at += 4 + size;
            switch (id)
            {
                case Zip64Extra:
                    if (zip64) throw Duplicate(); zip64 = true; break;
                case TimeExtra:
                    if (timeSeen) throw Duplicate(); timeSeen = true;
                    if (size < 1 || (data[0] & ~7) != 0 || data[0] == 0) throw Unsupported("A ZIP extended timestamp is outside this profile.");
                    timeFlags = data[0];
                    int expected = central ? ((timeFlags & 1) != 0 ? 5 : 1) : 1 + 4 * System.Numerics.BitOperations.PopCount(timeFlags);
                    if (size != expected) throw Unsupported("A ZIP extended timestamp has an unexpected length.");
                    int t = 1;
                    for (int bit = 0; bit < 3; bit++)
                        if ((timeFlags & (1 << bit)) != 0 && (!central || bit == 0)) { times[bit] = BinaryPrimitives.ReadInt32LittleEndian(data[t..]); t += 4; }
                    break;
                case NtfsExtra:
                    if (ntfsSeen) throw Duplicate(); ntfsSeen = true;
                    if (size != 32 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 1 ||
                        BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 24)
                        throw Unsupported("A ZIP NTFS time field is outside this profile.");
                    ntfs = new(BinaryPrimitives.ReadUInt64LittleEndian(data[8..]), BinaryPrimitives.ReadUInt64LittleEndian(data[16..]),
                        BinaryPrimitives.ReadUInt64LittleEndian(data[24..]));
                    break;
                case OwnerExtra:
                    if (ownerSeen) throw Duplicate(); ownerSeen = true;
                    if (central && size == 0) break;
                    // Version 1, then a UID and a GID, each 1–8 bytes with its own length byte; every length is checked before use.
                    if (size < 5 || data[0] != 1 || data[1] is < 1 or > 8 || 2 + data[1] >= size || data[2 + data[1]] is < 1 or > 8 ||
                        size != 3 + data[1] + data[2 + data[1]])
                        throw Unsupported("A ZIP Unix owner field is outside this profile.");
                    owner = new(data[1], Id(data.Slice(2, data[1])), data[2 + data[1]], Id(data.Slice(3 + data[1], data[2 + data[1]])));
                    break;
                default:
                    throw Unsupported($"A ZIP member has an extra field (0x{id:x4}) outside this profile.");
            }
        }
        return new(zip64, timeFlags, times[0], times[1], times[2], ntfs, owner, ownerSeen);
        static ArchiveWriteException Duplicate() => Unsupported("A ZIP member repeats an extra field.");
        static ulong Id(ReadOnlySpan<byte> bytes) { ulong value = 0; for (int i = 0; i < bytes.Length; i++) value |= (ulong)bytes[i] << (8 * i); return value; }
    }

    private static byte[] Zip64Data(ReadOnlySpan<byte> extra)
    {
        for (int at = 0; at + 4 <= extra.Length;)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra[at..]); int size = BinaryPrimitives.ReadUInt16LittleEndian(extra[(at + 2)..]);
            if (id == Zip64Extra) return extra.Slice(at + 4, size).ToArray();
            at += 4 + size;
        }
        throw Unsupported("A saturated ZIP header field has no ZIP64 extra field.");
    }

    private static string Text(ReadOnlySpan<byte> bytes, bool utf8, bool comment)
    {
        if (utf8)
        {
            try { return ArchiveWritePolicy.Utf8.GetString(bytes); }
            catch (DecoderFallbackException) { throw Unsupported("A ZIP name or comment marked UTF-8 is not valid UTF-8."); }
        }
        foreach (byte value in bytes)
            if (value is < 0x20 or > 0x7e && !(comment && value is 0x09 or 0x0a or 0x0d))
                throw Unsupported("A ZIP name or comment uses characters outside ASCII without the UTF-8 flag, so its code page is unknown.");
        return Encoding.ASCII.GetString(bytes);
    }

    /// <summary>The browsing reader (.NET's ZipArchive) must see the same members, sizes and checksums in the same order.</summary>
    private static void CompareDecoder(Stream source, IReadOnlyList<ZipWriteMember> members, CancellationToken ct)
    {
        source.Position = 0;
        try
        {
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            var entries = archive.Entries;
            if (entries.Count != members.Count) throw Disagree();
            for (int i = 0; i < members.Count; i++)
            {
                ct.ThrowIfCancellationRequested(); var entry = entries[i]; var member = members[i];
                if (entry.FullName != member.Name || entry.Length != member.Length || entry.CompressedLength != member.CompressedLength || entry.Crc32 != member.Crc)
                    throw Disagree();
            }
        }
        catch (ArchiveWriteException) { throw; }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or NotSupportedException or OverflowException)
        { throw new ArchiveWriteException(ArchiveWriteReason.InvalidArchive, "The ZIP reader used for browsing refuses this archive.", ex); }
        static ArchiveWriteException Disagree() => Unsupported("The ZIP reader used for browsing sees this archive differently.");
    }

    private static byte[] ReadAt(Stream source, long offset, int count, CancellationToken ct)
    {
        byte[] result = new byte[count]; source.Position = offset; int done = 0;
        while (done < count)
        {
            ct.ThrowIfCancellationRequested();
            int n = source.Read(result, done, Math.Min(count - done, ArchiveIoAuthority.MaximumReadBytes));
            if (n <= 0) throw Invalid("The ZIP is truncated."); done += n;
        }
        return result;
    }

    private static ushort U16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b[at..]);
    private static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b[at..]);
    private static ulong U64(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt64LittleEndian(b[at..]);
    private static long Long(ReadOnlySpan<byte> b, int at)
    { ulong value = U64(b, at); if (value > (ulong)ArchiveWritePolicy.MaxPhysicalBytes) throw ArchiveWriteBudget.Limit(); return (long)value; }

    internal static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidArchive, message);
    internal static ArchiveWriteException Unsupported(string message) => new(ArchiveWriteReason.UnsupportedFeature, message);

    /// <summary>Reads the member records once, in order, up to the central directory, hashing every byte it fetches.</summary>
    private sealed class SequentialReader(Stream source, IncrementalHash hash, long limit, CancellationToken ct)
    {
        private readonly byte[] buffer = new byte[1 << 20];
        private int start, count;
        private long fetched;
        internal long Position { get; private set; }

        internal ReadOnlySpan<byte> Peek(int wanted) { Fill(wanted, exact: false); return buffer.AsSpan(start, Math.Min(wanted, count)); }

        internal ReadOnlySpan<byte> Take(int wanted)
        {
            Fill(wanted, exact: true); var result = buffer.AsSpan(start, wanted); start += wanted; count -= wanted; Position += wanted; return result;
        }

        internal int Read(Span<byte> destination, long limit)
        {
            if (destination.IsEmpty || limit <= 0) return 0;
            if (count == 0) Fill(1, exact: false);
            int n = (int)Math.Min(Math.Min(destination.Length, count), limit);
            buffer.AsSpan(start, n).CopyTo(destination); start += n; count -= n; Position += n; return n;
        }

        private void Fill(int wanted, bool exact)
        {
            if (wanted > buffer.Length) throw Invalid("A ZIP record exceeds the reader's bound.");
            if (count >= wanted) return;
            if (start != 0) { buffer.AsSpan(start, count).CopyTo(buffer); start = 0; }
            while (count < wanted)
            {
                ct.ThrowIfCancellationRequested();
                int room = (int)Math.Min(Math.Min(buffer.Length - count, ArchiveIoAuthority.MaximumReadBytes), limit - fetched);
                int n = room <= 0 ? 0 : source.Read(buffer, count, room);
                if (n <= 0) { if (exact) throw Invalid("A ZIP member record runs into the central directory or past the end."); return; }
                hash.AppendData(buffer, count, n); count += n; fetched += n;
            }
        }
    }

    /// <summary>
    /// The central directory and end records at their real offsets, from memory, for the browsing reader's view; bytes
    /// before the directory read as zeros (that reader lists members from the directory alone).
    /// </summary>
    private sealed class TrailerView(long length, long start, byte[] trailer) : Stream
    {
        private long position;
        public override int Read(Span<byte> destination)
        {
            if (position >= length || destination.IsEmpty) return 0;
            int n = (int)Math.Min(destination.Length, length - position);
            for (int i = 0; i < n; i++)
            {
                long at = position + i;
                destination[i] = at < start ? (byte)0 : trailer[at - start];
            }
            position += n; return n;
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override long Seek(long offset, SeekOrigin origin)
        {
            long next = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => position + offset, _ => length + offset };
            if (next < 0) throw new IOException("Seek before the start."); return position = next;
        }
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => length; public override long Position { get => position; set => Seek(value, SeekOrigin.Begin); }
        public override void Flush() { } public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Exactly one member's compressed bytes from the sequential reader, then end of stream.</summary>
    private sealed class BoundedView(SequentialReader reader, long length) : Stream
    {
        internal long Remaining { get; private set; } = length;
        public override int Read(Span<byte> destination)
        {
            int n = reader.Read(destination, Remaining);
            if (n == 0 && Remaining != 0 && !destination.IsEmpty) throw Invalid("The ZIP is truncated.");
            Remaining -= n; return n;
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int ReadByte() { Span<byte> one = stackalloc byte[1]; return Read(one) == 0 ? -1 : one[0]; }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
