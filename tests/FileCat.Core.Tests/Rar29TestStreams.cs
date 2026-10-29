using System.Runtime.InteropServices;

namespace FileCat.Core.Tests;

/// <summary>Bits most significant first, as RAR packs them: hand-made RAR 2.9 LZ data and filter records.</summary>
internal sealed class BitWriter
{
    private readonly List<byte> _bytes = [];
    private int _used; // bits used in the last byte

    public byte[] ToArray() => [.. _bytes];

    public void Write(uint value, int count)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            if (_used == 0) _bytes.Add(0);
            if ((value >> i & 1) != 0) _bytes[^1] |= (byte)(0x80 >> _used);
            _used = (_used + 1) & 7;
        }
    }

    public void Write((uint Code, int Length) code) => Write(code.Code, code.Length);

    public void AlignToByte() => _used = 0;

    /// <summary>RAR 3's compact number form (RarBitReader.ReadRar3UInt32 reads it back).</summary>
    public void WriteRar3UInt32(uint value)
    {
        if (value < 16) { Write(0, 2); Write(value, 4); }
        else if (value < 256) { Write(1, 2); Write(value, 8); }
        else if (value < 65536) { Write(2, 2); Write(value, 16); }
        else { Write(3, 2); Write(value, 32); }
    }

    /// <summary>A filter program: its size, then its bytes.</summary>
    public void WriteProgram(byte[] program)
    {
        WriteRar3UInt32((uint)program.Length);
        foreach (byte b in program) Write(b, 8);
    }

    /// <summary>
    /// An LZ block header with new code tables: 299 main, 60 offset, 17 low-offset and 28 length code lengths. Every
    /// code-length symbol gets five bits, so symbol v is simply the five bits of v.
    /// </summary>
    public void WriteLzTables(byte[] lengths)
    {
        AlignToByte();
        Write(0, 1); // LZ, not PPMd
        Write(0, 1); // new tables, not added to the old ones
        for (int i = 0; i < 20; i++) Write(5, 4);
        for (int i = 0; i < lengths.Length;)
        {
            int run = 0;
            while (lengths[i] == 0 && i + run < lengths.Length && lengths[i + run] == 0 && run < 138) run++;
            if (run >= 11)
            {
                Write(19, 5);
                Write((uint)(run - 11), 7);
                i += run;
            }
            else if (run >= 3)
            {
                Write(18, 5);
                Write((uint)(run - 3), 3);
                i += run;
            }
            else
            {
                Write(lengths[i], 5);
                i++;
            }
        }
    }
}

/// <summary>
/// RAR 3 PPMd data for tests: PPMd variant H with RAR's range coder, wrapped in RAR's PPMd block headers and escape
/// codes. The model is a copy of FileCat's decoder model (PpmdModelH in src/ThirdParty/SharpCompress/Compressors/PPMd,
/// shared by RAR 3 and 7z) with its three coding points turned into encoding. Upstream SharpCompress (UnRAR's PPMd-H,
/// test-only) decoding this output shows the model matches RAR's; FileCat decoding it shows the decoding side does too.
/// Keep the two models in step.
/// </summary>
internal sealed class Rar3PpmdTestStream
{
    private readonly List<byte> _bytes = [];
    private readonly Rar3PpmdTestEncoder _model = new();
    private byte _escape = 2;
    private bool _inBlock;

    public byte[] ToArray() => [.. _bytes];

    /// <summary>The bytes so far (one member's packed data); the model goes on for the next member.</summary>
    public byte[] Take()
    {
        byte[] taken = [.. _bytes];
        _bytes.Clear();
        return taken;
    }

    /// <summary>A PPMd block header (byte-aligned), then a fresh range coder.</summary>
    public void StartBlock(int maxOrder, int megabytes, bool reset, byte? escape = null)
    {
        if (_inBlock) throw new InvalidOperationException("End the block first.");
        int field = maxOrder <= 16 ? maxOrder - 1 : 16 + (maxOrder - 16) / 3 - 1;
        int flags = field | (reset ? 0x20 : 0) | (escape is null ? 0 : 0x40);
        _bytes.Add((byte)(0x80 | flags)); // the PPMd bit, then seven flag bits
        if (reset) _bytes.Add((byte)(megabytes - 1));
        if (escape is byte e)
        {
            _bytes.Add(e);
            _escape = e;
        }
        _model.Start(_bytes, reset, maxOrder, megabytes);
        _inBlock = true;
    }

