// RAR 2.0 decoding (unpack versions 20 and 26: LZ and multimedia/audio blocks) ported from rardecode
// (github.com/nwaples/rardecode v2.4.5, decode20.go, decode20_lz.go, decode20_audio.go), Copyright (c) 2015
// Nicholas Waples, BSD-2-Clause; see licenses/rardecode/LICENSE. RAR 2.0 data has no end marker: as in rardecode, a
// member ends after exactly its unpacked size. Two rules differ from rardecode, which misreads valid streams that the
// reference reader (SharpCompress, used only as a black box on synthetic streams) decodes: "keep the old table" adds
// to the last table read, zero past its size; and a block switch that ends a member's data after its last byte
// applies to the next member of a solid run. Every symbol, count and channel value is checked before it indexes
// anything; lengths and distances go through RarWindow's checks.

namespace FileCat.Archives.Rar;

internal sealed class Rar2Decoder : RarDecoder
{
    private const int MainSize = 298;   // 256 literals, repeat (256), old offsets (257–260), short matches (261–268),
                                        // end of block (269), long matches (270–297)
    private const int OffsetSize = 48;
    private const int LengthSize = 28;
    private const int LzTableSize = MainSize + OffsetSize + LengthSize;
    private const int AudioSize = 257;  // 256 deltas and the end of block
    private const int MaxChannels = 4;  // the header's channel count has two bits
    private const int BitLengthSize = 19;

    // RAR 2.0 shares these with RAR 3 (rardecode decode29_lz.go); its offset code uses the first 48 slots.
    private static ReadOnlySpan<byte> LengthBase => [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 24, 28, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224];
    private static ReadOnlySpan<byte> LengthExtraBits => [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5];
    private static ReadOnlySpan<int> OffsetBase =>
    [
        0, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 1536, 2048, 3072, 4096,
        6144, 8192, 12288, 16384, 24576, 32768, 49152, 65536, 98304, 131072, 196608, 262144, 327680, 393216, 458752,
        524288, 589824, 655360, 720896, 786432, 851968, 917504, 983040,
    ];
    private static ReadOnlySpan<byte> OffsetExtraBits =>
    [
        0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15,
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
    ];
    private static ReadOnlySpan<byte> ShortOffsetBase => [0, 4, 8, 16, 32, 64, 128, 192];
    private static ReadOnlySpan<byte> ShortOffsetExtraBits => [2, 2, 3, 4, 5, 6, 6, 6];

    // The last code-length table read (LZ or audio), zero past its size: "keep the old table" adds to it.
    private readonly byte[] _lengths = new byte[AudioSize * MaxChannels];
    private readonly byte[] _newLengths = new byte[AudioSize * MaxChannels]; // a header's table until it is whole
    private readonly byte[] _bitLengths = new byte[BitLengthSize];
    private readonly RarHuffman _bitLengthCode = new();
    private readonly RarHuffman _main = new();
    private readonly RarHuffman _offset = new();
    private readonly RarHuffman _length = new();
    private readonly RarHuffman[] _audio = [new(), new(), new(), new()];
    private readonly AudioChannel[] _channels = [new(), new(), new(), new()];
    private readonly int[] _offsets = new int[4]; // the last four match distances, most recent first
    private int _lastLength;
    private int _channelCount = 1;
    private int _channel;                         // the channel the next audio byte belongs to; below _channelCount
    private int _channelDelta;                    // the last audio delta of any channel
    private bool _audioBlock;
    private bool _haveTables;                     // false until a block header is read, and after an end of block
    private long _remaining;                      // bytes of the member still to decode; negative when unknown
    private bool _tailRead;                       // the member's data after its last byte was looked at
    private RarBitReader _input = new();

    // RarReader always passes the size; without it the end of RAR 2.0 data cannot be found (Decode refuses).
    public override void StartMember(RarBitReader input, bool solid) => StartMember(input, solid, -1);

