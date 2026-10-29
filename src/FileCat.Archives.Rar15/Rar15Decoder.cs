// FileCat.Archives.Rar15: RAR 1.5 decompression.
//
// A C# port of XADMaster's XADRAR15Handle (https://github.com/MacPaw/XADMaster, commit
// 7cb9ee0abbb163f261e4cb74501e15067032319c), with the semantics it relies on from XADFastLZSSHandle, XADLZSSHandle,
// LZSS, XADPrefixCode and CSInputBuffer. This file holds XADRAR15Handle's decoding and the 64 KiB LZSS window.
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
/// Decompresses RAR 1.5 data (unpack version 15). The format has no end marker: a member ends when its unpacked size
/// has been produced. Members of a solid archive continue the previous member's window and adaptive tables.
/// </summary>
/// <remarks>
/// The input is untrusted. Every table index, length and distance taken from it is bounded, every symbol consumes
/// input, and a match may only reach back into bytes this member or its solid run produced and may not run past the
/// member's size. Malformed or truncated data ends in <see cref="InvalidDataException"/>, never another exception.
/// </remarks>
public sealed class Rar15Decoder
{
    private const int WindowSize = 0x10000; // XADRAR15Handle's window
    private const int WindowMask = WindowSize - 1;

    /// <summary>The longest match the format can express: a long match of raw length 255 plus 3 and 8.</summary>
    private const int MaxMatchLength = 266;

    private readonly byte[] _window = new byte[WindowSize];
    private readonly Rar15BitReader _input = new();
    private long _position;   // bytes decoded since the solid run began (LZSSPosition)
    private long _flushed;    // of those, bytes handed out
    private long _memberLeft; // bytes the current member has still to decode
    private bool _started;
    private bool _failed;

    // XADRAR15Handle's state. All of it stays small: the comments give the bounds the arithmetic keeps.
    private bool _storedBlock;
    private int _flags, _flagBits;                       // a byte of flags and how many of its bits are left (0-8)
    private int _literalWeight, _matchWeight;            // 0-0xff
    private int _numRepeatedLiterals;                    // saturates at 16, the only value it is compared with
    private int _numRepeatedLastMatches;                 // 0-2
    private int _runningAverageLiteral, _runningAverageSelector;
    private int _runningAverageLength, _runningAverageOffset, _runningAverageBelowMaximum;
    private int _maximumOffset;                          // 0x2001 or 0x7f00
    private bool _bugFixFlag;
    private int _lastOffset, _lastLength;
    private readonly int[] _oldOffset = new int[4];
    private int _oldOffsetIndex;                         // 0-3 (XADMaster keeps a counter and masks it with 3)
    private readonly int[] _flagTable = new int[256], _flagReverse = new int[256];
    private readonly int[] _literalTable = new int[256], _literalReverse = new int[256];
    private readonly int[] _offsetTable = new int[256], _offsetReverse = new int[256];
    private readonly int[] _shortOffsetTable = new int[256];

    public Rar15Decoder() => Reset();

    /// <summary>
    /// Starts a member whose packed bytes <paramref name="packed"/> reads and which decodes to
    /// <paramref name="unpackedSize"/> bytes. With <paramref name="solid"/> the member continues the previous member's
    /// window and adaptive tables, as in a RAR 1.5 solid archive; the previous member must have been read to its end.
    /// </summary>
    public void StartMember(Stream packed, long unpackedSize, bool solid)
    {
        ArgumentNullException.ThrowIfNull(packed);
        ArgumentOutOfRangeException.ThrowIfNegative(unpackedSize);
        if (solid && _started)
        {
            if (_failed) throw Rar15Data.Corrupt("an earlier member of this solid archive could not be decoded");
            if (_memberLeft != 0 || _flushed != _position)
                throw new InvalidOperationException("The previous member of the solid archive was not read to its end.");
        }
        if (!solid) Reset();
        // startNextFile: every member starts on a fresh byte of flags, outside a stored block and without pending
        // repeats of the last match; everything else carries over in a solid archive.
        _flagBits = 0;
        _storedBlock = false;
        _numRepeatedLastMatches = 0;
        _input.Reset(packed);
        _memberLeft = unpackedSize;
        _started = true;
    }

