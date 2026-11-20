using System.Buffers.Binary;

namespace FileCat.Core.Archives;

/// <summary>Exact supported time-record representation, including record/field presence and encoding flags.</summary>
public sealed record ArchiveRar5Times
{
    public ulong Flags { get; }
    public ArchiveTimestamp? Modified { get; }
    public ArchiveTimestamp? Created { get; }
    public ArchiveTimestamp? Accessed { get; }
    public ArchiveRar5Times(ulong flags, ArchiveTimestamp? modified = null, ArchiveTimestamp? created = null, ArchiveTimestamp? accessed = null)
    {
        if ((flags & ~31UL) != 0 || (flags & 16) != 0 && (flags & 1) == 0 ||
            ((flags & 2) != 0) != (modified is not null) || ((flags & 4) != 0) != (created is not null) || ((flags & 8) != 0) != (accessed is not null))
            throw ArchiveRar5Metadata.Invalid();
        foreach (var time in new[] { modified, created, accessed })
        {
            if (time is not { } value) continue;
            if ((flags & 1) != 0 ? value.Seconds is < 0 or > uint.MaxValue || (flags & 16) == 0 && value.Nanoseconds != 0 :
                value.Seconds < ArchiveRar5Metadata.FileTimeEpochSeconds || !value.FitsTicks) throw ArchiveRar5Metadata.Invalid();
        }
        Flags = flags; Modified = modified; Created = created; Accessed = accessed;
    }
}

/// <summary>Native owner strings are opaque bytes. Null and an explicitly present empty name are distinct.</summary>
public sealed class ArchiveRar5Owner
{
    private readonly byte[]? user, group;
    public ulong Flags { get; }
    public ReadOnlySpan<byte> UserName => user;
    public ReadOnlySpan<byte> GroupName => group;
    public ulong? UserId { get; }
    public ulong? GroupId { get; }
    internal int ByteCost => (user?.Length ?? 0) + (group?.Length ?? 0);
    public ArchiveRar5Owner(ulong flags, byte[]? userName = null, byte[]? groupName = null, ulong? userId = null, ulong? groupId = null)
    {
        if ((flags & ~15UL) != 0 || ((flags & 1) != 0) != (userName is not null) || ((flags & 2) != 0) != (groupName is not null) ||
            ((flags & 4) != 0) != (userId is not null) || ((flags & 8) != 0) != (groupId is not null)) throw ArchiveRar5Metadata.Invalid();
        if ((long)(userName?.Length ?? 0) + (groupName?.Length ?? 0) > ArchiveWritePolicy.MaxEntryMetadataBytes - 256)
            throw ArchiveWriteBudget.Limit();
        Flags = flags; user = userName?.ToArray(); group = groupName?.ToArray(); UserId = userId; GroupId = groupId;
    }
}