    public override void StartMember(RarBitReader input, bool solid, long unpackedSize)
    {
        _input = input;
        _remaining = unpackedSize;
        _tailRead = false;
        if (solid) return;
        // rardecode keeps the old distances across non-solid files (its window is cleared instead); a fresh member
        // starts with none here, so a match that reaches for one fails the window's distance check.
        _haveTables = false;
        _audioBlock = false;
        Array.Clear(_lengths);
        Array.Clear(_offsets);
        _lastLength = 0;
        _channelCount = 1;
        _channel = 0;
        _channelDelta = 0;
        foreach (var channel in _channels) channel.Reset();
    }

    public override bool Decode(RarWindow window)
    {
        // rardecode decodes nothing for an unknown size; decoding to the end of the data would turn padding into bytes.
        if (_remaining < 0) throw RarData.Unsupported("RAR 2.0 data of unknown size");
        // Every pass consumes at least one bit (a block header or a symbol), so damaged data ends in an exception.
        while (_remaining > 0)
        {
            if (!window.ShouldDecode) return false;
            if (!_haveTables)
            {
                ReadBlockHeader();
                continue;
            }
            if (_audioBlock) DecodeAudio(window);
            else DecodeLz(window);
        }
        if (!_tailRead)
        {
            _tailRead = true;
            ReadTrailingSwitch();
        }
        return true;
    }

    /// <summary>
    /// After a member's last byte, its data may still hold a block switch (an end-of-block symbol and a whole header)
    /// for the next member of a solid run: the reference reader applies it, and so does FileCat, where rardecode never
    /// reads past the last byte. Anything that is not a whole switch (padding, other bits, a cut-off header) changes
    /// nothing; the next member's own decoding and checksum judge it.
    /// </summary>
    private void ReadTrailingSwitch()
    {
        if (!_haveTables || _input.AtEnd) return;
        try
        {
            var code = _audioBlock ? _audio[_channel] : _main;
            if (code.TryDecode(_input, out int symbol) && symbol == (_audioBlock ? 256 : 269)) ReadBlockHeader();
        }
        catch (InvalidDataException)
        {
            // Not a whole switch; ReadBlockHeader changed no state.
        }
    }

    /// <summary>
    /// Reads a block header and its code-length table. No decoder state changes until the whole header has been read
    /// and every code in it checked, so a failed header (or a failed look past a member's end) leaves the tables as
    /// they were.
    /// </summary>
    private void ReadBlockHeader()
    {
        bool audio = _input.ReadBit();
        bool keepOld = _input.ReadBit();
        int channels = audio ? (int)_input.Read(2) + 1 : 1;
        if (channels is < 1 or > MaxChannels) throw RarData.Corrupt("audio channel count");
        var lengths = _newLengths.AsSpan(0, audio ? AudioSize * channels : LzTableSize);
        if (keepOld) _lengths.AsSpan(0, lengths.Length).CopyTo(lengths);
        else lengths.Clear(); // a fresh table rather than changes to the previous one
        ReadLengthTable(lengths);
        if (audio)
        {
            for (int i = 0; i < channels; i++) CheckCode(lengths.Slice(i * AudioSize, AudioSize));
        }
        else
        {
            CheckCode(lengths[..MainSize]);
            CheckCode(lengths.Slice(MainSize, OffsetSize));
            CheckCode(lengths.Slice(MainSize + OffsetSize, LengthSize));
        }

        // rardecode keeps one array for both block kinds, so an earlier, larger table's lengths past this one's size
        // would feed the next "keep the old table"; the reference reader starts those from zero.
        lengths.CopyTo(_lengths);
        _lengths.AsSpan(lengths.Length).Clear();
        _audioBlock = audio;
        if (audio)
        {
            _channelCount = channels;
            if (_channel >= channels) _channel = 0;
            for (int i = 0; i < channels; i++) _audio[i].Build(lengths.Slice(i * AudioSize, AudioSize));
        }
        else
        {
            _main.Build(lengths[..MainSize]);
            _offset.Build(lengths.Slice(MainSize, OffsetSize));
            _length.Build(lengths.Slice(MainSize + OffsetSize, LengthSize));
        }
        _haveTables = true;
    }

