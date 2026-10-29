namespace FileCat.Archives.Rar;

internal enum RarFilterType { Delta, E8, E8E9, Arm, Itanium, Rgb, Audio }

/// <summary>A transform of one block of decoded output (RAR 3 standard filters and RAR 5 filters).</summary>
internal sealed class RarFilter
{
    public RarFilterType Type { get; init; }
    public long Start { get; set; }   // window position of the block's first byte
    public int Length { get; init; }
    public int Channels { get; init; } // delta and audio
    public int Width { get; init; }    // RAR 3 RGB: bytes per pixel row
    public int PosR { get; init; }     // RAR 3 RGB: which byte of a pixel is red
    public bool Rar5 { get; init; }    // E8/E8E9 address rules differ between the versions
}

/// <summary>
/// The decoder's sliding window and the member output drawn from it. History grows with the output up to the
/// dictionary, but never past <see cref="RarLimits.MaxWindowBytes"/>; bytes not yet handed out are never
/// overwritten. Filters wait in start order and transform their block only once the whole block is decoded.
/// </summary>
internal sealed class RarWindow
{
    private byte[] _buffer;
    private int _mask;
    private long _written;             // bytes decoded since the window (or solid run) began
    private long _read;                // bytes handed out
    private readonly long _dictionary; // largest distance the archive may use
    private readonly long _maxCapacity;
    private readonly List<RarFilter> _filters = [];
    private byte[] _scratchA = [];
    private byte[] _scratchB = [];
    private byte[]? _filtered;
    private int _filteredPos;
    private int _filteredLength;
    private long _filterWork;         // every transform in this window's solid run, not just bytes handed out

    public RarWindow(long dictionary, long expectedBytes)
    {
        if (dictionary <= 0) throw RarData.Corrupt("dictionary size");
        _dictionary = dictionary;
        long wanted = Math.Max(RoundUpToPowerOfTwo(Math.Max(dictionary, RarLimits.MinWindowBytes)), 2L * RarLimits.MaxFilterBlockBytes);
        _maxCapacity = Math.Min(wanted, RarLimits.MaxWindowBytes);
        // A header's sizes are claims: start small and grow only with bytes actually decoded.
        long initial = Math.Clamp(RoundUpToPowerOfTwo(Math.Clamp(expectedBytes, RarLimits.MinWindowBytes, 8L << 20)), RarLimits.MinWindowBytes, _maxCapacity);
        _buffer = new byte[initial];
        _mask = _buffer.Length - 1;
    }

    /// <summary>Bytes decoded since the window began.</summary>
    public long Written => _written;

    /// <summary>Bytes handed out since the window began.</summary>
    public long Read => _read;

    /// <summary>Aggregate bytes transformed by filters in this decoder, including earlier solid members.</summary>
    public long FilterWorkBytes => _filterWork;

    /// <summary>True when the decoder may add one more symbol's output without touching unread bytes.</summary>
    public bool HasRoom => _written - _read + RarLimits.MaxSymbolOutput <= _maxCapacity;

    /// <summary>
    /// True while decoding should continue before the reader takes bytes: until about a mebibyte is waiting, or longer
    /// while the next filter's block is incomplete (its bytes cannot be handed out until it is whole).
    /// </summary>
    public bool ShouldDecode => HasRoom && (_written - _read < SoftTarget || FilterBlocksOutput);

    private const int SoftTarget = 1 << 20;

    private bool FilterBlocksOutput => _filters.Count > 0 && _filters[0].Start <= _read && _filters[0].Start + _filters[0].Length > _written;

    public void Put(byte value)
    {
        if (_written - _read >= _buffer.Length || _written >= _buffer.Length && _buffer.Length < _maxCapacity) EnsureRoom(1);
        _buffer[(int)(_written & _mask)] = value;
        _written++;
    }