    public void Literal(byte value)
    {
        if (value == _escape)
        {
            _model.EncodeByte(_escape);
            _model.EncodeByte(1); // RAR's code for the escape byte itself
        }
        else
        {
            _model.EncodeByte(value);
        }
    }

    public void Literals(ReadOnlySpan<byte> values)
    {
        foreach (byte value in values) Literal(value);
    }

    /// <summary>Escape 4: a match of 32..287 bytes at distance 2..0x1000001.</summary>
    public void Match(int length, int distance)
    {
        _model.EncodeByte(_escape);
        _model.EncodeByte(4);
        int d = distance - 2;
        _model.EncodeByte((byte)(d >> 16));
        _model.EncodeByte((byte)(d >> 8));
        _model.EncodeByte((byte)d);
        _model.EncodeByte((byte)(length - 32));
    }

    /// <summary>Escape 5: a run of 4..259 copies of the last byte.</summary>
    public void Run(int length)
    {
        _model.EncodeByte(_escape);
        _model.EncodeByte(5);
        _model.EncodeByte((byte)(length - 4));
    }

    /// <summary>Escape 3: a filter record (its flags byte, then the rest).</summary>
    public void Filter(ReadOnlySpan<byte> record)
    {
        _model.EncodeByte(_escape);
        _model.EncodeByte(3);
        foreach (byte b in record) _model.EncodeByte(b);
    }

    /// <summary>Any escape code, raw (adversarial streams).</summary>
    public void Escape(byte code)
    {
        _model.EncodeByte(_escape);
        _model.EncodeByte(code);
    }

    /// <summary>Escape 0: the block ends; another block header follows.</summary>
    public void EndBlock()
    {
        Escape(0);
        _model.Flush();
        _inBlock = false;
    }

    /// <summary>Escape 2: the block and the member end.</summary>
    public void EndFile()
    {
        Escape(2);
        _model.Flush();
        _inBlock = false;
    }

    /// <summary>The range coder's last bytes without an end code (truncation tests).</summary>
    public void FlushWithoutEnd()
    {
        _model.Flush();
        _inBlock = false;
    }
}

/// <summary>The PPMd variant H model of <c>PpmdModelH</c> (RAR's <c>Rar3Ppmd</c>), encoding instead of decoding.</summary>
internal sealed class Rar3PpmdTestEncoder
{
    private const uint RangeTop = 1 << 24;
    private const uint RangeBottom = 1 << 15;
    private const int MaxFreq = 124;
    private const int IntBits = 7;
    private const int PeriodBits = 7;
    private const int BinScale = 1 << (IntBits + PeriodBits);
    private const int IndexCount = 39;
    private const int UnitSize = 12;
    private const int FreeMark = -1;

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct State
    {
        public byte Symbol;
        public byte Freq;
        public int Successor;
    }

    private struct See2Context
    {
        public ushort Summ;
        public byte Shift;
        public byte Count;
    }

    private static readonly byte[] s_expEscape = [25, 14, 9, 7, 5, 5, 4, 4, 4, 3, 3, 3, 2, 2, 2, 2];
    private static readonly ushort[] s_initBinEsc = [0x3CDD, 0x1F3F, 0x59BF, 0x48F3, 0x64A1, 0x5ABC, 0x6632, 0x6051];
    private static readonly byte[] s_ns2BSIndex = BuildNs2BSIndex();
    private static readonly byte[] s_ns2Index = BuildNs2Index();
    private static readonly int[] s_index2Units = BuildIndex2Units();
    private static readonly byte[] s_units2Index = BuildUnits2Index();

    private List<byte> _output = [];
    private uint _low;
    private uint _range;

    private State[] _states = [];
    private int _stateCount;
    private int _glueCount;
    private int _heap1MaxBytes;
    private int _heap1Lo;
    private int _heap1Hi;
    private int _heap2Lo;
    private int _heap2Hi;
    private readonly int[] _freeList = new int[IndexCount];

