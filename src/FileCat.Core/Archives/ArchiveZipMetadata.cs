using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace FileCat.Core.Archives;

/// <summary>Info-ZIP owner IDs (extra field 0x7875) with their recorded widths.</summary>
public sealed record ArchiveZipOwner
{
    public byte UidSize { get; }
    public ulong Uid { get; }
    public byte GidSize { get; }
    public ulong Gid { get; }
    public ArchiveZipOwner(byte uidSize, ulong uid, byte gidSize, ulong gid)
    {
        if (uidSize is < 1 or > 8 || gidSize is < 1 or > 8 || uidSize < 8 && uid >> (8 * uidSize) != 0 || gidSize < 8 && gid >> (8 * gidSize) != 0)
            throw ArchiveZipMetadata.Unsupported();
        UidSize = uidSize; Uid = uid; GidSize = gidSize; Gid = gid;
    }
}

/// <summary>NTFS times (extra field 0x000a, tag 1) as recorded 100 ns FILETIME values.</summary>
public sealed record ArchiveZipNtfsTimes(ulong Modified, ulong Accessed, ulong Created);

/// <summary>
/// What a user can observe of one member of the worker ZIP profile (version 1): the recording host and attributes, the DOS
/// time, the extended timestamp, NTFS times, Unix owner IDs and the member comment. Method, compressed size, data
/// descriptors, ZIP64 records and the UTF-8 flag are encoding details and are not part of it.
/// </summary>
public sealed class ArchiveZipMetadata
{
    public const byte Schema = 1;
    public const byte DosHost = 0, UnixHost = 3, NtfsHost = 10, MacHost = 19;
    public ushort VersionMadeBy { get; }
    public ushort InternalAttributes { get; }
    public uint ExternalAttributes { get; }
    public ushort DosTime { get; }
    public ushort DosDate { get; }
    /// <summary>Extended timestamp flags: bit 0 modified, bit 1 accessed, bit 2 created; zero when the field is absent.</summary>
    public byte TimeFlags { get; }
    public int Modified { get; }
    public int Accessed { get; }
    public int Created { get; }
    public ArchiveZipNtfsTimes? Ntfs { get; }
    public ArchiveZipOwner? Owner { get; }
    public string Comment { get; }

    public ArchiveZipMetadata(ushort versionMadeBy, ushort internalAttributes, uint externalAttributes, ushort dosTime, ushort dosDate,
        byte timeFlags, int modified, int accessed, int created, ArchiveZipNtfsTimes? ntfs, ArchiveZipOwner? owner, string comment)
    {
        ArgumentNullException.ThrowIfNull(comment);
        if (versionMadeBy >> 8 is not (DosHost or UnixHost or NtfsHost or MacHost) || (versionMadeBy & 0xff) > 63 || (timeFlags & ~7) != 0 ||
            (timeFlags & 1) == 0 && modified != 0 || (timeFlags & 2) == 0 && accessed != 0 || (timeFlags & 4) == 0 && created != 0 ||
            ArchiveWritePolicy.Utf8.GetByteCount(comment) > ushort.MaxValue)
            throw Unsupported();
        VersionMadeBy = versionMadeBy; InternalAttributes = internalAttributes; ExternalAttributes = externalAttributes;
        DosTime = dosTime; DosDate = dosDate; TimeFlags = timeFlags; Modified = modified; Accessed = accessed; Created = created;
        Ntfs = ntfs; Owner = owner; Comment = comment;
    }

    /// <summary>
    /// Whether the external attributes describe the member type the name declares. Unix-style readers (Info-ZIP, libarchive)
    /// take a nonzero high half as the file mode whatever the recording host, so it must be absent, a regular file or a folder
    /// matching the name; a link, device, FIFO or socket would be recreated as such by those readers while FileCat shows a
    /// file. The DOS folder bit must not mark a file, and an NTFS reparse point is never admitted.
    /// </summary>
    public static bool AttributesFit(uint externalAttributes, ArchiveMemberType type)
    {
        uint unixType = externalAttributes >> 16 & 0xf000;
        if ((externalAttributes & 0x400) != 0) return false;
        return type == ArchiveMemberType.Directory
            ? unixType is 0 or 0x4000
            : unixType is 0 or 0x8000 && (externalAttributes & 0x10) == 0;
    }

