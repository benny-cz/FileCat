using FileCat.Core.Resources;

namespace FileCat.Recovery;

/// <summary>
/// A deleted item's content, read from its extents on the volume. Bytes whose space is in use by other data now, or
/// cannot be read, come back as zeros and are listed in <see cref="MissingRanges"/>, so a copy or a preview says they
/// are lost instead of passing them off as the file's data (plan §17.1).
/// </summary>
public sealed class RecoveryContent : IContentSource, IPartialContent
{
    private IBlockSource? _volume;
    private RecoveryItem? _item;
    private readonly ContentRevision _revision;
    private readonly List<(long Offset, long Length)> _missing = [];
    private readonly object _lock = new();

    public RecoveryContent(IBlockSource volume, RecoveryItem item)
    {
        _volume = volume;
        _item = item;
        DisplayName = item.Name;
        Length = item.Size;
        Caveat = item.State == RecoveryState.Uncertain ? RecoveryItem.UncertainStart : null;
        _revision = new(item.Size, item.ModifiedUtc?.Ticks ?? 0, item.RecordNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (item.Resident is null)
        {
            long position = 0;
            foreach (var extent in item.Extents)
            {
                if (extent.State is ExtentState.InUse or ExtentState.Unreadable) Missing(position, extent.Length);
                position += extent.Length;
            }
            if (position < item.Size) Missing(position, item.Size - position);
        }
    }

    public string DisplayName { get; }
    public long Length { get; }
    public bool CanSeek => true;
    public string? LocalPath => null;

    public string? Caveat { get; }

    public IReadOnlyList<(long Offset, long Length)> MissingRanges
    {
        get
        {
            lock (_lock) return _missing.ToArray();
        }
    }

    public ContentRevision? GetRevision() => _revision;

    public int Read(long offset, Span<byte> buffer)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_item is null, this);
            return ReadCore(_item, _volume!, offset, buffer);
        }
    }

    private int ReadCore(RecoveryItem item, IBlockSource volume, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset >= Length) return 0;
        int count = (int)Math.Min(buffer.Length, Length - offset);
        var target = buffer[..count];
        if (item.Compression is { } layout)
        {
            ReadCompressed(layout, volume, offset, target);
            return count;
        }
        if (item.Resident is { } resident)
        {
            int available = (int)Math.Max(0, Math.Min(count, resident.Length - offset));
            if (available > 0) resident.AsSpan((int)offset, available).CopyTo(target);
            target[available..].Clear();
            return count;
        }
        target.Clear();
        long position = 0;
        foreach (var extent in item.Extents)
        {
            long start = position, end = position + extent.Length;
            position = end;
            if (end <= offset) continue;
            if (start >= offset + count) break;
            long from = Math.Max(start, offset), to = Math.Min(end, offset + count);
            if (extent.State is not (ExtentState.Free or ExtentState.Owned)) continue; // lost or genuinely zero: stays zero
            var slice = target.Slice((int)(from - offset), (int)(to - from));
            try
            {
                int done = 0;
                while (done < slice.Length)
                {
                    int n = volume.Read(extent.Offset + (from - start) + done, slice[done..]);
                    if (n <= 0) break;
                    done += n;
                }
                if (done < slice.Length) Missing(from + done, slice.Length - done);
            }
            catch (IOException)
            {
                slice.Clear();
                Missing(from, slice.Length);
            }
        }
        return count;
    }

    private int _cachedUnit = -1;
    private byte[]? _unit;

    /// <summary>Compressed content: each unit is read whole, decompressed when it was compressed, and kept for the next read.</summary>
    private void ReadCompressed(CompressedLayout layout, IBlockSource volume, long offset, Span<byte> target)
    {
        int done = 0;
        while (done < target.Length)
        {
            long position = offset + done;
            int index = (int)(position / layout.UnitBytes);
            int within = (int)(position % layout.UnitBytes);
            int n = Math.Min(target.Length - done, layout.UnitBytes - within);
            var unit = Unit(layout, index, volume);
            if (unit is null) target.Slice(done, n).Clear();
            else unit.AsSpan(within, n).CopyTo(target[done..]);
            done += n;
        }
    }

    private byte[]? Unit(CompressedLayout layout, int index, IBlockSource volume)
    {
        lock (_lock)
        {
            if (index == _cachedUnit) return _unit;
        }
        if (index >= layout.Units.Count) return null;
        var unit = layout.Units[index];
        long start = (long)index * layout.UnitBytes;
        long length = Math.Min(layout.UnitBytes, Length - start);
        byte[]? bytes = null;
        if (!unit.Lost && unit.Kind != CompressedUnitKind.Sparse)
        {
            try
            {
                var stored = new byte[unit.Pieces.Sum(p => p.Length)];
                int at = 0;
                foreach (var (pieceOffset, pieceLength) in unit.Pieces)
                {
                    int done = 0;
                    while (done < pieceLength)
                    {
                        int n = volume.Read(pieceOffset + done, stored.AsSpan(at + done, (int)pieceLength - done));
                        if (n <= 0) throw new IOException("The volume ended inside a compression unit.");
                        done += n;
                    }
                    at += (int)pieceLength;
                }
                bytes = new byte[layout.UnitBytes];
                if (unit.Kind == CompressedUnitKind.Raw) stored.AsSpan(0, Math.Min(stored.Length, bytes.Length)).CopyTo(bytes);
                else Lznt1.Decompress(stored, bytes);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                bytes = null;
                Missing(start, length);
            }
        }
        else if (unit.Kind == CompressedUnitKind.Sparse && !unit.Lost)
        {
            bytes = new byte[layout.UnitBytes];
        }
        lock (_lock)
        {
            if (_item is not null)
            {
                _cachedUnit = index;
                _unit = bytes;
            }
        }
        return bytes;
    }

    private void Missing(long offset, long length)
    {
        lock (_lock)
        {
            if (_item is null) return; // A source callback may close this reader reentrantly.
            if (_missing.Any(m => m.Offset <= offset && m.Offset + m.Length >= offset + length)) return;
            _missing.Add((offset, length));
            _missing.Sort();
        }
    }

    /// <summary>Retire this reader's scan and decoded bytes; the session still owns the shared volume.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            // Read holds the same lock through decoding: a closing reader cannot publish another cached unit.
            _item = null;
            _volume = null;
            _unit = null;
            _cachedUnit = -1;
            _missing.Clear();
            _missing.TrimExcess();
        }
    }
}
