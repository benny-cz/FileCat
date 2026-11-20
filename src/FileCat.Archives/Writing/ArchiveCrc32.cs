namespace FileCat.Archives.Writing;

/// <summary>IEEE CRC-32 for archive wire records. Original implementation; no restored CRC dependency.</summary>
internal struct ArchiveCrc32
{
    private static readonly uint[] Table = MakeTable();
    private uint _inverted;
    private bool _started;

    internal readonly uint Value => _started ? ~_inverted : 0;

    internal void Append(ReadOnlySpan<byte> bytes)
    {
        uint value = _started ? _inverted : uint.MaxValue;
        foreach (byte item in bytes) value = Table[(value ^ item) & 255] ^ (value >> 8);
        _inverted = value;
        _started = true;
    }

    internal static uint Compute(ReadOnlySpan<byte> bytes)
    {
        var crc = new ArchiveCrc32();
        crc.Append(bytes);
        return crc.Value;
    }

    private static uint[] MakeTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320u);
            table[index] = value;
        }
        return table;
    }
}
