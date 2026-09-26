namespace FileCat.Core.Jobs;

/// <summary>IEEE CRC-32 (journal record integrity and range checksums); avoids an extra package dependency.</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint HashToUInt32(ReadOnlySpan<byte> data) => Append(0, data);

    /// <summary>Continues a running CRC (pass the previous result, 0 to start).</summary>
    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        uint c = ~crc;
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return ~c;
    }
}
