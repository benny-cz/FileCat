// RAR 2.9/3.x decoding (unpack versions 29 and 36: LZ, PPMd and RAR 3's standard filters) ported from rardecode
// (github.com/nwaples/rardecode v2.4.5, decode29.go, decode29_lz.go, decode29_ppm.go and the RAR 3 filter parsing of
// filters.go), Copyright (c) 2015 Nicholas Waples, BSD-2-Clause; see licenses/rardecode/LICENSE. A filter program is
// only identified, by the CRC-32 and length of its code, as one of RAR's six standard filters and runs as FileCat's own
// transform: the general RAR 3 VM is not ported. Every length, distance and filter goes through RarWindow's checks.

using FileCat.Core.Jobs;

namespace FileCat.Archives.Rar;

internal sealed class Rar3Decoder : RarDecoder
{
    private const int MainSize = 299;
    private const int OffsetSize = 60;
    private const int LowOffsetSize = 17;
    private const int LengthSize = 28;
    private const int MaxFilterRecord = 0x10000;     // a filter record: its flags byte and at most 0xFFFF bytes more
    private const int MaxCodeSize = 0x10000;         // a filter program
    private const int MaxUniqueFilters = 1024;       // highest filter slot number, as rardecode bounds it
    private const int MaxGlobalData = 0x2000 - 0x40; // a filter's global data: the VM's global area minus its fixed part

