// FileCat.Archives.Rar15: RAR 1.5 decompression.
//
// A C# port of XADMaster's XADRAR15Handle (https://github.com/MacPaw/XADMaster, commit
// 7cb9ee0abbb163f261e4cb74501e15067032319c), with the semantics it relies on from XADFastLZSSHandle, XADLZSSHandle,
// LZSS, XADPrefixCode and CSInputBuffer. This file follows XADPrefixCode and the code tables of XADRAR15Handle.
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
/// A fixed prefix code read most significant bit first, as XADPrefixCode builds and CSInputNextSymbolUsingCode reads
/// one. Decoding is a single lookup of the longest code's width of bits; every pattern either names a symbol or fails.
/// </summary>
internal sealed class Rar15PrefixCode
{
    private readonly int _maximumLength;
    private readonly ushort[] _table; // the next _maximumLength bits -> symbol << 4 | code length; 0 for no code

    private Rar15PrefixCode(int maximumLength)
    {
        if (maximumLength is < 1 or > 15) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        _maximumLength = maximumLength;
        _table = new ushort[1 << maximumLength];
    }

    /// <summary>
    /// A canonical code from one length per symbol, the shortest code all zeros (XADPrefixCode's
    /// initWithLengths:numberOfSymbols:maximumLength:shortestCodeIsZeros:YES): lengths in increasing order, symbols in
    /// increasing order within a length.
    /// </summary>
    public static Rar15PrefixCode FromLengths(ReadOnlySpan<byte> lengths, int maximumLength)
    {
        var prefixCode = new Rar15PrefixCode(maximumLength);
        uint code = 0;
        for (int length = 1; length <= maximumLength; length++)
        {
            for (int symbol = 0; symbol < lengths.Length; symbol++)
            {
                if (lengths[symbol] != length) continue;
                if (code >> length != 0) throw new InvalidOperationException("Over-subscribed RAR 1.5 code lengths.");
                prefixCode.Add(symbol, code, length);
                code++;
            }
            code <<= 1;
        }
        return prefixCode;
    }

    /// <summary>A code from explicit codewords (XADPrefixCode's addValue:forCodeWithHighBitFirst:length:).</summary>
    public static Rar15PrefixCode FromCodes(ReadOnlySpan<(int Value, uint Code, int Length)> codes)
    {
        int maximumLength = 0;
        foreach (var entry in codes) maximumLength = Math.Max(maximumLength, entry.Length);
        var prefixCode = new Rar15PrefixCode(maximumLength);
        foreach (var (value, code, length) in codes) prefixCode.Add(value, code, length);
        return prefixCode;
    }

    /// <summary>True when every bit pattern decodes to a symbol (the code's lengths satisfy Kraft's equality).</summary>
    internal bool IsComplete => Array.IndexOf(_table, (ushort)0) < 0;

    /// <summary>The longest codeword's length.</summary>
    internal int MaximumLength => _maximumLength;

    private void Add(int value, uint code, int length)
    {
        if (length < 1 || length > _maximumLength) throw new ArgumentOutOfRangeException(nameof(length));
        if ((uint)value > 0xFFF) throw new ArgumentOutOfRangeException(nameof(value));
        // XADPrefixCode walks the codeword from bit length-1 down to bit 0: only the low length bits count.
        code &= (1u << length) - 1;
        int shift = _maximumLength - length;
        int first = (int)(code << shift);
        ushort entry = (ushort)(value << 4 | length);
        for (int i = first; i < first + (1 << shift); i++)
        {
            if (_table[i] != 0) throw new InvalidOperationException("A RAR 1.5 codeword is a prefix of another.");
            _table[i] = entry;
        }
    }

    /// <summary>The next symbol; throws when the data ends inside its codeword or when the bits are no codeword.</summary>
    public int Decode(Rar15BitReader input)
    {
        int entry = _table[input.Peek(_maximumLength)];
        int length = entry & 0xF;
        if (length == 0) throw Rar15Data.Corrupt("an invalid prefix code");
        input.Skip(length);
        return entry >> 4;
    }
}

/// <summary>RAR 1.5's fixed codes, as XADRAR15Handle's initializer defines them.</summary>
internal static class Rar15Codes
{
    // lengthcode1: 256 symbols, at most 12 bits.
    public static readonly Rar15PrefixCode LengthCode1 = Rar15PrefixCode.FromLengths(
    [
        2, 2, 3, 4, 4, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7, 7, 8, 8, 8, 8, 9, 9, 9, 9, 10, 10, 10, 10, 10, 10, 10, 10, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
    ], maximumLength: 12);