    /// <summary>Repeats <paramref name="length"/> bytes from <paramref name="distance"/> bytes back.</summary>
    public void Copy(int length, long distance)
    {
        if (length <= 0 || length > RarLimits.MaxSymbolOutput) throw RarData.Corrupt("match length");
        if (distance <= 0 || distance > _written) throw RarData.Corrupt("match distance before the start of the data");
        if (distance > _dictionary) throw RarData.Corrupt("match distance beyond the archive's dictionary");
        EnsureRoom(length);
        if (distance > _buffer.Length)
            throw RarData.Unsupported($"it needs a decompression window over {RarLimits.MaxWindowBytes >> 20} MiB");
        int destination = (int)(_written & _mask);
        int source = (int)((_written - distance) & _mask);
        _written += length;
        if (distance >= length && source + length <= _buffer.Length && destination + length <= _buffer.Length)
        {
            _buffer.AsSpan(source, length).CopyTo(_buffer.AsSpan(destination, length));
            return;
        }
        for (int i = 0; i < length; i++)
        {
            _buffer[destination] = _buffer[source];
            destination = (destination + 1) & _mask;
            source = (source + 1) & _mask;
        }
    }

    /// <summary>Queues a filter for a block at or after the decoder's position.</summary>
    public void AddFilter(RarFilter filter)
    {
        if (_filters.Count >= RarLimits.MaxPendingFilters) throw RarData.Corrupt("too many filters");
        if (filter.Length < 0 || filter.Length > RarLimits.MaxFilterBlockBytes) throw RarData.Corrupt("filter block size");
        if (filter.Start < _written) throw RarData.Corrupt("filter block before the decoder's position");
        if (filter.Type is RarFilterType.Delta or RarFilterType.Audio && filter.Channels is < 1 or > RarLimits.MaxFilterChannels)
            throw RarData.Corrupt("filter channel count");
        if (filter.Type == RarFilterType.Rgb && (filter.Width < 3 || filter.Width > filter.Length || filter.PosR is < 0 or > 2))
            throw RarData.Corrupt("RGB filter geometry");
        if (_filters.Count > 0)
        {
            var last = _filters[^1];
            bool chained = filter.Start == last.Start && filter.Length == last.Length;
            if (!chained && filter.Start < last.Start + last.Length) throw RarData.Corrupt("overlapping filter blocks");
        }
        if (filter.Length == 0) return;
        _filters.Add(filter);
    }

    /// <summary>Pending filters that start at or past <paramref name="end"/> are dropped (a non-solid member ended).</summary>
    public void DropFiltersFrom(long end) => _filters.RemoveAll(f => f.Start >= end);

    /// <summary>True when a pending filter's block straddles <paramref name="end"/>: its bytes cannot all exist.</summary>
    public bool FilterStraddles(long end) => _filters.Exists(f => f.Start < end && f.Start + f.Length > end);

    /// <summary>
    /// Hands out decoded bytes up to window position <paramref name="end"/>, filtered where a filter applies.
    /// Returns 0 when the decoder must decode more first. <paramref name="memberStart"/> is the window position of
    /// the member's first byte, from which E8, ARM and Itanium filters count file offsets.
    /// </summary>
    public int ReadOutput(Span<byte> destination, long end, long memberStart, CancellationToken ct = default)
    {
        int total = 0;
        while (total < destination.Length)
        {
            if (_filtered is not null)
            {
                int n = Math.Min(destination.Length - total, _filteredLength - _filteredPos);
                _filtered.AsSpan(_filteredPos, n).CopyTo(destination[total..]);
                _filteredPos += n;
                total += n;
                if (_filteredPos == _filteredLength) _filtered = null;
                continue;
            }
            long limit = Math.Min(end, _written);
            if (_filters.Count > 0 && _filters[0].Start <= _read)
            {
                var first = _filters[0];
                if (first.Start + first.Length > _written) break; // the block is not all decoded yet
                if (first.Start + first.Length > end) throw RarData.Corrupt("filter block past the end of the member");
                ApplyFilters(memberStart, ct);
                continue;
            }
            if (_filters.Count > 0) limit = Math.Min(limit, _filters[0].Start);
            long available = limit - _read;
            if (available <= 0) break;
            int count = (int)Math.Min(available, destination.Length - total);
            int start = (int)(_read & _mask);
            int head = Math.Min(count, _buffer.Length - start);
            _buffer.AsSpan(start, head).CopyTo(destination[total..]);
            if (count > head) _buffer.AsSpan(0, count - head).CopyTo(destination[(total + head)..]);
            _read += count;
            total += count;
        }
        return total;
    }

