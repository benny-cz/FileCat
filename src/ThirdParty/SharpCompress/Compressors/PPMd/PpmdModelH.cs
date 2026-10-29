// FileCat addition, not SharpCompress code: PPMd variant H (Dmitry Shkarin's PPMd var.H, the model of RAR 2.9/3.x and
// 7z PPMd), ported from rardecode (github.com/nwaples/rardecode v2.4.5, ppm_model.go), Copyright (c) 2015 Nicholas
// Waples, BSD-2-Clause; see licenses/rardecode/LICENSE and licenses/sharpcompress/COMPONENTS.md in FileCat's repository.
// It contains no UnRAR source. FileCat moved the model here from its RAR reader (src/FileCat.Archives/Rar/Rar3Ppmd.cs)
// so that RAR 3 and 7z decode PPMd through one model: it codes through IPpmdRangeDecoder, RAR's range coder in
// FileCat.Archives or 7-Zip's PPMd7z coder (Ppmd7zRangeDecoder). The model memory is rardecode's array of six-byte
// states. FileCat checks every index the model reads back from that memory (context, state-block, free-list and text
// pointers), bounds every walk along its links, refuses zero divisors and stops gluing free blocks at the unallocated
// gap, as PPMd itself does: damaged data ends in a data error, never in a crash or a hang.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SharpCompress.Compressors.PPMd;

/// <summary>
/// The range decoder a <see cref="PpmdModelH"/> codes through: RAR's (FileCat.Archives) or 7-Zip's PPMd7z coder
/// (<see cref="Ppmd7zRangeDecoder"/>). The model passes intervals of a scaled total, as 7-Zip's IPpmd7_RangeDec does.
/// </summary>
internal interface IPpmdRangeDecoder
{
    /// <summary>Scales the range to <paramref name="total"/> and returns the count the code falls on (below the total in valid data).</summary>
    uint GetThreshold(uint total);

    /// <summary>Consumes the interval of <paramref name="size"/> counts from <paramref name="start"/> of the last scaled total.</summary>
    void Decode(uint start, uint size);

    /// <summary>A binary decision: 0 with probability <paramref name="size0"/> / 2^<paramref name="totalBits"/>, else 1.</summary>
    uint DecodeBit(uint size0, int totalBits);
}

/// <summary>The limits of <see cref="PpmdModelH{TDecoder}"/> and the damage it reports.</summary>
internal static class PpmdModelH
{
    /// <summary>Highest model order (RAR and 7z both stop at 64).</summary>
    public const int MaxOrder = 64;

    /// <summary>Smallest model memory: 7-Zip's PPMD7_MIN_MEM_SIZE, room for the order-0 context and its 256 states.</summary>
    public const int MinMemoryBytes = 1 << 11;

    /// <summary>Damaged data, or a model state no valid data leads to: "PPMd model" or "PPMd data".</summary>
    internal static InvalidDataException Damaged(string what) => new(what);
}

