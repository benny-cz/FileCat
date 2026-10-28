using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Compressors.LZMA.RangeCoder;

namespace SharpCompress.Compressors.PPMd.H;

/// <summary>
/// FileCat: upstream's PPMd variant H model (Compressors/PPMd/H) was ported from UnRAR and is removed (release issue I14).
/// This placeholder keeps PpmdStream's shape until FileCat's own public-domain-derived PPMd7 model replaces it.
/// </summary>
internal sealed class ModelPpm : IDisposable
{
    private static NotSupportedException Unavailable() =>
        new("PPMd variant H decoding is not available in this build.");

    internal bool DecodeInit(Stream? stream, int maxOrder, int maxMemory) => throw Unavailable();

    internal ValueTask<bool> DecodeInitAsync(Stream? stream, int maxOrder, int maxMemory, CancellationToken cancellationToken = default) =>
        throw Unavailable();

    internal int DecodeChar() => throw Unavailable();

    internal int DecodeChar(Decoder decoder) => throw Unavailable();

    internal ValueTask<int> DecodeCharAsync(CancellationToken cancellationToken = default) => throw Unavailable();

    internal ValueTask<int> DecodeCharAsync(Decoder decoder, CancellationToken cancellationToken = default) => throw Unavailable();

    public void Dispose() { }
}
