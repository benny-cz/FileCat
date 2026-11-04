using System.Buffers.Binary;

namespace FileCat.Core.Archives;

public enum ArchiveControlRecord : byte { AssessmentRequest = 1, Catalog = 2, Member = 3, Receipt = 4 }

/// <summary>
/// Bounded records carried by consecutive control fragments. Record type, total length and exact offset are repeated
/// on each fragment. Reserve the complete record against a cumulative budget before allocating its first buffer.
/// A partial or failed record poisons the reader/writer, including when a caller attempts a different record type.
/// </summary>
public sealed class ArchiveWorkerRecords
{
    private const int PrefixBytes = 9;
    private readonly ArchiveWorkerProtocol _protocol;
    private readonly Stream _pipe;
    private readonly bool _writing;
    private bool _failed;
    private long _bytes;

    public ArchiveWorkerRecords(ArchiveWorkerProtocol protocol, Stream pipe, bool writing)
    { _protocol = protocol; _pipe = pipe; _writing = writing; }

    public void Write(ArchiveControlRecord kind, ReadOnlySpan<byte> record)
    {
        try
        {
            if (!_writing || _failed || !Enum.IsDefined(kind) || record.Length is <= 0 or > ArchiveWritePolicy.MaxEntryMetadataBytes)
                throw Fault();
            Reserve(record.Length);
            int offset = 0;
            byte[] fragment = new byte[Math.Min(record.Length, ArchiveWritePolicy.MaxDataFrameBytes - PrefixBytes) + PrefixBytes];
            while (offset < record.Length)
            {
                int count = Math.Min(record.Length - offset, fragment.Length - PrefixBytes);
                fragment[0] = (byte)kind;
                BinaryPrimitives.WriteInt32LittleEndian(fragment.AsSpan(1), record.Length);
                BinaryPrimitives.WriteInt32LittleEndian(fragment.AsSpan(5), offset);
                record.Slice(offset, count).CopyTo(fragment.AsSpan(PrefixBytes));
                _protocol.Write(_pipe, ArchiveWorkerMessage.Control, fragment.AsSpan(0, PrefixBytes + count));
                offset += count;
            }
        }
        catch { _failed = true; throw; }
    }

    public byte[] Read(ArchiveControlRecord expected, ArchiveWorkerFrame? first = null)
    {
        try
        {
            if (_writing || _failed || !Enum.IsDefined(expected)) throw Fault();
            var frame = first ?? _protocol.Read(_pipe);
            int length = Header(frame, expected, 0, null);
            Reserve(length);
            byte[] record = new byte[length];
            int offset = 0;
            while (true)
            {
                int count = frame.Payload.Length - PrefixBytes;
                frame.Payload.AsSpan(PrefixBytes).CopyTo(record.AsSpan(offset));
                offset += count;
                if (offset == length) return record;
                frame = _protocol.Read(_pipe);
                Header(frame, expected, offset, length);
            }
        }
        catch { _failed = true; throw; }
    }

    private static int Header(ArchiveWorkerFrame frame, ArchiveControlRecord expected, int offset, int? total)
    {
        var data = frame.Payload.AsSpan();
        if (frame.Kind != ArchiveWorkerMessage.Control || data.Length <= PrefixBytes ||
            data.Length > ArchiveWritePolicy.MaxDataFrameBytes || data[0] != (byte)expected) throw Fault();
        int length = BinaryPrimitives.ReadInt32LittleEndian(data[1..]);
        if (length is <= 0 or > ArchiveWritePolicy.MaxEntryMetadataBytes || total is { } size && size != length ||
            BinaryPrimitives.ReadInt32LittleEndian(data[5..]) != offset || offset > length || data.Length - PrefixBytes > length - offset)
            throw Fault();
        return length;
    }

    private void Reserve(int length)
    {
        if (length > ArchiveWritePolicy.MaxMetadataBytes - _bytes) throw Fault();
        _bytes += length;
    }
    private static ArchiveWriteException Fault() => new(ArchiveWriteReason.ProtocolViolation, "The archive worker's bounded record is invalid.");
}