    private int _maxOrder;
    private int _orderFall;
    private int _initRL;
    private int _runLength;
    private byte _prevSuccess;
    private byte _escCount;
    private byte _prevSym;
    private byte _initEsc;
    private int _context;
    private readonly byte[] _charMask = new byte[256];
    private readonly ushort[] _binSumm = new ushort[128 * 64];
    private readonly See2Context[] _see2 = new See2Context[25 * 16];
    private readonly int[] _unmasked = new int[256];
    private readonly int[] _chain = new int[256];

    public void Start(List<byte> output, bool reset, int maxOrder, int megabytes)
    {
        _output = output;
        _low = 0;
        _range = uint.MaxValue;
        if (!reset) return;
        InitAllocator(megabytes);
        _maxOrder = maxOrder;
        _prevSym = 0;
        _context = 0;
    }

    public void Flush()
    {
        for (int i = 0; i < 4; i++)
        {
            _output.Add((byte)(_low >> 24));
            _low <<= 8;
        }
    }

    public void EncodeByte(byte symbol)
    {
        if (_context == 0) Restart();
        int minC = _context;
        int maxC = minC;
        int found = NumStates(minC) == 1 ? EncodeBinSymbol(minC, symbol) : EncodeSymbol1(minC, symbol);
        while (found < 0)
        {
            int masked = NumStates(minC);
            do
            {
                _orderFall++;
                minC = Suffix(minC);
                if (minC == 0) throw new InvalidOperationException("The model cannot code this symbol.");
            }
            while (NumStates(minC) == masked);
            found = EncodeSymbol2(minC, masked, symbol);
        }
        _context = Update(minC, maxC, found);
        _prevSym = symbol;
    }

    // ---- Range encoder (the decoder's mirror image) ----

    private void Encode(uint lowCount, uint highCount, uint scale)
    {
        _range /= scale;
        unchecked
        {
            _low += _range * lowCount;
            _range *= highCount - lowCount;
        }
        while (true)
        {
            if ((_low ^ unchecked(_low + _range)) >= RangeTop)
            {
                if (_range >= RangeBottom) return;
                _range = unchecked(0u - _low) & (RangeBottom - 1);
            }
            _output.Add((byte)(_low >> 24));
            _range <<= 8;
            _low <<= 8;
        }
    }

    private int EncodeBinSymbol(int context, byte symbol)
    {
        int s = context + 1;
        int i = _prevSuccess + s_ns2BSIndex[NumStates(Suffix(context)) - 1] + ((_runLength >> 26) & 0x20);
        if (_prevSym >= 64) i += 8;
        if (_states[s].Symbol >= 64) i += 2 * 8;
        int index = (_states[s].Freq - 1) * 64 + i;
        ushort bs = _binSumm[index];
        int mean = (ushort)(bs + (1 << (PeriodBits - 2))) >> PeriodBits;
        if (_states[s].Symbol == symbol)
        {
            Encode(0, bs, BinScale);
            if (_states[s].Freq < 128) _states[s].Freq++;
            _binSumm[index] = unchecked((ushort)(bs + (1 << IntBits) - mean));
            _prevSuccess = 1;
            _runLength = unchecked(_runLength + 1);
            return s;
        }
        Encode(bs, BinScale, BinScale);
        bs = unchecked((ushort)(bs - mean));
        _binSumm[index] = bs;
        _initEsc = s_expEscape[bs >> 10];
        _charMask[_states[s].Symbol] = _escCount;
        _prevSuccess = 0;
        return -1;
    }

