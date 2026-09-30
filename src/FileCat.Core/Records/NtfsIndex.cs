using System.Buffers.Binary;
using System.Text;

namespace FileCat.Core.Records;

/// <summary>A $FILE_NAME found in a folder's index: the item it names (file reference) and the name's own copy of its times and size.</summary>
public sealed record IndexName(long Record, ushort Sequence, NtfsFileName Name, bool InSlack);

/// <summary>
/// A folder's file-name index ($I30), read for what it holds (D-56): the live entries, and the entries left behind in
/// the unused tail of each index block. NTFS moves and removes entries without clearing the space they used, so the
/// tail often keeps names of items deleted, renamed, or moved away from the folder, with their times and sizes (the
/// "INDX slack" forensics reads). Found entries must name this folder as their parent, which keeps noise out.
/// </summary>
public static class NtfsIndex
{
    private const int EntryHeader = 16, FileNameMinimum = 66;

    /// <summary>The entries of an $INDEX_ROOT value (resident, in the folder's own record).</summary>
    public static List<IndexName> ReadRoot(ReadOnlySpan<byte> value, long folder, ushort folderSequence)
    {
        var found = new List<IndexName>();
        if (value.Length < 32) return found;
        // The index header follows the root's own 16 bytes; its offsets count from the header.
        ReadNode(value, 16, folder, folderSequence, found);
        return found;
    }

