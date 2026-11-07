using System.Buffers.Binary;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>Bounded 7z integer/bit/field reader. It never skips an unknown property on behalf of an assessment.</summary>
internal ref struct SevenZipStrictHeaderReader(ReadOnlySpan<byte> bytes)
{
    private ReadOnlySpan<byte> _remaining = bytes;
    internal int Remaining => _remaining.Length;
    internal byte Byte()
    {
        if (_remaining.IsEmpty) throw Invalid("The 7z metadata ends inside a field.");
        byte value = _remaining[0]; _remaining = _remaining[1..]; return value;
    }
    internal ReadOnlySpan<byte> Bytes(int count)
    {
        if (count < 0 || count > _remaining.Length) throw Invalid("The 7z metadata field exceeds its containing record.");
        var value = _remaining[..count]; _remaining = _remaining[count..]; return value;
    }
    internal ulong Number()
    {
        byte first = Byte(), mask = 128; ulong value = 0;
        for (int index = 0; index < 8; index++)
        {
            if ((first & mask) == 0) return value | ((ulong)(first & (mask - 1)) << (index * 8));
            value |= (ulong)Byte() << (index * 8); mask >>= 1;
        }
        return value;
    }
    internal long Size()
    {
        ulong value = Number();
        if (value > long.MaxValue) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "A 7z extent exceeds the strict write profile's supported range.");
        return (long)value;
    }
    internal int Count(int maximum)
    {
        ulong value = Number();
        if (value > (ulong)maximum) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "A 7z metadata count exceeds the strict write profile budget.");
        return (int)value;
    }
    internal uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
    internal ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Bytes(8));
    internal byte Tag()
    {
        ulong value = Number();
        if (value > byte.MaxValue) throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The 7z record identifier needs a preservation implementation.");
        return (byte)value;
    }
    internal void Require(byte tag)
    {
        if (Tag() != tag) throw Invalid("The 7z metadata records are missing, repeated or out of order.");
    }
    internal void End()
    {
        if (Remaining != 0) throw Invalid("The 7z metadata record contains unaccounted bytes.");
    }
    internal bool[] Bits(int count, bool optional = false)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (optional)
        {
            byte all = Byte();
            if (all > 1) throw Invalid("The 7z defined-vector flag is invalid.");
            if (all == 1) { var complete = new bool[count]; Array.Fill(complete, true); return complete; }
        }
        var raw = Bytes((count + 7) / 8); var result = new bool[count];
        for (int index = 0; index < count; index++) result[index] = (raw[index / 8] & (128 >> (index & 7))) != 0;
        // Bits outside the declared vector have no semantic value; do not invent a zero-padding requirement.
        return result;
    }
    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidArchive, message);
}
