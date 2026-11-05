using System.Buffers.Binary;

namespace FileCat.Core.Archives;

/// <summary>One read authority fixed by the parent. IDs carry no filesystem spelling.</summary>
public sealed record ArchiveInputGrant(int Id, long Length);

/// <summary>
/// Trusted-parent IO dispatcher. openSource must yield a view of a retained, reviewed regular-file object or private
/// snapshot, never reopen an unchecked worker-selected path. At most one such view is held here at a time.
/// </summary>
public sealed class ArchiveIoAuthority : IDisposable
{
    public const int StageId = -1;
    public const int MaximumReadBytes = ArchiveWritePolicy.MaxDataFrameBytes - 12;
    private readonly IReadOnlyDictionary<int, ArchiveInputGrant> _grants;
    private readonly Func<int, Stream> _openSource;
    private readonly ArchiveStageProxy _stage;
    private readonly long _maximumSourceIo;
    private long _sourceIo;
    private Stream? _current;
    private int _currentId = -1;
    private bool _poisoned;

    public ArchiveIoAuthority(IReadOnlyList<ArchiveInputGrant> grants, Func<int, Stream> openSource,
        ArchiveStageProxy stage, long maximumSourceIo)
    {
        if (grants.Count > ArchiveWritePolicy.MaxMembers + 1 || maximumSourceIo < 0 || maximumSourceIo > 4 * ArchiveWritePolicy.MaxPhysicalBytes + ArchiveWritePolicy.MaxLogicalBytes + (16 << 20))
            throw new ArgumentOutOfRangeException(nameof(grants));
        var frozen = new Dictionary<int, ArchiveInputGrant>(grants.Count);
        long inputBytes = 0;
        foreach (var grant in grants)
        {
            if (grant.Id < 0 || grant.Length < 0 || grant.Length > ArchiveWritePolicy.MaxPhysicalBytes || !frozen.TryAdd(grant.Id, grant))
                throw new ArgumentException("Input grants must have distinct nonnegative IDs and bounded lengths.", nameof(grants));
            ArchiveWriteBudget.Reserve(inputBytes, grant.Length, ArchiveWritePolicy.MaxPhysicalBytes + ArchiveWritePolicy.MaxLogicalBytes);
            inputBytes += grant.Length;
        }
        if (maximumSourceIo > 4 * inputBytes + (16 << 20))
            throw new ArgumentOutOfRangeException(nameof(maximumSourceIo), "The IO allowance must derive from the actual granted input lengths.");
        _grants = frozen;
        _openSource = openSource;
        _stage = stage;
        _maximumSourceIo = maximumSourceIo;
    }

    public ArchiveWorkerFrame Handle(ArchiveWorkerFrame request)
    {
        if (_poisoned) throw Fault();
        try
        {
            var payload = request.Payload.AsSpan();
            switch (request.Kind)
            {
                case ArchiveWorkerMessage.Read:
                {
                    if (payload.Length != 16) throw Fault();
                    int id = BinaryPrimitives.ReadInt32LittleEndian(payload);
                    long offset = BinaryPrimitives.ReadInt64LittleEndian(payload[4..]);
                    int count = BinaryPrimitives.ReadInt32LittleEndian(payload[12..]);
                    if (count < 0 || count > MaximumReadBytes || offset < 0) throw Fault();
                    long length = id == StageId ? _stage.Length : _grants.TryGetValue(id, out var grant) ? grant.Length : throw Fault();
                    if (offset > length || count > length - offset) throw Fault();
                    if (id != StageId && count > _maximumSourceIo - _sourceIo) throw Fault();
                    byte[] reply = new byte[12 + count];
                    BinaryPrimitives.WriteInt32LittleEndian(reply, id);
                    BinaryPrimitives.WriteInt64LittleEndian(reply.AsSpan(4), offset);
                    int read;
                    if (id == StageId) read = _stage.ReadAt(offset, reply.AsSpan(12));
                    else
                    {
                        _sourceIo += count;
                        if (_currentId != id)
                        {
                            _current?.Dispose();
                            _current = null;
                            _currentId = -1;
                            var opened = _openSource(id);
                            try
                            {
                                if (!opened.CanRead || !opened.CanSeek || opened.Length != length) throw Changed();
                                _current = opened;
                                _currentId = id;
                            }
                            catch { opened.Dispose(); throw; }
                        }
                        _current!.Position = offset;
                        read = _current.Read(reply.AsSpan(12));
                        if (read == 0 && count != 0) throw Changed();
                    }
                    if (read != count) Array.Resize(ref reply, 12 + read);
                    return new ArchiveWorkerFrame(ArchiveWorkerMessage.Data, reply);
                }
                case ArchiveWorkerMessage.Write:
                {
                    if (payload.Length < 8 || payload.Length > ArchiveWritePolicy.MaxDataFrameBytes) throw Fault();
                    long offset = BinaryPrimitives.ReadInt64LittleEndian(payload);
                    _stage.WriteAt(offset, payload[8..]);
                    return Ack(_stage.Length);
                }
                case ArchiveWorkerMessage.SetLength:
                    if (payload.Length != 8) throw Fault();
                    _stage.SetLength(BinaryPrimitives.ReadInt64LittleEndian(payload));
                    return Ack(_stage.Length);
                default: throw Fault(); // no worker message can publish, delete, rename or open a path
            }
        }
        catch { _poisoned = true; throw; }
    }