    private int EncodeSymbol1(int context, byte symbol)
    {
        int count = GetStates(context, out int start);
        uint scale = SummFreq(context);
        _prevSuccess = 0;
        uint n = 0;
        for (int i = 0; i < count; i++)
        {
            int s = start + i;
            n += _states[s].Freq;
            if (_states[s].Symbol != symbol) continue;
            Encode(n - _states[s].Freq, n, scale);
            unchecked
            {
                _states[s].Freq += 4;
                SetSummFreq(context, (ushort)(scale + 4));
            }
            if (i == 0)
            {
                if (2 * n > scale)
                {
                    _prevSuccess = 1;
                    _runLength = unchecked(_runLength + 1);
                }
            }
            else
            {
                if (_states[s].Freq <= _states[s - 1].Freq) return s;
                (_states[s - 1], _states[s]) = (_states[s], _states[s - 1]);
                s--;
            }
            return Rescale(context, s);
        }
        for (int i = 0; i < count; i++) _charMask[_states[start + i].Symbol] = _escCount;
        Encode(n, scale, scale);
        return -1;
    }

    private int EncodeSymbol2(int context, int numMasked, byte symbol)
    {
        int see = MakeEscFreq(context, numMasked);
        uint scale = SeeMean(see);
        int count = GetStates(context, out int start);
        int unmasked = count - numMasked;
        uint hi = 0;
        for (int i = 0, j = 0; j < unmasked; j++, i++)
        {
            while (_charMask[_states[start + i].Symbol] == _escCount) i++;
            hi += _states[start + i].Freq;
            _unmasked[j] = start + i;
        }
        scale += hi;
        uint lo = 0;
        int k = 0;
        while (k < unmasked && _states[_unmasked[k]].Symbol != symbol) lo += _states[_unmasked[k++]].Freq;
        if (k == unmasked)
        {
            Encode(hi, scale, scale);
            if (see >= 0) _see2[see].Summ = unchecked((ushort)(_see2[see].Summ + scale));
            for (int j = 0; j < unmasked; j++) _charMask[_states[_unmasked[j]].Symbol] = _escCount;
            return -1;
        }
        int s = _unmasked[k];
        Encode(lo, lo + _states[s].Freq, scale);
        SeeUpdate(see);
        unchecked
        {
            _escCount++;
            _runLength = _initRL;
            _states[s].Freq += 4;
            SetSummFreq(context, (ushort)(SummFreq(context) + 4));
        }
        return Rescale(context, s);
    }

    // ---- From here on, the model exactly as PpmdModelH has it (without its damage checks) ----

    private void InitAllocator(int megabytes)
    {
        int bytes = megabytes << 20;
        int heap2Units = bytes / 8 / UnitSize * 7;
        _heap1MaxBytes = bytes - heap2Units * UnitSize;
        int heap1Units = _heap1MaxBytes / UnitSize + 1;
        int count = (1 + heap1Units + heap2Units) * 2;
        if (_states.Length < count) _states = new State[count];
        _stateCount = count;
    }

    private void RestartAllocator()
    {
        _heap1Lo = UnitSize + (UnitSize - _heap1MaxBytes % UnitSize);
        _heap1Hi = UnitSize + (_heap1MaxBytes / UnitSize + 1) * UnitSize;
        _heap2Lo = _heap1Hi / UnitSize * 2;
        _heap2Hi = _stateCount;
        _glueCount = 0;
        Array.Clear(_freeList);
    }

    private int PushByte(byte value)
    {
        int position = _heap1Lo;
        ref State state = ref _states[position / 6];
        switch (position % 6)
        {
            case 0:
                state.Symbol = value;
                break;
            case 1:
                state.Freq = value;
                break;
            default:
            {
                int shift = (position % 6 - 2) * 8;
                uint successor = (uint)state.Successor & ~(0xFFu << shift);
                state.Successor = (int)(successor | (uint)value << shift);
                break;
            }
        }
        _heap1Lo++;
        if (_heap1Lo >= _heap1Hi) return 0;
        return -_heap1Lo;
    }

    private byte TextByte(int pointer)
    {
        int position = -pointer;
        var state = _states[position / 6];
        return (position % 6) switch
        {
            0 => state.Symbol,
            1 => state.Freq,
            _ => (byte)((uint)state.Successor >> ((position % 6 - 2) * 8)),
        };
    }

    private int RemoveFreeBlock(int index)
    {
        int n = _freeList[index];
        if (n == 0) return 0;
        _freeList[index] = _states[n].Successor;
        _states[n] = default;
        return n;
    }

