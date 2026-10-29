using System.Buffers.Binary;
using System.Text;

namespace FileCat.Archives.Rar;

/// <summary>Where one part of a member's packed data lies.</summary>
internal readonly record struct RarSegment(int Volume, long Offset, long Length);

/// <summary>One member as the headers describe it, its packed data gathered across volumes.</summary>
internal sealed class RarMember
{
    public required string Name { get; init; }
    public MemberKind Kind { get; set; }
    public string? LinkTarget { get; set; }
    public long UnpackedSize { get; set; }      // -1 when the archive does not record it
    public long PackedSize { get; set; }
    public DateTime? ModifiedUtc { get; set; }
    public bool Encrypted { get; set; }
    public int Method { get; set; }              // 0 when stored
    public int Version { get; set; }             // decoder: 15, 20, 26, 29, 36 (RAR 1.5–4.x), 50, 70 (RAR 5/7)
    public long Dictionary { get; set; }
    public bool SolidContinuation { get; set; }  // decodes with the previous member's state
    public uint? Crc32 { get; set; }
    public byte[]? Blake2sp { get; set; }
    public List<RarSegment> Segments { get; } = [];
    public bool HasFirstPart { get; set; }
    public bool HasLastPart { get; set; }
    public bool HasGap { get; set; }            // a part in between is missing: listed once, never decoded
    public bool Complete => HasFirstPart && HasLastPart && !HasGap;
}

/// <summary>A bounds-checked reader over one header's bytes; anything short or oversized is damage.</summary>
internal ref struct RarHeaderReader(ReadOnlySpan<byte> data)
{
    private ReadOnlySpan<byte> _data = data;

    public readonly int Remaining => _data.Length;

    public ulong VarInt()
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (_data.IsEmpty) throw RarData.Corrupt("header ends inside a number");
            byte b = _data[0];
            _data = _data[1..];
            if (shift == 63 && (b & 0x7E) != 0) throw RarData.Corrupt("header number over 64 bits");
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
        }
        throw RarData.Corrupt("header number over 64 bits");
    }

    /// <summary>A size or count that must fit a non-negative <see cref="long"/>.</summary>
    public long Size()
    {
        ulong value = VarInt();
        if (value > long.MaxValue) throw RarData.Corrupt("header size out of range");
        return (long)value;
    }

    public int Int(int max)
    {
        ulong value = VarInt();
        if (value > (ulong)max) throw RarData.Corrupt("header value out of range");
        return (int)value;
    }

    public byte Byte()
    {
        if (_data.IsEmpty) throw RarData.Corrupt("header too short");
        byte b = _data[0];
        _data = _data[1..];
        return b;
    }

    public ushort UInt16()
    {
        var b = Bytes(2);
        return BinaryPrimitives.ReadUInt16LittleEndian(b);
    }

    public uint UInt32()
    {
        var b = Bytes(4);
        return BinaryPrimitives.ReadUInt32LittleEndian(b);
    }

    public ulong UInt64()
    {
        var b = Bytes(8);
        return BinaryPrimitives.ReadUInt64LittleEndian(b);
    }

    public ReadOnlySpan<byte> Bytes(long count)
    {
        if (count < 0 || count > _data.Length) throw RarData.Corrupt("header field past the end of the header");
        var result = _data[..(int)count];
        _data = _data[(int)count..];
        return result;
    }

    public ReadOnlySpan<byte> Rest()
    {
        var rest = _data;
        _data = default;
        return rest;
    }

    public static string Utf8(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > RarLimits.MaxNameBytes) throw RarData.Corrupt("name too long");
        int nul = bytes.IndexOf((byte)0);
        if (nul >= 0) bytes = bytes[..nul];
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>A Windows FILETIME (100 ns since 1601) as UTC, or null when out of range.</summary>
    public static DateTime? FileTime(ulong ticks) =>
        ticks <= (ulong)(DateTime.MaxValue.Ticks - DateTime.FromFileTimeUtc(0).Ticks) ? DateTime.FromFileTimeUtc((long)ticks) : null;

    public static DateTime? UnixTime(long seconds, long nanoseconds = 0)
    {
        try { return DateTime.UnixEpoch.AddSeconds(seconds).AddTicks(nanoseconds / 100); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
