using System.Buffers.Binary;
using System.Text;

namespace FileCat.Core.Records;

/// <summary>NTFS attribute types, as the volume's $AttrDef names them.</summary>
public static class NtfsAttributeTypes
{
    public const uint StandardInformation = 0x10, AttributeList = 0x20, FileName = 0x30, ObjectId = 0x40, SecurityDescriptor = 0x50,
        VolumeName = 0x60, VolumeInformation = 0x70, Data = 0x80, IndexRoot = 0x90, IndexAllocation = 0xA0, Bitmap = 0xB0,
        ReparsePoint = 0xC0, EaInformation = 0xD0, Ea = 0xE0, PropertySet = 0xF0, LoggedUtilityStream = 0x100;

    public static string Name(uint type) => type switch
    {
        StandardInformation => "$STANDARD_INFORMATION",
        AttributeList => "$ATTRIBUTE_LIST",
        FileName => "$FILE_NAME",
        ObjectId => "$OBJECT_ID",
        SecurityDescriptor => "$SECURITY_DESCRIPTOR",
        VolumeName => "$VOLUME_NAME",
        VolumeInformation => "$VOLUME_INFORMATION",
        Data => "$DATA",
        IndexRoot => "$INDEX_ROOT",
        IndexAllocation => "$INDEX_ALLOCATION",
        Bitmap => "$BITMAP",
        ReparsePoint => "$REPARSE_POINT",
        EaInformation => "$EA_INFORMATION",
        Ea => "$EA",
        PropertySet => "$PROPERTY_SET",
        LoggedUtilityStream => "$LOGGED_UTILITY_STREAM",
        _ => $"0x{type:X}",
    };
}

/// <summary>A run of clusters: <see cref="Length"/> clusters from virtual cluster <see cref="Vcn"/>, at <see cref="Lcn"/> on the volume (-1: sparse, nothing stored).</summary>
public sealed record NtfsRun(long Vcn, long Lcn, long Length)
{
    public bool IsSparse => Lcn < 0;
}

/// <summary>One attribute of a record: its header, and its value (resident) or runs (non-resident).</summary>
public sealed record NtfsAttribute(uint Type, string Name, ushort Id, bool Resident, ushort Flags)
{
    public string TypeName => NtfsAttributeTypes.Name(Type);
    public bool IsCompressed => (Flags & 0x00FF) != 0;
    public bool IsEncrypted => (Flags & 0x4000) != 0;
    public bool IsSparse => (Flags & 0x8000) != 0;
    /// <summary>The record it is kept in (an extension record for very fragmented or much-linked files).</summary>
    public long InRecord { get; init; }
    /// <summary>Where its header starts in that record (what $LogFile's records name when they change it).</summary>
    public int OffsetInRecord { get; init; }
    /// <summary>The value's size (resident), or the attribute's data size (non-resident; 0 in a later piece of a split attribute).</summary>
    public long Size { get; init; }
    public long Allocated { get; init; }
    public long Initialized { get; init; }
    /// <summary>Clusters actually allocated to a compressed or sparse attribute.</summary>
    public long? TotalAllocated { get; init; }
    public long StartVcn { get; init; }
    public long LastVcn { get; init; }
    /// <summary>Compression unit as a power of two in clusters (4: 16 clusters), 0 when not compressed.</summary>
    public int CompressionUnit { get; init; }
    public IReadOnlyList<NtfsRun> Runs { get; init; } = [];
    /// <summary>A resident attribute's value.</summary>
    public byte[]? Value { get; init; }
    /// <summary>Whether it is in an index ($FILE_NAME values are, in their folder's $I30).</summary>
    public bool Indexed { get; init; }
}

/// <summary>$STANDARD_INFORMATION: the times and attributes Windows shows and sets. Times are FILETIMEs (100 ns since 1601, UTC).</summary>
public sealed record NtfsStandardInformation(long Created, long Modified, long Changed, long Accessed, uint Attributes)
{
    public uint MaxVersions { get; init; }
    public uint Version { get; init; }
    public uint ClassId { get; init; }
    /// <summary>NTFS 3.0 and later: the owner's quota ID, the security ID (in $Secure), quota charged, and the last USN.</summary>
    public bool HasExtendedFields { get; init; }
    public uint OwnerId { get; init; }
    public uint SecurityId { get; init; }
    public long QuotaCharged { get; init; }
    public long Usn { get; init; }
}