    /// <summary>
    /// The names left behind that the folder no longer has: entries move between blocks as an index grows, so copies of
    /// names still in use linger too, and those are left out. Each name once, its newest copy.
    /// </summary>
    public static List<IndexName> LeftBehind(IEnumerable<IndexName> all)
    {
        var entries = all.Where(n => !n.Name.IsDosAlias).ToList();
        var live = new HashSet<string>(entries.Where(n => !n.InSlack).Select(n => n.Name.Name), StringComparer.OrdinalIgnoreCase);
        return entries.Where(n => n.InSlack && !live.Contains(n.Name.Name))
            .GroupBy(n => n.Name.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(n => n.Name.Changed).ThenByDescending(n => n.Record).First())
            .OrderBy(n => n.Name.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The bytes per index block an $INDEX_ROOT names (4,096 on most volumes), or 0.</summary>
    public static int BlockSize(ReadOnlySpan<byte> root) => root.Length >= 12 ? (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(root[8..]), 1 << 20) : 0;

    /// <summary>
    /// The entries of one index block ("INDX", from $INDEX_ALLOCATION), live and left behind. The block's update
    /// sequence is applied first; a torn block is still read (its entries are checked one by one).
    /// </summary>
    public static List<IndexName> ReadBlock(byte[] block, long folder, ushort folderSequence)
    {
        var found = new List<IndexName>();
        if (block.Length < 64 || !block.AsSpan(0, 4).SequenceEqual("INDX"u8)) return found;
        ApplyFixups(block);
        ReadNode(block, 24, folder, folderSequence, found);
        return found;
    }

    private static void ApplyFixups(byte[] block)
    {
        int usa = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(4));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(6));
        if (count < 2 || usa + count * 2 > block.Length) return;
        ushort usn = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(usa));
        for (int i = 1; i < count && i * 512 <= block.Length; i++)
        {
            int at = i * 512 - 2;
            if (BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(at)) == usn)
            {
                block[at] = block[usa + i * 2];
                block[at + 1] = block[usa + i * 2 + 1];
            }
        }
    }

    /// <summary>A node's live entries (walked from its first to its last), then $FILE_NAMEs carved from the rest of its space.</summary>
    private static void ReadNode(ReadOnlySpan<byte> data, int header, long folder, ushort folderSequence, List<IndexName> found)
    {
        if (header + 16 > data.Length) return;
        int first = header + (int)BinaryPrimitives.ReadUInt32LittleEndian(data[header..]);
        int used = header + (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data[(header + 4)..]), (uint)data.Length);
        int allocated = header + (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data[(header + 8)..]), (uint)data.Length);
        used = Math.Min(used, data.Length);
        allocated = Math.Clamp(allocated, used, data.Length);
        var live = new HashSet<(long, string)>();
        int at = first;
        for (int n = 0; at + EntryHeader <= used && n < 4096; n++)
        {
            int length = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 8)..]);
            int keyLength = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 10)..]);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 12)..]);
            if (length < EntryHeader || at + length > used) break;
            if (keyLength >= FileNameMinimum && TryName(data.Slice(at + EntryHeader, Math.Min(keyLength, length - EntryHeader)), folder, folderSequence, out var name))
            {
                ulong reference = BinaryPrimitives.ReadUInt64LittleEndian(data[at..]);
                var entry = new IndexName((long)(reference & 0xFFFF_FFFF_FFFF), (ushort)(reference >> 48), name, false);
                found.Add(entry);
                live.Add((entry.Record, name.Name));
            }
            if ((flags & 2) != 0) break; // the last entry
            at += length;
        }
        // Left behind: carve $FILE_NAMEs (their parent is this folder) from the space after the live entries, 8 bytes at a time.
        var carved = new HashSet<(long, string, long)>();
        for (int offset = (Math.Max(at, first) + 7) & ~7; offset + FileNameMinimum <= allocated; offset += 8)
        {
            if (!TryName(data.Slice(offset, allocated - offset), folder, folderSequence, out var name)) continue;
            // The entry's header, when it survived, sits 16 bytes before its key.
            long record = -1;
            ushort sequence = 0;
            if (offset - EntryHeader >= header)
            {
                ulong reference = BinaryPrimitives.ReadUInt64LittleEndian(data[(offset - EntryHeader)..]);
                long candidate = (long)(reference & 0xFFFF_FFFF_FFFF);
                if (candidate is > 0 and < 1L << 40)
                {
                    record = candidate;
                    sequence = (ushort)(reference >> 48);
                }
            }
            if (live.Contains((record, name.Name)) || !carved.Add((record, name.Name, name.Modified))) continue;
            found.Add(new IndexName(record, sequence, name, true));
            offset += (FileNameMinimum + name.Name.Length * 2 + 7 & ~7) - 8;
        }
    }

    /// <summary>A $FILE_NAME at the start of <paramref name="key"/> that names <paramref name="folder"/> as its parent and reads as one.</summary>
    private static bool TryName(ReadOnlySpan<byte> key, long folder, ushort folderSequence, out NtfsFileName name)
    {
        name = null!;
        if (key.Length < FileNameMinimum) return false;
        ulong parent = BinaryPrimitives.ReadUInt64LittleEndian(key);
        if ((long)(parent & 0xFFFF_FFFF_FFFF) != folder || (ushort)(parent >> 48) != folderSequence) return false;
        int length = key[64], space = key[65];
        if (length == 0 || space > 3 || FileNameMinimum + length * 2 > key.Length) return false;
        long created = BinaryPrimitives.ReadInt64LittleEndian(key[8..]), modified = BinaryPrimitives.ReadInt64LittleEndian(key[16..]);
        if (!Plausible(created) || !Plausible(modified)) return false;
        long size = BinaryPrimitives.ReadInt64LittleEndian(key[48..]), allocated = BinaryPrimitives.ReadInt64LittleEndian(key[40..]);
        if (size < 0 || allocated < 0) return false;
        string text = Encoding.Unicode.GetString(key.Slice(FileNameMinimum, length * 2));
        foreach (char c in text)
            if (char.IsControl(c) || c == '/' || (c is '\\' or ':') && space != 0) return false;
        name = new NtfsFileName(folder, folderSequence, created, modified, BinaryPrimitives.ReadInt64LittleEndian(key[24..]),
            BinaryPrimitives.ReadInt64LittleEndian(key[32..]), allocated, size, BinaryPrimitives.ReadUInt32LittleEndian(key[56..]),
            BinaryPrimitives.ReadUInt32LittleEndian(key[60..]), (byte)space, text);
        return true;
    }

    /// <summary>Between 1980 and a year from now: what a time kept by a real file looks like.</summary>
    private static bool Plausible(long fileTime) =>
        fileTime > Earliest && RecordText.ToUtc(fileTime) is { } t && t < DateTime.UtcNow.AddYears(1);

    private static readonly long Earliest = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
}