    /// <summary>
    /// RAR 2.0's code-length table: 19 four-bit lengths that define a code for the lengths, then values that add to
    /// the old lengths (0–15), repeat the previous length (16) or write zeros (17, 18).
    /// </summary>
    private void ReadLengthTable(Span<byte> lengths)
    {
        for (int i = 0; i < _bitLengths.Length; i++) _bitLengths[i] = (byte)_input.Read(4);
        _bitLengthCode.Build(_bitLengths);
        for (int i = 0; i < lengths.Length;)
        {
            int symbol = _bitLengthCode.Decode(_input);
            if (symbol < 16)
            {
                lengths[i] = (byte)((lengths[i] + symbol) & 0xF);
                i++;
            }
            else if (symbol == 16)
            {
                if (i == 0) throw RarData.Corrupt("code-length repeat with nothing to repeat");
                int end = Math.Min(i + (int)_input.Read(2) + 3, lengths.Length);
                byte previous = lengths[i - 1];
                lengths[i..end].Fill(previous);
                i = end;
            }
            else
            {
                int count = symbol == 17 ? (int)_input.Read(3) + 3 : (int)_input.Read(7) + 11;
                int end = Math.Min(i + count, lengths.Length);
                lengths[i..end].Clear();
                i = end;
            }
        }
    }

    /// <summary>Refuses an over-subscribed code: the lengths <see cref="RarHuffman.Build"/> would reject (all are 0–15 here).</summary>
    private static void CheckCode(ReadOnlySpan<byte> lengths)
    {
        int sum = 0;
        foreach (byte length in lengths)
            if (length != 0) sum += 1 << (RarHuffman.MaxBits - length);
        if (sum > 1 << RarHuffman.MaxBits) throw RarData.Corrupt("over-subscribed Huffman table");
    }

    private void DecodeLz(RarWindow window)
    {
        int symbol = _main.Decode(_input);
        if (symbol < 256)
        {
            window.Put((byte)symbol);
            _remaining--;
            return;
        }
        if (symbol == 269)
        {
            _haveTables = false; // a block header follows
            return;
        }
        if (symbol > 269) LongMatch(symbol - 270);
        else if (symbol == 256) PushOffset(_offsets[0]); // the previous match again
        else if (symbol < 261) OldOffsetMatch(symbol - 257);
        else ShortMatch(symbol - 261);
        // Symbol 256 before any match repeats nothing, as in rardecode.
        if (_lastLength == 0) return;
        // A match past the member's size would put another member's bytes (or none) into this one.
        if (_lastLength > _remaining) throw RarData.Corrupt("match past the end of the member");
        window.Copy(_lastLength, _offsets[0]);
        _remaining -= _lastLength;
    }

    private void LongMatch(int slot)
    {
        if ((uint)slot >= (uint)LengthBase.Length) throw RarData.Corrupt("match length slot");
        int length = LengthBase[slot] + 3 + (int)_input.Read(LengthExtraBits[slot]);
        int offsetSlot = _offset.Decode(_input);
        if ((uint)offsetSlot >= (uint)OffsetBase.Length) throw RarData.Corrupt("match distance slot");
        int offset = OffsetBase[offsetSlot] + 1 + (int)_input.Read(OffsetExtraBits[offsetSlot]);
        if (offset >= 0x2000)
        {
            length++;
            if (offset >= 0x40000) length++;
        }
        PushOffset(offset);
        _lastLength = length;
    }

    private void OldOffsetMatch(int index)
    {
        if ((uint)index >= (uint)_offsets.Length) throw RarData.Corrupt("old distance index");
        int offset = _offsets[index];
        PushOffset(offset);
        int slot = _length.Decode(_input);
        if ((uint)slot >= (uint)LengthBase.Length) throw RarData.Corrupt("match length slot");
        int length = LengthBase[slot] + 2 + (int)_input.Read(LengthExtraBits[slot]);
        if (offset >= 0x101)
        {
            length++;
            if (offset >= 0x2000)
            {
                length++;
                if (offset >= 0x40000) length++;
            }
        }
        _lastLength = length;
    }

    private void ShortMatch(int slot)
    {
        if ((uint)slot >= (uint)ShortOffsetBase.Length) throw RarData.Corrupt("short distance slot");
        int offset = ShortOffsetBase[slot] + 1 + (int)_input.Read(ShortOffsetExtraBits[slot]);
        PushOffset(offset);
        _lastLength = 2;
    }

