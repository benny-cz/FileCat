// Canonical decoding as in rardecode (github.com/nwaples/rardecode v2.4.5, huffman.go),
// Copyright (c) 2015 Nicholas Waples, BSD-2-Clause; see licenses/rardecode/LICENSE. FileCat rejects
// over-subscribed tables and never indexes outside the symbol list.

namespace FileCat.Archives.Rar;

/// <summary>A canonical Huffman code as RAR transmits it: code lengths of 0–15 bits per symbol, read most significant bit first.</summary>
internal sealed class RarHuffman
{
    public const int MaxBits = 15;

    private readonly int[] _limit = new int[MaxBits + 1]; // first left-justified code value past each length
    private readonly int[] _pos = new int[MaxBits + 1];   // index in _symbols of each length's first code
    private ushort[] _symbols = [];
    private int _symbolCount;
    private int _quickBits;
    private byte[] _quickLength = [];
    private ushort[] _quickSymbol = [];

    public bool IsBuilt { get; private set; }

    /// <summary>Builds the code from one length per symbol. An over-subscribed set of lengths is damaged data.</summary>
    public void Build(ReadOnlySpan<byte> lengths)
    {
        Span<int> count = stackalloc int[MaxBits + 1];
        count.Clear();
        foreach (byte n in lengths)
        {
            if (n > MaxBits) throw RarData.Corrupt("Huffman code length over 15 bits");
            count[n]++;
        }
        count[0] = 0;
        int limit = 0;
        _limit[0] = 0;
        _pos[0] = 0;
        for (int bits = 1; bits <= MaxBits; bits++)
        {
            limit += count[bits] << (MaxBits - bits);
            if (limit > 1 << MaxBits) throw RarData.Corrupt("over-subscribed Huffman table");
            _limit[bits] = limit;
            _pos[bits] = _pos[bits - 1] + count[bits - 1];
        }
        _symbolCount = _pos[MaxBits] + count[MaxBits];
        if (_symbols.Length < _symbolCount) _symbols = new ushort[Math.Max(_symbolCount, lengths.Length)];
        Span<int> next = stackalloc int[MaxBits + 1];
        _pos.CopyTo(next);
        for (int symbol = 0; symbol < lengths.Length; symbol++)
            if (lengths[symbol] != 0) _symbols[next[lengths[symbol]]++] = (ushort)symbol;

        _quickBits = lengths.Length >= 298 ? 10 : 7;
        int quickSize = 1 << _quickBits;
        if (_quickLength.Length != quickSize)
        {
            _quickLength = new byte[quickSize];
            _quickSymbol = new ushort[quickSize];
        }
        int length = 1;
        for (int i = 0; i < quickSize; i++)
        {
            int value = i << (MaxBits - _quickBits);
            while (length < MaxBits && value >= _limit[length]) length++;
            if (value >= _limit[_quickBits])
            {
                // Longer than the quick table: Decode walks the lengths instead. Keep the entry harmless.
                _quickLength[i] = 0;
                _quickSymbol[i] = 0;
                continue;
            }
            int index = _pos[length] + ((value - _limit[length - 1]) >> (MaxBits - length));
            _quickLength[i] = (byte)length;
            _quickSymbol[i] = _symbols[index];
        }
        IsBuilt = true;
    }

    /// <summary>The next symbol; throws when the data ends inside it or when it is no code of this table.</summary>
    public int Decode(RarBitReader input)
    {
        if (!TryDecode(input, out int symbol)) throw RarData.Truncated();
        return symbol;
    }

    /// <summary>
    /// The next symbol, or false when the data (or the RAR 5 block) ends before a whole code: what is left is then
    /// fewer bits than a code. Throws for a bit pattern that is no code of a table with data left to read.
    /// </summary>
    public bool TryDecode(RarBitReader input, out int symbol)
    {
        if (!IsBuilt) throw RarData.Corrupt("compressed block without code tables");
        int value = (int)input.Peek(MaxBits);
        int length;
        if (value < _limit[_quickBits])
        {
            int quick = value >> (MaxBits - _quickBits);
            length = _quickLength[quick];
            symbol = _quickSymbol[quick];
        }
        else
        {
            length = _quickBits + 1;
            while (length <= MaxBits && value >= _limit[length]) length++;
            if (length > MaxBits)
            {
                // Zero padding past the end can form an unused code; with real bits there it is damage.
                symbol = 0;
                if (input.BufferedRealBits < MaxBits) return false;
                throw RarData.Corrupt("invalid Huffman code");
            }
            int index = _pos[length] + ((value - _limit[length - 1]) >> (MaxBits - length));
            if ((uint)index >= (uint)_symbolCount) throw RarData.Corrupt("invalid Huffman code");
            symbol = _symbols[index];
        }
        return input.TrySkip(length);
    }

    /// <summary>
    /// Reads a code-length table as RAR 3 and RAR 5 store one: 20 bit lengths that define a code for the lengths, then
    /// run-length-coded lengths. With <paramref name="addToOld"/> (RAR 3), each value adds to the previous table's.
    /// </summary>
    public static void ReadLengthTable(RarBitReader input, Span<byte> lengths, bool addToOld)
    {
        Span<byte> bitLengths = stackalloc byte[20];
        bitLengths.Clear();
        for (int i = 0; i < bitLengths.Length; i++)
        {
            int n = (int)input.Read(4);
            if (n == 15)
            {
                int zeros = (int)input.Read(4);
                if (zeros > 0)
                {
                    // Lengths are already zero; the run may run past the end harmlessly.
                    i += zeros + 1;
                    continue;
                }
            }
            bitLengths[i] = (byte)n;
        }
        var lengthCode = new RarHuffman();
        lengthCode.Build(bitLengths);
        for (int i = 0; i < lengths.Length;)
        {
            int l = lengthCode.Decode(input);
            if (l < 16)
            {
                lengths[i] = addToOld ? (byte)((lengths[i] + l) & 0xF) : (byte)l;
                i++;
                continue;
            }
            int count = l is 16 or 18 ? (int)input.Read(3) + 3 : (int)input.Read(7) + 11;
            byte value = 0;
            if (l < 18)
            {
                if (i == 0) throw RarData.Corrupt("code-length repeat with nothing to repeat");
                value = lengths[i - 1];
            }
            for (; count > 0 && i < lengths.Length; count--) lengths[i++] = value;
        }
    }
}
