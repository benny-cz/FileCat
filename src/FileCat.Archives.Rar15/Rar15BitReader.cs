// FileCat.Archives.Rar15: RAR 1.5 decompression.
//
// A C# port of XADMaster's XADRAR15Handle (https://github.com/MacPaw/XADMaster, commit
// 7cb9ee0abbb163f261e4cb74501e15067032319c), with the semantics it relies on from XADFastLZSSHandle, XADLZSSHandle,
// LZSS, XADPrefixCode and CSInputBuffer. This file follows CSInputBuffer's most-significant-bit-first reading.
//
// Copyright (c) 2017-present, MacPaw Way Ltd. All rights reserved. XADMaster was originally written by Dag Ågren.
// C# port: Copyright (c) 2026 Benny and FileCat contributors.
//
// This library is free software; you can redistribute it and/or modify it under the terms of the GNU Lesser General
// Public License as published by the Free Software Foundation; either version 2.1 of the License, or (at your option)
// any later version.
//
// This library is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied
// warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Lesser General Public License for more
// details.
//
// You should have received a copy of the GNU Lesser General Public License along with this library (licenses/xadmaster
// in FileCat's source and packages); if not, write to the Free Software Foundation, Inc., 51 Franklin Street, Fifth
// Floor, Boston, MA 02110-1301 USA.

namespace FileCat.Archives.Rar15;

/// <summary>
/// The bits of one member's packed data, most significant first (CSInputBuffer's CSInputNextBit order). Peeking past
/// the end of the data yields zero bits, so a prefix code can be looked up near the end; consuming a bit that is not in
/// the data throws, so short or hostile data can never make the decoder loop.
/// </summary>
internal sealed class Rar15BitReader
{
    private readonly byte[] _buffer = new byte[16 * 1024];
    private Stream _source = Stream.Null;
    private int _bufferPos;
    private int _bufferLength;
    private bool _sourceEnded;
    private ulong _bits;  // unread data bits, the next one in bit 63; every bit below them is zero
    private int _count;   // how many data bits _bits holds

    /// <summary>Starts reading <paramref name="source"/> from its first bit.</summary>
    public void Reset(Stream source)
    {
        _source = source;
        _bufferPos = _bufferLength = 0;
        _sourceEnded = false;
        _bits = 0;
        _count = 0;
    }

    /// <summary>The next <paramref name="count"/> bits (1–32) without consuming them; zero past the end of the data.</summary>
    public uint Peek(int count)
    {
        if ((uint)(count - 1) >= 32) throw new ArgumentOutOfRangeException(nameof(count));
        if (_count < count) Fill();
        return (uint)(_bits >> (64 - count));
    }

    /// <summary>Consumes <paramref name="count"/> bits (0–32); throws when they are not all in the data.</summary>
    public void Skip(int count)
    {
        if ((uint)count > 32) throw new ArgumentOutOfRangeException(nameof(count));
        if (_count < count)
        {
            Fill();
            if (_count < count) throw Rar15Data.Truncated();
        }
        _bits <<= count;
        _count -= count;
    }

    /// <summary>The next <paramref name="count"/> bits (1–32) as a number, most significant first.</summary>
    public uint Read(int count)
    {
        uint value = Peek(count);
        Skip(count);
        return value;
    }

    public bool ReadBit() => Read(1) != 0;

    private void Fill()
    {
        while (_count <= 56)
        {
            if (_bufferPos == _bufferLength)
            {
                if (_sourceEnded) return;
                int read = _source.Read(_buffer, 0, _buffer.Length);
                if (read <= 0)
                {
                    _sourceEnded = true;
                    return;
                }
                if (read > _buffer.Length) throw new InvalidOperationException("The packed data stream returned more bytes than were asked for.");
                _bufferPos = 0;
                _bufferLength = read;
            }
            _bits |= (ulong)_buffer[_bufferPos++] << (56 - _count);
            _count += 8;
        }
    }
}

/// <summary>The decoder's only exceptions for bad data, worded as FileCat's RAR reader words its own.</summary>
internal static class Rar15Data
{
    public static InvalidDataException Truncated() => new("The archive's compressed data ends early.");

    public static InvalidDataException Corrupt(string what) => new("The archive's compressed data is damaged: " + what + ".");
}