/// <summary>
/// Public semantic metadata for one admitted RAR5 member. Content SHA binds checksum values; presence and algorithm
/// remain explicit here. Salts, IVs and password-check bytes remain worker-local and are never manifest metadata.
/// Raw compression properties describe retained content; they do not authorize a new encoder or unsafe dictionary.
/// </summary>
public sealed class ArchiveRar5Metadata
{
    internal const long FileTimeEpochSeconds = -11644473600;
    public ArchiveMemberType Type { get; }
    public ulong HeaderFlags { get; }
    public uint Attributes { get; }
    public int Host { get; }
    public ulong Compression { get; }
    public int FilterFeatures { get; }
    public uint? BasicModifiedSeconds { get; }
    public bool HasCrc32 { get; }
    public bool HasBlake2sp { get; }
    public ArchiveRar5Times? Times { get; }
    public ArchiveRar5Owner? Owner { get; }
    public int? EncryptionFlags { get; }
    public int? CountPower { get; }
    public ArchiveRar5Metadata(ArchiveMemberType type, ulong headerFlags, uint attributes, int host, ulong compression,
        uint? basicModifiedSeconds = null, bool hasCrc32 = true, bool hasBlake2sp = false,
        ArchiveRar5Times? times = null, ArchiveRar5Owner? owner = null, int? encryptionFlags = null, int? countPower = null,
        int filterFeatures = 0)
    {
        int algorithm = (int)(compression & 63), method = (int)((compression >> 7) & 7), dictionary = (int)((compression >> 10) & 31);
        if (!Enum.IsDefined(type) || (headerFlags & ~7UL) != 0 || host is not (0 or 1) ||
            host == 1 && ((attributes >> 12) & 15) is not (0 or 4 or 8) ||
            host == 1 && (attributes & 0xf000) != 0 && (attributes & 0xf000) != (type == ArchiveMemberType.Directory ? 0x4000U : 0x8000U) ||
            algorithm > 1 || method > 5 || (compression & 64) != 0 ||
            algorithm == 0 && ((compression & ~0x7fffUL) != 0 || dictionary > 15) ||
            algorithm == 1 && ((compression & ~0x1fffffUL) != 0 || dictionary > 23) ||
            filterFeatures < 0 || (filterFeatures & ~15) != 0 || (type == ArchiveMemberType.Directory || method == 0) && filterFeatures != 0 ||
            (encryptionFlags is null) != (countPower is null) || encryptionFlags is < 0 or > 3 || countPower is < 0 or > 24 ||
            type == ArchiveMemberType.Directory && encryptionFlags is not null ||
            (times is not null || owner is not null || hasBlake2sp || encryptionFlags is not null) && (headerFlags & 1) == 0)
            throw Invalid();
        Type = type; HeaderFlags = headerFlags; Attributes = attributes; Host = host; Compression = compression;
        BasicModifiedSeconds = basicModifiedSeconds; HasCrc32 = hasCrc32; HasBlake2sp = hasBlake2sp;
        Times = times; Owner = owner; EncryptionFlags = encryptionFlags; CountPower = countPower; FilterFeatures = filterFeatures;
    }
    public byte[] Encode()
    {
        // Fixed scalar/presence encoding followed by bounded native names. No payload or immutable password text.
        using var output = new MemoryStream(128 + (Owner?.ByteCost ?? 0)); using var writer = new BinaryWriter(output);
        writer.Write((ushort)1); writer.Write((int)Type); writer.Write(HeaderFlags); writer.Write(Attributes); writer.Write(Host); writer.Write(Compression); writer.Write(FilterFeatures);
        writer.Write(BasicModifiedSeconds is not null); if (BasicModifiedSeconds is { } basic) writer.Write(basic);
        writer.Write(HasCrc32); writer.Write(HasBlake2sp); writer.Write(Times is not null);
        if (Times is { } times) { writer.Write(times.Flags); Time(times.Modified); Time(times.Created); Time(times.Accessed); }
        writer.Write(Owner is not null);
        if (Owner is { } owner)
        {
            writer.Write(owner.Flags);
            if ((owner.Flags & 1) != 0) { writer.Write(owner.UserName.Length); writer.Write(owner.UserName); }
            if ((owner.Flags & 2) != 0) { writer.Write(owner.GroupName.Length); writer.Write(owner.GroupName); }
            if (owner.UserId is { } uid) writer.Write(uid); if (owner.GroupId is { } gid) writer.Write(gid);
        }
        writer.Write(EncryptionFlags is not null); if (EncryptionFlags is { } flags) { writer.Write(flags); writer.Write(CountPower!.Value); }
        writer.Flush(); if (output.Length > ArchiveWritePolicy.MaxEntryMetadataBytes) throw ArchiveWriteBudget.Limit(); return output.ToArray();
        void Time(ArchiveTimestamp? value)
        { writer.Write(value is not null); if (value is { } time) { writer.Write(time.Seconds); writer.Write(time.Nanoseconds); } }
    }
    public static ArchiveRar5Metadata Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length > ArchiveWritePolicy.MaxEntryMetadataBytes) throw ArchiveWriteBudget.Limit();
        var reader = new Packet(data); if (reader.Short() != 1) throw Invalid();
        var type = (ArchiveMemberType)reader.Int(); ulong header = reader.ULong(); uint attrs = reader.UInt(); int host = reader.Int(); ulong compression = reader.ULong(); int filters = reader.Int();
        uint? basic = reader.Flag() ? reader.UInt() : null; bool crc = reader.Flag(), blake = reader.Flag();
        ArchiveRar5Times? times = reader.Flag() ? new(reader.ULong(), reader.Time(), reader.Time(), reader.Time()) : null;
        ArchiveRar5Owner? owner = null;
        if (reader.Flag())
        {
            ulong flags = reader.ULong(); if ((flags & ~15UL) != 0) throw Invalid();
            byte[]? user = (flags & 1) != 0 ? reader.Blob() : null, group = (flags & 2) != 0 ? reader.Blob() : null;
            ulong? uid = (flags & 4) != 0 ? reader.ULong() : null, gid = (flags & 8) != 0 ? reader.ULong() : null;
            owner = new(flags, user, group, uid, gid);
        }
        int? encrypted = null, power = null; if (reader.Flag()) { encrypted = reader.Int(); power = reader.Int(); }
        reader.End(); return new(type, header, attrs, host, compression, basic, crc, blake, times, owner, encrypted, power, filters);
    }
    internal static ArchiveWriteException Invalid() => new(ArchiveWriteReason.UnsupportedFeature, "The RAR member metadata is incomplete, invalid or unsupported.");
    private ref struct Packet(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> remaining = data;
        private ReadOnlySpan<byte> Take(int count)
        { if (count < 0 || count > remaining.Length) throw Invalid(); var result = remaining[..count]; remaining = remaining[count..]; return result; }
        internal ushort Short() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        internal int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        internal uint UInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        internal ulong ULong() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        internal bool Flag() => Take(1)[0] switch { 0 => false, 1 => true, _ => throw Invalid() };
        internal ArchiveTimestamp? Time() => Flag() ? new ArchiveTimestamp(BinaryPrimitives.ReadInt64LittleEndian(Take(8)), Int()) : null;
        internal byte[] Blob() { int length = Int(); if (length > ArchiveWritePolicy.MaxEntryMetadataBytes - 256) throw ArchiveWriteBudget.Limit(); return Take(length).ToArray(); }
        internal void End() { if (!remaining.IsEmpty) throw Invalid(); }
    }
}
