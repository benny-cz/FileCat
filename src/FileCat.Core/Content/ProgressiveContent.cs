using System.Buffers;
using System.Runtime.ExceptionServices;
using FileCat.Core.Diagnostics;
using FileCat.Core.Resources;

namespace FileCat.Core.Content;

/// <summary>
/// What an archive member may produce (plan §15). Its declared size is untrusted: output is capped a little above it,
/// the expansion ratio is bounded once output is large, output short of the declared size means damage, and so does a
/// checksum (where the format keeps one) that does not match.
/// </summary>
public sealed record MemberLimits(long Declared, long Compressed, long MaxLength, long MaxRatio, uint? Crc = null)
{
    /// <summary>Output beyond the declared size that is still accepted.</summary>
    public const long Slack = 1024 * 1024;

    /// <summary>Small members may compress extremely well; the ratio is judged once this much was produced.</summary>
    public const long RatioAfter = 64L * 1024 * 1024;

    /// <param name="maxLength">The cap when the size is unknown (with a known size, the size plus <see cref="Slack"/>).</param>
    public static MemberLimits Of(long declared, long compressed, long maxLength, long maxRatio, uint? crc = null) =>
        new(declared, Math.Max(1, compressed), declared >= 0 ? declared + Slack : maxLength, maxRatio, crc);

    public void Check(long produced)
    {
        if (produced > MaxLength)
            throw new InvalidDataException(Declared >= 0 ? "The member expands beyond its declared size; extraction was stopped."
                : "The member is larger than FileCat extracts in one piece.");
        if (produced > RatioAfter && produced / Compressed > MaxRatio)
            throw new InvalidDataException("The member exceeds the expansion-ratio limit (possible decompression bomb); extraction was stopped.");
    }

    public void CheckEnd(long produced, uint? crc)
    {
        if (Declared >= 0 && produced < Declared)
            throw new InvalidDataException($"The member ended after {produced:N0} of {Declared:N0} bytes; the archive is damaged.");
        if (Crc is { } expected && crc is { } actual && actual != expected)
            throw new InvalidDataException("The member is damaged: its checksum does not match its content.");
    }