    /// <summary>
    /// Copies the member's next bytes into <paramref name="destination"/>; returns 0 once the member's unpacked size
    /// has been produced. Throws <see cref="InvalidDataException"/> for malformed or truncated data.
    /// </summary>
    public int Read(Span<byte> destination)
    {
        if (!_started) throw new InvalidOperationException("No member has been started.");
        if (_failed) throw Rar15Data.Corrupt("an earlier read of this member failed");
        int total = 0;
        try
        {
            while (total < destination.Length)
            {
                // Decode ahead until the request is covered, but never let the bytes not yet handed out come within
                // one match of the window's size: a match then never overwrites a byte the caller has not had.
                while (_memberLeft > 0 && _position - _flushed < destination.Length - total
                       && _position - _flushed <= WindowSize - MaxMatchLength)
                    DecodeSymbol();
                int count = (int)Math.Min(_position - _flushed, destination.Length - total);
                if (count == 0) break;
                CopyOut(destination.Slice(total, count));
                total += count;
            }
        }
        catch
        {
            // The adaptive state is part-way through a symbol: nothing more can be decoded from it.
            _failed = true;
            throw;
        }
        return total;
    }

    /// <summary>XADRAR15Handle's resetLZSSHandle with LZSS's RestartLZSS: the state of a new (or non-solid) stream.</summary>
    private void Reset()
    {
        Array.Clear(_window);
        _position = 0;
        _flushed = 0;
        _memberLeft = 0;
        _failed = false;

        _numRepeatedLiterals = 0;
        _bugFixFlag = false;

        _runningAverageSelector = 0;
        _runningAverageLiteral = 0x3500;
        _runningAverageLength = 0;
        _runningAverageOffset = 0;
        _runningAverageBelowMaximum = 0;

        _maximumOffset = 0x2001;
        _literalWeight = _matchWeight = 0x80;

        for (int i = 0; i < 256; i++)
        {
            _flagTable[i] = ((-i) & 0xff) << 8;
            _literalTable[i] = i << 8;
            _offsetTable[i] = i << 8;
            _shortOffsetTable[i] = i;
        }
        Array.Clear(_flagReverse);
        Array.Clear(_literalReverse);

        ResetTable(_offsetTable, _offsetReverse);

        _lastOffset = 0;
        _lastLength = 0;
        Array.Clear(_oldOffset);
        _oldOffsetIndex = 0;

        _flagBits = 0;
        _storedBlock = false;
        _numRepeatedLastMatches = 0;
    }

    /// <summary>One step of XADRAR15Handle's expandFromPosition: a literal, a match, or a change of mode.</summary>
    private void DecodeSymbol()
    {
        if (_storedBlock)
        {
            EmitLiteral();
            return;
        }
        bool expectingMatch = _matchWeight > _literalWeight;
        if (GetFlagBit())
        {
            // The expected case.
            if (expectingMatch) EmitLongMatch();
            else EmitLiteral();
        }
        else if (GetFlagBit())
        {
            // The unexpected case.
            if (!expectingMatch) EmitLongMatch();
            else EmitLiteral();
        }
        else
        {
            EmitShortMatch();
        }
    }

    private bool GetFlagBit()
    {
        if (_flagBits == 0)
        {
            int index = Rar15Codes.HuffmanCode2.Decode(_input);
            if (index == 256) throw Rar15Data.Corrupt("flag code 256");
            _flags = LookupByte(_flagTable, _flagReverse, 0xff, index);
            _flagBits = 8;
        }
        _flagBits--;
        return ((_flags >> _flagBits) & 1) != 0;
    }

    private void EmitLiteral()
    {
        var code = _runningAverageLiteral switch
        {
            < 0xe00 => Rar15Codes.HuffmanCode0,
            < 0x3600 => Rar15Codes.HuffmanCode1,
            < 0x5e00 => Rar15Codes.HuffmanCode2,
            < 0x7600 => Rar15Codes.HuffmanCode3,
            _ => Rar15Codes.HuffmanCode4,
        };
        int index = code.Decode(_input);

        if (_storedBlock)
        {
            // As in XADMaster, which differs slightly from RAR here: symbol 0 escapes, every other symbol is the
            // literal one below it.
            if (index == 0)
            {
                if (_input.ReadBit())
                {
                    _storedBlock = false;
                    _numRepeatedLiterals = 0;
                    return;
                }
                int length = _input.ReadBit() ? 4 : 3;
                int offset = Rar15Codes.HuffmanCode2.Decode(_input) << 5;
                offset |= (int)_input.Read(5);
                EmitMatch(offset, length);
                return;
            }
            index--;
        }
        else
        {
            index &= 0xff;
            // if(numrepeatedliterals++>=16&&flagbits==0) storedblock=YES; - the count is only ever compared with 16.
            bool repeated = _numRepeatedLiterals >= 16;
            if (!repeated) _numRepeatedLiterals++;
            if (repeated && _flagBits == 0) _storedBlock = true;
        }

        _runningAverageLiteral += index;
        _runningAverageLiteral -= _runningAverageLiteral >> 8;

        _literalWeight += 16;
        if (_literalWeight > 0xff)
        {
            _literalWeight = 0x90;
            _matchWeight >>= 1;
        }

        EmitByte((byte)LookupByte(_literalTable, _literalReverse, 0xa1, index));
    }