    private void AddFreeBlock(int n, int index)
    {
        _states[n].Successor = _freeList[index];
        _freeList[index] = n;
    }

    private void FreeUnits(int n, int units)
    {
        int i = s_units2Index[units];
        if (units != s_index2Units[i])
        {
            i--;
            AddFreeBlock(n, i);
            units -= s_index2Units[i];
            n += s_index2Units[i] << 1;
            i = s_units2Index[units];
        }
        AddFreeBlock(n, i);
    }

    private static int BlockUnits(in State state) => state.Symbol | state.Freq << 8;

    private static void SetBlockUnits(ref State state, int units)
    {
        state.Symbol = (byte)units;
        state.Freq = (byte)(units >> 8);
    }

    private void GlueFreeBlocks()
    {
        int head = 0;
        for (int i = 0; i < IndexCount; i++)
        {
            var stamp = new State { Successor = FreeMark };
            SetBlockUnits(ref stamp, s_index2Units[i]);
            for (int n = _freeList[i]; n != 0;)
            {
                _states[n + 1].Successor = head;
                head = n;
                int next = _states[n].Successor;
                _states[n] = stamp;
                n = next;
            }
            _freeList[i] = 0;
        }
        if (_heap2Lo != _heap2Hi) _states[_heap2Lo].Successor = 0;
        for (int n = head; n != 0; n = _states[n + 1].Successor)
        {
            if (_states[n].Successor != FreeMark) continue;
            int units = BlockUnits(_states[n]);
            for (int next = n + (units << 1); next < _stateCount && _states[next].Successor == FreeMark; next = n + (units << 1))
            {
                units += BlockUnits(_states[next]);
                if (units > ushort.MaxValue) break;
                _states[next].Successor = 0;
                SetBlockUnits(ref _states[n], units);
            }
        }
        for (int n = head; n != 0; n = _states[n + 1].Successor)
        {
            if (_states[n].Successor != FreeMark) continue;
            _states[n].Successor = 0;
            int units = BlockUnits(_states[n]);
            int m = n;
            for (; units > 128; units -= 128, m += 256) AddFreeBlock(m, IndexCount - 1);
            FreeUnits(m, units);
        }
    }

    private int AllocUnitsRare(int index)
    {
        if (_glueCount == 0)
        {
            _glueCount = 255;
            GlueFreeBlocks();
            int n = RemoveFreeBlock(index);
            if (n > 0) return n;
        }
        for (int i = index + 1; i < IndexCount; i++)
        {
            int n = RemoveFreeBlock(i);
            if (n > 0)
            {
                FreeUnits(n + (s_index2Units[index] << 1), s_index2Units[i] - s_index2Units[index]);
                return n;
            }
        }
        _glueCount--;
        int top = _heap1Hi - s_index2Units[index] * UnitSize;
        if (top > _heap1Lo)
        {
            _heap1Hi = top;
            return _heap1Hi / UnitSize * 2;
        }
        return 0;
    }

    private int AllocUnits(int index)
    {
        int n = RemoveFreeBlock(index);
        if (n > 0) return n;
        int size = s_index2Units[index] << 1;
        if (_heap2Lo + size <= _heap2Hi)
        {
            int lo = _heap2Lo;
            _heap2Lo += size;
            return lo;
        }
        return AllocUnitsRare(index);
    }

    private int NewContext(State state, int suffix)
    {
        int n;
        if (_heap2Lo < _heap2Hi)
        {
            _heap2Hi -= 2;
            n = _heap2Hi;
        }
        else
        {
            n = RemoveFreeBlock(1);
            if (n == 0)
            {
                n = AllocUnitsRare(1);
                if (n == 0) return 0;
            }
        }
        _states[n] = new State { Successor = suffix };
        _states[n + 1] = state;
        return n;
    }

    private int NumStates(int context) => _states[context].Symbol + 1;

    private ushort SummFreq(int context) => (ushort)(_states[context + 1].Symbol | _states[context + 1].Freq << 8);

    private void SetSummFreq(int context, int value)
    {
        _states[context + 1].Symbol = (byte)value;
        _states[context + 1].Freq = (byte)(value >> 8);
    }

