using System.IO;

namespace SharpCompress.Compressors.Filters;

internal class DeltaFilter : Filter
{
    private const int DISTANCE_MAX = 256;
    private const int DISTANCE_MASK = DISTANCE_MAX - 1;

    private int _distance;
    private byte[] _history;
    private int _position;

    public DeltaFilter(bool isEncoder, Stream baseStream, byte[] info)
        : base(isEncoder, baseStream, 1)
    {
        if (info is null || info.Length != 1) throw new System.ArgumentException("Delta requires one property byte.", nameof(info));
        _distance = info[0];
        _history = new byte[DISTANCE_MAX];
        _position = 0;
    }

    protected override void Dispose(bool disposing)
    { try { base.Dispose(disposing); } finally { if (_history is not null) System.Array.Clear(_history); } }

    protected override int Transform(byte[] buffer, int offset, int count)
    {
        var end = offset + count;

        for (var i = offset; i < end; i++)
        {
            byte original = buffer[i];
            byte previous = _history[(_distance + _position--) & DISTANCE_MASK];
            buffer[i] = unchecked((byte)(_isEncoder ? original - previous : original + previous));
            _history[_position & DISTANCE_MASK] = _isEncoder ? original : buffer[i];
        }

        return count;
    }
}
