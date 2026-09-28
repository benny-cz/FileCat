using FileCat.Core.Resources;

namespace FileCat.Recovery;

/// <summary>
/// A deleted item's content, read from its extents on the volume. Bytes whose space is in use by other data now, or
/// cannot be read, come back as zeros and are listed in <see cref="MissingRanges"/>, so a copy or a preview says they
/// are lost instead of passing them off as the file's data (plan §17.1).
/// </summary>
public sealed class RecoveryContent : IContentSource, IPartialContent
{
    private readonly IBlockSource _volume;
    private readonly RecoveryItem _item;
    private readonly List<(long Offset, long Length)> _missing = [];
    private readonly object _lock = new();

    public RecoveryContent(IBlockSource volume, RecoveryItem item)
    {
        _volume = volume;
        _item = item;
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

    public string DisplayName => _item.Name;
    public long Length => _item.Size;
    public bool CanSeek => true;
    public string? LocalPath => null;

    public IReadOnlyList<(long Offset, long Length)> MissingRanges
    {
        get
        {
            lock (_lock) return _missing.ToArray();
        }
    }

    public ContentRevision? GetRevision() => new(_item.Size, _item.ModifiedUtc?.Ticks ?? 0, _item.RecordNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public int Read(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset >= Length) return 0;
        int count = (int)Math.Min(buffer.Length, Length - offset);
        var target = buffer[..count];
        if (_item.Resident is { } resident)
        {
            int available = (int)Math.Max(0, Math.Min(count, resident.Length - offset));
            if (available > 0) resident.AsSpan((int)offset, available).CopyTo(target);
            target[available..].Clear();
            return count;
        }
        target.Clear();
        long position = 0;
        foreach (var extent in _item.Extents)
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
                    int n = _volume.Read(extent.Offset + (from - start) + done, slice[done..]);
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

    private void Missing(long offset, long length)
    {
        lock (_lock)
        {
            if (_missing.Any(m => m.Offset <= offset && m.Offset + m.Length >= offset + length)) return;
            _missing.Add((offset, length));
            _missing.Sort();
        }
    }

    /// <summary>The window onto the volume is owned by the session, not by one reader.</summary>
    public void Dispose() { }
}
