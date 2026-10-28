// Bit order and EOF semantics follow rardecode (github.com/nwaples/rardecode v2.4.5, bit_reader.go),
// Copyright (c) 2015 Nicholas Waples, BSD-2-Clause; see licenses/rardecode/LICENSE.

namespace FileCat.Archives.Rar;

/// <summary>
/// The bits of one member's packed data, most significant first as RAR packs them. Peeking past the end of the data,
/// or of the current RAR 5 block, yields zero bits so a decoder can look ahead; consuming such a bit throws, so a
/// short or hostile stream can never make a decoder loop.
/// </summary>
internal sealed class RarBitReader
{
    private readonly byte[] _buffer = new byte[64 * 1024];
    private Stream _source = Stream.Null;
    private int _bufferPos;
    private int _bufferLength;
    private bool _sourceEnded;
    private ulong _cache;              // unread bits, most significant first; below them only zeros
    private int _cacheBits;            // bits in the cache, zero padding included
    private int _realBits;             // of those, bits that came from the data
    private long _limit = long.MaxValue; // data bits still allowed into the cache (the end of a RAR 5 block)
    private long _consumed;            // bits consumed since Reset or the start of the current block

    /// <summary>Starts reading <paramref name="source"/>, a member's packed data, from its first bit.</summary>
    public void Reset(Stream source)
    {
        _source = source;
        _bufferPos = _bufferLength = 0;
        _sourceEnded = false;
        DiscardCache();
        _limit = long.MaxValue;
        _consumed = 0;
    }

    /// <summary>Data bits left that the reader has already seen; at least <c>count</c> after <see cref="Peek"/>(count) unless the data ends sooner.</summary>
    public int BufferedRealBits => _realBits;

    /// <summary>True when no data bit remains (in the data or, for a RAR 5 block, in that block).</summary>
    public bool AtEnd
    {
        get
        {
            if (_realBits == 0) Fill();
            return _realBits == 0;
        }
    }

    /// <summary>The next <paramref name="count"/> bits (0–32), zero beyond the end, without consuming them.</summary>
    public uint Peek(int count)
    {
        if ((uint)count > 32) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return 0;
        if (_cacheBits < count) Fill();
        return (uint)(_cache >> (64 - count));
    }

    /// <summary>Consumes <paramref name="count"/> bits (0–32); throws when they are not all data bits.</summary>
    public void Skip(int count)
    {
        if (!TrySkip(count)) throw RarData.Truncated();
    }

    /// <summary>Consumes <paramref name="count"/> bits (0–32) if they are all data bits; otherwise consumes nothing.</summary>
    public bool TrySkip(int count)
    {
        if ((uint)count > 32) throw new ArgumentOutOfRangeException(nameof(count));
        if (_realBits < count) Fill();
        if (_realBits < count) return false;
        _cache <<= count;
        _cacheBits -= count;
        _realBits -= count;
        _consumed += count;
        return true;
    }

    public uint Read(int count)
    {
        uint value = Peek(count);
        Skip(count);
        return value;
    }

    public bool ReadBit() => Read(1) != 0;

    public byte ReadByte() => (byte)Read(8);

    /// <summary>Skips to the next byte boundary of the data; padding beyond the end is not an error here.</summary>
    public void AlignToByte()
    {
        int pad = (int)((8 - (_consumed & 7)) & 7);
        if (pad == 0) return;
        if (_realBits < pad) Fill();
        int real = Math.Min(pad, _realBits);
        _cache <<= pad;
        _cacheBits = Math.Max(0, _cacheBits - pad);
        _realBits -= real;
        _consumed += pad;
    }

    /// <summary>
    /// Starts a RAR 5 block of <paramref name="bits"/> data bits at the next byte of the data. Bits the previous block
    /// left unread are discarded: a block always ends where its header says.
    /// </summary>
    public void BeginBlock(long bits)
    {
        if (bits < 0) throw RarData.Corrupt("negative block size");
        DiscardCache();
        _limit = bits;
        _consumed = 0;
    }

    /// <summary>Leaves the current RAR 5 block: the next read starts at the first byte after it.</summary>
    public void EndBlock()
    {
        DiscardCache();
        _limit = long.MaxValue;
        _consumed = 0;
    }

    /// <summary>One whole byte straight from the data, outside any block (a RAR 5 block header).</summary>
    public bool TryReadAlignedByte(out byte value)
    {
        if (_cacheBits != 0 || _limit != long.MaxValue) throw new InvalidOperationException("Byte reads need an empty, unlimited cache.");
        return TryNextByte(out value);
    }

    private void DiscardCache()
    {
        _cache = 0;
        _cacheBits = 0;
        _realBits = 0;
    }

    private void Fill()
    {
        // Real bits always precede padding, so once padding is in the cache no more data may follow it.
        if (_cacheBits > _realBits) return;
        while (_cacheBits <= 56)
        {
            if (_limit <= 0 || !TryNextByte(out byte b))
            {
                _cacheBits = 64; // the low bits are already zero
                return;
            }
            int real = _limit >= 8 ? 8 : (int)_limit;
            ulong value = real == 8 ? b : (ulong)(b & (0xFF00 >> real) & 0xFF);
            _cache |= value << (56 - _cacheBits);
            _cacheBits += 8;
            _realBits += real;
            _limit -= real;
            if (real < 8)
            {
                _cacheBits = 64;
                return;
            }
        }
    }

    private bool TryNextByte(out byte value)
    {
        if (_bufferPos == _bufferLength)
        {
            if (_sourceEnded) { value = 0; return false; }
            _bufferPos = 0;
            _bufferLength = _source.Read(_buffer, 0, _buffer.Length);
            if (_bufferLength <= 0)
            {
                _bufferLength = 0;
                _sourceEnded = true;
                value = 0;
                return false;
            }
        }
        value = _buffer[_bufferPos++];
        return true;
    }
}
