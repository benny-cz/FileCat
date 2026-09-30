using System.Buffers.Binary;

namespace FileCat.Core.Records;

/// <summary>An entry of $Secure's $SII index, and the header each descriptor has in $SDS: where the descriptor with this ID is kept.</summary>
/// <param name="Length">The entry's length in $SDS, its 20-byte header included.</param>
public sealed record SecureEntry(uint Hash, uint SecurityId, long Offset, uint Length);

/// <summary>
/// NTFS's $Secure (MFT record 9): every distinct security descriptor of the volume is stored once in its $SDS stream, and
/// each file points to one by the security ID in its $STANDARD_INFORMATION. $SII indexes the descriptors by ID, $SDH by
/// hash. $SDS is written in 256 KiB blocks, each followed by a mirror copy of itself.
/// </summary>
public static class NtfsSecure
{
    public const int HeaderLength = 20;
    public const long MirrorDistance = 256 * 1024;

    /// <summary>The hash NTFS keeps for a descriptor: each 32-bit little-endian word added to the running hash rotated left by 3.</summary>
    public static uint Hash(ReadOnlySpan<byte> descriptor)
    {
        uint hash = 0;
        for (int i = 0; i + 4 <= descriptor.Length; i += 4)
            hash = ((hash << 3) | (hash >> 29)) + BinaryPrimitives.ReadUInt32LittleEndian(descriptor[i..]);
        return hash;
    }

    /// <summary>The header before a descriptor in $SDS, or null when the bytes there are not one.</summary>
    public static SecureEntry? ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength) return null;
        var entry = new SecureEntry(BinaryPrimitives.ReadUInt32LittleEndian(data), BinaryPrimitives.ReadUInt32LittleEndian(data[4..]),
            BinaryPrimitives.ReadInt64LittleEndian(data[8..]), BinaryPrimitives.ReadUInt32LittleEndian(data[16..]));
        return entry.Length >= HeaderLength && entry.Length < 1 << 20 && entry.Offset >= 0 ? entry : null;
    }

    /// <summary>The entries of $SII's $INDEX_ROOT value (the index root header, then a node of view entries).</summary>
    public static List<SecureEntry> ReadSiiRoot(ReadOnlySpan<byte> rootValue) =>
        rootValue.Length < 32 ? [] : ReadNode(rootValue[16..]);

    /// <summary>The entries of one $SII INDX block (its update sequence applied first).</summary>
    public static List<SecureEntry> ReadSiiBlock(byte[] block)
    {
        if (block.Length < 64 || !block.AsSpan(0, 4).SequenceEqual("INDX"u8)) return [];
        NtfsIndexFixups.Apply(block);
        return ReadNode(block.AsSpan(24));
    }

    /// <summary>A node header (entries' offset, used size, allocated size, flags), then entries: data offset and length, entry length, key length, flags, key (the ID), data (the header).</summary>
    private static List<SecureEntry> ReadNode(ReadOnlySpan<byte> node)
    {
        var found = new List<SecureEntry>();
        if (node.Length < 16) return found;
        int at = (int)BinaryPrimitives.ReadUInt32LittleEndian(node);
        int used = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(node[4..]), (uint)node.Length);
        for (int guard = 0; at + 16 <= used && guard < 100_000; guard++)
        {
            int dataOffset = BinaryPrimitives.ReadUInt16LittleEndian(node[at..]);
            int dataLength = BinaryPrimitives.ReadUInt16LittleEndian(node[(at + 2)..]);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(node[(at + 8)..]);
            int flags = BinaryPrimitives.ReadUInt16LittleEndian(node[(at + 12)..]);
            if ((flags & 2) != 0 || length < 16) break; // the last entry holds no key
            if (dataLength >= HeaderLength && at + dataOffset + HeaderLength <= used && ReadHeader(node.Slice(at + dataOffset, HeaderLength)) is { } entry)
                found.Add(entry);
            at += length;
        }
        return found;
    }
}

/// <summary>The update sequence of an NTFS multi-sector structure (INDX, RCRD, RSTR): each 512-byte sector's last two bytes restored.</summary>
public static class NtfsIndexFixups
{
    /// <summary>Restores the sectors' last bytes; false when a sector's check bytes do not match (a torn or damaged write).</summary>
    public static bool Apply(byte[] block, int sector = 512)
    {
        if (block.Length < 8) return false;
        int usa = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(4));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(6));
        if (count < 2 || usa + count * 2 > block.Length) return false;
        ushort check = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(usa));
        bool intact = true;
        for (int i = 1; i < count && i * sector <= block.Length; i++)
        {
            int at = i * sector - 2;
            if (BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(at)) != check) intact = false;
            block[at] = block[usa + i * 2];
            block[at + 1] = block[usa + i * 2 + 1];
        }
        return intact;
    }
}