    /// <summary>True while filtered output of an earlier block is still being handed out.</summary>
    public bool HasFilteredOutput => _filtered is not null;

    private void ApplyFilters(long memberStart, CancellationToken ct)
    {
        var filter = _filters[0];
        int length = filter.Length;
        // One tiny packed record may queue thousands of full-block transforms. Check the whole chain before doing
        // any of its work; the counter stays with the window across every member of a solid run.
        long work = 0;
        foreach (var pending in _filters)
        {
            ct.ThrowIfCancellationRequested();
            if (pending.Start != filter.Start) break;
            if (pending.Length != length) throw RarData.Corrupt("chained filters of different sizes");
            work = checked(work + pending.Length);
            if (work > RarLimits.MaxFilterWorkBytes - _filterWork)
                throw RarData.Unsupported("the solid run exceeds the 1 GiB filter-work limit");
        }
        _filterWork += work;
        EnsureScratch(ref _scratchA, length);
        CopyOut(filter.Start, length, _scratchA);
        byte[] current = _scratchA;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _filters.RemoveAt(0);
            long fileOffset = filter.Start - memberStart;
            current = ReferenceEquals(current, _scratchA)
                ? RarFilters.Apply(filter, current, length, fileOffset, ref _scratchB)
                : RarFilters.Apply(filter, current, length, fileOffset, ref _scratchA);
            if (_filters.Count == 0 || _filters[0].Start != filter.Start) break;
            filter = _filters[0];
            if (filter.Length != length) throw RarData.Corrupt("chained filters of different sizes");
        }
        _read = filter.Start + length;
        _filtered = current;
        _filteredPos = 0;
        _filteredLength = length;
        if (length == 0) _filtered = null;
    }

    private void CopyOut(long position, int length, byte[] target)
    {
        int start = (int)(position & _mask);
        int first = Math.Min(length, _buffer.Length - start);
        _buffer.AsSpan(start, first).CopyTo(target);
        if (length > first) _buffer.AsSpan(0, length - first).CopyTo(target.AsSpan(first));
    }

    private static void EnsureScratch(ref byte[] scratch, int length)
    {
        if (scratch.Length < length) scratch = new byte[Math.Max(length, Math.Min(scratch.Length * 2, RarLimits.MaxFilterBlockBytes))];
    }

    private void EnsureRoom(int count)
    {
        long unread = _written + count - _read;
        long wanted = Math.Max(unread, Math.Min(_written + count, _maxCapacity));
        if (wanted <= _buffer.Length) return;
        if (unread > _maxCapacity) throw RarData.Corrupt("decoded data the window cannot hold");
        long capacity = _buffer.Length;
        while (capacity < wanted) capacity *= 2;
        capacity = Math.Min(capacity, _maxCapacity);
        var grown = new byte[capacity];
        // Keep everything the old buffer still holds: the last min(written, old length) bytes.
        long from = Math.Max(0, _written - _buffer.Length);
        int newMask = grown.Length - 1;
        for (long position = from; position < _written;)
        {
            int oldIndex = (int)(position & _mask);
            int newIndex = (int)(position & newMask);
            int run = (int)Math.Min(_written - position, Math.Min(_buffer.Length - oldIndex, grown.Length - newIndex));
            _buffer.AsSpan(oldIndex, run).CopyTo(grown.AsSpan(newIndex, run));
            position += run;
        }
        _buffer = grown;
        _mask = newMask;
    }

    private static long RoundUpToPowerOfTwo(long value)
    {
        long result = 1;
        while (result < value && result < (1L << 62)) result <<= 1;
        return result;
    }
}
