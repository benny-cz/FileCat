using System.Buffers.Binary;

namespace FileCat.Core.Records;

/// <summary>The two view indexes in $Extend that list items: $ObjId's $O (object IDs) and $Reparse's $R (reparse points).</summary>
public enum ViewIndexKind
{
    ObjectIds,
    ReparsePoints,
}

/// <summary>
/// An entry of $ObjId's $O or $Reparse's $R: the record (and sequence) of the item it is about, and for $O the object ID
/// with the IDs it was born with, for $R the reparse tag.
/// </summary>
public sealed record ViewIndexEntry(long Record, ushort Sequence, bool InSlack)
{
    public Guid ObjectId { get; init; }
    public Guid BirthVolume { get; init; }
    public Guid BirthObject { get; init; }
    public Guid Domain { get; init; }
    public uint Tag { get; init; }
}

/// <summary>
/// One of NTFS's view indexes in $Extend, read for what it holds (D-56): $ObjId's $O, which maps every object ID on the
/// volume to the record that has it (link tracking finds moved items through it), and $Reparse's $R, which lists every
/// reparse point by tag and record. Unlike a folder's $I30, a view entry carries a key and data of its own:
/// <code>
/// 0  data offset (u16)   2  data length (u16)   8  entry length (u16)   10  key length (u16)
/// 12 flags (u16: 1 has a subnode, whose VCN ends the entry; 2 the last entry)   16 key
/// $O: key the object ID (16); data the MFT reference (8), then the birth volume, birth object, and domain IDs (16 each)
/// $R: key the tag (u32) and the MFT reference (u64); no data
/// </code>
/// The root ($INDEX_ROOT) and every block ($INDEX_ALLOCATION) are walked from their first entry to their last. What lies
/// after a node's last entry, and every entry of a block the index's bitmap marks unused, is left behind: entries whose
/// header survived are carved from there, and so are the keys of entries NTFS removed by writing the node's end entry
/// over their header; copies of entries still in use are left out. Every entry in use is counted;
/// only those <c>keep</c> accepts are kept (an item's own, out of a volume's millions). Damage is said in
/// <see cref="Problems"/>, never thrown.
/// </summary>
public sealed class NtfsViewIndex(ViewIndexKind kind, Func<ViewIndexEntry, bool>? keep = null)
{
    private const int EntryHeader = 16, MaxProblems = 20, MaxKept = 4096;
    /// <summary>An $O entry: header, object ID, then its data (MFT reference and three IDs).</summary>
    private const int ObjectIdKey = 16, ObjectIdData = 56, ObjectIdEntry = EntryHeader + ObjectIdKey + ObjectIdData;
    /// <summary>An $R entry: header, tag and MFT reference, padded to 8 bytes.</summary>
    private const int ReparseKey = 12, ReparseEntry = 32;

    private readonly List<ViewIndexEntry> _live = [];
    private readonly HashSet<ViewIndexEntry> _liveSet = [];
    private readonly List<ViewIndexEntry> _carved = [];
    private readonly HashSet<ViewIndexEntry> _carvedSet = [];
    private bool _keptAll = true;

    public ViewIndexKind Kind => kind;
    /// <summary>The index block size its root names, or 0 when the root was not read or names no valid size.</summary>
    public int BlockSize { get; private set; }
    /// <summary>Entries in use: in the root and in the blocks in use.</summary>
    public int Live { get; private set; }
    public int Blocks { get; private set; }
    public int BlocksInUse { get; private set; }
    public List<string> Problems { get; } = [];

    /// <summary>The kept entries: those in use in index order, then those left behind that are not copies of one in use.</summary>
    public IReadOnlyList<ViewIndexEntry> Entries => [.. _live, .. _carved.Where(e => !_liveSet.Contains(e with { InSlack = false }))];

    /// <summary>The root's value: 16 bytes (indexed type, collation, block size, clusters per block), then the root node.</summary>
    public void ReadRoot(ReadOnlySpan<byte> value)
    {
        if (value.Length < 32)
        {
            Problem($"Its root ($INDEX_ROOT) is {value.Length} bytes, shorter than the 32 its header needs.");
            return;
        }
        uint indexed = BinaryPrimitives.ReadUInt32LittleEndian(value);
        uint collation = BinaryPrimitives.ReadUInt32LittleEndian(value[4..]);
        uint blockSize = BinaryPrimitives.ReadUInt32LittleEndian(value[8..]);
        if (indexed != 0) Problem($"Its root says it indexes {NtfsAttributeTypes.Name(indexed)}; a view index indexes no attribute (0).");
        // COLLATION_NTOFS_ULONGS: keys compared as a run of 32-bit numbers, as both $O's and $R's are.
        if (collation != 0x13) Problem($"Its root names collation rule 0x{collation:X}, not the 0x13 (keys compared as 32-bit numbers) that $O and $R use.");
        if (blockSize is >= 512 and <= 65536 && (blockSize & (blockSize - 1)) == 0) BlockSize = (int)blockSize;
        else Problem($"Its root names index blocks of {blockSize:N0} bytes, which is not a valid size; its blocks cannot be read.");
        ReadNode(value, 16, "its root", inUse: true);
    }

