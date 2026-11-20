using System.Formats.Tar;

namespace FileCat.Archives;

/// <summary>
/// Stands between a TAR stream and .NET's <see cref="TarReader"/> (trust boundary B02). The reader takes a PAX extended
/// header or a GNU long name ('x', 'g', 'L', 'K') whole, into an array as long as its header says, up to 2 GiB, before it
/// finds out whether that much data follows: one damaged size field in a 31 KiB archive made it take 512 MiB. Each such
/// header is checked here as it passes, before the reader acts on it: its data may not exceed <see cref="MaxMetadata"/>,
/// nor run past the end of a plain archive. Headers are where the TAR framing puts them: each one's data follows it, and
/// after each member the reader returns it is told so (<see cref="Returned"/>).
/// </summary>
internal sealed class TarHeaderGuard(Stream inner, int maximumMetadata = TarHeaderGuard.MaxMetadata) : Stream
{
    /// <summary>The most metadata one member may carry (its PAX attributes, its GNU long name): far beyond real archives.</summary>
    public const int MaxMetadata = 16 << 20;

    private const int Block = 512;
    private readonly byte[] _header = new byte[Block];
    private long _read;
    // Where the next header starts (-1: within a member, until the reader returns it) and how much of it has passed.
    private long _next;
    private int _filled;
    // Once a header is refused, the reader is left mid-header: nothing more is read through it.
    private string? _refusal;

    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.CanSeek ? inner.Position : _read; set => inner.Position = value; }

    /// <summary>The reader returned <paramref name="entry"/>: the next header follows its data.</summary>
    public void Returned(TarEntry entry)
    {
        // A seekable stream is left at the next header; otherwise at the member's data, which the reader skips later. A
        // global PAX header's data was taken whole (it has no data stream).
        _next = inner.CanSeek ? inner.Position : _read + (entry.DataStream is null ? 0 : Blocks(entry.Length));
        _filled = 0;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_refusal is not null) throw new InvalidDataException(_refusal);
        long at = Position;
        int n = inner.Read(buffer);
        _read = at + n;
        Inspect(at, buffer[..n]);
        return n;
    }

    private void Inspect(long at, ReadOnlySpan<byte> read)
    {
        while (_next >= 0)
        {
            long want = _next + _filled;
            if (want < at)
            {
                // The header was passed by without being read: nothing to check until the next member.
                _next = -1;
                return;
            }
            if (want - at >= read.Length) return;
            int offset = (int)(want - at), take = Math.Min(Block - _filled, read.Length - offset);
            read.Slice(offset, take).CopyTo(_header.AsSpan(_filled));
            _filled += take;
            if (_filled < Block) return;
            Check();
        }
    }

    private void Check()
    {
        long start = _next;
        _next = -1;
        _filled = 0;
        if (_header[156] is not ((byte)'x' or (byte)'g' or (byte)'L' or (byte)'K')) return;
        if (Size(_header.AsSpan(124, 12)) is not long size) return;
        if (size > maximumMetadata)
            _refusal = $"A member's header gives it {size:N0} bytes of metadata, more than FileCat reads ({maximumMetadata >> 20} MiB): the archive is damaged.";
        else if (inner.CanSeek && size > inner.Length - start - Block)
            _refusal = "A member's metadata runs past the end of the archive: it is damaged or cut short.";
        if (_refusal is not null) throw new InvalidDataException(_refusal);
        _next = start + Block + Blocks(size);
    }

    private static long Blocks(long size) => (size + Block - 1) / Block * Block;

    /// <summary>A header's size field as <see cref="TarReader"/> reads it; null when it refuses the field itself.</summary>
    private static long? Size(ReadOnlySpan<byte> field)
    {
        if (field[0] == 0xFF) return null; // base-256, negative
        if (field[0] == 0x80)
        {
            ulong big = 0;
            foreach (byte b in field[1..])
            {
                if (big > ulong.MaxValue >> 8) return long.MaxValue;
                big = big << 8 | b;
            }
            return big > long.MaxValue ? long.MaxValue : (long)big;
        }
        field = field.Trim((ReadOnlySpan<byte>)[0, (byte)' ']);
        long value = 0;
        foreach (byte b in field)
        {
            uint digit = (uint)(b - '0');
            if (digit >= 8) return null;
            if (value > long.MaxValue >> 3) return long.MaxValue;
            value = value << 3 | digit;
        }
        return value;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
