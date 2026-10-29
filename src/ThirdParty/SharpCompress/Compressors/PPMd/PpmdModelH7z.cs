// FileCat addition, not SharpCompress code (release issue I14): 7z's PPMd (variant H with 7-Zip's range coder) decoded by
// FileCat's shared PPMd variant H model (PpmdModelH.cs, ported from rardecode, BSD-2-Clause) through 7-Zip's PPMd7z range
// decoder as SharpCompress's LZMA range decoder (Compressors/LZMA/RangeCoder, from the public-domain LZMA SDK) offers it.
// It replaces upstream's ModelPpm, which was ported from UnRAR. See THIRD-PARTY-CHANGES.md beside the project file.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Compressors.LZMA.RangeCoder;

namespace SharpCompress.Compressors.PPMd;

/// <summary>
/// 7-Zip's PPMd7z range decoder (Ppmd7z_RangeDec of the LZMA SDK: GetThreshold, Decode, DecodeBit) over SharpCompress's
/// LZMA <see cref="Decoder"/>. Arguments that would leave the range at zero are refused first: the decoder's
/// normalization would never end on one, and past the end of its input it reads 0xFF bytes forever.
/// </summary>
internal readonly struct Ppmd7zRangeDecoder(Decoder decoder) : IPpmdRangeDecoder
{
    public uint GetThreshold(uint total)
    {
        // Decoder.GetThreshold divides the range by the total and the code by that quotient: neither may be zero.
        if (total == 0 || decoder._range < total) throw PpmdModelH.Damaged("PPMd data");
        return decoder.GetThreshold(total);
    }

    public void Decode(uint start, uint size)
    {
        ulong range = (ulong)decoder._range * size;
        if (range == 0 || range > uint.MaxValue) throw PpmdModelH.Damaged("PPMd data");
        decoder.Decode(start, size);
    }

    public uint DecodeBit(uint size0, int totalBits)
    {
        // A first interval as large as the whole range would leave nothing for the second one.
        if (size0 >= 1u << totalBits) throw PpmdModelH.Damaged("PPMd data");
        return decoder.DecodeBit(size0, totalBits);
    }
}

/// <summary>
/// <see cref="PpmdStream"/>'s PPMd variant H decoding (<see cref="PpmdVersion.H7Z"/>, 7z's PPMd): FileCat's shared model
/// with 7-Zip's coder. It keeps the members of the ModelPpm it replaces, so PpmdStream keeps its shape. Variant H with
/// RAR's coder over a plain stream (<see cref="PpmdVersion.H"/>, which no <see cref="PpmdProperties"/> produces) is
/// refused: FileCat decodes RAR's PPMd in its own RAR reader, through the same model.
/// </summary>
internal sealed class PpmdModelH7z : IDisposable
{
    /// <summary>The most model memory FileCat gives 7z PPMd data (as much as it gives a RAR window); 7z allows nearly 4 GiB.</summary>
    public const int MaxMemoryBytes = 1 << 30;

    private readonly PpmdModelH<Ppmd7zRangeDecoder> _model = new();
    private bool _initialized;
    private bool _ended;   // the end mark was decoded: nothing follows it
    private bool _failed;  // damage was found: the model state is unknown, so nothing more is decoded

    private static NotSupportedException RarCoderUnavailable() =>
        new("PPMd variant H with RAR's range coder is not decoded through PpmdStream in this build.");

    internal bool DecodeInit(Stream? stream, int maxOrder, int maxMemory)
    {
        if (stream is not null) throw RarCoderUnavailable();
        if (maxOrder is < 2 or > PpmdModelH.MaxOrder || maxMemory < PpmdModelH.MinMemoryBytes)
            throw new InvalidDataException($"The PPMd parameters are invalid: model order {maxOrder}, {maxMemory} bytes of model memory.");
        if (maxMemory > MaxMemoryBytes)
            throw new NotSupportedException($"FileCat does not decode PPMd data whose model needs more than {MaxMemoryBytes >> 20} MiB of memory (this one needs {maxMemory} bytes).");
        _model.Init(maxOrder, maxMemory);
        _initialized = true;
        _ended = false;
        _failed = false;
        return true;
    }

    internal ValueTask<bool> DecodeInitAsync(Stream? stream, int maxOrder, int maxMemory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<bool>(DecodeInit(stream, maxOrder, maxMemory));
    }

    internal int DecodeChar() => throw RarCoderUnavailable();

    /// <summary>The next byte, or -1 once the data's end mark (an escape from the order-0 context) is decoded.</summary>
    internal int DecodeChar(Decoder decoder)
    {
        if (!_initialized) throw new InvalidOperationException("The PPMd model is not set up.");
        if (_failed) throw new InvalidDataException("The compressed PPMd data is damaged.");
        if (_ended) return -1;
        _model.Coder = new Ppmd7zRangeDecoder(decoder);
        int symbol;
        try
        {
            symbol = _model.DecodeSymbol();
        }
        catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException)
        {
            // The model's own damage; an index out of range is its backstop (see PpmdModelH), damage all the same.
            _failed = true;
            throw new InvalidDataException("The compressed PPMd data is damaged: " + (ex is InvalidDataException ? ex.Message : "PPMd model") + ".", ex);
        }
        if (symbol < 0) _ended = true;
        return symbol;
    }

    internal ValueTask<int> DecodeCharAsync(CancellationToken cancellationToken = default) => throw RarCoderUnavailable();

    /// <summary>
    /// As <see cref="DecodeChar(Decoder)"/>: the model decodes synchronously, so the range decoder reads its input with
    /// its synchronous Decode and DecodeBit here too.
    /// </summary>
    internal ValueTask<int> DecodeCharAsync(Decoder decoder, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<int>(DecodeChar(decoder));
    }

    public void Dispose() { }
}
