using System.Buffers.Binary;
using System.Text;

namespace FileCat.Core.Records;

/// <summary>
/// One change-journal record (D-56): what happened to a file, when, and under which name. NTFS writes version 2
/// records with 64-bit file references; ReFS (and NTFS when asked) version 3 with 128-bit IDs.
/// </summary>
/// <param name="FileId">The file's ID: an NTFS file reference (record and sequence) or a ReFS 128-bit ID.</param>
/// <param name="Time">FILETIME (UTC) of the change.</param>
public sealed record UsnRecord(int Version, UInt128 FileId, UInt128 ParentId, long Usn, long Time, uint Reasons, uint SourceInfo, uint SecurityId, uint Attributes, string Name)
{
    /// <summary>The MFT record number of an NTFS file reference (its low 48 bits).</summary>
    public static long RecordOf(UInt128 id) => (long)(ulong)(id & 0xFFFF_FFFF_FFFF);

    /// <summary>The sequence number of an NTFS file reference (bits 48 to 63).</summary>
    public static ushort SequenceOf(UInt128 id) => (ushort)((ulong)id >> 48);

    /// <summary>
    /// The records in a buffer that FSCTL_READ_USN_JOURNAL, FSCTL_ENUM_USN_DATA, or FSCTL_READ_FILE_USN_DATA filled,
    /// from <paramref name="offset"/> (8 after the next-USN field of the journal reads). Malformed data ends the list.
    /// </summary>
    public static List<UsnRecord> ParseAll(ReadOnlySpan<byte> buffer, int offset = 0)
    {
        var records = new List<UsnRecord>();
        while (offset + 8 <= buffer.Length)
        {
            int length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]), int.MaxValue);
            if (length < 8 || offset + length > buffer.Length) break;
            if (TryParse(buffer.Slice(offset, length), out var record)) records.Add(record);
            // Records are 8-byte aligned; their lengths include the padding.
            offset += (length + 7) & ~7;
        }
        return records;
    }

    /// <summary>One record (version 2 or 3); version 4 records (ranges written) carry no name or time and are skipped.</summary>
    public static bool TryParse(ReadOnlySpan<byte> r, out UsnRecord record)
    {
        record = null!;
        if (r.Length < 8) return false;
        int major = BinaryPrimitives.ReadUInt16LittleEndian(r[4..]);
        int b; // where the fields after the IDs start
        UInt128 file, parent;
        switch (major)
        {
            case 2 when r.Length >= 60:
                file = BinaryPrimitives.ReadUInt64LittleEndian(r[8..]);
                parent = BinaryPrimitives.ReadUInt64LittleEndian(r[16..]);
                b = 24;
                break;
            case 3 when r.Length >= 76:
                file = BinaryPrimitives.ReadUInt128LittleEndian(r[8..]);
                parent = BinaryPrimitives.ReadUInt128LittleEndian(r[24..]);
                b = 40;
                break;
            default:
                return false;
        }
        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(r[(b + 32)..]);
        int nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(r[(b + 34)..]);
        string name = nameOffset + nameLength <= r.Length ? Encoding.Unicode.GetString(r.Slice(nameOffset, nameLength & ~1)) : "";
        record = new UsnRecord(major, file, parent,
            BinaryPrimitives.ReadInt64LittleEndian(r[b..]), BinaryPrimitives.ReadInt64LittleEndian(r[(b + 8)..]),
            BinaryPrimitives.ReadUInt32LittleEndian(r[(b + 16)..]), BinaryPrimitives.ReadUInt32LittleEndian(r[(b + 20)..]),
            BinaryPrimitives.ReadUInt32LittleEndian(r[(b + 24)..]), BinaryPrimitives.ReadUInt32LittleEndian(r[(b + 28)..]), name);
        return true;
    }

    public const uint BasicInfoChange = 0x0000_8000, FileCreate = 0x0000_0100, FileDelete = 0x0000_0200, Close = 0x8000_0000;

    private static readonly (uint Flag, string Text)[] ReasonNames =
    [
        (0x0000_0100, "created"),
        (0x0000_0001, "data overwritten"),
        (0x0000_0002, "data extended"),
        (0x0000_0004, "data truncated"),
        (0x0000_0010, "stream overwritten"),
        (0x0000_0020, "stream extended"),
        (0x0000_0040, "stream truncated"),
        (0x0000_1000, "renamed from"),
        (0x0000_2000, "renamed to"),
        (0x0000_8000, "times or attributes set"),
        (0x0000_0400, "EAs changed"),
        (0x0000_0800, "security changed"),
        (0x0000_4000, "indexing changed"),
        (0x0001_0000, "hard link added or removed"),
        (0x0002_0000, "compression changed"),
        (0x0004_0000, "encryption changed"),
        (0x0008_0000, "object ID changed"),
        (0x0010_0000, "reparse point changed"),
        (0x0020_0000, "stream added, removed, or renamed"),
        (0x0040_0000, "transacted change"),
        (0x0080_0000, "integrity changed"),
        (0x0100_0000, "storage class changed"),
        (0x0000_0200, "deleted"),
        (0x8000_0000, "closed"),
    ];

    /// <summary>The reasons in words, in the order things happen ("created, data extended, closed").</summary>
    public static string ReasonsText(uint reasons)
    {
        var parts = new List<string>();
        uint known = 0;
        foreach (var (flag, text) in ReasonNames)
        {
            known |= flag;
            if ((reasons & flag) != 0) parts.Add(text);
        }
        if ((reasons & ~known) != 0) parts.Add($"0x{reasons & ~known:X8}");
        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    /// <summary>Who made the change, when a program said (source information).</summary>
    public static string? SourceText(uint source)
    {
        if (source == 0) return null;
        var parts = new List<string>();
        if ((source & 1) != 0) parts.Add("data management (storage tiering, deduplication, HSM)");
        if ((source & 2) != 0) parts.Add("auxiliary data (a system-only stream)");
        if ((source & 4) != 0) parts.Add("replication (DFS Replication)");
        if ((source & 8) != 0) parts.Add("client replication (cloud file sync)");
        if ((source & ~0xFu) != 0) parts.Add($"0x{source & ~0xFu:X}");
        return string.Join(", ", parts);
    }
}