    private int Suffix(int context) => Math.Max(_states[context].Successor, 0);

    private int GetStates(int context, out int start)
    {
        int count = _states[context].Symbol + 1;
        start = count == 1 ? context + 1 : _states[context + 1].Successor;
        return count;
    }

    private int FindState(int context, byte symbol)
    {
        int count = GetStates(context, out int start);
        int i = 0;
        while (i < count - 1 && _states[start + i].Symbol != symbol) i++;
        return start + i;
    }

    private int NewContextSize(int count)
    {
        int context = NewContext(default, 0);
        _states[context].Symbol = (byte)(count - 1);
        _states[context + 1].Successor = AllocUnits(s_units2Index[(count + 1) >> 1]);
        return context;
    }

    private int ShrinkStates(int context, int start, int count, int size)
    {
        int i1 = s_units2Index[(count + 1) >> 1];
        int i2 = s_units2Index[(size + 1) >> 1];
        if (size == 1)
        {
            _states[context + 1] = _states[start];
            AddFreeBlock(start, i1);
            start = context + 1;
        }
        else if (i1 != i2)
        {
            int n = RemoveFreeBlock(i2);
            if (n > 0)
            {
                Array.Copy(_states, start, _states, n, size);
                AddFreeBlock(start, i1);
                _states[context + 1].Successor = n;
                start = n;
            }
            else
            {
                FreeUnits(start + (s_index2Units[i2] << 1), s_index2Units[i1] - s_index2Units[i2]);
            }
        }
        _states[context].Symbol = (byte)(size - 1);
        return start;
    }

    private int ExpandStates(int context, out int newCount)
    {
        newCount = 0;
        int count = GetStates(context, out int start);
        if (count == 1)
        {
            var only = _states[start];
            int n = AllocUnits(1);
            if (n == 0) return -1;
            _states[context + 1].Successor = n;
            _states[n] = only;
            start = n;
        }
        else if ((count & 1) == 0)
        {
            int units = count >> 1;
            int i1 = s_units2Index[units];
            int i2 = s_units2Index[units + 1];
            if (i1 != i2)
            {
                int n = AllocUnits(i2);
                if (n == 0) return -1;
                Array.Copy(_states, start, _states, n, count);
                AddFreeBlock(start, i1);
                _states[context + 1].Successor = n;
                start = n;
            }
        }
        _states[context].Symbol = (byte)count;
        newCount = count + 1;
        return start;
    }

    private void Restart()
    {
        Array.Clear(_charMask);
        _escCount = 1;
        _initRL = _maxOrder < 12 ? -_maxOrder - 1 : -12 - 1;
        _orderFall = _maxOrder;
        _runLength = _initRL;
        _prevSuccess = 0;
        RestartAllocator();
        _context = NewContextSize(256);
        SetSummFreq(_context, 257);
        GetStates(_context, out int start);
        for (int i = 0; i < 256; i++) _states[start + i] = new State { Symbol = (byte)i, Freq = 1 };
        for (int i = 0; i < 128; i++)
        {
            for (int j = 0; j < s_initBinEsc.Length; j++)
            {
                ushort value = (ushort)(BinScale - s_initBinEsc[j] / (i + 2));
                for (int k = j; k < 64; k += s_initBinEsc.Length) _binSumm[i * 64 + k] = value;
            }
        }
        for (int i = 0; i < 25; i++)
        {
            var see = new See2Context { Summ = (ushort)((5 * i + 10) << (PeriodBits - 4)), Shift = PeriodBits - 4, Count = 4 };
            _see2.AsSpan(i * 16, 16).Fill(see);
        }
    }