/// <summary>
/// The PPMd variant H model with its sub-allocator, coding through <see cref="Coder"/> (a struct, so that its calls are
/// direct). <see cref="Init"/> sets up a model; <see cref="DecodeSymbol"/> decodes one byte. Damage ends in an
/// <see cref="InvalidDataException"/> whose message names what was damaged ("PPMd model" or "PPMd data"), which the
/// caller words for its format. Every index the model reads back from its memory is checked before use, so an
/// <see cref="IndexOutOfRangeException"/> from it is a backstop only: callers report it as a damaged model too.
/// </summary>
internal sealed class PpmdModelH<TDecoder>
    where TDecoder : struct, IPpmdRangeDecoder
{
    private const int MaxOrder = PpmdModelH.MaxOrder;
    private const int MaxFreq = 124;
    private const int IntBits = 7;
    private const int PeriodBits = 7;
    private const int BinScaleBits = IntBits + PeriodBits;
    private const int BinScale = 1 << BinScaleBits;
    private const int IndexCount = 39;  // free-list sizes (rardecode's n0..n4)
    private const int UnitSize = 12;    // bytes in a unit: one context or two states
    private const int FreeMark = -1;    // stamp of a free block while free blocks are glued
    private const int MaxWalk = 256;    // context-chain steps per symbol; a valid model needs at most its order (64)

    /// <summary>
    /// One cell of the model memory: a symbol with its frequency and successor, half a context, or six bytes of text.
    /// A successor above zero is a context (an index of this array), below zero minus a text byte's position, zero none.
    /// </summary>
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
    private static readonly int[] s_index2Units = BuildIndex2Units(); // units of each free-list block size
    private static readonly byte[] s_units2Index = BuildUnits2Index(); // smallest free list holding that many units

    // Sub-allocator: heap 1 holds text from its bottom and units from its top, heap 2 holds units (contexts from its
    // top). Positions in heap 1 are bytes; every other position is a state index.
    private State[] _states = [];
    private int _stateCount;            // states the current model uses (rardecode's len(states))
    private int _glueCount;
    private int _heap1MaxBytes;
    private int _heap1Lo;
    private int _heap1Hi;
    private int _heap2Lo;
    private int _heap2Hi;
    private readonly int[] _freeList = new int[IndexCount];

    // Model.
    private int _maxOrder;
    private int _orderFall;
    private int _initRL;
    private int _runLength;
    private byte _prevSuccess;
    private byte _escCount;
    private byte _prevSym;
    private byte _initEsc;
    private int _context;               // the current context; zero restarts the model
    private readonly byte[] _charMask = new byte[256];
    private readonly ushort[] _binSumm = new ushort[128 * 64];
    private readonly See2Context[] _see2 = new See2Context[25 * 16];
    private readonly int[] _unmasked = new int[256]; // rardecode's ibuf
    private readonly int[] _chain = new int[256];    // rardecode's sbuf

    /// <summary>The range decoder the model codes through; its owner starts it where each run of coded data starts.</summary>
    public TDecoder Coder;

    private static InvalidDataException Damaged(string what) => PpmdModelH.Damaged(what);

    /// <summary>
    /// Sets up a new model of order <paramref name="maxOrder"/> (2–64) in <paramref name="memoryBytes"/> bytes of model
    /// memory (at least <see cref="PpmdModelH.MinMemoryBytes"/>; the caller bounds the most it allows). The first symbol
    /// starts it.
    /// </summary>
    public void Init(int maxOrder, int memoryBytes)
    {
        if (maxOrder is < 2 or > MaxOrder) throw new ArgumentOutOfRangeException(nameof(maxOrder));
        if (memoryBytes < PpmdModelH.MinMemoryBytes) throw new ArgumentOutOfRangeException(nameof(memoryBytes));
        InitAllocator(memoryBytes);
        _maxOrder = maxOrder;
        _prevSym = 0;
        _context = 0;
    }

    // ---- Sub-allocator ----

    private void InitAllocator(int bytes)
    {
        int heap2Units = bytes / 8 / UnitSize * 7;
        _heap1MaxBytes = bytes - heap2Units * UnitSize;
        // One unit more for a size that is no multiple of the unit, and the first unit is reserved: index 0 is "none".
        int heap1Units = _heap1MaxBytes / UnitSize + 1;
        int count = (1 + heap1Units + heap2Units) * 2;
        if (_states.Length < count)
        {
            _states = [];
            _states = new State[count];
        }
        // A reused array keeps old contents; nothing reads them before writing (GlueFreeBlocks stops at the gap).
        _stateCount = count;
    }

    private void RestartAllocator()
    {
        // Heap 1 starts one unit in, padded so that it ends exactly where heap 2 begins.
        _heap1Lo = UnitSize + (UnitSize - _heap1MaxBytes % UnitSize);
        _heap1Hi = UnitSize + (_heap1MaxBytes / UnitSize + 1) * UnitSize;
        _heap2Lo = _heap1Hi / UnitSize * 2;
        _heap2Hi = _stateCount;
        _glueCount = 0;
        Array.Clear(_freeList);
    }

    /// <summary>Stores a text byte at the bottom of heap 1; returns a successor for the byte after it, or 0 when heap 1 is full.</summary>
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

    /// <summary>The text byte a negative successor points to.</summary>
    private byte TextByte(int pointer)
    {
        int position = unchecked(-pointer);
        if (pointer >= 0 || position <= 0 || position / 6 >= _stateCount) throw Damaged("PPMd model");
        var state = _states[position / 6];
        return (position % 6) switch
        {
            0 => state.Symbol,
            1 => state.Freq,
            _ => (byte)((uint)state.Successor >> ((position % 6 - 2) * 8)),
        };
    }

    /// <summary>A block of <paramref name="units"/> units at state <paramref name="index"/> must lie inside the model memory.</summary>
    private void CheckBlock(int index, int units)
    {
        if (index < 1 || index > _stateCount - Math.Max(units << 1, 2)) throw Damaged("PPMd model");
    }

    private int RemoveFreeBlock(int index)
    {
        int n = _freeList[index];
        if (n == 0) return 0;
        CheckBlock(n, s_index2Units[index]);
        _freeList[index] = _states[n].Successor;
        _states[n] = default;
        return n;
    }

    private void AddFreeBlock(int n, int index)
    {
        CheckBlock(n, s_index2Units[index]);
        _states[n].Successor = _freeList[index];
        _freeList[index] = n;
    }

    /// <summary>Frees <paramref name="units"/> units at <paramref name="n"/>, split into the sizes the free lists hold.</summary>
    private void FreeUnits(int n, int units)
    {
        if ((uint)units > 128) throw Damaged("PPMd model");
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

    /// <summary>Merges adjacent free blocks and refills the free lists with them (PPMd's GlueFreeBlocks).</summary>
    private void GlueFreeBlocks()
    {
        // Free blocks never exceed the memory's units, so a list walk that takes longer has looped: damaged data.
        int budget = _stateCount;

        // Collect every free block into one list linked through each block's second state, stamped with its size.
        int head = 0;
        for (int i = 0; i < IndexCount; i++)
        {
            var stamp = new State { Successor = FreeMark };
            SetBlockUnits(ref stamp, s_index2Units[i]);
            for (int n = _freeList[i]; n != 0;)
            {
                if (--budget < 0) throw Damaged("PPMd model");
                CheckBlock(n, s_index2Units[i]);
                _states[n + 1].Successor = head;
                head = n;
                int next = _states[n].Successor;
                _states[n] = stamp;
                n = next;
            }
            _freeList[i] = 0;
        }

        // The unallocated gap of heap 2 is no free block, whatever its old contents say (PPMd marks its start too).
        if (_heap2Lo != _heap2Hi) _states[_heap2Lo].Successor = 0;

        // Merge each block with the free blocks that directly follow it.
        budget = _stateCount;
        for (int n = head; n != 0; n = _states[n + 1].Successor)
        {
            if (--budget < 0) throw Damaged("PPMd model");
            CheckBlock(n, 1);
            if (_states[n].Successor != FreeMark) continue;
            int units = BlockUnits(_states[n]);
            for (int next = n + (units << 1); next < _stateCount && _states[next].Successor == FreeMark; next = n + (units << 1))
            {
                if (--budget < 0) throw Damaged("PPMd model");
                units += BlockUnits(_states[next]);
                if (units > ushort.MaxValue) break;
                _states[next].Successor = 0;
                SetBlockUnits(ref _states[n], units);
            }
        }

        // Return the merged blocks to the free lists.
        budget = _stateCount;
        for (int n = head; n != 0; n = _states[n + 1].Successor)
        {
            if (--budget < 0) throw Damaged("PPMd model");
            CheckBlock(n, 1);
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
        // Split a larger free block.
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
        // Take units from the top of heap 1, above the text.
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

    /// <summary>A new context holding <paramref name="state"/> as its only state; 0 when the memory is full.</summary>
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
        // The number of states is stored minus one: the zeroed symbol field means one state.
        _states[n] = new State { Successor = suffix };
        _states[n + 1] = state;
        return n;
    }

    // ---- Contexts: two states. The first holds the number of states minus one and the suffix; the second holds the
    // only state, or the frequency sum and the index of the state block. ----

    /// <summary>A context index read back from the model memory must name two states inside it.</summary>
    private int CheckContext(int context)
    {
        if ((uint)(context - 1) >= (uint)(_stateCount - 2)) throw Damaged("PPMd model");
        return context;
    }

    private int NumStates(int context) => _states[context].Symbol + 1;

    private ushort SummFreq(int context) => (ushort)(_states[context + 1].Symbol | _states[context + 1].Freq << 8);

    private void SetSummFreq(int context, int value)
    {
        _states[context + 1].Symbol = (byte)value;
        _states[context + 1].Freq = (byte)(value >> 8);
    }

    private int Suffix(int context)
    {
        int suffix = _states[context].Successor;
        return suffix <= 0 ? 0 : CheckContext(suffix);
    }

    /// <summary>The states of <paramref name="context"/>: their count, and the index of the first in <paramref name="start"/>.</summary>
    private int GetStates(int context, out int start)
    {
        int count = _states[context].Symbol + 1;
        if (count == 1)
        {
            start = context + 1;
            return 1;
        }
        start = _states[context + 1].Successor;
        if (start < 1 || start > _stateCount - count) throw Damaged("PPMd model");
        return count;
    }

    /// <summary>The state of <paramref name="context"/> with <paramref name="symbol"/>, or its last state when none has it.</summary>
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
        if (context == 0) throw Damaged("PPMd model");
        _states[context].Symbol = (byte)(count - 1);
        int n = AllocUnits(s_units2Index[(count + 1) >> 1]);
        if (n == 0) throw Damaged("PPMd model");
        _states[context + 1].Successor = n;
        return context;
    }

    /// <summary>Shrinks the state block of <paramref name="context"/> to <paramref name="size"/> states; returns its new start.</summary>
    private int ShrinkStates(int context, int start, int count, int size)
    {
        int i1 = s_units2Index[(count + 1) >> 1];
        int i2 = s_units2Index[(size + 1) >> 1];
        if (size == 1)
        {
            // The only state moves into the context; its block is freed.
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

    /// <summary>Adds room for one state to <paramref name="context"/>; returns the block's start, or -1 when the memory is full.</summary>
    private int ExpandStates(int context, out int newCount)
    {
        newCount = 0;
        int count = GetStates(context, out int start);
        if (count >= 256) throw Damaged("PPMd model");
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

    // ---- Model ----

    private void Restart()
    {
        if (_maxOrder == 0) throw new InvalidOperationException("The PPMd model is not set up.");
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

    /// <summary>
    /// The next byte, or -1 for an escape from the order-0 context: 7z's end mark, which RAR's encoder never writes.
    /// </summary>
    public int DecodeSymbol()
    {
        if (_context == 0) Restart();
        int minC = CheckContext(_context);
        int maxC = minC;
        int found = NumStates(minC) == 1 ? DecodeBinSymbol(minC) : DecodeSymbol1(minC);
        int walk = 0;
        while (found < 0)
        {
            // Escape to the nearest shorter context with more symbols; the masked ones are excluded there.
            int masked = NumStates(minC);
            do
            {
                if (++walk > MaxWalk) throw Damaged("PPMd model");
                _orderFall++;
                minC = Suffix(minC);
                // An escape from the order-0 context: the end mark. The model is left as it is; nothing follows it.
                if (minC == 0) return -1;
            }
            while (NumStates(minC) == masked);
            found = DecodeSymbol2(minC, masked);
        }
        byte symbol = _states[found].Symbol;
        _context = Update(minC, maxC, found);
        _prevSym = symbol;
        return symbol;
    }

    /// <summary>Rescales the frequencies of <paramref name="context"/> once state <paramref name="s"/> is over the limit; returns the found state's index.</summary>
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
                // Keep the states sorted by frequency, most frequent first.
                int j = i - 1;
                while (j > 0 && f > _states[start + j - 1].Freq) j--;
                var moved = _states[start + i];
                Array.Copy(_states, start + j, _states, start + j + 1, i - j);
                _states[start + j] = moved;
            }
            int last = count - 1;
            while (_states[start + last].Freq == 0)
            {
                if (last == 0) throw Damaged("PPMd model");
                last--;
                escFreq++;
            }
            if (last != count - 1) start = ShrinkStates(context, start, count, last + 1);
            if (last == 0)
            {
                // One state left: the context is binary and keeps no frequency sum.
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

    /// <summary>Decodes in a context with one state; returns that state, or -1 for an escape.</summary>
    private int DecodeBinSymbol(int context)
    {
        int s = context + 1;
        int i = _prevSuccess + s_ns2BSIndex[NumStates(Suffix(context)) - 1] + ((_runLength >> 26) & 0x20);
        if (_prevSym >= 64) i += 8;
        if (_states[s].Symbol >= 64) i += 2 * 8;
        int freq = _states[s].Freq;
        if (freq is < 1 or > 128) throw Damaged("PPMd model");
        int index = (freq - 1) * 64 + i;
        ushort bs = _binSumm[index];
        int mean = (ushort)(bs + (1 << (PeriodBits - 2))) >> PeriodBits;
        if (Coder.DecodeBit(bs, BinScaleBits) == 0)
        {
            if (_states[s].Freq < 128) _states[s].Freq++;
            _binSumm[index] = unchecked((ushort)(bs + (1 << IntBits) - mean));
            _prevSuccess = 1;
            _runLength = unchecked(_runLength + 1);
            return s;
        }
        bs = unchecked((ushort)(bs - mean));
        _binSumm[index] = bs;
        if (bs >> 10 >= s_expEscape.Length) throw Damaged("PPMd model");
        _initEsc = s_expEscape[bs >> 10];
        _charMask[_states[s].Symbol] = _escCount;
        _prevSuccess = 0;
        return -1;
    }

    /// <summary>Decodes in a context with several states, none masked; returns the found state, or -1 for an escape.</summary>
    private int DecodeSymbol1(int context)
    {
        int count = GetStates(context, out int start);
        uint scale = SummFreq(context);
        if (scale == 0) throw Damaged("PPMd data");
        uint target = Coder.GetThreshold(scale);
        if (target >= scale) throw Damaged("PPMd data"); // the code lies beyond every symbol and the escape
        _prevSuccess = 0;
        uint n = 0;
        for (int i = 0; i < count; i++)
        {
            int s = start + i;
            n += _states[s].Freq;
            if (n <= target) continue;
            Coder.Decode(n - _states[s].Freq, _states[s].Freq);
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
        Coder.Decode(n, unchecked(scale - n));
        return -1;
    }

    /// <summary>The SEE context for an escape from <paramref name="context"/>, or -1 for the order-0 context of all 256 symbols.</summary>
    private int MakeEscFreq(int context, int numMasked)
    {
        int count = NumStates(context);
        if (count == 256) return -1;
        int diff = count - numMasked;
        if (diff < 1) throw Damaged("PPMd model");
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

    /// <summary>Decodes after an escape, skipping the <paramref name="numMasked"/> masked symbols; returns the found state, or -1.</summary>
    private int DecodeSymbol2(int context, int numMasked)
    {
        int see = MakeEscFreq(context, numMasked);
        uint scale = SeeMean(see);
        int count = GetStates(context, out int start);
        int unmasked = count - numMasked;
        if (unmasked < 1) throw Damaged("PPMd model");
        uint hi = 0;
        for (int i = 0, j = 0; j < unmasked; j++, i++)
        {
            while (true)
            {
                if (i >= count) throw Damaged("PPMd model");
                if (_charMask[_states[start + i].Symbol] != _escCount) break;
                i++;
            }
            hi += _states[start + i].Freq;
            _unmasked[j] = start + i;
        }
        scale += hi;
        uint target = Coder.GetThreshold(scale);
        if (target >= scale) throw Damaged("PPMd data");
        if (target >= hi)
        {
            Coder.Decode(hi, scale - hi);
            if (see >= 0) _see2[see].Summ = unchecked((ushort)(_see2[see].Summ + scale));
            for (int j = 0; j < unmasked; j++) _charMask[_states[_unmasked[j]].Symbol] = _escCount;
            return -1;
        }
        int k = 0;
        hi = _states[_unmasked[0]].Freq;
        while (hi <= target)
        {
            if (++k >= unmasked) throw Damaged("PPMd model");
            hi += _states[_unmasked[k]].Freq;
        }
        int s = _unmasked[k];
        Coder.Decode(hi - _states[s].Freq, _states[s].Freq);
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

    /// <summary>
    /// Creates the contexts that follow state <paramref name="s"/> of <paramref name="c"/> along the suffix chain;
    /// <paramref name="ss"/> is the symbol's state in the suffix of <paramref name="c"/>, if already found (else -1).
    /// Returns the longest new context, or 0 when the memory is full.
    /// </summary>
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
                // A different successor is that context's own (a valid model never points it into text here).
                c = CheckContext(_states[ss].Successor);
                break;
            }
            if (chain == _chain.Length) throw Damaged("PPMd model");
            _chain[chain++] = ss;
            ss = -1;
        }
        if (chain == 0) return c;

        int textPointer = _states[s].Successor;
        var up = new State { Symbol = TextByte(textPointer), Successor = unchecked(textPointer - 1) };
        int count = GetStates(c, out int start);
        if (count > 1)
        {
            int f = FindState(c, up.Symbol);
            unchecked
            {
                ushort cf = (ushort)(_states[f].Freq - 1);
                ushort s0 = (ushort)(SummFreq(c) - count - cf);
                if ((ushort)(2 * cf) <= s0)
                {
                    up.Freq = (byte)((ushort)(5 * cf) > s0 ? 2 : 1);
                }
                else
                {
                    ushort divisor = (ushort)(2 * s0);
                    if (divisor == 0) throw Damaged("PPMd model");
                    up.Freq = (byte)(1 + (ushort)(2 * cf + 3 * s0 - 1) / divisor);
                }
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

    /// <summary>Updates the model after state <paramref name="s"/> of <paramref name="minC"/> was decoded; returns the next context (0 restarts).</summary>
    private int Update(int minC, int maxC, int s)
    {
        if (_orderFall == 0 && _states[s].Successor > 0) return CheckContext(_states[s].Successor);
        if (_escCount == 0)
        {
            _escCount = 1;
            Array.Clear(_charMask);
        }

        int ss = -1; // the symbol's state in the suffix of minC
        int suffix;
        if (_states[s].Freq < MaxFreq / 4 && (suffix = Suffix(minC)) > 0)
        {
            int count = GetStates(suffix, out int start);
            int i = 0;
            if (count > 1)
            {
                byte symbol = _states[s].Symbol;
                while (_states[start + i].Symbol != symbol)
                {
                    if (++i >= count) throw Damaged("PPMd model");
                }
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
                newC = CheckContext(_states[s].Successor);
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
                if (maxC != minC) _heap1Lo--; // take back the text byte
            }
        }

        // Add the symbol to every context between the one it was coded in and the longest one.
        int n = NumStates(minC);
        int s0 = SummFreq(minC) - n - unchecked((byte)(_states[s].Freq - 1));
        int walk = 0;
        for (int c = maxC; c != minC; c = Suffix(c))
        {
            if (++walk > MaxWalk) throw Damaged("PPMd model");
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

    // ---- Tables ----

    private static byte[] BuildNs2BSIndex()
    {
        var table = new byte[256];
        table[0] = 2 * 0;
        table[1] = 2 * 1;
        table.AsSpan(2, 9).Fill(2 * 2);
        table.AsSpan(11).Fill(2 * 3);
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
        ReadOnlySpan<int> counts = [1, 4, 4, 4, 26]; // rardecode's n0..n4: sizes growing by 0, 1, 2, 3, 4 units
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