/// <summary>
/// $FILE_NAME: one name of the file in one folder, with a copy of its times and sizes that NTFS writes when the name is
/// created or changed (and lazily after that), and that programs cannot set.
/// </summary>
public sealed record NtfsFileName(long ParentRecord, ushort ParentSequence, long Created, long Modified, long Changed, long Accessed,
    long Allocated, long Size, uint Flags, uint ReparseOrEa, byte Namespace, string Name)
{
    public string NamespaceText => Namespace switch
    {
        0 => "POSIX",
        1 => "Win32",
        2 => "DOS 8.3",
        3 => "Win32 and DOS 8.3",
        _ => $"namespace {Namespace}",
    };

    /// <summary>An 8.3 alias only: the same file's long name is another $FILE_NAME.</summary>
    public bool IsDosAlias => Namespace == 2;
}

/// <summary>$OBJECT_ID: the ID link tracking uses, and optionally where it was first given (birth volume and object) and its domain.</summary>
public sealed record NtfsObjectId(Guid ObjectId, Guid? BirthVolumeId, Guid? BirthObjectId, Guid? DomainId);

/// <summary>$REPARSE_POINT: the tag and its data (after the vendor GUID, for tags not Microsoft's).</summary>
public sealed record NtfsReparse(uint Tag, byte[] Data, Guid? Vendor);

/// <summary>An entry of $ATTRIBUTE_LIST: where one attribute (or piece of one) of the file is kept.</summary>
public sealed record NtfsAttributeListEntry(uint Type, string Name, long StartVcn, long Record, ushort RecordSequence, ushort Id);

/// <summary>
/// An MFT file record, parsed for display (D-56): its header, every attribute, and the values FileCat names. Parsing
/// never throws on damage: what does not fit is left out and said in <see cref="Problems"/>.
/// </summary>
public sealed class NtfsRecord
{
    /// <summary>NTFS protects every 512 bytes of a record with an update sequence (fixups), whatever the sector size.</summary>
    private const int Stride = 512;
    private const int MaxAttributes = 512;
    private const int MaxRuns = 100_000;

    /// <summary>The record's number as its header records it (NTFS 3.1), or -1 (older volumes do not keep it).</summary>
    public long Number { get; private set; } = -1;
    public ushort Sequence { get; private set; }
    public ushort LinkCount { get; private set; }
    public ushort Flags { get; private set; }
    /// <summary>The $LogFile sequence number of its last change.</summary>
    public ulong Lsn { get; private set; }
    /// <summary>For an extension record, the base record it belongs to; 0 for a base record.</summary>
    public long BaseRecord { get; private set; }
    public ushort BaseSequence { get; private set; }
    public int UsedSize { get; private set; }
    public int AllocatedSize { get; private set; }
    public ushort NextAttributeId { get; private set; }

    public bool InUse => (Flags & 0x01) != 0;
    public bool IsDirectory => (Flags & 0x02) != 0;
    /// <summary>Records in $Extend ($Quota, $ObjId, $Reparse, $UsnJrnl) carry flag 4.</summary>
    public bool InExtend => (Flags & 0x04) != 0;
    /// <summary>An index other than a file-name index ($Secure's $SII and $SDH, $ObjId's $O, $Reparse's $R).</summary>
    public bool HasViewIndex => (Flags & 0x08) != 0;

    public List<NtfsAttribute> Attributes { get; } = [];
    public List<NtfsAttributeListEntry> AttributeList { get; } = [];
    public NtfsStandardInformation? Standard { get; private set; }
    public List<NtfsFileName> Names { get; } = [];
    public NtfsObjectId? ObjectId { get; private set; }
    public NtfsReparse? Reparse { get; private set; }
    public string? VolumeName { get; private set; }
    public (byte Major, byte Minor, ushort Flags)? VolumeVersion { get; private set; }
    /// <summary>$EA_INFORMATION: the packed size of the EAs, how many need to be understood (NEED_EA), and their unpacked size.</summary>
    public (int PackedSize, int NeedEa, long UnpackedSize)? EaInformation { get; private set; }
    /// <summary>The bytes between the end of the record's attributes and the end of its space: earlier contents may linger there.</summary>
    public byte[] Slack { get; private set; } = [];
    public List<string> Problems { get; } = [];