    private int Rescale(int context, int s)
    {
        if (_states[s].Freq <= MaxFreq) return s;
        unchecked
        {
            _states[s].Freq += 4;
            int count = GetStates(context, out int start);
            ushort escFreq = (ushort)(SummFreq(context) + 4);
            ushort summFreq = 0;
            for (int i = 0; i < count; i++)
            {
                byte f = _states[start + i].Freq;
                escFreq -= f;
                if (_orderFall != 0) f++;
                f >>= 1;
                summFreq += f;
                _states[start + i].Freq = f;
                if (i == 0 || f <= _states[start + i - 1].Freq) continue;
                int j = i - 1;
                while (j > 0 && f > _states[start + j - 1].Freq) j--;
                var moved = _states[start + i];
                Array.Copy(_states, start + j, _states, start + j + 1, i - j);
                _states[start + j] = moved;
            }
            int last = count - 1;
            while (_states[start + last].Freq == 0)
            {
                last--;
                escFreq++;
            }
            if (last != count - 1) start = ShrinkStates(context, start, count, last + 1);
            if (last == 0)
            {
                do
                {
                    _states[start].Freq -= (byte)(_states[start].Freq >> 1);
                    escFreq >>= 1;
                }
                while (escFreq > 1);
                return start;
            }
            summFreq += (ushort)(escFreq - (escFreq >> 1));
            SetSummFreq(context, summFreq);
            return start;
        }
    }

    private int MakeEscFreq(int context, int numMasked)
    {
        int count = NumStates(context);
        if (count == 256) return -1;
        int diff = count - numMasked;
        int i = _prevSym >= 64 ? 8 : 0;
        if (diff < NumStates(Suffix(context)) - count) i++;
        if (SummFreq(context) < 11 * count) i += 2;
        if (numMasked > diff) i += 4;
        return s_ns2Index[diff - 1] * 16 + i;
    }

    private uint SeeMean(int see)
    {
        if (see < 0) return 1;
        ref var context = ref _see2[see];
        int n = context.Summ >> context.Shift;
        if (n == 0) return 1;
        context.Summ = (ushort)(context.Summ - n);
        return (uint)n;
    }

    private void SeeUpdate(int see)
    {
        if (see < 0) return;
        ref var context = ref _see2[see];
        if (context.Shift >= PeriodBits) return;
        context.Count--;
        if (context.Count == 0)
        {
            context.Summ = unchecked((ushort)(context.Summ + context.Summ));
            context.Count = (byte)(3 << context.Shift);
            context.Shift++;
        }
    }

    private int CreateSuccessors(int c, int s, int ss)
    {
        int chain = 0;
        if (_orderFall != 0) _chain[chain++] = s;
        for (int suffix = Suffix(c); suffix > 0; suffix = Suffix(c))
        {
            c = suffix;
            if (ss < 0) ss = FindState(c, _states[s].Symbol);
            if (_states[ss].Successor != _states[s].Successor)
            {
                c = _states[ss].Successor;
                break;
            }
            _chain[chain++] = ss;
            ss = -1;
        }
        if (chain == 0) return c;
        int textPointer = _states[s].Successor;
        var up = new State { Symbol = TextByte(textPointer), Successor = textPointer - 1 };
        int count = GetStates(c, out int start);
        if (count > 1)
        {
            int f = FindState(c, up.Symbol);
            unchecked
            {
                ushort cf = (ushort)(_states[f].Freq - 1);
                ushort s0 = (ushort)(SummFreq(c) - count - cf);
                if ((ushort)(2 * cf) <= s0) up.Freq = (byte)((ushort)(5 * cf) > s0 ? 2 : 1);
                else up.Freq = (byte)(1 + (ushort)(2 * cf + 3 * s0 - 1) / (ushort)(2 * s0));
            }
        }
        else
        {
            up.Freq = _states[start].Freq;
        }
        for (int i = chain - 1; i >= 0; i--)
        {
            c = NewContext(up, c);
            if (c == 0) return 0;
            _states[_chain[i]].Successor = c;
        }
        return c;
    }