    /// <summary>
    /// One index block (INDX, <see cref="BlockSize"/> bytes; its update sequence is applied here). <paramref name="inUse"/>:
    /// what the index's bitmap says. A block not in use is not complained about, and all it holds is left behind.
    /// </summary>
    public void ReadBlock(byte[] block, int number, bool inUse)
    {
        Blocks++;
        if (inUse) BlocksInUse++;
        if (block.Length < 64 || !block.AsSpan(0, 4).SequenceEqual("INDX"u8))
        {
            if (inUse) Problem($"Block {number:N0} is in use but is not an index block (it does not start with INDX).");
            return;
        }
        if (!NtfsIndexFixups.Apply(block) && inUse)
            Problem($"Block {number:N0}: its update sequence does not check out (a torn write, or damage); its entries were read anyway.");
        ReadNode(block, 24, $"block {number:N0}", inUse);
    }

    /// <summary>
    /// A node header (the entries' offset, used size, and allocated size, all counted from the header), then its entries
    /// walked from the first to the last, then entries carved from the space after them.
    /// </summary>
    private void ReadNode(ReadOnlySpan<byte> data, int header, string where, bool inUse)
    {
        if (header + 16 > data.Length)
        {
            Problem($"In {where}, the node header runs past its end.");
            return;
        }
        uint entries = BinaryPrimitives.ReadUInt32LittleEndian(data[header..]);
        uint usedSize = BinaryPrimitives.ReadUInt32LittleEndian(data[(header + 4)..]);
        uint allocatedSize = BinaryPrimitives.ReadUInt32LittleEndian(data[(header + 8)..]);
        if (entries < 16 || entries > data.Length - header)
        {
            if (inUse) Problem($"In {where}, the entries start {entries:N0} bytes into the node, outside it; nothing there was read.");
            return;
        }
        int first = header + (int)entries;
        int used = usedSize <= data.Length - header ? header + (int)usedSize : data.Length;
        if (usedSize > data.Length - header && inUse) Problem($"In {where}, the node says {usedSize:N0} bytes are used, more than it has; it was read to its end.");
        int allocated = allocatedSize <= data.Length - header ? Math.Max(used, header + (int)allocatedSize) : data.Length;

        int at = first;
        string? stopped = null;
        while (true)
        {
            if (at + EntryHeader > used)
            {
                stopped = "its entries end without the last-entry mark";
                break;
            }
            int length = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 8)..]);
            int flags = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 12)..]);
            if (length < EntryHeader || (length & 7) != 0 || at + length > used)
            {
                stopped = $"the entry at offset {at:N0} has a length of {length:N0} bytes, which does not fit; the entries after it were not read";
                break;
            }
            if ((flags & 2) != 0) break;
            if (Parse(data.Slice(at, length), inSlack: !inUse, strict: true) is { } entry)
            {
                if (!inUse) Carved(entry);
                else
                {
                    Live++;
                    if (Wanted(entry) && _liveSet.Add(entry)) _live.Add(entry);
                }
            }
            else if (inUse) Problem($"In {where}, the entry at offset {at:N0} does not read as {(kind == ViewIndexKind.ObjectIds ? "an $O" : "an $R")} entry (its key or data is not the size it must be); it was skipped.");
            at += length;
        }
        if (stopped is not null && inUse) Problem($"In {where}, {stopped}.");

        // Left behind, 8 bytes apart after the node's last entry: entries whose header survived, and keys whose header an
        // end entry was written over (NTFS removes a node's last entry so, and the entry's key and data stay after it).
        int size = kind == ViewIndexKind.ObjectIds ? ObjectIdEntry : ReparseEntry;
        int bare = kind == ViewIndexKind.ObjectIds ? ObjectIdKey + ObjectIdData : ReparseKey;
        for (int offset = (Math.Max(at, first) + 7) & ~7; offset + bare <= allocated; offset += 8)
        {
            if (Parse(data[offset..allocated], inSlack: true, strict: false) is { } entry) offset += size - 8;
            else if (Bare(data[..allocated], offset, header) is { } key)
            {
                entry = key;
                offset += (bare + 7 & ~7) - 8;
            }
            else continue;
            Carved(entry);
        }
    }

    /// <summary>
    /// The key (and for $O the data) of an entry whose header the node's end entry was written over: right after 16
    /// bytes that read as an end entry (24 with a subnode's VCN). The record it names must be plausible, and an $R tag
    /// must keep its reserved bits clear.
    /// </summary>
    private ViewIndexEntry? Bare(ReadOnlySpan<byte> data, int offset, int header)
    {
        bool afterEnd = false;
        foreach (int length in (ReadOnlySpan<int>)[16, 24])
        {
            if (offset - length < header) continue;
            var end = data.Slice(offset - length, length);
            afterEnd |= BinaryPrimitives.ReadUInt16LittleEndian(end[8..]) == length && BinaryPrimitives.ReadUInt16LittleEndian(end[10..]) == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(end[12..]) == (length == 16 ? 2 : 3);
        }
        if (!afterEnd) return null;
        var rest = data[offset..];
        var found = kind == ViewIndexKind.ObjectIds ? ObjectIdOf(rest[..ObjectIdKey], rest.Slice(ObjectIdKey, ObjectIdData), true) : ReparseOf(rest[..ReparseKey], true);
        return Plausible(found) && (found.Tag & 0x4FFF_0000) == 0 ? found : null;
    }

    private static ViewIndexEntry ObjectIdOf(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data, bool inSlack)
    {
        ulong reference = BinaryPrimitives.ReadUInt64LittleEndian(data);
        return new ViewIndexEntry((long)(reference & 0xFFFF_FFFF_FFFF), (ushort)(reference >> 48), inSlack)
        {
            ObjectId = new Guid(key),
            BirthVolume = new Guid(data.Slice(8, 16)),
            BirthObject = new Guid(data.Slice(24, 16)),
            Domain = new Guid(data.Slice(40, 16)),
        };
    }

    private static ViewIndexEntry ReparseOf(ReadOnlySpan<byte> key, bool inSlack)
    {
        ulong reference = BinaryPrimitives.ReadUInt64LittleEndian(key[4..]);
        return new ViewIndexEntry((long)(reference & 0xFFFF_FFFF_FFFF), (ushort)(reference >> 48), inSlack) { Tag = BinaryPrimitives.ReadUInt32LittleEndian(key) };
    }

    /// <summary>
    /// What a carved entry must look like to be taken for one: records below 16 are NTFS's own files, one past any
    /// volume is noise, and an entry has a key (an object ID, or a tag).
    /// </summary>
    private static bool Plausible(ViewIndexEntry entry) => entry.Record is >= 16 and < 1L << 40 && (entry.Tag != 0 || entry.ObjectId != Guid.Empty);

    private void Carved(ViewIndexEntry entry)
    {
        if (Wanted(entry) && _carvedSet.Add(entry)) _carved.Add(entry);
    }

    private bool Wanted(ViewIndexEntry entry)
    {
        if (keep is not null && !keep(entry)) return false;
        if (_live.Count + _carved.Count < MaxKept) return true;
        if (_keptAll) Problem($"More than {MaxKept:N0} entries matched; the rest are not listed.");
        _keptAll = false;
        return false;
    }

    /// <summary>
    /// An entry at the start of <paramref name="entry"/>. <paramref name="strict"/>: a walked entry, bounded by its own
    /// length. Carved: its header must have exactly the shape NTFS writes, and the record it names must be plausible.
    /// </summary>
    private ViewIndexEntry? Parse(ReadOnlySpan<byte> entry, bool inSlack, bool strict)
    {
        if (entry.Length < EntryHeader) return null;
        int dataOffset = BinaryPrimitives.ReadUInt16LittleEndian(entry);
        int dataLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
        int length = BinaryPrimitives.ReadUInt16LittleEndian(entry[8..]);
        int keyLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[10..]);
        int flags = BinaryPrimitives.ReadUInt16LittleEndian(entry[12..]);
        if (!strict && ((flags & ~1) != 0 || length > entry.Length)) return null;
        int span = strict ? entry.Length : length;
        // An entry with a subnode keeps the subnode's VCN in its last 8 bytes.
        int body = (flags & 1) != 0 ? span - 8 : span;
        ViewIndexEntry found;
        if (kind == ViewIndexKind.ObjectIds)
        {
            if (keyLength != ObjectIdKey || dataLength != ObjectIdData || dataOffset < EntryHeader + ObjectIdKey || dataOffset + ObjectIdData > body) return null;
            if (!strict && (dataOffset != EntryHeader + ObjectIdKey || length != ((flags & 1) != 0 ? ObjectIdEntry + 8 : ObjectIdEntry))) return null;
            found = ObjectIdOf(entry.Slice(EntryHeader, ObjectIdKey), entry.Slice(dataOffset, ObjectIdData), inSlack);
        }
        else
        {
            if (keyLength != ReparseKey || EntryHeader + ReparseKey > body) return null;
            if (!strict && (dataLength != 0 || length != ((flags & 1) != 0 ? ReparseEntry + 8 : ReparseEntry))) return null;
            found = ReparseOf(entry.Slice(EntryHeader, ReparseKey), inSlack);
        }
        return strict || Plausible(found) ? found : null;
    }

    private void Problem(string text)
    {
        if (Problems.Count < MaxProblems) Problems.Add(text);
        else if (Problems.Count == MaxProblems) Problems.Add("More problems were found; they are not listed.");
    }
}