    /// <summary>Whether the record came from NTFS's memory (FSCTL_GET_NTFS_FILE_RECORD), where fixups do not apply.</summary>
    private bool _live;

    /// <summary>
    /// Parses a base record. <paramref name="live"/>: it came from NTFS's memory rather than the disk, so its update
    /// sequence array is whatever the last write left and is not checked.
    /// </summary>
    public static NtfsRecord Parse(ReadOnlySpan<byte> raw, long number = -1, bool live = false)
    {
        var record = new NtfsRecord { Number = number, _live = live };
        record.Load(raw.ToArray(), isBase: true);
        return record;
    }

    /// <summary>Adds the attributes kept in one of the file's extension records.</summary>
    public void AddExtension(ReadOnlySpan<byte> raw, long number)
    {
        var extension = new NtfsRecord { Number = number, _live = _live };
        var bytes = raw.ToArray();
        if (!extension.ReadHeader(bytes, out int firstAttribute))
        {
            Problems.Add($"Extension record {number:N0} is not a readable MFT record.");
            return;
        }
        if (Number >= 0 && extension.BaseRecord != Number)
            Problems.Add($"Extension record {number:N0} names record {extension.BaseRecord:N0} as its base, not this record.");
        foreach (var p in extension.Problems) Problems.Add($"Extension record {number:N0}: {p}");
        ReadAttributes(bytes, firstAttribute, Math.Min(extension.UsedSize, bytes.Length), number);
    }

    /// <summary>Entries of an attribute list whose value was kept outside the record (read from its clusters).</summary>
    public void AddAttributeList(ReadOnlySpan<byte> value) => ParseAttributeList(value);

    private void Load(byte[] bytes, bool isBase)
    {
        if (!ReadHeader(bytes, out int firstAttribute)) return;
        int used = Math.Min(UsedSize, bytes.Length);
        ReadAttributes(bytes, firstAttribute, used, Number);
        if (isBase && AllocatedSize > used && AllocatedSize <= bytes.Length) Slack = bytes[used..AllocatedSize];
    }