    private void EmitLongMatch()
    {
        _numRepeatedLiterals = 0;
        _matchWeight += 16;
        if (_matchWeight > 0xff)
        {
            _matchWeight = 0x90;
            _literalWeight >>= 1;
        }

        int rawLength;
        if (_runningAverageLength >= 122) rawLength = Rar15Codes.LengthCode2.Decode(_input);
        else if (_runningAverageLength >= 64) rawLength = Rar15Codes.LengthCode1.Decode(_input);
        else
        {
            rawLength = 0;
            while (rawLength < 8 && !_input.ReadBit()) rawLength++;
            if (rawLength == 8) rawLength = (int)_input.Read(8);
        }

        int offsetIndex;
        if (_runningAverageOffset < 0x700) offsetIndex = Rar15Codes.HuffmanCode0.Decode(_input);
        else if (_runningAverageOffset < 0x2900) offsetIndex = Rar15Codes.HuffmanCode1.Decode(_input);
        else offsetIndex = Rar15Codes.HuffmanCode2.Decode(_input);
        if (offsetIndex == 0x100) throw Rar15Data.Corrupt("offset code 256");

        int offset = LookupByte(_offsetTable, _offsetReverse, 0xff, offsetIndex) << 7;
        offset |= (int)_input.Read(7);

        int length = rawLength + 3;
        if (offset >= _maximumOffset) length++;
        if (offset <= 256) length += 8;

        int oldMaximumOffset = _maximumOffset;
        if (_runningAverageBelowMaximum > 0xb0 || (_runningAverageLiteral >= 0x2a00 && _runningAverageLength < 0x40))
            _maximumOffset = 0x7f00;
        else
            _maximumOffset = 0x2001;

        _runningAverageLength += rawLength;
        _runningAverageLength -= _runningAverageLength >> 5;

        _runningAverageOffset += offsetIndex;
        _runningAverageOffset -= _runningAverageOffset >> 8;

        if (rawLength == 0 && offset <= oldMaximumOffset)
        {
            _runningAverageBelowMaximum++;
            _runningAverageBelowMaximum -= _runningAverageBelowMaximum >> 8;
        }
        else if (rawLength != 1 && rawLength != 4)
        {
            if (_runningAverageBelowMaximum > 0) _runningAverageBelowMaximum--;
        }

        _lastOffset = PushOldOffset(offset);
        _lastLength = length;

        EmitMatch(_lastOffset, _lastLength);
    }

    private void EmitShortMatch()
    {
        _numRepeatedLiterals = 0;

        if (_numRepeatedLastMatches == 2)
        {
            if (_input.ReadBit())
            {
                EmitMatch(_lastOffset, _lastLength);
                return;
            }
            _numRepeatedLastMatches = 0;
        }

        int selector;
        if (_runningAverageSelector < 37)
            selector = (_bugFixFlag ? Rar15Codes.ShortMatchCode0 : Rar15Codes.ShortMatchCode1).Decode(_input);
        else
            selector = (_bugFixFlag ? Rar15Codes.ShortMatchCode2 : Rar15Codes.ShortMatchCode3).Decode(_input);

        if (selector < 9)
        {
            _numRepeatedLastMatches = 0;

            _runningAverageSelector += selector;
            _runningAverageSelector -= _runningAverageSelector >> 4;

            int offsetIndex = Rar15Codes.HuffmanCode2.Decode(_input) & 0xff;

            int offset = _shortOffsetTable[offsetIndex];
            if (offsetIndex != 0)
            {
                _shortOffsetTable[offsetIndex] = _shortOffsetTable[offsetIndex - 1];
                _shortOffsetTable[offsetIndex - 1] = offset;
            }
            offset++;

            int length = selector + 2;

            _lastOffset = PushOldOffset(offset);
            _lastLength = length;

            EmitMatch(offset, length);
        }
        else if (selector == 9)
        {
            _numRepeatedLastMatches++;

            EmitMatch(_lastOffset, _lastLength);
        }
        else if (selector < 14)
        {
            _numRepeatedLastMatches = 0;

            int offset = _oldOffset[(_oldOffsetIndex - (selector - 9)) & 3];

            int length = Rar15Codes.LengthCode1.Decode(_input) + 2;
            if (length == 0x101 && selector == 10)
            {
                _bugFixFlag = !_bugFixFlag;
                return;
            }

            if (offset > 256) length++;
            if (offset >= _maximumOffset) length++;

            _lastOffset = PushOldOffset(offset);
            _lastLength = length;

            EmitMatch(offset, length);
        }
        else
        {
            _numRepeatedLastMatches = 0;

            int length = Rar15Codes.LengthCode2.Decode(_input) + 5;
            int offset = (int)_input.Read(15) + 0x8000;

            _lastOffset = offset;
            _lastLength = length;

            EmitMatch(offset, length);
        }
    }

