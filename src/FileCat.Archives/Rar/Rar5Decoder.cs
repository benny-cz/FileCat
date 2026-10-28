// RAR 5 (and RAR 7) LZ decoding ported from rardecode (github.com/nwaples/rardecode v2.4.5, decode50.go),
// Copyright (c) 2015 Nicholas Waples, BSD-2-Clause; see licenses/rardecode/LICENSE. Block ends are taken from
// the block headers; every length, distance and filter goes through RarWindow's checks.

namespace FileCat.Archives.Rar;

internal sealed class Rar5Decoder : RarDecoder
{
    private const int MainSize = 306;
    private const int OffsetSize5 = 64;
    private const int OffsetSize7 = 80;
    private const int LowOffsetSize = 16;
    private const int LengthSize = 44;

    private readonly int _offsetSize;
    private readonly byte[] _lengths;
    private readonly RarHuffman _main = new();
    private readonly RarHuffman _offset = new();
    private readonly RarHuffman _lowOffset = new();
    private readonly RarHuffman _length = new();
    private readonly long[] _distances = new long[4];
    private int _lastLength;
    private RarBitReader _input = new();
    private bool _inBlock;
    private bool _lastBlock;
    private bool _finished;

    /// <param name="rar7">RAR 7's larger distance code (format version 1 in the compression information).</param>
    public Rar5Decoder(bool rar7)
    {
        _offsetSize = rar7 ? OffsetSize7 : OffsetSize5;
        _lengths = new byte[MainSize + _offsetSize + LowOffsetSize + LengthSize];
    }

    public override void StartMember(RarBitReader input, bool solid)
    {
        _input = input;
        _inBlock = false;
        _lastBlock = false;
        _finished = false;
        if (solid) return;
        Array.Clear(_distances);
        _lastLength = 0;
        Array.Clear(_lengths);
        foreach (var table in (ReadOnlySpan<RarHuffman>)[_main, _offset, _lowOffset, _length]) table.Build([]);
    }

    public override bool Decode(RarWindow window)
    {
        while (window.ShouldDecode)
        {
            if (_finished) return true;
            if (!_inBlock)
            {
                if (_lastBlock)
                {
                    _finished = true;
                    return true;
                }
                if (!ReadBlockHeader())
                {
                    // A member without packed data (an empty file of a solid run) decodes to nothing.
                    _finished = true;
                    return true;
                }
                continue;
            }
            if (!_main.TryDecode(_input, out int symbol))
            {
                _input.EndBlock();
                _inBlock = false;
                continue;
            }
            if (symbol < 256)
            {
                window.Put((byte)symbol);
            }
            else if (symbol >= 262)
            {
                DecodeMatch(window, symbol - 262);
            }
            else if (symbol >= 258)
            {
                DecodeRepeat(window, symbol - 258);
            }
            else if (symbol == 257)
            {
                if (_lastLength != 0) window.Copy(_lastLength, _distances[0]);
            }
            else
            {
                ReadFilter(window);
            }
        }
        return false;
    }

    private bool _sawHeader;

    /// <summary>Reads the next block header; false when the packed data ended cleanly before any block.</summary>
    private bool ReadBlockHeader()
    {
        if (!_input.TryReadAlignedByte(out byte flags))
        {
            if (!_sawHeader) return false;
            throw RarData.Truncated();
        }
        _sawHeader = true;
        int byteCount = ((flags >> 3) & 3) + 1;
        if (byteCount == 4) throw RarData.Corrupt("RAR 5 block header");
        byte checksum = NextHeaderByte();
        int sum = 0x5A ^ flags;
        long blockBytes = 0;
        for (int i = 0; i < byteCount; i++)
        {
            byte b = NextHeaderByte();
            sum ^= b;
            blockBytes |= (long)b << (8 * i);
        }
        if ((byte)sum != checksum) throw RarData.Corrupt("RAR 5 block header checksum");
        long blockBits = (flags & 7) + 1 + (blockBytes - 1) * 8;
        _input.BeginBlock(blockBits);
        _inBlock = true;
        _lastBlock = (flags & 0x40) != 0;
        if ((flags & 0x80) != 0)
        {
            RarHuffman.ReadLengthTable(_input, _lengths, addToOld: false);
            var lengths = _lengths.AsSpan();
            _main.Build(lengths[..MainSize]);
            _offset.Build(lengths.Slice(MainSize, _offsetSize));
            _lowOffset.Build(lengths.Slice(MainSize + _offsetSize, LowOffsetSize));
            _length.Build(lengths.Slice(MainSize + _offsetSize + LowOffsetSize, LengthSize));
        }
        else if (!_main.IsBuilt || !_offset.IsBuilt)
        {
            throw RarData.Corrupt("RAR 5 block without code tables");
        }
        return true;
    }

    private byte NextHeaderByte() => _input.TryReadAlignedByte(out byte b) ? b : throw RarData.Truncated();

    private int SlotToLength(int slot)
    {
        if (slot < 8) return slot + 2;
        int bits = slot / 4 - 1;
        int length = (4 | (slot & 3)) << bits;
        return length + (int)_input.Read(bits) + 2;
    }

    private void DecodeRepeat(RarWindow window, int index)
    {
        long distance = _distances[index];
        for (int i = index; i > 0; i--) _distances[i] = _distances[i - 1];
        _distances[0] = distance;
        _lastLength = SlotToLength(_length.Decode(_input));
        window.Copy(_lastLength, distance);
    }

    private void DecodeMatch(RarWindow window, int lengthSlot)
    {
        int length = SlotToLength(lengthSlot);
        int slot = _offset.Decode(_input);
        long distance = 1;
        if (slot < 4)
        {
            distance += slot;
        }
        else
        {
            int bitCount = slot / 2 - 1;
            distance += (long)(2 | (slot & 1)) << bitCount;
            if (bitCount >= 4)
            {
                if (bitCount > 4) distance += ReadLong(_input, bitCount - 4) << 4;
                distance += _lowOffset.Decode(_input);
            }
            else
            {
                distance += _input.Read(bitCount);
            }
        }
        if (distance > 0x100)
        {
            length++;
            if (distance > 0x2000)
            {
                length++;
                if (distance > 0x40000) length++;
            }
        }
        for (int i = _distances.Length - 1; i > 0; i--) _distances[i] = _distances[i - 1];
        _distances[0] = distance;
        _lastLength = length;
        window.Copy(length, distance);
    }

    private long ReadFilterData()
    {
        int bytes = (int)_input.Read(2) + 1;
        long value = 0;
        for (int i = 0; i < bytes; i++) value |= (long)_input.Read(8) << (8 * i);
        return value;
    }

    private void ReadFilter(RarWindow window)
    {
        long offset = ReadFilterData();
        long length = ReadFilterData();
        int type = (int)_input.Read(3);
        if (length > RarLimits.MaxFilterBlockBytes) throw RarData.Corrupt("filter block size");
        long start = window.Written + offset;
        RarFilter filter = type switch
        {
            0 => new RarFilter { Type = RarFilterType.Delta, Start = start, Length = (int)length, Channels = (int)_input.Read(5) + 1, Rar5 = true },
            1 => new RarFilter { Type = RarFilterType.E8, Start = start, Length = (int)length, Rar5 = true },
            2 => new RarFilter { Type = RarFilterType.E8E9, Start = start, Length = (int)length, Rar5 = true },
            3 => new RarFilter { Type = RarFilterType.Arm, Start = start, Length = (int)length, Rar5 = true },
            _ => throw RarData.Unsupported("an unknown RAR 5 filter"),
        };
        window.AddFilter(filter);
    }
}
