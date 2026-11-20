namespace FileCat.Core.Analysis;

public static partial class AnalysisInventoryFormat
{
    internal enum FrameStatus { Pending, Record, End }
    internal readonly record struct FrameStep(FrameStatus Status, int BytesRead);

    /// <summary>
    /// Strict LF framing with one at-most-64-KiB source read per step. A record can span turns.
    /// The caller owns the stream and must consume Line before the next step reuses its buffer.
    /// </summary>
    internal sealed class RecordFramer
    {
        private readonly Stream _source;
        private readonly byte[] _input = new byte[64 * 1024], _line;
        private readonly long _limit;
        private int _position, _available, _length;
        private long _bytes;
        private bool _ready, _ended, _failed;
        internal long BytesRead => _bytes;
        internal RecordFramer(Stream source, long byteLimit = AnalysisLimits.InventoryBytes, int recordLimit = AnalysisLimits.RecordBytes)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!source.CanRead) throw new ArgumentException("A readable inventory stream is required.", nameof(source));
            if (byteLimit is < 1 or > AnalysisLimits.InventoryBytes) throw new ArgumentOutOfRangeException(nameof(byteLimit));
            if (recordLimit is < 1 or > AnalysisLimits.RecordBytes) throw new ArgumentOutOfRangeException(nameof(recordLimit));
            _source = source; _limit = byteLimit; _line = new byte[recordLimit];
        }
        internal ReadOnlyMemory<byte> Line => _ready ? _line.AsMemory(0, _length) : throw new InvalidOperationException("No framed record is ready.");
        internal FrameStep Step(CancellationToken cancellation = default)
        {
            if (_failed) throw new InvalidOperationException("The inventory framer failed and cannot resume.");
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (_ended) return new(FrameStatus.End, 0);
                if (_ready) { _length = 0; _ready = false; }
                int read = 0;
                if (_position == _available)
                {
                    int request = (int)Math.Min(_input.Length, _limit - _bytes + 1);
                    read = _source.Read(_input.AsSpan(0, request));
                    cancellation.ThrowIfCancellationRequested();
                    if (read < 0 || read > request) throw Invalid("Invalid inventory source read count.");
                    _bytes += read;
                    if (_bytes > _limit) throw Invalid("Inventory exceeds its byte limit.");
                    _available = read; _position = 0;
                    if (read == 0)
                    {
                        if (_length != 0) throw Invalid("Inventory record lacks its LF delimiter.");
                        _ended = true; return new(FrameStatus.End, 0);
                    }
                }
                var remaining = _input.AsSpan(_position, _available - _position);
                int lf = remaining.IndexOf((byte)'\n'), count = lf < 0 ? remaining.Length : lf;
                if (count > _line.Length - _length) throw Invalid("Inventory record exceeds its record limit.");
                remaining[..count].CopyTo(_line.AsSpan(_length)); _length += count;
                _position += count + (lf < 0 ? 0 : 1);
                if (lf < 0) return new(FrameStatus.Pending, read);
                _ready = true; return new(FrameStatus.Record, read);
            }
            catch { _failed = true; _ready = false; throw; }
        }
        internal ReadOnlyMemory<byte>? Next(CancellationToken cancellation)
        {
            while (true)
            {
                var step = Step(cancellation);
                if (step.Status == FrameStatus.Record) return Line;
                if (step.Status == FrameStatus.End) return null;
            }
        }
    }
}