    private static ArchiveWorkerFrame Ack(long length)
    {
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(payload, length);
        return new ArchiveWorkerFrame(ArchiveWorkerMessage.Data, payload);
    }
    private static ArchiveWriteException Fault() => new(ArchiveWriteReason.ProtocolViolation, "The archive worker requested IO outside its granted input or stage.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "A captured archive input changed while it was being read.");
    public void Dispose() { _poisoned = true; _current?.Dispose(); _current = null; }
}

/// <summary>Serialized, request/response worker transport. Its owner supplies framed pipes and monitors lifetime.</summary>
public sealed class ArchiveProxyConnection(Func<ArchiveWorkerFrame, ArchiveWorkerFrame> exchange)
{
    private readonly object _gate = new();
    private bool _poisoned;

    internal ArchiveWorkerFrame Exchange(ArchiveWorkerFrame request)
    {
        lock (_gate)
        {
            if (_poisoned) throw new ArchiveWriteException(ArchiveWriteReason.ProtocolViolation, "The archive worker connection is closed.");
            try { return exchange(request); }
            catch { _poisoned = true; throw; }
        }
    }

    internal void Poison() { lock (_gate) _poisoned = true; }
}

/// <summary>A seekable worker view of one fixed grant, or the seekable stage. Contains no filesystem operations.</summary>
public sealed class ArchiveProxyStream : Stream
{
    private readonly ArchiveProxyConnection _connection;
    private readonly int _id;
    private readonly long _maximumLength;
    private readonly bool _writable;
    private long _length;
    private long _position;
    private bool _disposed;

    public ArchiveProxyStream(ArchiveProxyConnection connection, int id, long length, long maximumLength, bool writable = false)
    {
        if (id < 0 && id != ArchiveIoAuthority.StageId || writable && id != ArchiveIoAuthority.StageId ||
            length < 0 || maximumLength < length || maximumLength > ArchiveWritePolicy.MaxPhysicalBytes ||
            !writable && maximumLength != length) throw new ArgumentException("The stream grant is invalid.");
        _connection = connection;
        _id = id;
        _length = length;
        _maximumLength = maximumLength;
        _writable = writable;
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => !_disposed && _writable;
    public override long Length { get { Check(); return _length; } }
    public override long Position { get { Check(); return _position; } set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        Check();
        if (_position >= _length || buffer.Length == 0) return 0;
        int count = (int)Math.Min(Math.Min(buffer.Length, ArchiveIoAuthority.MaximumReadBytes), _length - _position);
        byte[] request = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(request, _id);
        BinaryPrimitives.WriteInt64LittleEndian(request.AsSpan(4), _position);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(12), count);
        var response = _connection.Exchange(new(ArchiveWorkerMessage.Read, request));
        if (response.Kind != ArchiveWorkerMessage.Data || response.Payload.Length < 12 || response.Payload.Length > 12 + count ||
            BinaryPrimitives.ReadInt32LittleEndian(response.Payload) != _id ||
            BinaryPrimitives.ReadInt64LittleEndian(response.Payload.AsSpan(4)) != _position || response.Payload.Length == 12)
            Refuse();
        int read = response.Payload.Length - 12;
        response.Payload.AsSpan(12).CopyTo(buffer);
        _position += read;
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Check();
        if (!_writable) throw new NotSupportedException();
        if (buffer.Length > _maximumLength - _position) Refuse();
        while (buffer.Length > 0)
        {
            int count = Math.Min(buffer.Length, ArchiveWritePolicy.MaxDataFrameBytes - 8);
            byte[] request = new byte[8 + count];
            BinaryPrimitives.WriteInt64LittleEndian(request, _position);
            buffer[..count].CopyTo(request.AsSpan(8));
            long expectedLength = Math.Max(_length, _position + count);
            ValidateAck(_connection.Exchange(new(ArchiveWorkerMessage.Write, request)), expectedLength);
            _position += count;
            _length = expectedLength;
            buffer = buffer[count..];
        }
    }

    public override void SetLength(long value)
    {
        Check();
        if (!_writable) throw new NotSupportedException();
        if (value < 0 || value > _maximumLength) Refuse();
        byte[] request = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(request, value);
        ValidateAck(_connection.Exchange(new(ArchiveWorkerMessage.SetLength, request)), value);
        _length = value;
        if (_position > value) _position = value;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Check();
        long basis = origin switch { SeekOrigin.Begin => 0, SeekOrigin.Current => _position, SeekOrigin.End => _length, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
        if (offset < -basis || offset > _maximumLength - basis) Refuse();
        _position = basis + offset;
        return _position;
    }

    private void ValidateAck(ArchiveWorkerFrame response, long expectedLength)
    {
        if (response.Kind != ArchiveWorkerMessage.Data || response.Payload.Length != 8 ||
            BinaryPrimitives.ReadInt64LittleEndian(response.Payload) != expectedLength) Refuse();
    }

    private void Check() => ObjectDisposedException.ThrowIf(_disposed, this);
    private void Refuse()
    {
        _connection.Poison();
        throw new ArchiveWriteException(ArchiveWriteReason.ProtocolViolation, "The archive proxy received an invalid response or stream offset.");
    }
    public override void Flush() { Check(); } // durability belongs to the real parent-owned descriptor
    protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
}