    /// <summary>oldoffset[oldoffsetindex++&amp;3]=offset, with the index kept masked.</summary>
    private int PushOldOffset(int offset)
    {
        _oldOffset[_oldOffsetIndex] = offset;
        _oldOffsetIndex = (_oldOffsetIndex + 1) & 3;
        return offset;
    }

    /// <summary>
    /// An adaptive byte mapping: entries hold a byte above an 8-bit use count, sorted by count, and
    /// <paramref name="reverse"/> holds where each count's entries begin. The looked-up entry moves to the front of its
    /// count and its count rises; a count reaching <paramref name="limit"/> resets the counts. Those invariants keep
    /// every position inside the table; the checks below only make a broken invariant damage instead of a crash.
    /// </summary>
    private static int LookupByte(int[] table, int[] reverse, int limit, int index)
    {
        if ((uint)index > 0xff) throw Rar15Data.Corrupt("an adaptive table index");
        int value = table[index];
        int newIndex = reverse[value & 0xff]++;

        if ((value & 0xff) >= limit)
        {
            ResetTable(table, reverse);
            value = table[index];
            newIndex = reverse[value & 0xff]++;
        }

        if ((uint)newIndex > 0xff) throw Rar15Data.Corrupt("an adaptive table position");
        table[index] = table[newIndex];
        table[newIndex] = value + 1;

        return value >> 8;
    }

    private static void ResetTable(int[] table, int[] reverse)
    {
        for (int i = 0; i < 8; i++)
            for (int j = 0; j < 32; j++)
                table[i * 32 + j] = (table[i * 32 + j] & ~0xff) | (7 - i);

        Array.Clear(reverse);
        for (int i = 0; i < 7; i++) reverse[i] = (7 - i) * 32;
    }

    // ---- The window (XADFastLZSSHandle over LZSS) ----

    private void EmitByte(byte value)
    {
        if (_memberLeft <= 0) throw Rar15Data.Corrupt("a literal past the end of the member");
        _window[(int)(_position & WindowMask)] = value;
        _position++;
        _memberLeft--;
    }

    /// <summary>
    /// Repeats <paramref name="length"/> bytes from <paramref name="distance"/> bytes back, one byte at a time so that
    /// an overlapping match repeats its start (EmitLZSSMatch). Unlike XADMaster, which reads whatever the zeroed
    /// window holds, a distance before the first byte of the solid run or a match past the member's size is damage.
    /// </summary>
    private void EmitMatch(int distance, int length)
    {
        if (length == 0) return; // "repeat the last match" before any match: XADMaster copies nothing either
        if ((uint)length > MaxMatchLength) throw Rar15Data.Corrupt("a match longer than the format allows");
        if (length > _memberLeft) throw Rar15Data.Corrupt("a match past the end of the member");
        if (distance <= 0 || distance > _position) throw Rar15Data.Corrupt("a match distance before the start of the data");
        if (distance > WindowSize) throw Rar15Data.Corrupt("a match distance beyond the window");

        int source = (int)((_position - distance) & WindowMask);
        int target = (int)(_position & WindowMask);
        for (int i = 0; i < length; i++)
        {
            _window[target] = _window[source];
            target = (target + 1) & WindowMask;
            source = (source + 1) & WindowMask;
        }
        _position += length;
        _memberLeft -= length;
    }

    private void CopyOut(Span<byte> destination)
    {
        int start = (int)(_flushed & WindowMask);
        int first = Math.Min(destination.Length, WindowSize - start);
        _window.AsSpan(start, first).CopyTo(destination);
        _window.AsSpan(0, destination.Length - first).CopyTo(destination[first..]);
        _flushed += destination.Length;
    }
}