    private bool ReadHeader(byte[] bytes, out int firstAttribute)
    {
        firstAttribute = 0;
        if (bytes.Length < 48)
        {
            Problems.Add("An MFT record is at least 48 bytes; this one is shorter.");
            return false;
        }
        var magic = bytes.AsSpan(0, 4);
        if (magic.SequenceEqual("BAAD"u8))
        {
            Problems.Add("NTFS marked this record as damaged (BAAD): chkdsk found it unreadable.");
            return false;
        }
        if (!magic.SequenceEqual("FILE"u8))
        {
            Problems.Add("This is not an MFT record: it does not start with FILE.");
            return false;
        }
        if (!_live) ApplyFixups(bytes);
        Lsn =BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8));
        Sequence = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(16));
        LinkCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(18));
        firstAttribute = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(20));
        Flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(22));
        UsedSize = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24)), int.MaxValue);
        AllocatedSize = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28)), int.MaxValue);
        ulong baseReference = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(32));
        BaseRecord = (long)(baseReference & 0xFFFF_FFFF_FFFF);
        BaseSequence = (ushort)(baseReference >> 48);
        NextAttributeId = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(40));
        // NTFS 3.1 keeps the record's own number after the header; the update sequence array then starts at 48.
        int usa = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        if (usa >= 48 && Number < 0) Number = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(44));
        if (UsedSize > bytes.Length) Problems.Add($"Its header says {UsedSize:N0} bytes are used, more than the record's {bytes.Length:N0}.");
        if (firstAttribute < 24 || firstAttribute >= bytes.Length)
        {
            Problems.Add("Its first attribute lies outside the record.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// The last two bytes of every 512 were replaced by the update sequence number when the record was written; their
    /// real values wait in the update sequence array. A record read from the volume has the number there, or the real
    /// bytes when it was already fixed up; anything else is a torn write.
    /// </summary>
    private void ApplyFixups(byte[] bytes)
    {
        int usa = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6));
        if (count < 2 || usa < 40 || usa + count * 2 > bytes.Length)
        {
            Problems.Add("Its update sequence array lies outside the record, so torn writes cannot be detected.");
            return;
        }
        ushort usn = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(usa));
        for (int i = 1; i < count; i++)
        {
            int at = i * Stride - 2;
            if (at + 2 > bytes.Length) break;
            ushort current = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
            ushort saved = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(usa + i * 2));
            if (current == usn)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), saved);
            }
            else if (current != saved)
            {
                Problems.Add($"The check bytes at the end of its part {i} do not match: that part was written by a different write than the rest (a torn write).");
            }
        }
    }

    private void ReadAttributes(byte[] record, int at, int used, long inRecord)
    {
        for (int n = 0; at + 4 <= used; n++)
        {
            if (n >= MaxAttributes)
            {
                Problems.Add($"It lists more than {MaxAttributes} attributes; the rest are left out.");
                return;
            }
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(at));
            if (type == 0xFFFF_FFFF) return;
            int length = at + 8 <= used ? (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(at + 4)), int.MaxValue) : 0;
            if (length < 16 || at + length > used)
            {
                Problems.Add($"Attribute {NtfsAttributeTypes.Name(type)} at offset {at} runs past the record's used space.");
                return;
            }
            try
            {
                ReadAttribute(record.AsSpan(at, length), type, inRecord, at);
            }
            catch (ArgumentOutOfRangeException)
            {
                Problems.Add($"Attribute {NtfsAttributeTypes.Name(type)} at offset {at} is malformed.");
            }
            at += length;
        }
        Problems.Add("Its attributes do not end with the end marker.");
    }

    private void ReadAttribute(ReadOnlySpan<byte> attr, uint type, long inRecord, int offset)
    {
        bool resident = attr[8] == 0;
        int nameLength = attr[9];
        int nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(attr[10..]);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(attr[12..]);
        ushort id = BinaryPrimitives.ReadUInt16LittleEndian(attr[14..]);
        string name = nameLength > 0 && nameOffset + nameLength * 2 <= attr.Length ? Encoding.Unicode.GetString(attr.Slice(nameOffset, nameLength * 2)) : "";
        NtfsAttribute attribute;
        if (resident)
        {
            if (attr.Length < 24)
            {
                Problems.Add($"Resident attribute {NtfsAttributeTypes.Name(type)} has a truncated header.");
                return;
            }
            uint valueLength = BinaryPrimitives.ReadUInt32LittleEndian(attr[16..]);
            int valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(attr[20..]);
            byte[] value;
            if (valueOffset <= attr.Length && valueLength <= (uint)(attr.Length - valueOffset)) value = attr.Slice(valueOffset, (int)valueLength).ToArray();
            else
            {
                Problems.Add($"The value of {NtfsAttributeTypes.Name(type)} runs past its attribute.");
                value = [];
            }
            attribute = new NtfsAttribute(type, name, id, true, flags)
            {
                InRecord = inRecord,
                OffsetInRecord = offset,
                Size = value.Length,
                Allocated = attr.Length - valueOffset,
                Initialized = value.Length,
                Value = value,
                Indexed = attr[22] != 0,
            };
            Interpret(type, value);
        }
        else
        {
            if (attr.Length < 64)
            {
                Problems.Add($"Non-resident attribute {NtfsAttributeTypes.Name(type)} has a truncated header.");
                return;
            }
            long startVcn = BinaryPrimitives.ReadInt64LittleEndian(attr[16..]);
            long lastVcn = BinaryPrimitives.ReadInt64LittleEndian(attr[24..]);
            int runsOffset = BinaryPrimitives.ReadUInt16LittleEndian(attr[32..]);
            int unit = BinaryPrimitives.ReadUInt16LittleEndian(attr[34..]);
            long? total = unit != 0 && attr.Length >= 72 && startVcn == 0 ? BinaryPrimitives.ReadInt64LittleEndian(attr[64..]) : null;
            attribute = new NtfsAttribute(type, name, id, false, flags)
            {
                InRecord = inRecord,
                OffsetInRecord = offset,
                StartVcn = startVcn,
                LastVcn = lastVcn,
                CompressionUnit = unit,
                Allocated = BinaryPrimitives.ReadInt64LittleEndian(attr[40..]),
                Size = BinaryPrimitives.ReadInt64LittleEndian(attr[48..]),
                Initialized = BinaryPrimitives.ReadInt64LittleEndian(attr[56..]),
                TotalAllocated = total,
                Runs = DecodeRuns(attr, runsOffset, startVcn),
            };
        }
        Attributes.Add(attribute);
    }

    private void Interpret(uint type, byte[] value)
    {
        var v = value.AsSpan();
        switch (type)
        {
            case NtfsAttributeTypes.StandardInformation when v.Length >= 48:
                Standard = new NtfsStandardInformation(
                    BinaryPrimitives.ReadInt64LittleEndian(v), BinaryPrimitives.ReadInt64LittleEndian(v[8..]),
                    BinaryPrimitives.ReadInt64LittleEndian(v[16..]), BinaryPrimitives.ReadInt64LittleEndian(v[24..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(v[32..]))
                {
                    MaxVersions = BinaryPrimitives.ReadUInt32LittleEndian(v[36..]),
                    Version = BinaryPrimitives.ReadUInt32LittleEndian(v[40..]),
                    ClassId = BinaryPrimitives.ReadUInt32LittleEndian(v[44..]),
                    HasExtendedFields = v.Length >= 72,
                    OwnerId = v.Length >= 72 ? BinaryPrimitives.ReadUInt32LittleEndian(v[48..]) : 0,
                    SecurityId = v.Length >= 72 ? BinaryPrimitives.ReadUInt32LittleEndian(v[52..]) : 0,
                    QuotaCharged = v.Length >= 72 ? BinaryPrimitives.ReadInt64LittleEndian(v[56..]) : 0,
                    Usn = v.Length >= 72 ? BinaryPrimitives.ReadInt64LittleEndian(v[64..]) : 0,
                };
                break;
            case NtfsAttributeTypes.StandardInformation:
                Problems.Add($"Its $STANDARD_INFORMATION is {v.Length} bytes, shorter than the 48 it needs.");
                break;
            case NtfsAttributeTypes.FileName when v.Length >= 66 && 66 + v[64] * 2 <= v.Length:
                ulong parent = BinaryPrimitives.ReadUInt64LittleEndian(v);
                Names.Add(new NtfsFileName((long)(parent & 0xFFFF_FFFF_FFFF), (ushort)(parent >> 48),
                    BinaryPrimitives.ReadInt64LittleEndian(v[8..]), BinaryPrimitives.ReadInt64LittleEndian(v[16..]),
                    BinaryPrimitives.ReadInt64LittleEndian(v[24..]), BinaryPrimitives.ReadInt64LittleEndian(v[32..]),
                    BinaryPrimitives.ReadInt64LittleEndian(v[40..]), BinaryPrimitives.ReadInt64LittleEndian(v[48..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(v[56..]), BinaryPrimitives.ReadUInt32LittleEndian(v[60..]),
                    v[65], Encoding.Unicode.GetString(v.Slice(66, v[64] * 2))));
                break;
            case NtfsAttributeTypes.FileName:
                Problems.Add("A $FILE_NAME is too short for the name it declares.");
                break;
            case NtfsAttributeTypes.ObjectId when v.Length >= 16:
                ObjectId = new NtfsObjectId(new Guid(v[..16]),
                    v.Length >= 32 ? new Guid(v.Slice(16, 16)) : null,
                    v.Length >= 48 ? new Guid(v.Slice(32, 16)) : null,
                    v.Length >= 64 ? new Guid(v.Slice(48, 16)) : null);
                break;
            case NtfsAttributeTypes.AttributeList:
                ParseAttributeList(v);
                break;
            case NtfsAttributeTypes.VolumeName:
                VolumeName = Encoding.Unicode.GetString(v[..(v.Length & ~1)]);
                break;
            case NtfsAttributeTypes.VolumeInformation when v.Length >= 12:
                VolumeVersion = (v[8], v[9], BinaryPrimitives.ReadUInt16LittleEndian(v[10..]));
                break;
            case NtfsAttributeTypes.ReparsePoint when v.Length >= 8:
                uint tag = BinaryPrimitives.ReadUInt32LittleEndian(v);
                int dataLength = BinaryPrimitives.ReadUInt16LittleEndian(v[4..]);
                // Microsoft's tags (bit 31) carry their data right after the header; others after a vendor GUID.
                bool microsoft = (tag & 0x8000_0000) != 0;
                int start = microsoft ? 8 : 24;
                Guid? vendor = !microsoft && v.Length >= 24 ? new Guid(v.Slice(8, 16)) : null;
                Reparse = new NtfsReparse(tag, v.Length > start ? v.Slice(start, Math.Min(dataLength, v.Length - start)).ToArray() : [], vendor);
                break;
            case NtfsAttributeTypes.EaInformation when v.Length >= 8:
                EaInformation = (BinaryPrimitives.ReadUInt16LittleEndian(v), BinaryPrimitives.ReadUInt16LittleEndian(v[2..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(v[4..]));
                break;
        }
    }

    private void ParseAttributeList(ReadOnlySpan<byte> list)
    {
        for (int at = 0, n = 0; at + 26 <= list.Length; n++)
        {
            if (n >= 4096)
            {
                Problems.Add("Its attribute list has more than 4,096 entries; the rest are left out.");
                return;
            }
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(list[at..]);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(list[(at + 4)..]);
            if (type == 0xFFFF_FFFF || length == 0) return;
            if (length < 26 || at + length > list.Length)
            {
                Problems.Add("An entry of its attribute list is malformed; the rest are left out.");
                return;
            }
            int nameLength = list[at + 6], nameOffset = list[at + 7];
            string name = nameLength > 0 && nameOffset + nameLength * 2 <= length ? Encoding.Unicode.GetString(list.Slice(at + nameOffset, nameLength * 2)) : "";
            ulong reference = BinaryPrimitives.ReadUInt64LittleEndian(list[(at + 16)..]);
            AttributeList.Add(new NtfsAttributeListEntry(type, name, BinaryPrimitives.ReadInt64LittleEndian(list[(at + 8)..]),
                (long)(reference & 0xFFFF_FFFF_FFFF), (ushort)(reference >> 48), BinaryPrimitives.ReadUInt16LittleEndian(list[(at + 24)..])));
            at += length;
        }
    }

    /// <summary>
    /// Data runs: a header byte (low nibble: bytes of the length; high nibble: bytes of the offset), the length, then
    /// the offset relative to the previous run's start (signed); no offset bytes means a sparse run.
    /// </summary>
    private List<NtfsRun> DecodeRuns(ReadOnlySpan<byte> attr, int at, long vcn)
    {
        var runs = new List<NtfsRun>();
        long lcn = 0;
        while (at < attr.Length && attr[at] != 0)
        {
            if (runs.Count >= MaxRuns)
            {
                Problems.Add($"It lists more than {MaxRuns:N0} runs; the rest are left out.");
                break;
            }
            int lengthBytes = attr[at] & 0x0F, offsetBytes = attr[at] >> 4;
            if (lengthBytes is 0 or > 8 || offsetBytes > 8 || at + 1 + lengthBytes + offsetBytes > attr.Length)
            {
                Problems.Add("A data run is malformed; the runs after it are left out.");
                break;
            }
            long length = (long)ReadUnsigned(attr.Slice(at + 1, lengthBytes));
            if (length <= 0)
            {
                Problems.Add("A data run has no length; the runs after it are left out.");
                break;
            }
            if (offsetBytes == 0) runs.Add(new NtfsRun(vcn, -1, length));
            else
            {
                lcn += ReadSigned(attr.Slice(at + 1 + lengthBytes, offsetBytes));
                if (lcn < 0)
                {
                    Problems.Add("A data run points before the start of the volume; the runs after it are left out.");
                    break;
                }
                runs.Add(new NtfsRun(vcn, lcn, length));
            }
            vcn += length;
            at += 1 + lengthBytes + offsetBytes;
        }
        return runs;
    }

    private static ulong ReadUnsigned(ReadOnlySpan<byte> bytes)
    {
        ulong v = 0;
        for (int i = bytes.Length - 1; i >= 0; i--) v = v << 8 | bytes[i];
        return v;
    }

    private static long ReadSigned(ReadOnlySpan<byte> bytes)
    {
        long v = (long)ReadUnsigned(bytes);
        int bits = bytes.Length * 8;
        return bits < 64 && (v & 1L << (bits - 1)) != 0 ? v - (1L << bits) : v;
    }
}
