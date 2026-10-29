// FileCat replacement for DotNetZip's CRC32 class, whose header says it is based on example code from vbAccelerator: a
// plain table-driven CRC-32 (IEEE 802.3, reflected polynomial 0xEDB88320) with only the members ZlibBaseStream's gzip
// flavour uses, so the shipped library carries no code of unestablished provenance. See THIRD-PARTY-CHANGES.md.
// Copyright (c) 2026 Benny and FileCat contributors. MIT license.

using System;

namespace SharpCompress.Compressors.Deflate;

internal sealed class CRC32
{
    private static readonly uint[] Table = MakeTable();
    private uint _register = 0xFFFF_FFFF;

    /// <summary>How many bytes have gone through the CRC so far.</summary>
    public long TotalBytesRead { get; private set; }

    /// <summary>The CRC-32 of the bytes so far, as a signed integer, the way gzip's trailer is compared.</summary>
    public int Crc32Result => unchecked((int)~_register);

    /// <summary>Runs <paramref name="count"/> bytes of <paramref name="block"/> from <paramref name="offset"/> through the CRC.</summary>
    public void SlurpBlock(byte[] block, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, block.Length - offset);
        uint crc = _register;
        for (int i = offset, end = offset + count; i < end; i++)
            crc = Table[(crc ^ block[i]) & 0xFF] ^ (crc >> 8);
        _register = crc;
        TotalBytesRead += count;
    }

    private static uint[] MakeTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB8_8320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