/// <summary>What FSCTL_QUERY_USN_JOURNAL says about a volume's change journal.</summary>
public sealed record UsnJournalInfo(ulong JournalId, long FirstUsn, long NextUsn, long LowestValidUsn, long MaxUsn, long MaximumSize, long AllocationDelta)
{
    public int MinVersion { get; init; }
    public int MaxVersion { get; init; }
    /// <summary>Range tracking (version 4 records of the ranges written) is on.</summary>
    public bool RangeTracking { get; init; }

    /// <summary>USN_JOURNAL_DATA_V0, V1, or V2, by the length the file system returned.</summary>
    public static UsnJournalInfo? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 56) return null;
        return new UsnJournalInfo(BinaryPrimitives.ReadUInt64LittleEndian(data), BinaryPrimitives.ReadInt64LittleEndian(data[8..]),
            BinaryPrimitives.ReadInt64LittleEndian(data[16..]), BinaryPrimitives.ReadInt64LittleEndian(data[24..]),
            BinaryPrimitives.ReadInt64LittleEndian(data[32..]), BinaryPrimitives.ReadInt64LittleEndian(data[40..]),
            BinaryPrimitives.ReadInt64LittleEndian(data[48..]))
        {
            MinVersion = data.Length >= 60 ? BinaryPrimitives.ReadUInt16LittleEndian(data[56..]) : 2,
            MaxVersion = data.Length >= 60 ? BinaryPrimitives.ReadUInt16LittleEndian(data[58..]) : 2,
            RangeTracking = data.Length >= 64 && (BinaryPrimitives.ReadUInt32LittleEndian(data[60..]) & 1) != 0,
        };
    }
}
