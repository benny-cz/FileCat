using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Core.Content;

/// <summary>
/// Sparse line index for "go to line" (plan §13.1). The text view never indexes a whole file; a far-away line costs
/// one bounded, cancellable scan from the nearest checkpoint, with progress, and the scan leaves a checkpoint every
/// <see cref="Stride"/> lines for later jumps. Memory is eight bytes per <see cref="Stride"/> lines.
/// </summary>
public sealed class LineIndex
{
    public const int Stride = 4096;
    private const int ChunkBytes = 1024 * 1024;
    private readonly IContentSource _source;
    private readonly int _unit;
    private readonly bool _bigEndian;
    private readonly long _start;
    private readonly object _lock = new();
    // Checkpoint k is the byte offset where line k * Stride + 1 starts (lines are numbered from 1).
    private readonly List<long> _checkpoints;

    public LineIndex(IContentSource source, Encoding encoding, long contentStart)
    {
        _source = source;
        Encoding = encoding;
        _unit = TextDecoding.UnitSize(encoding);
        _bigEndian = encoding.CodePage is 1201 or 12001;
        _start = contentStart;
        _checkpoints = [contentStart];
    }

    public Encoding Encoding { get; }

    /// <summary>Lines counted in total when a scan reached the end, otherwise null.</summary>
    public long? TotalLines { get; private set; }

    /// <summary>
    /// Byte offset where the 1-based <paramref name="line"/> starts, or null when the content has fewer lines.
    /// Reports the bytes scanned so far; cancellation stops at the next chunk.
    /// </summary>
    public long? FindLineStart(long line, IProgress<long>? scanned, CancellationToken ct)
    {
        if (line <= 1) return _start;
        long wantedBreaks = line - 1;
        long pos, breaks;
        lock (_lock)
        {
            int k = (int)Math.Min(wantedBreaks / Stride, _checkpoints.Count - 1);
            if (k * (long)Stride == wantedBreaks) return _checkpoints[k];
            pos = _checkpoints[k];
            breaks = k * (long)Stride;
        }
        var buffer = new byte[ChunkBytes];
        long scannedBytes = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int n = _source.Read(pos, buffer);
            n -= n % _unit; // a partial trailing unit waits for the next read
            if (n <= 0)
            {
                TotalLines = breaks + 1;
                return null;
            }
            var span = buffer.AsSpan(0, n);
            int i = 0;
            while (i < n)
            {
                int hit = NextBreak(span, i);
                if (hit < 0) break;
                breaks++;
                long lineStart = pos + hit + _unit;
                if (breaks % Stride == 0)
                {
                    lock (_lock)
                    {
                        if (breaks / Stride == _checkpoints.Count) _checkpoints.Add(lineStart);
                    }
                }
                if (breaks == wantedBreaks) return lineStart;
                i = hit + _unit;
            }
            pos += n;
            scannedBytes += n;
            scanned?.Report(scannedBytes);
        }
    }

    /// <summary>Position of the next line feed code unit at or after <paramref name="from"/> (unit aligned), or -1.</summary>
    private int NextBreak(ReadOnlySpan<byte> span, int from)
    {
        if (_unit == 1)
        {
            int at = span[from..].IndexOf((byte)'\n');
            return at < 0 ? -1 : from + at;
        }
        for (int i = from; i + _unit <= span.Length; i += _unit)
        {
            // Little endian: 0A 00 (00 00); big endian: (00 00) 00 0A.
            int low = _bigEndian ? i + _unit - 1 : i;
            if (span[low] != (byte)'\n') continue;
            bool rest = true;
            for (int b = 0; b < _unit && rest; b++) rest = i + b == low || span[i + b] == 0;
            if (rest) return i;
        }
        return -1;
    }
}
