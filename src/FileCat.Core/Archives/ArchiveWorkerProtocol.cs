using System.Buffers.Binary;

namespace FileCat.Core.Archives;

public enum ArchiveWorkerMessage : byte
{
    Hello = 1, Control = 2, Read = 3, Data = 4, Seek = 5, Write = 6, SetLength = 7,
    Progress = 8, Completed = 9, Refused = 10, Cancel = 11,
}

/// <summary>
/// Versioned stream-only framing. Validate the fixed header, nonce, sequence and size before allocating a payload.
/// Each direction owns a separate instance; a failed instance is permanently poisoned.
/// </summary>
public sealed class ArchiveWorkerProtocol(Guid nonce, long maximumPayloadBytes)
{
    public const ushort Version = 1;
    public const int HeaderBytes = 36;
    private readonly Guid _nonce = nonce != Guid.Empty ? nonce : throw new ArgumentException("A task nonce is required.", nameof(nonce));
    private readonly long _maximumPayloadBytes = maximumPayloadBytes is >= 0 and <= ArchiveWritePolicy.MaxWorkerWireBytes
        ? maximumPayloadBytes : throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
    private uint _next;
    private long _payloadBytes;
    private long _controlBytes;
    private bool _poisoned;

    public ArchiveWorkerFrame Read(Stream input)
    {
        EnsureHealthy();
        try
        {
            Span<byte> header = stackalloc byte[HeaderBytes];
            input.ReadExactly(header);
            if (!header[..4].SequenceEqual("FCAW"u8) || BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) != Version ||
                header[7] != 0 || new Guid(header.Slice(8, 16)) != _nonce ||
                BinaryPrimitives.ReadUInt32LittleEndian(header[24..]) != _next ||
                BinaryPrimitives.ReadUInt32LittleEndian(header[32..]) != 0)
                throw Fault();
            var kind = (ArchiveWorkerMessage)header[6];
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);
            Validate(kind, length);
            byte[] payload = new byte[(int)length];
            input.ReadExactly(payload);
            Commit(kind, length);
            return new ArchiveWorkerFrame(kind, payload);
        }
        catch { _poisoned = true; throw; }
    }

    public void Write(Stream output, ArchiveWorkerMessage kind, ReadOnlySpan<byte> payload)
    {
        EnsureHealthy();
        try
        {
            Validate(kind, (uint)payload.Length);
            Span<byte> header = stackalloc byte[HeaderBytes];
            header.Clear();
            "FCAW"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..], Version);
            header[6] = (byte)kind;
            _nonce.TryWriteBytes(header.Slice(8, 16));
            BinaryPrimitives.WriteUInt32LittleEndian(header[24..], _next);
            BinaryPrimitives.WriteUInt32LittleEndian(header[28..], (uint)payload.Length);
            output.Write(header);
            output.Write(payload);
            output.Flush();
            Commit(kind, (uint)payload.Length);
        }
        catch { _poisoned = true; throw; }
    }

    private void Validate(ArchiveWorkerMessage kind, uint length)
    {
        if (!Enum.IsDefined(kind) || _next == uint.MaxValue) throw Fault();
        int limit = kind is ArchiveWorkerMessage.Control or ArchiveWorkerMessage.Hello
            ? ArchiveWritePolicy.MaxControlFrameBytes : ArchiveWritePolicy.MaxDataFrameBytes;
        if (length > limit || length > _maximumPayloadBytes - _payloadBytes) throw Fault();
        if (kind is not (ArchiveWorkerMessage.Data or ArchiveWorkerMessage.Write) &&
            length > ArchiveWritePolicy.MaxMetadataBytes - _controlBytes) throw Fault();
        // Even zero-length frames consume a finite allowance: a progress flood cannot keep a session alive forever.
        if (_next >= 4_000_000) throw Fault();
    }

    private void Commit(ArchiveWorkerMessage kind, uint length)
    {
        _payloadBytes += length;
        if (kind is not (ArchiveWorkerMessage.Data or ArchiveWorkerMessage.Write)) _controlBytes += length;
        _next++;
    }
    private void EnsureHealthy() { if (_poisoned) throw Fault(); }
    private static ArchiveWriteException Fault() => new(ArchiveWriteReason.ProtocolViolation, "The archive worker sent an invalid or over-budget message.");
}

public sealed record ArchiveWorkerFrame(ArchiveWorkerMessage Kind, byte[] Payload);

/// <summary>
/// Parent-side authority for a stage proxy. The child supplies offsets, never paths or an output allowance.
/// Bounds and repeated IO are checked before the underlying stream is touched, including SetLength and holes.
/// </summary>
public sealed class ArchiveStageProxy
{
    private readonly Stream _stage;
    private readonly long _maximumLength;
    private readonly long _maximumIo;
    private long _io;
    private bool _poisoned;

    public long Length => _stage.Length;

    public ArchiveStageProxy(Stream stage, long maximumLength, long maximumIo)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (!stage.CanSeek || !stage.CanRead || !stage.CanWrite || stage.Length != 0)
            throw new ArgumentException("An empty parent-owned seekable stage is required.", nameof(stage));
        if (maximumLength < 0 || maximumLength > ArchiveWritePolicy.MaxPhysicalBytes || maximumIo < maximumLength ||
            maximumIo > 4 * maximumLength + (16 << 20))
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        _stage = stage;
        _maximumLength = maximumLength;
        _maximumIo = maximumIo;
    }

    public void WriteAt(long offset, ReadOnlySpan<byte> data)
    {
        Validate(offset, data.Length);
        try { _stage.Position = offset; _stage.Write(data); }
        catch { _poisoned = true; throw; }
    }

    public int ReadAt(long offset, Span<byte> buffer)
    {
        Validate(offset, buffer.Length);
        try { _stage.Position = offset; return _stage.Read(buffer); }
        catch { _poisoned = true; throw; }
    }

    public void SetLength(long length)
    {
        if (_poisoned || length < 0 || length > _maximumLength) Refuse();
        long growth = Math.Max(0, length - _stage.Length);
        ReserveIo(growth);
        try { _stage.SetLength(length); }
        catch { _poisoned = true; throw; }
    }

    private void Validate(long offset, int count)
    {
        if (_poisoned || offset < 0 || offset > _maximumLength || count > ArchiveWritePolicy.MaxDataFrameBytes || count > _maximumLength - offset)
            Refuse();
        // Charge the gap too: repeated sparse extension/truncation cannot amplify physical IO without accounting.
        long growth = Math.Max(0, offset - _stage.Length);
        if (growth > _maximumIo - _io || count > _maximumIo - _io - growth) Refuse();
        ReserveIo(growth + count);
    }

    private void ReserveIo(long amount)
    {
        if (amount > _maximumIo - _io) Refuse();
        _io += amount;
    }

    private void Refuse()
    {
        _poisoned = true;
        throw new ArchiveWriteException(ArchiveWriteReason.ProtocolViolation, "The archive worker exceeded its granted stage or IO allowance.");
    }
}
