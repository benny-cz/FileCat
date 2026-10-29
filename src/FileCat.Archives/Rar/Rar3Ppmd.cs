// RAR 2.9/3.x PPMd blocks: RAR's range coder and the block start, ported from rardecode (github.com/nwaples/rardecode
// v2.4.5, ppm_model.go), Copyright (c) 2015 Nicholas Waples, BSD-2-Clause; see licenses/rardecode/LICENSE. The PPMd
// variant H model itself is shared with 7z's PPMd: SharpCompress.Compressors.PPMd.PpmdModelH in FileCat's build of
// SharpCompress (src/ThirdParty/SharpCompress/Compressors/PPMd/PpmdModelH.cs), ported from the same rardecode file.

using SharpCompress.Compressors.PPMd;

namespace FileCat.Archives.Rar;

internal sealed class Rar3Ppmd
{
    private readonly PpmdModelH<RarPpmdRangeDecoder> _model = new();
    private bool _ready; // a block with the reset flag has set up a model

    /// <summary>
    /// Starts a PPMd block: reads the range coder's first bytes and, with <paramref name="reset"/>, sets up a new model of
    /// order <paramref name="maxOrder"/> in <paramref name="megabytes"/> MiB. Without it the block continues the model.
    /// </summary>
    public void StartBlock(RarBitReader input, bool reset, int maxOrder, int megabytes)
    {
        _model.Coder.Start(input);
        if (!reset)
        {
            if (!_ready) throw RarData.Corrupt("a PPMd block continues a model that was never set up");
            return;
        }
        _ready = false;
        if (maxOrder < 2) throw RarData.Corrupt("PPMd model order");
        if (megabytes < 1 || (long)megabytes << 20 > RarLimits.MaxPpmdMemoryBytes) throw RarData.Corrupt("PPMd model size");
        _model.Init(maxOrder, megabytes << 20);
        _ready = true;
    }

    /// <summary>Drops the model (a member that does not continue a solid run); its memory is kept for reuse.</summary>
    public void Forget() => _ready = false;

    /// <summary>The next byte of the PPMd stream.</summary>
    public byte DecodeByte()
    {
        int symbol;
        try
        {
            symbol = _model.DecodeSymbol();
        }
        catch (IndexOutOfRangeException)
        {
            // A backstop only: every index the model reads back from its memory is checked before use (CheckContext,
            // GetStates, CheckBlock, TextByte), every derived table index is checked where it is formed, and the walks
            // are bounded. No path is meant to reach this; if one does, it is damaged data, not a crash.
            throw RarData.Corrupt("PPMd model");
        }
        catch (InvalidDataException ex) when (!RarData.IsDamage(ex))
        {
            // The model's own damage ("PPMd model" or "PPMd data"); the coder's reads report theirs as RAR data errors.
            throw RarData.Corrupt(ex.Message);
        }
        // An escape from the order-0 context is 7z's end mark; RAR's encoder never writes one.
        if (symbol < 0) throw RarData.Corrupt("PPMd data");
        return (byte)symbol;
    }
}

/// <summary>
/// RAR's range decoder for PPMd (Dmitry Subbotin's carry-less coder, as RAR 2.9/3.x codes PPMd), reading the bytes of
/// RAR's bit stream.
/// </summary>
internal struct RarPpmdRangeDecoder : IPpmdRangeDecoder
{
    private const uint RangeTop = 1 << 24;
    private const uint RangeBottom = 1 << 15;

    private RarBitReader _input;
    private uint _code;
    private uint _low;
    private uint _range;

    /// <summary>Starts decoding at the next byte of <paramref name="input"/> (a PPMd block's first coder bytes).</summary>
    public void Start(RarBitReader input)
    {
        _input = input;
        _low = 0;
        _range = uint.MaxValue;
        for (int i = 0; i < 4; i++) _code = _code << 8 | _input.ReadByte();
    }

    public uint GetThreshold(uint total)
    {
        if (total == 0) throw RarData.Corrupt("PPMd data");
        _range /= total;
        if (_range == 0) throw RarData.Corrupt("PPMd data");
        return unchecked(_code - _low) / _range;
    }

    public void Decode(uint start, uint size)
    {
        unchecked
        {
            _low += _range * start;
            _range *= size;
        }
        // Each pass consumes a byte: at the end of the data ReadByte throws, so this always ends.
        while (true)
        {
            if ((_low ^ unchecked(_low + _range)) >= RangeTop)
            {
                if (_range >= RangeBottom) return;
                _range = unchecked(0u - _low) & (RangeBottom - 1);
            }
            _code = _code << 8 | _input.ReadByte();
            _range <<= 8;
            _low <<= 8;
        }
    }

    public uint DecodeBit(uint size0, int totalBits)
    {
        // RAR codes PPMd's binary contexts as an ordinary interval of the scaled total.
        uint total = 1u << totalBits;
        if (GetThreshold(total) < size0)
        {
            Decode(0, size0);
            return 0;
        }
        Decode(size0, unchecked(total - size0));
        return 1;
    }
}