    // lengthcode2: 256 symbols, at most 12 bits.
    public static readonly Rar15PrefixCode LengthCode2 = Rar15PrefixCode.FromLengths(
    [
        3, 3, 3, 3, 3, 4, 4, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7, 7, 8, 8, 8, 8, 9, 9, 9, 9, 10, 10, 10, 10, 10, 10, 10, 10, 11,
        11, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
    ], maximumLength: 12);

    // huffmancode0: 257 symbols, at most 12 bits.
    public static readonly Rar15PrefixCode HuffmanCode0 = Rar15PrefixCode.FromLengths(
    [
        4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5, 5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
    ], maximumLength: 12);

    // huffmancode1: 257 symbols, at most 12 bits.
    public static readonly Rar15PrefixCode HuffmanCode1 = Rar15PrefixCode.FromLengths(
    [
        5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
        6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 9, 9,
        9, 9, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11,
        11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
    ], maximumLength: 12);

    // huffmancode2: 257 symbols, at most 10 bits.
    public static readonly Rar15PrefixCode HuffmanCode2 = Rar15PrefixCode.FromLengths(
    [
        5, 5, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
        7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9,
        9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9,
        9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 10,
        10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10,
    ], maximumLength: 10);

    // huffmancode3: 257 symbols, at most 10 bits.
    public static readonly Rar15PrefixCode HuffmanCode3 = Rar15PrefixCode.FromLengths(
    [
        6, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9,
        9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 10, 10, 10, 10, 10, 10,
    ], maximumLength: 10);

    // huffmancode4: 257 symbols, at most 9 bits.
    public static readonly Rar15PrefixCode HuffmanCode4 = Rar15PrefixCode.FromLengths(
    [
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 9, 9,
    ], maximumLength: 9);

    // shortmatchcode0..3: the short-match selector, chosen by the bug-fix flag and the running average of selectors.
    // As in XADMaster, value 5's codeword in shortmatchcode2 and 3 is written 0x3e with length 4: its low 4 bits, 1110.
    public static readonly Rar15PrefixCode ShortMatchCode0 = Rar15PrefixCode.FromCodes(
    [
        (0, 0x00, 1), (1, 0x0a, 4), (2, 0x0d, 4), (3, 0x0e, 4), (4, 0x1e, 5), (5, 0x3e, 6), (6, 0x7e, 7), (7, 0xfe, 8),
        (8, 0xff, 8), (9, 0x0c, 4), (10, 0x08, 4), (11, 0x12, 5), (12, 0x26, 6), (13, 0x27, 6), (14, 0x0b, 4),
    ]);

    public static readonly Rar15PrefixCode ShortMatchCode1 = Rar15PrefixCode.FromCodes(
    [
        (0, 0x00, 1), (1, 0x05, 3), (2, 0x0d, 4), (3, 0x0e, 4), (4, 0x1e, 5), (5, 0x3e, 6), (6, 0x7e, 7), (7, 0xfe, 8),
        (8, 0xff, 8), (9, 0x0c, 4), (10, 0x08, 4), (11, 0x12, 5), (12, 0x26, 6), (13, 0x27, 6),
    ]);

    public static readonly Rar15PrefixCode ShortMatchCode2 = Rar15PrefixCode.FromCodes(
    [
        (0, 0x00, 2), (1, 0x02, 3), (2, 0x03, 3), (3, 0x0a, 4), (4, 0x0d, 4), (5, 0x3e, 4), (6, 0x1e, 5), (7, 0x3e, 6),
        (8, 0x3f, 6), (9, 0x0c, 4), (10, 0x08, 4), (11, 0x12, 5), (12, 0x26, 6), (13, 0x27, 6), (14, 0x0b, 4),
    ]);

    public static readonly Rar15PrefixCode ShortMatchCode3 = Rar15PrefixCode.FromCodes(
    [
        (0, 0x00, 2), (1, 0x02, 3), (2, 0x03, 3), (3, 0x05, 3), (4, 0x0d, 4), (5, 0x3e, 4), (6, 0x1e, 5), (7, 0x3e, 6),
        (8, 0x3f, 6), (9, 0x0c, 4), (10, 0x08, 4), (11, 0x12, 5), (12, 0x26, 6), (13, 0x27, 6),
    ]);
}