    public byte[] Encode()
    {
        byte[] comment = ArchiveWritePolicy.Utf8.GetBytes(Comment);
        int length = 14 + 4 * System.Numerics.BitOperations.PopCount(TimeFlags) + 1 + (Ntfs is null ? 0 : 24) + 1 +
            (Owner is { } o ? 2 + o.UidSize + o.GidSize : 0) + 2 + comment.Length;
        byte[] result = new byte[length]; int at = 0;
        result[at++] = Schema; U16(VersionMadeBy); U16(InternalAttributes); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at), ExternalAttributes); at += 4;
        U16(DosTime); U16(DosDate); result[at++] = TimeFlags;
        if ((TimeFlags & 1) != 0) I32(Modified); if ((TimeFlags & 2) != 0) I32(Accessed); if ((TimeFlags & 4) != 0) I32(Created);
        result[at++] = Ntfs is null ? (byte)0 : (byte)1;
        if (Ntfs is { } n) { U64(n.Modified); U64(n.Accessed); U64(n.Created); }
        result[at++] = Owner is null ? (byte)0 : (byte)1;
        if (Owner is { } owner) { Id(owner.UidSize, owner.Uid); Id(owner.GidSize, owner.Gid); }
        U16((ushort)comment.Length); comment.CopyTo(result, at); at += comment.Length;
        if (at != length) throw Unsupported();
        return result;
        void U16(ushort value) { BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at), value); at += 2; }
        void I32(int value) { BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at), value); at += 4; }
        void U64(ulong value) { BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(at), value); at += 8; }
        void Id(byte size, ulong value) { result[at++] = size; for (int i = 0; i < size; i++) result[at++] = (byte)(value >> (8 * i)); }
    }

    public static ArchiveZipMetadata Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length > ArchiveWritePolicy.MaxEntryMetadataBytes) throw Unsupported();
        byte[] data = encoded.ToArray(); int at = 0;
        if (Byte() != Schema) throw Unsupported();
        ushort madeBy = U16(), internalAttributes = U16(); uint external = U32(); ushort time = U16(), date = U16(); byte flags = Byte();
        int modified = (flags & 1) != 0 ? I32() : 0, accessed = (flags & 2) != 0 ? I32() : 0, created = (flags & 4) != 0 ? I32() : 0;
        ArchiveZipNtfsTimes? ntfs = Flag() ? new(U64(), U64(), U64()) : null;
        ArchiveZipOwner? owner = null;
        if (Flag()) { byte uidSize = Byte(); ulong uid = Id(uidSize); byte gidSize = Byte(); owner = new(uidSize, uid, gidSize, Id(gidSize)); }
        int commentLength = U16(); if (commentLength > data.Length - at) throw Unsupported();
        string comment;
        try { comment = ArchiveWritePolicy.Utf8.GetString(data, at, commentLength); } catch (DecoderFallbackException) { throw Unsupported(); }
        at += commentLength; if (at != data.Length) throw Unsupported();
        return new(madeBy, internalAttributes, external, time, date, flags, modified, accessed, created, ntfs, owner, comment);
        byte Byte() { if (at >= data.Length) throw Unsupported(); return data[at++]; }
        bool Flag() { byte value = Byte(); if (value > 1) throw Unsupported(); return value == 1; }
        ushort U16() { if (at > data.Length - 2) throw Unsupported(); ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)); at += 2; return value; }
        uint U32() { if (at > data.Length - 4) throw Unsupported(); uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)); at += 4; return value; }
        int I32() => unchecked((int)U32());
        ulong U64() { if (at > data.Length - 8) throw Unsupported(); ulong value = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(at)); at += 8; return value; }
        ulong Id(byte size)
        {
            if (size is < 1 or > 8 || at > data.Length - size) throw Unsupported(); ulong value = 0;
            for (int i = 0; i < size; i++) value |= (ulong)data[at + i] << (8 * i); at += size; return value;
        }
    }

    /// <summary>
    /// A new member from a captured file or directory. The recording host is Unix with the captured permission bits on every
    /// platform, as 7z records them; the DOS field holds the local clock time where FileCat runs and the extended timestamp
    /// the exact UTC second. A time the extended timestamp cannot hold is refused rather than rounded.
    /// </summary>
    public static ArchiveZipMetadata ForCaptured(ArchiveMemberType type, int mode, ArchiveTimestamp modified, int localOffsetMinutes)
    {
        if ((mode & ~0x1ff) != 0 || !ArchiveLocalClock.Valid(localOffsetMinutes)) throw ArchiveManifestMember.Invalid();
        if (modified.Seconds is < int.MinValue or > int.MaxValue)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature,
                "ZIP cannot record this modification time exactly: its extended timestamp covers December 1901 to January 2038.");
        var (time, date) = DosDateTime(modified.Seconds + localOffsetMinutes * 60L);
        bool directory = type == ArchiveMemberType.Directory;
        uint external = (uint)(mode | (directory ? 0x4000 : 0x8000)) << 16 | (directory ? 0x10u : 0u);
        return new((ushort)(UnixHost << 8 | 63), 0, external, time, date, 1, (int)modified.Seconds, 0, 0, null, null, "");
    }

    /// <summary>DOS time and date of a local clock instant, clamped to the field's 1980–2107 range (two-second steps).</summary>
    public static (ushort Time, ushort Date) DosDateTime(long localSeconds)
    {
        const long first = 315532800L, last = 4354819198L; // 1980-01-01 00:00:00 and 2107-12-31 23:59:58
        var clock = DateTime.UnixEpoch.AddSeconds(Math.Clamp(localSeconds, first, last));
        return ((ushort)(clock.Hour << 11 | clock.Minute << 5 | clock.Second / 2), (ushort)((clock.Year - 1980) << 9 | clock.Month << 5 | clock.Day));
    }

    internal static ArchiveWriteException Unsupported() =>
        new(ArchiveWriteReason.UnsupportedFeature, "The ZIP member's metadata is outside what this profile can preserve.");
}