    private int Update(int minC, int maxC, int s)
    {
        if (_orderFall == 0 && _states[s].Successor > 0) return _states[s].Successor;
        if (_escCount == 0)
        {
            _escCount = 1;
            Array.Clear(_charMask);
        }
        int ss = -1;
        int suffix;
        if (_states[s].Freq < MaxFreq / 4 && (suffix = Suffix(minC)) > 0)
        {
            int count = GetStates(suffix, out int start);
            int i = 0;
            if (count > 1)
            {
                byte symbol = _states[s].Symbol;
                while (_states[start + i].Symbol != symbol) i++;
                if (i > 0 && _states[start + i].Freq >= _states[start + i - 1].Freq)
                {
                    (_states[start + i - 1], _states[start + i]) = (_states[start + i], _states[start + i - 1]);
                    i--;
                }
                if (_states[start + i].Freq < MaxFreq - 9)
                {
                    _states[start + i].Freq += 2;
                    SetSummFreq(suffix, unchecked((ushort)(SummFreq(suffix) + 2)));
                }
            }
            else if (_states[start].Freq < 32)
            {
                _states[start].Freq++;
            }
            ss = start + i;
        }
        if (_orderFall == 0)
        {
            minC = CreateSuccessors(minC, s, ss);
            _states[s].Successor = minC;
            return minC;
        }
        int successor = PushByte(_states[s].Symbol);
        if (successor == 0) return 0;
        int newC;
        if (_states[s].Successor == 0)
        {
            _states[s].Successor = successor;
            newC = minC;
        }
        else
        {
            if (_states[s].Successor > 0)
            {
                newC = _states[s].Successor;
            }
            else
            {
                newC = CreateSuccessors(minC, s, ss);
                if (newC == 0) return 0;
            }
            _orderFall--;
            if (_orderFall == 0)
            {
                successor = newC;
                if (maxC != minC) _heap1Lo--;
            }
        }
        int n = NumStates(minC);
        int s0 = SummFreq(minC) - n - unchecked((byte)(_states[s].Freq - 1));
        for (int c = maxC; c != minC; c = Suffix(c))
        {
            int start = ExpandStates(c, out int count);
            if (start < 0) return 0;
            unchecked
            {
                ushort summFreq;
                int ns = count - 1;
                if (ns != 1)
                {
                    summFreq = SummFreq(c);
                    if (4 * ns <= n && summFreq <= 8 * ns) summFreq += 2;
                    if (2 * ns < n) summFreq++;
                }
                else
                {
                    ref State p = ref _states[start];
                    if (p.Freq < MaxFreq / 4 - 1) p.Freq += p.Freq;
                    else p.Freq = MaxFreq - 4;
                    summFreq = (ushort)(p.Freq + _initEsc);
                    if (n > 3) summFreq++;
                }
                int cf = 2 * _states[s].Freq * (ushort)(summFreq + 6);
                int sf = s0 + summFreq;
                byte freq;
                if (cf >= 6 * sf)
                {
                    freq = (byte)(cf >= 15 * sf ? 7 : cf >= 12 * sf ? 6 : cf >= 9 * sf ? 5 : 4);
                    summFreq += freq;
                }
                else
                {
                    freq = (byte)(cf >= 4 * sf ? 3 : cf > sf ? 2 : 1);
                    summFreq += 3;
                }
                _states[start + count - 1] = new State { Symbol = _states[s].Symbol, Freq = freq, Successor = successor };
                SetSummFreq(c, summFreq);
            }
        }
        return newC;
    }

    private static byte[] BuildNs2BSIndex()
    {
        var table = new byte[256];
        table[1] = 2;
        table.AsSpan(2, 9).Fill(4);
        table.AsSpan(11).Fill(6);
        return table;
    }

    private static byte[] BuildNs2Index()
    {
        var table = new byte[256];
        int step = 0, value = 0;
        for (int i = 0; i < table.Length; i++)
        {
            table[i] = (byte)value;
            if (step <= 3)
            {
                value++;
                step = value;
            }
            else
            {
                step--;
            }
        }
        return table;
    }

    private static int[] BuildIndex2Units()
    {
        var table = new int[IndexCount];
        ReadOnlySpan<int> counts = [1, 4, 4, 4, 26];
        int index = 0, units = 0;
        for (int step = 0; step < counts.Length; step++)
        {
            for (int j = 0; j < counts[step]; j++)
            {
                units += step;
                table[index++] = units;
            }
        }
        return table;
    }

    private static byte[] BuildUnits2Index()
    {
        var table = new byte[129];
        int index = 0;
        for (int units = 0; units < table.Length; units++)
        {
            while (s_index2Units[index] < units) index++;
            table[units] = (byte)index;
        }
        return table;
    }
}