    private static ReadOnlySpan<byte> LengthBase =>
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 24, 28, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224];

    private static ReadOnlySpan<byte> LengthExtraBits =>
        [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5];

    private static readonly int[] s_offsetBase =
    [
        0, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 1536, 2048, 3072, 4096,
        6144, 8192, 12288, 16384, 24576, 32768, 49152, 65536, 98304, 131072, 196608, 262144, 327680, 393216, 458752,
        524288, 589824, 655360, 720896, 786432, 851968, 917504, 983040, 1048576, 1310720, 1572864, 1835008, 2097152,
        2359296, 2621440, 2883584, 3145728, 3407872, 3670016, 3932160,
    ];

    private static ReadOnlySpan<byte> OffsetExtraBits =>
    [
        0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15,
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18,
    ];

    private static ReadOnlySpan<byte> ShortOffsetBase => [0, 4, 8, 16, 32, 64, 128, 192];

    private static ReadOnlySpan<byte> ShortOffsetExtraBits => [2, 2, 3, 4, 5, 6, 6, 6];

    /// <summary>RAR's standard RAR 3 filter programs, known by the CRC-32 and length of their code (rardecode's standardV3Filters).</summary>
    private static readonly (uint Crc, int Length, RarFilterType Type)[] s_standardFilters =
    [
        (0xAD576887, 53, RarFilterType.E8),
        (0x3CD7E57E, 57, RarFilterType.E8E9),
        (0x3769893F, 120, RarFilterType.Itanium),
        (0x0E06077D, 29, RarFilterType.Delta),
        (0x1C2C5DC8, 149, RarFilterType.Rgb),
        (0xBC85E701, 216, RarFilterType.Audio),
    ];

    /// <summary>Why decoding a block stopped.</summary>
    private enum BlockEnd
    {
        Pending,      // the window is full: an output unit waits for the next call
        Block,        // end of block: a new block header follows
        File,         // end of the member; a solid run's next member continues this block
        BlockAndFile, // end of block and member
        DataEnded,    // the packed data ran out without an end-of-file marker
    }

    private readonly byte[] _lengths = new byte[MainSize + OffsetSize + LowOffsetSize + LengthSize];
    private readonly RarHuffman _main = new();
    private readonly RarHuffman _offset = new();
    private readonly RarHuffman _lowOffset = new();
    private readonly RarHuffman _length = new();
    private readonly int[] _distances = new int[4];
    private int _lastLength;
    private int _lowDistance;
    private int _lowDistanceRepeats;

    private Rar3Ppmd? _ppmd;
    private byte _escape = 2;

    private readonly List<RarFilterType> _filterTypes = []; // filter slots: each program seen, by slot number
    private readonly List<int> _filterLengths = [];         // each slot's last block length
    private int _filterNumber;
    private byte[] _filterRecord = [];
    private byte[] _filterCode = [];
    private RarBitReader? _filterBits;
    private readonly uint[] _registers = new uint[7];

    // An output unit decoded after the window asked to stop: written first on the next call. Decoding one unit ahead
    // means a member's end-of-file marker is always read while the member is still being read, so the next member of
    // a solid run starts exactly where its data does.
    private int _pendingLiteral = -1;
    private int _pendingLength;
    private int _pendingDistance;

    private RarBitReader _input = new();
    private bool _started;      // a member was started: a solid member has state to continue
    private bool _fresh;        // nothing of the current member's data was read yet
    private bool _blockStarted; // a block header was read and its block has not ended
    private bool _ppm;          // the current block is PPMd, not LZ
    private bool _finished;     // the member's data ended
    private bool _cleanEnd;     // ...with an end-of-file marker (or before any data): a solid run may continue
    private bool _runBroken;    // a solid member follows one whose data did not end cleanly
    private bool _failed;       // a decode ended in an exception: the state is unknown, so nothing more is decoded

    /// <summary>Every member ends with an end-of-file marker (an empty one in a solid run included).</summary>
    public override bool EndsWithMarker => true;

    public override void StartMember(RarBitReader input, bool solid)
    {
        _input = input;
        _fresh = true;
        if (solid && _started)
        {
            // Like rardecode, a solid member needs its predecessor read up to that one's end-of-file marker: the
            // look-ahead and the member stream's end-marker read have done so unless the data was damaged.
            if (!_finished || !_cleanEnd || HasPending) _runBroken = true;
            _finished = false;
            return;
        }
        _started = true;
        _finished = false;
        _cleanEnd = true;
        _runBroken = false;
        _failed = false;
        _pendingLiteral = -1;
        _pendingLength = 0;
        _blockStarted = false;
        _filterNumber = 0;
        _filterTypes.Clear();
        _filterLengths.Clear();
        Array.Clear(_distances);
        _lastLength = 0;
        Array.Clear(_lengths);
        _escape = 2;
        _ppmd?.Forget();
    }

    public override bool Decode(RarWindow window)
    {
        if (_runBroken) throw RarData.Corrupt("a solid member whose predecessor's data did not end cleanly");
        if (_failed) throw RarData.Corrupt("data after an earlier error");
        if (_finished) return true;
        _failed = true; // cleared on every normal return below
        if (_fresh)
        {
            _fresh = false;
            if (_input.AtEnd)
            {
                // No packed data at all (an empty member): nothing changes.
                _finished = true;
                _failed = false;
                return true;
            }
        }
        while (true)
        {
            if (HasPending)
            {
                if (!window.ShouldDecode) break;
                WritePending(window);
            }
            BlockEnd end;
            if (_blockStarted) end = _ppm ? DecodePpmd(window) : DecodeLz(window);
            else if (ReadBlockHeader()) continue;
            else end = BlockEnd.DataEnded;
            switch (end)
            {
                case BlockEnd.Pending:
                    continue;
                case BlockEnd.Block:
                    _blockStarted = false;
                    continue;
                case BlockEnd.File:
                    // The next member of a solid run continues this block with the same tables.
                    _cleanEnd = true;
                    break;
                case BlockEnd.BlockAndFile:
                    _blockStarted = false;
                    _cleanEnd = true;
                    break;
                default:
                    // As in rardecode, a member may end without its marker; its size and checksum decide.
                    _cleanEnd = false;
                    break;
            }
            _finished = true;
            _failed = false;
            return true;
        }
        _failed = false;
        return false;
    }

    private bool HasPending => _pendingLiteral >= 0 || _pendingLength != 0;

    private void WritePending(RarWindow window)
    {
        if (_pendingLiteral >= 0) window.Put((byte)_pendingLiteral);
        else window.Copy(_pendingLength, _pendingDistance);
        _pendingLiteral = -1;
        _pendingLength = 0;
    }

    /// <summary>Writes a literal, or keeps it for the next call when the window wants to stop; false then.</summary>
    private bool EmitLiteral(RarWindow window, byte value)
    {
        if (!window.ShouldDecode)
        {
            _pendingLiteral = value;
            return false;
        }
        window.Put(value);
        return true;
    }

    /// <summary>Writes a match, or keeps it for the next call when the window wants to stop; false then.</summary>
    private bool EmitMatch(RarWindow window, int length, int distance)
    {
        if (!window.ShouldDecode)
        {
            _pendingLength = length;
            _pendingDistance = distance;
            return false;
        }
        window.Copy(length, distance);
        return true;
    }

    /// <summary>
    /// Reads a block header at the next byte: PPMd or LZ, with the block's model parameters or code tables. False when
    /// the data has ended instead.
    /// </summary>
    private bool ReadBlockHeader()
    {
        _input.AlignToByte();
        if (_input.AtEnd) return false;
        _ppm = _input.ReadBit();
        if (_ppm) StartPpmdBlock();
        else StartLzBlock();
        _blockStarted = true;
        return true;
    }

    // ---- LZ (decode29_lz.go) ----

    private void StartLzBlock()
    {
        _lowDistance = 0;
        _lowDistanceRepeats = 0;
        bool addToOld = _input.ReadBit();
        RarHuffman.ReadLengthTable(_input, _lengths, addToOld);
        var lengths = _lengths.AsSpan();
        _main.Build(lengths[..MainSize]);
        _offset.Build(lengths.Slice(MainSize, OffsetSize));
        _lowOffset.Build(lengths.Slice(MainSize + OffsetSize, LowOffsetSize));
        _length.Build(lengths.Slice(MainSize + OffsetSize + LowOffsetSize, LengthSize));
    }

    private BlockEnd DecodeLz(RarWindow window)
    {
        while (true)
        {
            // Every symbol indexes only tables of its own code's size.
            if (!_main.TryDecode(_input, out int symbol)) return BlockEnd.DataEnded;
            if (symbol < 256)
            {
                if (!EmitLiteral(window, (byte)symbol)) return BlockEnd.Pending;
                continue;
            }
            if (symbol == 258)
            {
                // The previous match again; before any match there is nothing to repeat.
                if (_lastLength != 0 && !EmitMatch(window, _lastLength, _distances[0])) return BlockEnd.Pending;
                continue;
            }
            if (symbol >= 271) DecodeMatch(symbol - 271);
            else if (symbol >= 263) DecodeShortMatch(symbol - 263);
            else if (symbol >= 259) DecodeRepeat(symbol - 259);
            else if (symbol == 256) return ReadEndOfBlock();
            else
            {
                ReadFilter(window, ReadLzFilterRecord());
                continue;
            }
            if (!EmitMatch(window, _lastLength, _distances[0])) return BlockEnd.Pending;
        }
    }

    private BlockEnd ReadEndOfBlock()
    {
        if (_input.ReadBit()) return BlockEnd.Block; // new tables follow
        return _input.ReadBit() ? BlockEnd.BlockAndFile : BlockEnd.File;
    }

    /// <summary>One of the four last distances again, with a new length.</summary>
    private void DecodeRepeat(int index)
    {
        int distance = _distances[index];
        for (int i = index; i > 0; i--) _distances[i] = _distances[i - 1];
        _distances[0] = distance;
        int slot = _length.Decode(_input);
        _lastLength = LengthBase[slot] + 2 + (int)_input.Read(LengthExtraBits[slot]);
    }

    /// <summary>A two-byte match at a short distance.</summary>
    private void DecodeShortMatch(int index)
    {
        for (int i = _distances.Length - 1; i > 0; i--) _distances[i] = _distances[i - 1];
        _distances[0] = ShortOffsetBase[index] + 1 + (int)_input.Read(ShortOffsetExtraBits[index]);
        _lastLength = 2;
    }

    private void DecodeMatch(int lengthSlot)
    {
        int length = LengthBase[lengthSlot] + 3 + (int)_input.Read(LengthExtraBits[lengthSlot]);
        int slot = _offset.Decode(_input);
        int distance = s_offsetBase[slot] + 1;
        int bits = OffsetExtraBits[slot];
        if (bits >= 4)
        {
            if (bits > 4) distance += (int)_input.Read(bits - 4) << 4;
            // The low four bits have their own code, and a symbol that repeats the last ones 16 times.
            if (_lowDistanceRepeats > 0)
            {
                _lowDistanceRepeats--;
                distance += _lowDistance;
            }
            else
            {
                int low = _lowOffset.Decode(_input);
                if (low == 16)
                {
                    _lowDistanceRepeats = 15;
                    distance += _lowDistance;
                }
                else
                {
                    distance += low;
                    _lowDistance = low;
                }
            }
        }
        else if (bits > 0)
        {
            distance += (int)_input.Read(bits);
        }
        if (distance >= 0x2000)
        {
            length++;
            if (distance >= 0x40000) length++;
        }
        for (int i = _distances.Length - 1; i > 0; i--) _distances[i] = _distances[i - 1];
        _distances[0] = distance;
        _lastLength = length;
    }

    /// <summary>Reads an LZ filter record into the record buffer; returns its length.</summary>
    private int ReadLzFilterRecord()
    {
        int flags = (int)_input.Read(8);
        int length = (flags & 7) + 1;
        if (length == 7) length = (int)_input.Read(8) + 7;
        else if (length == 8) length = (int)_input.Read(16);
        var record = FilterRecord(length);
        record[0] = (byte)flags;
        for (int i = 1; i < record.Length; i++) record[i] = _input.ReadByte();
        return record.Length;
    }

    // ---- PPMd (decode29_ppm.go) ----

    private void StartPpmdBlock()
    {
        int flags = (int)_input.Read(7);
        bool reset = (flags & 0x20) != 0;
        int megabytes = reset ? _input.ReadByte() + 1 : 0;
        if ((flags & 0x40) != 0) _escape = _input.ReadByte();
        int maxOrder = (flags & 0x1F) + 1;
        if (maxOrder > 16) maxOrder = 16 + (maxOrder - 16) * 3;
        _ppmd ??= new Rar3Ppmd();
        _ppmd.StartBlock(_input, reset, maxOrder, megabytes);
    }

    private BlockEnd DecodePpmd(RarWindow window)
    {
        var ppmd = _ppmd!;
        while (true)
        {
            byte value = ppmd.DecodeByte();
            if (value != _escape)
            {
                if (!EmitLiteral(window, value)) return BlockEnd.Pending;
                continue;
            }
            switch (ppmd.DecodeByte())
            {
                case 0:
                    return BlockEnd.Block;
                case 2:
                    return BlockEnd.BlockAndFile;
                case 3:
                    ReadFilter(window, ReadPpmdFilterRecord());
                    break;
                case 4:
                {
                    // A long match: three distance bytes, most significant first, then the length.
                    int distance = 0;
                    for (int i = 0; i < 3; i++) distance = distance << 8 | ppmd.DecodeByte();
                    int length = ppmd.DecodeByte();
                    if (!EmitMatch(window, length + 32, distance + 2)) return BlockEnd.Pending;
                    break;
                }
                case 5:
                {
                    // A run of the last byte.
                    int length = ppmd.DecodeByte();
                    if (!EmitMatch(window, length + 4, 1)) return BlockEnd.Pending;
                    break;
                }
                default:
                    // Any other code after the escape stands for the escape byte itself.
                    if (!EmitLiteral(window, _escape)) return BlockEnd.Pending;
                    break;
            }
        }
    }

    /// <summary>Reads a PPMd filter record (the same layout as an LZ one, in decoded bytes); returns its length.</summary>
    private int ReadPpmdFilterRecord()
    {
        var ppmd = _ppmd!;
        int flags = ppmd.DecodeByte();
        int length = (flags & 7) + 1;
        if (length == 7)
        {
            length = ppmd.DecodeByte() + 7;
        }
        else if (length == 8)
        {
            int high = ppmd.DecodeByte();
            length = high << 8 | ppmd.DecodeByte();
        }
        var record = FilterRecord(length);
        record[0] = (byte)flags;
        for (int i = 1; i < record.Length; i++) record[i] = ppmd.DecodeByte();
        return record.Length;
    }

    // ---- Filters (decode29.go parseVMFilter and readVMCode, filters.go getV3Filter) ----

    /// <summary>The record buffer for a flags byte and <paramref name="length"/> bytes more.</summary>
    private Span<byte> FilterRecord(int length)
    {
        if (length + 1 > MaxFilterRecord) throw RarData.Corrupt("RAR 3 filter record size");
        if (_filterRecord.Length == 0) _filterRecord = new byte[MaxFilterRecord];
        return _filterRecord.AsSpan(0, length + 1);
    }

    /// <summary>Parses a filter record (the first <paramref name="recordLength"/> bytes of the record buffer) and queues its filter.</summary>
    private void ReadFilter(RarWindow window, int recordLength)
    {
        int flags = _filterRecord[0];
        var bits = _filterBits ??= new RarBitReader();
        bits.Reset(new MemoryStream(_filterRecord, 1, recordLength - 1, writable: false));

        // The slot: a new one, a numbered earlier one, or (by default) the slot of the previous filter.
        if ((flags & 0x80) != 0)
        {
            uint number = bits.ReadRar3UInt32();
            if (number == 0)
            {
                _filterTypes.Clear();
                _filterLengths.Clear();
            }
            else
            {
                number--;
                if (number > MaxUniqueFilters || number > (uint)_filterTypes.Count) throw RarData.Corrupt("RAR 3 filter number");
            }
            _filterNumber = (int)number;
        }
        bool newSlot = _filterNumber == _filterTypes.Count;

        uint offset = bits.ReadRar3UInt32();
        if ((flags & 0x40) != 0) offset = unchecked(offset + 258);

        // A slot remembers its block length; a new slot without one has none (rardecode would take another slot's).
        int length = newSlot ? 0 : _filterLengths[_filterNumber];
        if ((flags & 0x20) != 0)
        {
            uint value = bits.ReadRar3UInt32();
            if (value > RarLimits.MaxFilterBlockBytes) throw RarData.Corrupt("filter block size");
            length = (int)value;
        }

        Array.Clear(_registers);
        if ((flags & 0x10) != 0)
        {
            uint mask = bits.Read(_registers.Length);
            for (int i = 0; i < _registers.Length; i++, mask >>= 1)
            {
                if ((mask & 1) != 0) _registers[i] = bits.ReadRar3UInt32();
            }
        }

        RarFilterType type;
        if (newSlot)
        {
            type = ReadFilterProgram(bits);
            _filterTypes.Add(type);
            _filterLengths.Add(length);
        }
        else
        {
            type = _filterTypes[_filterNumber];
            _filterLengths[_filterNumber] = length;
        }

        if ((flags & 0x08) != 0)
        {
            // Data for the program's global area: bounded as rardecode bounds it; the standard filters do not use it.
            uint size = bits.ReadRar3UInt32();
            if (size > MaxGlobalData) throw RarData.Corrupt("RAR 3 filter data size");
            for (uint i = 0; i < size; i++) bits.ReadByte();
        }

        long start = window.Written + offset;
        window.AddFilter(type switch
        {
            RarFilterType.Delta or RarFilterType.Audio =>
                new RarFilter { Type = type, Start = start, Length = length, Channels = ToInt(_registers[0]) },
            RarFilterType.Rgb =>
                new RarFilter { Type = type, Start = start, Length = length, Width = ToInt(_registers[0] - 3L), PosR = ToInt(_registers[1]) },
            _ => new RarFilter { Type = type, Start = start, Length = length },
        });
    }

    /// <summary>Reads a filter program and names the standard filter it is; any other program is refused.</summary>
    private RarFilterType ReadFilterProgram(RarBitReader bits)
    {
        uint size = bits.ReadRar3UInt32();
        if (size == 0 || size > MaxCodeSize) throw RarData.Corrupt("RAR 3 filter program size");
        if (_filterCode.Length == 0) _filterCode = new byte[MaxCodeSize];
        var code = _filterCode.AsSpan(0, (int)size);
        for (int i = 0; i < code.Length; i++) code[i] = bits.ReadByte();
        byte xor = 0;
        foreach (byte b in code[1..]) xor ^= b;
        if (xor != code[0]) throw RarData.Corrupt("RAR 3 filter program checksum");
        uint crc = Crc32.HashToUInt32(code);
        foreach (var known in s_standardFilters)
        {
            if (known.Crc == crc && known.Length == code.Length) return known.Type;
        }
        throw RarData.Unsupported("a RAR 3 filter that is not one of RAR's standard filters");
    }

    /// <summary>A filter parameter as an int; out-of-range values stay out of range for RarWindow's checks to refuse.</summary>
    private static int ToInt(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
}
