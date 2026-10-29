// Test support for FileCat.Archives.Rar15: writes RAR 1.5 data (literals and long matches) by mirroring the adaptive
// state of XADMaster's XADRAR15Handle (https://github.com/MacPaw/XADMaster, commit
// 7cb9ee0abbb163f261e4cb74501e15067032319c) as src/FileCat.Archives.Rar15 ports it, so it is under the same license.
// Not shipped.
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

using System.Buffers.Binary;
using FileCat.Archives.Rar15;

namespace FileCat.Core.Tests;

/// <summary>
/// Encodes literals and long matches as RAR 1.5 data, tracking the decoder's adaptive state so that each member decodes
/// to exactly the planned bytes; consecutive members continue that state as a solid archive does. It never plans a
/// stored block or a short match. Matches are encoded as given, valid or not, so damage can be planned too.
/// </summary>
internal sealed class Rar15TestEncoder
{
    private static readonly Dictionary<Rar15PrefixCode, (uint Code, int Length)[]> CodewordCache = [];

    private int _literalWeight = 0x80, _matchWeight = 0x80;
    private int _numRepeatedLiterals;
    private int _runningAverageLiteral = 0x3500;
    private int _runningAverageLength, _runningAverageOffset, _runningAverageBelowMaximum;
    private int _maximumOffset = 0x2001;
    private readonly int[] _flagTable = new int[256], _flagReverse = new int[256];
    private readonly int[] _literalTable = new int[256], _literalReverse = new int[256];
    private readonly int[] _offsetTable = new int[256], _offsetReverse = new int[256];

    // The member being written: bit strings, and the flag bytes that precede the symbols they govern.
    private readonly List<(uint Value, int Length, int FlagGroup)> _items = [];
    private readonly List<int> _flagGroups = [];
    private int _flagBitsUsed = 8; // bits of the current flag byte already planned; 8 means the next bit needs a new one

    public Rar15TestEncoder()
    {
        for (int i = 0; i < 256; i++)
        {
            _flagTable[i] = ((-i) & 0xff) << 8;
            _literalTable[i] = i << 8;
            _offsetTable[i] = i << 8;
        }
        ResetTable(_offsetTable, _offsetReverse);
    }

    /// <summary>The bytes this encoder's members decode to, in order, as one solid stream.</summary>
    public List<byte> Output { get; } = [];

    public void Literals(ReadOnlySpan<byte> values)
    {
        foreach (byte value in values) Literal(value);
    }

    public void Literal(byte value)
    {
        Flags(expected: _matchWeight <= _literalWeight);
        var code = _runningAverageLiteral switch
        {
            < 0xe00 => Rar15Codes.HuffmanCode0,
            < 0x3600 => Rar15Codes.HuffmanCode1,
            < 0x5e00 => Rar15Codes.HuffmanCode2,
            < 0x7600 => Rar15Codes.HuffmanCode3,
            _ => Rar15Codes.HuffmanCode4,
        };
        int index = Array.FindIndex(_literalTable, entry => entry >> 8 == value);
        Symbol(code, index);
        bool repeated = _numRepeatedLiterals >= 16;
        if (!repeated) _numRepeatedLiterals++;
        if (repeated && _flagBitsUsed == 8) throw new InvalidOperationException("The plan would start a stored block; put a match between at most 16 literals.");
        _runningAverageLiteral += index;
        _runningAverageLiteral -= _runningAverageLiteral >> 8;
        _literalWeight += 16;
        if (_literalWeight > 0xff)
        {
            _literalWeight = 0x90;
            _matchWeight >>= 1;
        }
        LookupByte(_literalTable, _literalReverse, 0xa1, index);
        Output.Add(value);
    }

    /// <summary>The shortest long match at <paramref name="distance"/> now; the longest is 255 more.</summary>
    public int ShortestMatch(int distance) => 3 + (distance >= _maximumOffset ? 1 : 0) + (distance <= 256 ? 8 : 0);

    /// <summary>A long match; <paramref name="distance"/> 1-32767 and a length its raw length (0-255) can express.</summary>
    public void Match(int distance, int length)
    {
        if (distance is < 0 or > 0x7fff) throw new ArgumentOutOfRangeException(nameof(distance));
        Flags(expected: _matchWeight > _literalWeight);
        _numRepeatedLiterals = 0;
        _matchWeight += 16;
        if (_matchWeight > 0xff)
        {
            _matchWeight = 0x90;
            _literalWeight >>= 1;
        }
        int rawLength = length - 3 - (distance >= _maximumOffset ? 1 : 0) - (distance <= 256 ? 8 : 0);
        if (rawLength is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(length));
        if (_runningAverageLength >= 122) Symbol(Rar15Codes.LengthCode2, rawLength);
        else if (_runningAverageLength >= 64) Symbol(Rar15Codes.LengthCode1, rawLength);
        else if (rawLength < 8) Bits(1, rawLength + 1);
        else Bits((uint)rawLength, 16);

        var offsetCode = _runningAverageOffset < 0x700 ? Rar15Codes.HuffmanCode0
            : _runningAverageOffset < 0x2900 ? Rar15Codes.HuffmanCode1 : Rar15Codes.HuffmanCode2;
        int offsetIndex = Array.FindIndex(_offsetTable, entry => entry >> 8 == distance >> 7);
        Symbol(offsetCode, offsetIndex);
        LookupByte(_offsetTable, _offsetReverse, 0xff, offsetIndex);
        Bits((uint)distance & 0x7f, 7);

        int oldMaximumOffset = _maximumOffset;
        _maximumOffset = _runningAverageBelowMaximum > 0xb0 || (_runningAverageLiteral >= 0x2a00 && _runningAverageLength < 0x40) ? 0x7f00 : 0x2001;
        _runningAverageLength += rawLength;
        _runningAverageLength -= _runningAverageLength >> 5;
        _runningAverageOffset += offsetIndex;
        _runningAverageOffset -= _runningAverageOffset >> 8;
        if (rawLength == 0 && distance <= oldMaximumOffset)
        {
            _runningAverageBelowMaximum++;
            _runningAverageBelowMaximum -= _runningAverageBelowMaximum >> 8;
        }
        else if (rawLength != 1 && rawLength != 4)
        {
            if (_runningAverageBelowMaximum > 0) _runningAverageBelowMaximum--;
        }

        // The decoder copies byte by byte, so an overlapping match repeats its start; a distance before the start is
        // left for the decoder to refuse.
        for (int i = 0; i < length; i++) Output.Add(Output.Count - distance >= 0 ? Output[Output.Count - distance] : (byte)0);
    }

