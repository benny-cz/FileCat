// FileCat addition: bounded byte-order conversion, checked against the public-domain LZMA SDK SwapBytes.c
// (Igor Pavlov). Complete two/four-byte units reverse; an incomplete EOF tail is preserved by Filter.
using System;
using System.IO;

namespace SharpCompress.Compressors.Filters;

internal sealed class SwapFilter : Filter
{
    private readonly int _width;
    internal SwapFilter(bool isEncoder, Stream stream, int width) : base(isEncoder, stream, width)
    {
        if (width is not (2 or 4)) throw new ArgumentOutOfRangeException(nameof(width));
        _width = width;
    }
    protected override int Transform(byte[] buffer, int offset, int count)
    {
        int consumed = count - count % _width;
        for (int at = offset; at < offset + consumed; at += _width) buffer.AsSpan(at, _width).Reverse();
        return consumed;
    }
}
