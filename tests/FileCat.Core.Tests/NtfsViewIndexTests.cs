using System.Buffers.Binary;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

/// <summary>D-56: $Extend's view indexes ($ObjId's $O, $Reparse's $R), read from synthetic nodes the way NTFS lays them out.</summary>
public sealed class NtfsViewIndexTests
{
    private static readonly Guid Volume = new("0b5f2a64-9c1e-4b8e-9a51-3f0c2d7e8a10");
    private static readonly Guid Birth = new("e9b1a2c0-5f3e-11ea-8000-00155d8a1b2c");

    /// <summary>An $O entry: data offset 32, data length 56, entry 88 bytes (96 with a subnode's VCN), key 16.</summary>
    private static byte[] ObjectIdEntry(Guid id, long record, ushort sequence, Guid? birthObject = null, int flags = 0)
    {
        var e = new byte[(flags & 1) != 0 ? 96 : 88];
        BinaryPrimitives.WriteUInt16LittleEndian(e, 32);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(2), 56);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(8), (ushort)e.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(10), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(12), (ushort)flags);
        id.TryWriteBytes(e.AsSpan(16));
        BinaryPrimitives.WriteUInt64LittleEndian(e.AsSpan(32), (ulong)record | (ulong)sequence << 48);
        Volume.TryWriteBytes(e.AsSpan(40));
        (birthObject ?? id).TryWriteBytes(e.AsSpan(56));
        if ((flags & 1) != 0) BinaryPrimitives.WriteInt64LittleEndian(e.AsSpan(88), 4); // the subnode's VCN
        return e;
    }

    /// <summary>An $R entry: data offset 28, no data, entry 32 bytes (40 with a subnode's VCN), key 12: the tag and the MFT reference.</summary>
    private static byte[] ReparseEntry(uint tag, long record, ushort sequence, int flags = 0)
    {
        var e = new byte[(flags & 1) != 0 ? 40 : 32];
        BinaryPrimitives.WriteUInt16LittleEndian(e, 28);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(8), (ushort)e.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(10), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(12), (ushort)flags);
        BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(16), tag);
        BinaryPrimitives.WriteUInt64LittleEndian(e.AsSpan(20), (ulong)record | (ulong)sequence << 48);
        return e;
    }

    private static byte[] LastEntry()
    {
        var e = new byte[16];
        e[8] = 16;
        e[12] = 2;
        return e;
    }

    /// <summary>A node at <paramref name="header"/> in <paramref name="size"/> bytes: its header, the entries, the last entry, then what was left after it.</summary>
    private static byte[] Node(int size, int header, int entriesOffset, IEnumerable<byte[]> entries, byte[]? slack = null, bool last = true)
    {
        var node = new byte[size];
        int at = header + entriesOffset;
        foreach (var entry in entries.Concat(last ? [LastEntry()] : []))
        {
            entry.CopyTo(node, at);
            at += entry.Length;
        }
        slack?.CopyTo(node, at);
        BinaryPrimitives.WriteUInt32LittleEndian(node.AsSpan(header), (uint)entriesOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(node.AsSpan(header + 4), (uint)(at - header));
        BinaryPrimitives.WriteUInt32LittleEndian(node.AsSpan(header + 8), (uint)(size - header));
        return node;
    }

    /// <summary>An $INDEX_ROOT value: a view index (no attribute), ULONGs collation, 4 KiB blocks, then the root node.</summary>
    private static byte[] Root(IEnumerable<byte[]> entries, byte[]? slack = null, int size = 512)
    {
        var root = Node(size, 16, 16, entries, slack);
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(4), 0x13);
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(8), 4096);
        root[12] = 1;
        return root;
    }

    /// <summary>A 4 KiB INDX block as the disk holds it: the last two bytes of each sector replaced by the update sequence number.</summary>
    private static byte[] Block(IEnumerable<byte[]> entries, byte[]? slack = null, bool last = true)
    {
        var block = Node(4096, 24, 40, entries, slack, last);
        "INDX"u8.CopyTo(block);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(4), 40);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(6), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(40), 7);
        for (int i = 1; i <= 8; i++)
        {
            block[40 + i * 2] = block[i * 512 - 2];
            block[41 + i * 2] = block[i * 512 - 1];
            BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(i * 512 - 2), 7);
        }
        return block;
    }

    [Fact]
    public void Object_IDs_read_with_their_records_and_birth_IDs_and_all_are_counted()
    {
        var mine = Guid.NewGuid();
        var index = new NtfsViewIndex(ViewIndexKind.ObjectIds, e => e.Record == 100);
        index.ReadRoot(Root([ObjectIdEntry(Guid.NewGuid(), 40, 1, flags: 1)]));
        index.ReadBlock(Block([ObjectIdEntry(Guid.NewGuid(), 41, 2), ObjectIdEntry(mine, 100, 7, Birth), ObjectIdEntry(Guid.NewGuid(), 42, 3)]), 0, inUse: true);
        Assert.Empty(index.Problems);
        Assert.Equal(4096, index.BlockSize);
        Assert.Equal(4, index.Live);
        Assert.Equal((1, 1), (index.Blocks, index.BlocksInUse));
        var entry = Assert.Single(index.Entries);
        Assert.Equal((100L, (ushort)7, false), (entry.Record, entry.Sequence, entry.InSlack));
        Assert.Equal((mine, Volume, Birth, Guid.Empty), (entry.ObjectId, entry.BirthVolume, entry.BirthObject, entry.Domain));
    }

    [Fact]
    public void Entries_left_behind_are_carved_and_copies_of_ones_in_use_left_out()
    {
        var mine = Guid.NewGuid();
        var earlier = Guid.NewGuid();
        var entry = ObjectIdEntry(mine, 100, 7);
        var index = new NtfsViewIndex(ViewIndexKind.ObjectIds, e => e.Record == 100);
        // A block not in use first: what it holds is left behind, but a copy of an entry in use (found later) is not.
        index.ReadBlock(Block([entry, ObjectIdEntry(earlier, 100, 5)]), 0, inUse: false);
        // After the last entry: a copy of the entry in use, an earlier item's entry for the record, zeros, and noise.
        var slack = entry.Concat(ObjectIdEntry(Guid.NewGuid(), 100, 6)).Concat(new byte[24]).Concat(ObjectIdEntry(Guid.NewGuid(), 7, 1)).ToArray();
        index.ReadBlock(Block([entry, ObjectIdEntry(Guid.NewGuid(), 200, 1)], slack), 1, inUse: true);
        Assert.Empty(index.Problems);
        Assert.Equal(2, index.Live);
        Assert.Equal((2, 1), (index.Blocks, index.BlocksInUse));
        Assert.Equal([mine], index.Entries.Where(e => !e.InSlack).Select(e => e.ObjectId));
        var left = index.Entries.Where(e => e.InSlack).ToList();
        Assert.Equal([(ushort)5, (ushort)6], left.Select(e => e.Sequence).Order());
        Assert.Equal(earlier, left.Single(e => e.Sequence == 5).ObjectId);
        // Its ID's first eight bytes also look like a plausible MFT reference when a carver mistakes
        // the intact entry header for a bare key after the end mark.
        var all = new NtfsViewIndex(ViewIndexKind.ObjectIds);
        all.ReadBlock(Block([ObjectIdEntry(mine, 100, 7)], ObjectIdEntry(new Guid("00000064-0000-0000-0000-000000000001"), 7, 1)), 0, inUse: true);
        Assert.Single(all.Entries);
    }

    /// <summary>What NTFS does to remove a node's last entry: its end entry written over that entry's start, the used size shrunk to it.</summary>
    private static void RemoveLast(byte[] block, int at)
    {
        LastEntry().CopyTo(block, at);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(28), (uint)(at + 16 - 24));
    }

    [Fact]
    public void Removed_last_entries_are_found_by_the_key_after_the_end_entry_written_over_them()
    {
        // Three reparse points; the last two removed, last first: each one's key stays behind the end entry that replaced it.
        var reparse = Block([ReparseEntry(0xA000_000C, 301, 1), ReparseEntry(0xA000_0003, 300, 8), ReparseEntry(0xA000_001D, 300, 9)]);
        RemoveLast(reparse, 64 + 64);
        RemoveLast(reparse, 64 + 32);
        var index = new NtfsViewIndex(ViewIndexKind.ReparsePoints, e => e.Record == 300);
        index.ReadBlock(reparse, 0, inUse: true);
        Assert.Empty(index.Problems);
        Assert.Equal(1, index.Live);
        Assert.Equal([(0xA000_0003u, (ushort)8, true), (0xA000_001Du, (ushort)9, true)], index.Entries.Select(e => (e.Tag, e.Sequence, e.InSlack)));

        // An object ID removed the same way keeps its key and data: the record and the birth IDs.
        var gone = Guid.NewGuid();
        var objectIds = Block([ObjectIdEntry(Guid.NewGuid(), 101, 1), ObjectIdEntry(gone, 100, 4, Birth)]);
        RemoveLast(objectIds, 64 + 88);
        var ids = new NtfsViewIndex(ViewIndexKind.ObjectIds, e => e.Record == 100);
        ids.ReadBlock(objectIds, 0, inUse: true);
        Assert.Empty(ids.Problems);
        var left = Assert.Single(ids.Entries);
        Assert.Equal((gone, 100L, (ushort)4, Volume, Birth, true), (left.ObjectId, left.Record, left.Sequence, left.BirthVolume, left.BirthObject, left.InSlack));

        // Zeros, or a tag with its reserved bits set, after an end entry are not keys.
        var noise = Block([ReparseEntry(0xA000_000C, 301, 1), ReparseEntry(0x0123_4567, 300, 8)]);
        RemoveLast(noise, 64 + 32);
        var none = new NtfsViewIndex(ViewIndexKind.ReparsePoints);
        none.ReadBlock(noise, 0, inUse: true);
        Assert.Single(none.Entries);
    }

    [Fact]
    public void Reparse_points_read_as_tags_and_records_with_ones_left_behind()
    {
        var index = new NtfsViewIndex(ViewIndexKind.ReparsePoints, e => e.Record == 300);
        index.ReadRoot(Root([ReparseEntry(0xA000_0003, 300, 9, flags: 1), ReparseEntry(0xA000_000C, 301, 1)], ReparseEntry(0x9000_601A, 300, 8)));
        index.ReadBlock(Block([ReparseEntry(0x8000_0017, 302, 1)], ReparseEntry(0xA000_000C, 300, 8)), 0, inUse: true);
        Assert.Empty(index.Problems);
        Assert.Equal(3, index.Live);
        var live = Assert.Single(index.Entries, e => !e.InSlack);
        Assert.Equal((0xA000_0003u, 300L, (ushort)9), (live.Tag, live.Record, live.Sequence));
        // The root's own slack, and the block's: what this record's earlier items were (a cloud file, a link).
        Assert.Equal([0x9000_601Au, 0xA000_000Cu], index.Entries.Where(e => e.InSlack).Select(e => e.Tag));
    }

    [Fact]
    public void Damaged_view_index_nodes_are_described_not_thrown()
    {
        static List<string> Problems(Action<NtfsViewIndex> read, ViewIndexKind kind = ViewIndexKind.ReparsePoints)
        {
            var index = new NtfsViewIndex(kind);
            read(index);
            return index.Problems;
        }
        var entry = ReparseEntry(0xA000_0003, 300, 9);

        Assert.Contains("shorter than the 32", Assert.Single(Problems(i => i.ReadRoot(new byte[20]))), StringComparison.Ordinal);
        // The root's header: the wrong attribute, collation, and block size; its entries are still read.
        var odd = Root([entry]);
        BinaryPrimitives.WriteUInt32LittleEndian(odd, 0x30);
        BinaryPrimitives.WriteUInt32LittleEndian(odd.AsSpan(4), 0x01);
        BinaryPrimitives.WriteUInt32LittleEndian(odd.AsSpan(8), 3000);
        var index = new NtfsViewIndex(ViewIndexKind.ReparsePoints);
        index.ReadRoot(odd);
        Assert.Equal(3, index.Problems.Count);
        Assert.Equal(0, index.BlockSize);
        Assert.Equal(1, index.Live);

        // An entry's length of zero, or one past the node's used space: the walk stops there and says so.
        var zero = Block([entry, entry]);
        BinaryPrimitives.WriteUInt16LittleEndian(zero.AsSpan(64 + 32 + 8), 0);
        Assert.Contains("has a length of 0 bytes", Assert.Single(Problems(i => i.ReadBlock(zero, 3, true))), StringComparison.Ordinal);
        var past = Block([entry]);
        BinaryPrimitives.WriteUInt16LittleEndian(past.AsSpan(64 + 8), 4000);
        Assert.Contains("does not fit", Assert.Single(Problems(i => i.ReadBlock(past, 3, true))), StringComparison.Ordinal);
        // Entries said to start outside the node, a used size larger than the node, and no last entry.
        var outside = Block([entry]);
        BinaryPrimitives.WriteUInt32LittleEndian(outside.AsSpan(24), 9000);
        Assert.Contains("outside it", Assert.Single(Problems(i => i.ReadBlock(outside, 3, true))), StringComparison.Ordinal);
        var used = Block([entry]);
        BinaryPrimitives.WriteUInt32LittleEndian(used.AsSpan(28), 70_000);
        Assert.Contains(Problems(i => i.ReadBlock(used, 3, true)), p => p.Contains("more than it has", StringComparison.Ordinal));
        Assert.Contains("without the last-entry mark", Assert.Single(Problems(i => i.ReadBlock(Block([entry], last: false), 3, true))), StringComparison.Ordinal);

        // A key of the wrong size is skipped and the walk goes on.
        var wrongKey = ObjectIdEntry(Guid.NewGuid(), 100, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wrongKey.AsSpan(10), 12);
        var objectIds = new NtfsViewIndex(ViewIndexKind.ObjectIds);
        objectIds.ReadBlock(Block([wrongKey, ObjectIdEntry(Guid.NewGuid(), 101, 1)]), 0, true);
        Assert.Contains("does not read as an $O entry", Assert.Single(objectIds.Problems), StringComparison.Ordinal);
        Assert.Equal(101, Assert.Single(objectIds.Entries).Record);

        // Not an index block: a problem when in use, nothing when the bitmap says it is free.
        Assert.Contains("not an index block", Assert.Single(Problems(i => i.ReadBlock(new byte[4096], 2, true))), StringComparison.Ordinal);
        Assert.Empty(Problems(i => i.ReadBlock(new byte[4096], 2, false)));
        // A torn block is said, and its entries are still read.
        var torn = Block([entry]);
        torn[1022] ^= 0xFF;
        var tornIndex = new NtfsViewIndex(ViewIndexKind.ReparsePoints);
        tornIndex.ReadBlock(torn, 5, true);
        Assert.Contains("torn write", Assert.Single(tornIndex.Problems), StringComparison.Ordinal);
        Assert.Equal(1, tornIndex.Live);

        // The problems said are capped: 30 bad blocks give 20 and one line for the rest.
        var many = Problems(i => { for (int n = 0; n < 30; n++) i.ReadBlock(new byte[4096], n, true); });
        Assert.Equal(21, many.Count);
        Assert.Equal("More problems were found; they are not listed.", many[^1]);
    }

    [Fact]
    public void The_entries_kept_are_capped()
    {
        var index = new NtfsViewIndex(ViewIndexKind.ReparsePoints);
        for (int block = 0; block < 40; block++)
            index.ReadBlock(Block(Enumerable.Range(0, 120).Select(i => ReparseEntry(0xA000_000C, 1000 + block * 120 + i, 1))), block, true);
        Assert.Equal(4800, index.Live);
        Assert.Equal(4096, index.Entries.Count);
        Assert.Contains($"More than {4096:N0} entries matched", Assert.Single(index.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var random = new Random(56);
        for (int round = 0; round < 3000; round++)
        {
            var kind = round % 2 == 0 ? ViewIndexKind.ObjectIds : ViewIndexKind.ReparsePoints;
            var index = new NtfsViewIndex(kind);
            var root = new byte[random.Next(0, 600)];
            random.NextBytes(root);
            // Plausible headers often enough to get past them.
            if (root.Length >= 32 && round % 3 == 0) BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(16), 16);
            index.ReadRoot(root);
            var block = Block([kind == ViewIndexKind.ObjectIds ? ObjectIdEntry(Guid.NewGuid(), 100, 1) : ReparseEntry(0xA000_0003, 100, 1)]);
            for (int flips = random.Next(1, 64); flips > 0; flips--) block[random.Next(block.Length)] = (byte)random.Next(256);
            index.ReadBlock(block, round, random.Next(2) == 0);
            _ = index.Entries;
        }
    }
}