    /// <summary>
    /// Ends the member: its packed bytes, padded to a whole byte. The next member continues every table, as a solid
    /// archive's next member does, and starts on a fresh byte of flags.
    /// </summary>
    public byte[] EndMember()
    {
        var bytes = new List<byte>();
        uint pending = 0;
        int pendingBits = 0;
        void Write(uint value, int length)
        {
            for (int bit = length - 1; bit >= 0; bit--)
            {
                pending = pending << 1 | (value >> bit & 1);
                if (++pendingBits == 8)
                {
                    bytes.Add((byte)pending);
                    pending = 0;
                    pendingBits = 0;
                }
            }
        }
        foreach (var (value, length, group) in _items)
        {
            if (group < 0)
            {
                Write(value, length);
                continue;
            }
            // The decoder reads this flag byte here: its index is wherever the flag table now keeps that byte.
            int flags = _flagGroups[group];
            int index = Array.FindIndex(_flagTable, entry => entry >> 8 == flags);
            var (code, codeLength) = Codewords(Rar15Codes.HuffmanCode2)[index];
            Write(code, codeLength);
            LookupByte(_flagTable, _flagReverse, 0xff, index);
        }
        if (pendingBits > 0) bytes.Add((byte)(pending << (8 - pendingBits)));
        _items.Clear();
        _flagGroups.Clear();
        _flagBitsUsed = 8;
        return [.. bytes];
    }

    private void Flags(bool expected)
    {
        Flag(expected);
        if (!expected) Flag(true);
    }

    private void Flag(bool bit)
    {
        if (_flagBitsUsed == 8)
        {
            _flagGroups.Add(0);
            _items.Add((0, 0, _flagGroups.Count - 1));
            _flagBitsUsed = 0;
        }
        if (bit) _flagGroups[^1] |= 0x80 >> _flagBitsUsed;
        _flagBitsUsed++;
    }

    private void Symbol(Rar15PrefixCode code, int symbol)
    {
        var (value, length) = Codewords(code)[symbol];
        Bits(value, length);
    }

    private void Bits(uint value, int length) => _items.Add((value, length, -1));

    /// <summary>Every symbol's codeword, read back from what the decoder's code decodes for each bit pattern.</summary>
    private static (uint Code, int Length)[] Codewords(Rar15PrefixCode code)
    {
        lock (CodewordCache)
        {
            if (CodewordCache.TryGetValue(code, out var cached)) return cached;
            int width = code.MaximumLength;
            var first = new Dictionary<int, int>();
            var count = new Dictionary<int, int>();
            var bytes = new byte[4];
            var reader = new Rar15BitReader();
            for (int pattern = 0; pattern < 1 << width; pattern++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)pattern << (32 - width));
                reader.Reset(new MemoryStream(bytes, writable: false));
                int symbol = code.Decode(reader);
                first.TryAdd(symbol, pattern);
                count[symbol] = count.GetValueOrDefault(symbol) + 1;
            }
            var codewords = new (uint, int)[first.Keys.Max() + 1];
            foreach (var (symbol, pattern) in first)
            {
                int length = width - System.Numerics.BitOperations.Log2((uint)count[symbol]);
                codewords[symbol] = ((uint)pattern >> (width - length), length);
            }
            CodewordCache[code] = codewords;
            return codewords;
        }
    }

    private static void LookupByte(int[] table, int[] reverse, int limit, int index)
    {
        int value = table[index];
        int newIndex = reverse[value & 0xff]++;
        if ((value & 0xff) >= limit)
        {
            ResetTable(table, reverse);
            value = table[index];
            newIndex = reverse[value & 0xff]++;
        }
        table[index] = table[newIndex];
        table[newIndex] = value + 1;
    }

    private static void ResetTable(int[] table, int[] reverse)
    {
        for (int i = 0; i < 8; i++)
            for (int j = 0; j < 32; j++)
                table[i * 32 + j] = (table[i * 32 + j] & ~0xff) | (7 - i);
        Array.Clear(reverse);
        for (int i = 0; i < 7; i++) reverse[i] = (7 - i) * 32;
    }
}
