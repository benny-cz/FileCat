using FileCat.Core.Jobs;

namespace FileCat.Archives.Rar;

/// <summary>A member's packed bytes, read in order across the volumes that hold its parts.</summary>
internal sealed class RarPackedStream(IReadOnlyList<Stream> volumes, IReadOnlyList<RarSegment> segments) : Stream
{
    private int _segment;
    private long _offset;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        while (!buffer.IsEmpty && _segment < segments.Count)
        {
            var segment = segments[_segment];
            long left = segment.Length - _offset;
            if (left <= 0)
            {
                _segment++;
                _offset = 0;
                continue;
            }
            var volume = volumes[segment.Volume];
            volume.Position = segment.Offset + _offset;
            int n = volume.Read(buffer[..(int)Math.Min(buffer.Length, left)]);
            if (n <= 0) throw RarData.Truncated(); // the volume is shorter than its headers say
            _offset += n;
            return n;
        }
        return 0;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// The content of one member: stored bytes, or the decoder's output for exactly this member. The checksum the archive
/// records is verified when the content ends, so damaged data is reported, never handed out as complete.
/// </summary>
internal sealed class RarMemberStream : Stream
{
    private readonly RarMember _member;
    private readonly Stream? _stored;
    private readonly RarDecoder? _decoder;
    private readonly RarWindow? _window;
    private readonly long _start;
    private readonly long _end;
    private readonly CancellationToken _ct;
    private readonly Blake2sp? _blake;
    private uint _crc;
    private long _produced;
    private bool _decoderDone;
    private bool _finished;

    private RarMemberStream(RarMember member, Stream? stored, RarDecoder? decoder, RarWindow? window, CancellationToken ct)
    {
        _member = member;
        _stored = stored;
        _decoder = decoder;
        _window = window;
        _ct = ct;
        _start = window?.Written ?? 0;
        _end = member.UnpackedSize < 0 ? long.MaxValue : _start + member.UnpackedSize;
        _blake = member.Blake2sp is not null ? new Blake2sp() : null;
    }

    public static RarMemberStream Stored(RarMember member, Stream packed, CancellationToken ct)
    {
        if (member.UnpackedSize >= 0 && member.UnpackedSize != member.PackedSize) throw RarData.Corrupt("stored member sizes differ");
        return new RarMemberStream(member, packed, null, null, ct);
    }

    /// <summary>Content decoded by <paramref name="decoder"/> into <paramref name="window"/>, which must have no unread bytes.</summary>
    public static RarMemberStream Decoded(RarMember member, RarDecoder decoder, RarWindow window, CancellationToken ct)
    {
        if (window.Read != window.Written || window.HasFilteredOutput) throw new InvalidOperationException("The window still holds an earlier member's output.");
        return new RarMemberStream(member, null, decoder, window, ct);
    }

    /// <summary>True once the whole member was produced and its checksum verified.</summary>
    public bool Finished => _finished;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _member.UnpackedSize >= 0 ? _member.UnpackedSize : throw new NotSupportedException();
    public override long Position { get => _produced; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_finished || buffer.IsEmpty) return 0;
        int n = _window is null ? ReadStored(buffer) : ReadDecoded(buffer);
        if (n == 0)
        {
            Finish();
            return 0;
        }
        var produced = buffer[..n];
        _crc = Crc32.Append(_crc, produced);
        _blake?.Append(produced);
        _produced += n;
        return n;
    }

    /// <summary>
    /// Produces the rest of the member without handing it out (a solid run's next member needs it decoded). A checksum
    /// mismatch here is this member's problem; a later member verifies its own bytes. Decoding damage still throws:
    /// the decoder's state would be unknown.
    /// </summary>
    public void Drain()
    {
        var sink = new byte[64 * 1024];
        try
        {
            while (Read(sink) > 0) { }
        }
        catch (InvalidDataException ex) when (RarData.IsChecksum(ex))
        {
            _finished = true;
        }
    }

    private int ReadStored(Span<byte> buffer)
    {
        long left = _member.UnpackedSize - _produced;
        if (left <= 0) return 0;
        int n = _stored!.Read(buffer[..(int)Math.Min(buffer.Length, left)]);
        if (n <= 0) throw RarData.Truncated();
        return n;
    }

    private int ReadDecoded(Span<byte> buffer)
    {
        var window = _window!;
        bool stoppedWithoutOutput = false;
        while (true)
        {
            _ct.ThrowIfCancellationRequested();
            int n = window.ReadOutput(buffer, _end, _start);
            if (n > 0) return n;
            if (window.Read >= _end && !window.HasFilteredOutput) return 0;
            if (_decoderDone)
            {
                if (_end != long.MaxValue) throw RarData.Truncated();
                return 0;
            }
            if (stoppedWithoutOutput) throw RarData.Corrupt("a filter block the window cannot complete");
            _decoderDone = _decoder!.Decode(window);
            if (_decoderDone)
            {
                if (window.Written > _end) throw RarData.Corrupt("more data than the member's size");
                if (window.FilterStraddles(window.Written)) throw RarData.Corrupt("filter block past the end of the member");
            }
            else
            {
                stoppedWithoutOutput = true;
            }
            // The decoder ran: the next ReadOutput must make progress unless it finished.
            if (window.ReadOutput(buffer, _end, _start) is int more and > 0) return more;
            if (_decoderDone) continue;
        }
    }

    private void Finish()
    {
        if (_finished) return;
        if (_member.UnpackedSize >= 0 && _produced != _member.UnpackedSize) throw RarData.Truncated();
        if (_member.Crc32 is { } expected && _crc != expected)
            throw RarData.ChecksumMismatch("The member's checksum does not match its data: the archive is damaged.");
        if (_blake is not null && !_blake.Finish().AsSpan().SequenceEqual(_member.Blake2sp))
            throw RarData.ChecksumMismatch("The member's BLAKE2sp hash does not match its data: the archive is damaged.");
        _finished = true;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
