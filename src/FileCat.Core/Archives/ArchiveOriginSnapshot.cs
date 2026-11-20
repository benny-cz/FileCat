using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FileCat.Core.Archives;

public enum ArchiveOriginFormat { WindowsZoneIdentifier = 1, MacQuarantine = 2, LinuxOriginUrl = 3 }
public enum ArchiveOriginState { Absent = 0, Present = 1, Unavailable = 2 }
public enum ArchiveOriginFailure { None = 0, AccessDenied = 1, InUse = 2, IoFailure = 3, TooLarge = 4, Unsupported = 5, Changed = 6 }

/// <summary>
/// Exact bounded native provenance evidence, not an executable trust decision or a parsed aggregate mark. An empty
/// present attribute remains present; a failed read never means absent. Bytes are private copies. The canonical digest
/// includes schema, format, presence/failure and length, so empty, absent and unavailable cannot share evidence.
/// </summary>
public sealed class ArchiveOriginSnapshot
{
    public const int MaxBytes = 64 * 1024;
    private readonly byte[] bytes;
    public ArchiveOriginFormat Format { get; }
    public ArchiveOriginState State { get; }
    public ArchiveOriginFailure Failure { get; }
    public ReadOnlySpan<byte> Bytes => bytes;
    public string Digest { get; }

    private ArchiveOriginSnapshot(ArchiveOriginFormat format, ArchiveOriginState state, ArchiveOriginFailure failure, ReadOnlySpan<byte> bytes)
    {
        if (!Enum.IsDefined(format) || !Enum.IsDefined(state) || !Enum.IsDefined(failure) || bytes.Length > MaxBytes ||
            state != ArchiveOriginState.Present && !bytes.IsEmpty ||
            (state == ArchiveOriginState.Unavailable) != (failure != ArchiveOriginFailure.None))
            throw new ArgumentException("Invalid archive origin evidence.");
        Format = format; State = state; Failure = failure; this.bytes = bytes.ToArray();
        Span<byte> header = stackalloc byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(header, 1);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], (int)format);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], (int)state);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], (int)failure);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], bytes.Length);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(header); hash.AppendData(bytes); Digest = Convert.ToHexString(hash.GetHashAndReset());
    }

    public static ArchiveOriginSnapshot Absent(ArchiveOriginFormat format) => new(format, ArchiveOriginState.Absent, ArchiveOriginFailure.None, []);
    public static ArchiveOriginSnapshot Present(ArchiveOriginFormat format, ReadOnlySpan<byte> bytes) => new(format, ArchiveOriginState.Present, ArchiveOriginFailure.None, bytes);
    public static ArchiveOriginSnapshot Unavailable(ArchiveOriginFormat format, ArchiveOriginFailure failure) => new(format, ArchiveOriginState.Unavailable, failure, []);
}