/// <summary>Archive-level ZIP features (version 1): the archive comment's exact bytes.</summary>
public static class ArchiveZipFeatures
{
    public const byte Schema = 1;
    public static byte[] Create(ReadOnlySpan<byte> comment)
    {
        if (comment.Length > ushort.MaxValue) throw ArchiveZipMetadata.Unsupported();
        byte[] result = new byte[1 + comment.Length]; result[0] = Schema; comment.CopyTo(result.AsSpan(1)); return result;
    }
    public static ReadOnlySpan<byte> Comment(ReadOnlySpan<byte> features)
    {
        Validate(features); return features[1..];
    }
    public static void Validate(ReadOnlySpan<byte> features)
    {
        if (features.Length is < 1 or > 1 + ushort.MaxValue || features[0] != Schema) throw ArchiveZipMetadata.Unsupported();
    }
    /// <summary>
    /// The profile's level for new or replaced payloads, matching the choices the existing ZIP route offers: 0 and 2 normal
    /// Deflate, 1 fastest Deflate, 3 stored without compression (null).
    /// </summary>
    public static CompressionLevel? Level(ArchiveWriteProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Format != ArchiveWriteFormat.Zip || profile.Version != 1) throw ArchiveZipMetadata.Unsupported();
        return profile.CompressionLevel switch { 1 => CompressionLevel.Fastest, 3 => null, _ => CompressionLevel.Optimal };
    }
}