    /// <summary>Makes <paramref name="offset"/> the most recent distance; the oldest one drops out.</summary>
    private void PushOffset(int offset)
    {
        _offsets[3] = _offsets[2];
        _offsets[2] = _offsets[1];
        _offsets[1] = _offsets[0];
        _offsets[0] = offset;
    }

    private void DecodeAudio(RarWindow window)
    {
        int symbol = _audio[_channel].Decode(_input);
        if (symbol == 256)
        {
            _haveTables = false; // a block header follows
            return;
        }
        window.Put(PredictAudio(symbol));
        _remaining--;
        if (++_channel >= _channelCount) _channel = 0;
    }

    /// <summary>
    /// One audio byte: a prediction from the channel's recent deltas, minus the coded <paramref name="delta"/>. Every
    /// 32 bytes of a channel, the weight whose change would have predicted best moves by one (staying within -17..16).
    /// All values stay small (|prediction| below 2^15), so no arithmetic here can overflow.
    /// </summary>
    private byte PredictAudio(int delta)
    {
        var v = _channels[_channel];
        v.ByteCount = (v.ByteCount + 1) & 0x1F; // only the count modulo 32 matters
        v.D[3] = v.D[2];
        v.D[2] = v.D[1];
        v.D[1] = v.LastDelta - v.D[0];
        v.D[0] = v.LastDelta;
        int predicted = 8 * v.LastChar + v.K[0] * v.D[0] + v.K[1] * v.D[1] + v.K[2] * v.D[2] + v.K[3] * v.D[3] + v.K[4] * _channelDelta;
        predicted = (predicted >> 3) & 0xFF;
        int value = predicted - delta;
        int scaled = unchecked((sbyte)delta) << 3; // the delta as a signed byte, times 8

        v.Dif[0] += Math.Abs(scaled);
        v.Dif[1] += Math.Abs(scaled - v.D[0]);
        v.Dif[2] += Math.Abs(scaled + v.D[0]);
        v.Dif[3] += Math.Abs(scaled - v.D[1]);
        v.Dif[4] += Math.Abs(scaled + v.D[1]);
        v.Dif[5] += Math.Abs(scaled - v.D[2]);
        v.Dif[6] += Math.Abs(scaled + v.D[2]);
        v.Dif[7] += Math.Abs(scaled - v.D[3]);
        v.Dif[8] += Math.Abs(scaled + v.D[3]);
        v.Dif[9] += Math.Abs(scaled - _channelDelta);
        v.Dif[10] += Math.Abs(scaled + _channelDelta);

        _channelDelta = unchecked((sbyte)(value - v.LastChar));
        v.LastDelta = _channelDelta;
        v.LastChar = value; // kept unmasked (-255..255), as rardecode keeps it
        if (v.ByteCount == 0) AdaptWeights(v);
        return unchecked((byte)value);
    }

    private static void AdaptWeights(AudioChannel v)
    {
        int best = 0;
        int min = v.Dif[0];
        v.Dif[0] = 0;
        for (int i = 1; i < v.Dif.Length; i++)
        {
            if (v.Dif[i] < min)
            {
                min = v.Dif[i];
                best = i;
            }
            v.Dif[i] = 0;
        }
        if (best == 0) return;
        best--;
        int k = best / 2;
        if (best % 2 == 0)
        {
            if (v.K[k] >= -16) v.K[k]--;
        }
        else if (v.K[k] < 16)
        {
            v.K[k]++;
        }
    }

    /// <summary>One audio channel's predictor state (rardecode's audioVar).</summary>
    private sealed class AudioChannel
    {
        public readonly int[] K = new int[5];    // prediction weights, each within -17..16
        public readonly int[] D = new int[4];    // the last delta, then differences of earlier ones
        public readonly int[] Dif = new int[11]; // how well each one-step weight change would have predicted
        public int LastDelta;
        public int LastChar;
        public int ByteCount;                    // bytes of this channel decoded, modulo 32

        public void Reset()
        {
            Array.Clear(K);
            Array.Clear(D);
            Array.Clear(Dif);
            LastDelta = 0;
            LastChar = 0;
            ByteCount = 0;
        }
    }
}