    /// <summary>Copies a whole member under these limits and verifies its end; returns the bytes copied.</summary>
    public long CopyAll(Stream source, Stream destination, CancellationToken ct = default)
    {
        uint crc = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
        try
        {
            long total = 0;
            int n;
            while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                total += n;
                Check(total);
                if (Crc is not null) crc = Jobs.Crc32.Append(crc, buffer.AsSpan(0, n));
                destination.Write(buffer, 0, n);
            }
            CheckEnd(total, Crc is null ? null : crc);
            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

/// <summary>
/// A large archive member, decompressed only as far as reads need (plan §15): its first bytes are there at once, a copy
/// shows progress and stops between reads when canceled, and <see cref="MemberLimits"/> apply as bytes appear. What was
/// produced is kept in a private spool file for reads behind the current position, except for readers that only move
/// forward (<see cref="ForwardOnly"/>, such as copies): then nothing is kept or written twice. A member that can be read
/// at any position (a disc image's file, a plain TAR member) is read in place. Archives whose members share one forward
/// cursor take it back with <see cref="Abandon"/>; the stream is then reopened and skipped forward when needed.
/// </summary>
public sealed class ProgressiveContent : IContentSource
{
    private const int ChunkSize = 256 * 1024;
    private readonly object _gate;
    private readonly Func<Stream> _open;
    private readonly Action? _closed;
    private readonly MemberLimits _limits;
    private readonly long _maxKept;
    private readonly string _tempDirectory;
    private uint _crc;
    private Stream? _source;
    private long _sourceAt;
    private FileStream? _spool;
    private long _produced;
    private bool _decided, _inPlace, _forwardOnly, _ended, _read, _closedOnce;
    private string? _damage;
    private volatile bool _disposed;

    /// <param name="gate">The archive's lock: every use of its reader is serialized on it.</param>
    /// <param name="open">Opens the member's bytes from the start (called under the gate).</param>
    /// <param name="maxKept">The most a spool keeps for reading back; forward-only readers are not limited by it.</param>
    /// <param name="closed">Called once when the content is disposed (under the gate).</param>
    public ProgressiveContent(string displayName, object gate, Func<Stream> open, MemberLimits limits, long maxKept, string tempDirectory, Action? closed = null)
    {
        if (limits.Declared < 0) throw new ArgumentException("Progressive content needs a declared size.", nameof(limits));
        DisplayName = displayName;
        _gate = gate;
        _open = open;
        _limits = limits;
        _maxKept = maxKept;
        _tempDirectory = tempDirectory;
        _closed = closed;
    }

    public string DisplayName { get; }
    public bool CanSeek => true;
    public string? LocalPath => null;
    public ContentRevision? GetRevision() => null;

    /// <summary>The declared size until the end was reached, then what was produced.</summary>
    public long Length
    {
        get
        {
            lock (_gate) return _ended && !_inPlace ? _produced : _limits.Declared;
        }
    }

    /// <summary>Content about to be read once from start to end (a copy): progressive content then keeps nothing.</summary>
    public static T? Sequential<T>(T? content) where T : class, IContentSource
    {
        (content as ProgressiveContent)?.ForwardOnly();
        return content;
    }

    /// <summary>For readers that read once from start to end (copies): nothing is kept for reading back.</summary>
    public void ForwardOnly()
    {
        lock (_gate)
        {
            if (!_read) _forwardOnly = true;
        }
    }

    /// <summary>The archive's shared cursor moves to another member (called under the gate): the stream is given up.</summary>
    public void Abandon() => CloseSource();

    public int Read(long offset, Span<byte> buffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (buffer.IsEmpty) return 0;
            _read = true;
            if (!_decided)
            {
                // A member stream that can seek is read in place (no checksum to verify then: formats with one compress).
                _inPlace = Source().CanSeek && _limits.Crc is null;
                _decided = true;
            }
            if (_inPlace) return ReadInPlace(offset, buffer);
            if (_forwardOnly) return ReadForward(offset, buffer);
            Produce(offset + buffer.Length);
            if (offset >= _produced) return _damage is null ? 0 : throw new InvalidDataException(_damage);
            int n = (int)Math.Min(buffer.Length, _produced - offset);
            _spool!.Position = offset;
            _spool.ReadExactly(buffer[..n]);
            return n;
        }
    }

    private Stream Source()
    {
        if (_source is not null) return _source;
        _source = _open();
        _sourceAt = 0;
        return _source;
    }

    private void CloseSource()
    {
        var source = _source;
        _source = null; // A failed close still retires this uncertain stream; never close or reuse it twice.
        source?.Dispose();
    }

    private int ReadInPlace(long offset, Span<byte> buffer)
    {
        var source = Source();
        if (offset >= source.Length) return 0;
        source.Position = offset;
        return source.Read(buffer);
    }

    /// <summary>Keeps producing into the spool until <paramref name="upTo"/> bytes are there or the member ends.</summary>
    private void Produce(long upTo)
    {
        if (_damage is not null) return;
        var chunk = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            while (!_ended && _produced < upTo)
            {
                if (_disposed) throw new ObjectDisposedException(DisplayName);
                var source = Source();
                // After the cursor was taken away, the reopened stream first catches up with what was kept.
                int want = _sourceAt < _produced ? (int)Math.Min(ChunkSize, _produced - _sourceAt) : ChunkSize;
                int n = source.Read(chunk, 0, want);
                if (n <= 0)
                {
                    End();
                    break;
                }
                Advance(chunk.AsSpan(0, n));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    /// <summary>Straight from the member's stream into the caller's buffer; reading behind starts the stream over.</summary>
    private int ReadForward(long offset, Span<byte> buffer)
    {
        if (_ended && offset >= _produced) return _damage is null ? 0 : throw new InvalidDataException(_damage);
        var source = Source();
        if (offset < _sourceAt)
        {
            CloseSource();
            source = Source();
        }
        if (_sourceAt < offset)
        {
            var chunk = ArrayPool<byte>.Shared.Rent(ChunkSize);
            try
            {
                while (_sourceAt < offset)
                {
                    if (_disposed) throw new ObjectDisposedException(DisplayName);
                    int skipped = source.Read(chunk, 0, (int)Math.Min(ChunkSize, offset - _sourceAt));
                    if (skipped <= 0)
                    {
                        End();
                        return 0;
                    }
                    Advance(chunk.AsSpan(0, skipped));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chunk);
            }
        }
        int n = source.Read(buffer);
        if (n <= 0)
        {
            End();
            return 0;
        }
        Advance(buffer[..n]);
        return n;
    }

    /// <summary>Accounts for bytes the stream just delivered at <see cref="_sourceAt"/>: new ones are checked and kept.</summary>
    private void Advance(ReadOnlySpan<byte> bytes)
    {
        long start = _sourceAt;
        _sourceAt += bytes.Length;
        if (_sourceAt <= _produced) return;
        var fresh = bytes[(int)Math.Max(0, _produced - start)..];
        long total = _produced + fresh.Length;
        try
        {
            _limits.Check(total);
            if (!_forwardOnly && total > _maxKept)
                throw new InvalidDataException($"The member is larger than FileCat keeps for viewing ({_maxKept / (1024 * 1024 * 1024)} GiB); extract it with F5 to read all of it.");
        }
        catch (InvalidDataException ex)
        {
            Damaged(ex.Message);
        }
        if (_limits.Crc is not null) _crc = Jobs.Crc32.Append(_crc, fresh);
        if (!_forwardOnly)
        {
            _spool ??= CreateSpool();
            _spool.Position = _produced;
            _spool.Write(fresh);
        }
        _produced = total;
    }

    private FileStream CreateSpool()
    {
        Directory.CreateDirectory(_tempDirectory);
        return new FileStream(Path.Combine(_tempDirectory, $"member-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.Read | FileShare.Delete, 1, FileOptions.DeleteOnClose);
    }

    /// <summary>
    /// The member's stream ended. A stream that ends before what an earlier one delivered, a shortfall against the
    /// declared size, or a checksum that does not match is damage: reported now and on every later read past it.
    /// </summary>
    private void End()
    {
        if (_sourceAt < _produced) Damaged("The member could not be read again: its data ended early.");
        _ended = true;
        try
        {
            _limits.CheckEnd(_produced, _limits.Crc is null ? null : _crc);
        }
        catch (InvalidDataException ex)
        {
            Damaged(ex.Message);
        }
        CloseSource();
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private void Damaged(string message)
    {
        _damage = message;
        _ended = true;
        try { CloseSource(); }
        catch (Exception ex) { AppLog.Warn("Archive member close failed after damage was detected", ex); }
        throw new InvalidDataException(message);
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_gate)
        {
            if (_closedOnce) return;
            _closedOnce = true;
            Exception? failure = null;
            void Close(Action close)
            {
                try { close(); }
                catch (Exception ex)
                {
                    if (failure is null) failure = ex;
                    else AppLog.Warn("Archive member cleanup failed after an earlier close failure", ex);
                }
            }
            Close(CloseSource);
            var spool = _spool;
            _spool = null;
            if (spool is not null) Close(spool.Dispose);
            if (_closed is not null) Close(_closed);
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
