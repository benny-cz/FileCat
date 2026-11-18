using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.Analysis;

namespace FileCat.Platform.Windows.Analysis;

internal enum NtfsMetadataMessage
{
    Hello, Accepted, ObservationBatch, Progress, Issue, Pause, Paused, Resume, Cancel, Complete, Refused,
}

internal enum NtfsMetadataIssue
{
    UnsupportedVolume, ChangedVolume, Unavailable, InvalidRecords, LimitReached, TimedOut,
}

internal enum NtfsMetadataCompletion { Finished, Partial, Canceled }

internal readonly record struct NtfsMetadataFrame(NtfsMetadataMessage Kind, JsonElement Payload);

/// <summary>Bounded metadata-only frames. A disconnected or damaged byte stream has no resynchronization marker.</summary>
internal static class NtfsMetadataProtocol
{
    internal const int Version = 1;
    internal const int MaximumFrameBytes = 1024 * 1024;
    internal const int MaximumRecordBatch = 1024;
    internal const int MaximumNameBytes = 510;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 8 };

    internal static string NewNonce()
    {
        string nonce;
        do { nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); } while (!ValidNonce(nonce));
        return nonce;
    }
    internal static bool ValidNonce(string? nonce) => ValidHex(nonce, 64);
    private static bool ValidHex(string? value, int length) =>
        value is not null && value.Length == length && value.Any(c => c != '0') &&
        value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    internal static async ValueTask<NtfsMetadataFrame> ReadAsync(Stream stream, string nonce, uint sequence, CancellationToken cancellation)
    {
        byte[] header = new byte[sizeof(uint)];
        await stream.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length is 0 or > MaximumFrameBytes) throw InvalidFrame();
        byte[] body = ArrayPool<byte>.Shared.Rent((int)length);
        try
        {
            var frame = body.AsMemory(0, (int)length);
            await stream.ReadExactlyAsync(frame, cancellation).ConfigureAwait(false);
            return Parse(frame, nonce, sequence);
        }
        finally { ArrayPool<byte>.Shared.Return(body, clearArray: true); }
    }

    private static NtfsMetadataFrame Parse(ReadOnlyMemory<byte> body, string nonce, uint sequence)
    {
        try
        {
            _ = StrictUtf8.GetCharCount(body.Span);
            using var doc = JsonDocument.Parse(body, JsonOptions);
            var fields = Fields(doc.RootElement, "ProtocolVersion", "SessionNonce", "Sequence", "Kind", "Payload");
            if (!fields[0].TryGetInt32(out int version) || version != Version ||
                fields[1].GetString() != nonce || !fields[2].TryGetUInt32(out uint actual) || actual != sequence ||
                !ClosedEnum<NtfsMetadataMessage>(fields[3], out var kind))
                throw InvalidFrame();
            ValidatePayload(kind, fields[4]);
            return new(kind, fields[4].Clone());
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or InvalidOperationException)
        {
            // The source may disclose privileged names. Parser excerpts and paths must stay out of the error.
            throw InvalidFrame();
        }
    }

    internal static async ValueTask WriteAsync(Stream stream, string nonce, uint sequence, NtfsMetadataFrame frame,
        CancellationToken cancellation)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ProtocolVersion = Version,
            SessionNonce = nonce,
            Sequence = sequence,
            Kind = frame.Kind.ToString(),
            frame.Payload,
        });
        try
        {
            if (body.Length > MaximumFrameBytes) throw InvalidFrame();
            byte[] header = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)body.Length);
            await stream.WriteAsync(header, cancellation).ConfigureAwait(false);
            await stream.WriteAsync(body, cancellation).ConfigureAwait(false);
            await stream.FlushAsync(cancellation).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(body); }
    }

    internal static void ValidatePayload(NtfsMetadataMessage kind, JsonElement payload)
    {
        switch (kind)
        {
            case NtfsMetadataMessage.Hello:
            case NtfsMetadataMessage.Pause:
            case NtfsMetadataMessage.Paused:
            case NtfsMetadataMessage.Resume:
            case NtfsMetadataMessage.Cancel:
                _ = Fields(payload);
                return;
            case NtfsMetadataMessage.Accepted:
                if (!ValidHex(Fields(payload, "RootId")[0].GetString(), 32)) throw InvalidFrame();
                return;
            case NtfsMetadataMessage.Issue:
            case NtfsMetadataMessage.Refused:
                if (!ClosedEnum<NtfsMetadataIssue>(Fields(payload, "Code")[0], out _)) throw InvalidFrame();
                return;
            case NtfsMetadataMessage.Progress:
                _ = ReadCount(Fields(payload, "Seen")[0]);
                return;
            case NtfsMetadataMessage.Complete:
                var complete = Fields(payload, "Seen", "Outcome");
                _ = ReadCount(complete[0]);
                if (!ClosedEnum<NtfsMetadataCompletion>(complete[1], out _)) throw InvalidFrame();
                return;
            case NtfsMetadataMessage.ObservationBatch:
                var records = Fields(payload, "Records")[0];
                if (records.ValueKind != JsonValueKind.Array || records.GetArrayLength() is 0 or > MaximumRecordBatch)
                    throw InvalidFrame();
                foreach (var record in records.EnumerateArray()) ValidateRecord(record);
                return;
            default:
                throw InvalidFrame();
        }
    }

    private static void ValidateRecord(JsonElement record)
    {
        var fields = Fields(record, "ObjectId", "ParentId", "NameUtf16", "Attributes");
        if (!ValidHex(fields[0].GetString(), 32) || !ValidHex(fields[1].GetString(), 32) || !fields[3].TryGetUInt32(out _))
            throw InvalidFrame();
        string? name = fields[2].GetString();
        Span<byte> bytes = stackalloc byte[MaximumNameBytes];
        if (name is null || name.Length > (MaximumNameBytes + 2) / 3 * 4 ||
            !Convert.TryFromBase64String(name, bytes, out int written) || written is 0 or 1 || (written & 1) != 0 ||
            !string.Equals(name, Convert.ToBase64String(bytes[..written]), StringComparison.Ordinal))
            throw InvalidFrame();
        if (!ValidRawName(bytes[..written])) throw InvalidFrame();
    }

    internal static bool ValidRawName(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MaximumNameBytes || (bytes.Length & 1) != 0 ||
            bytes.Length == 2 && BinaryPrimitives.ReadUInt16LittleEndian(bytes) == '.' ||
            bytes.Length == 4 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0x002E002E) return false;
        for (int index = 0; index < bytes.Length; index += 2)
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[index..]) is 0 or (ushort)'/' or (ushort)'\\' or (ushort)':')
                return false;
        return true;
    }

    internal static uint ReadCount(JsonElement count) =>
        count.TryGetUInt32(out uint seen) && seen <= AnalysisLimits.Observations ? seen : throw InvalidFrame();

    private static JsonElement[] Fields(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw InvalidFrame();
        var values = new JsonElement[names.Length];
        foreach (var property in element.EnumerateObject())
        {
            int index = Array.FindIndex(names, property.NameEquals);
            if (index < 0 || values[index].ValueKind != JsonValueKind.Undefined) throw InvalidFrame();
            values[index] = property.Value;
        }
        if (values.Any(value => value.ValueKind == JsonValueKind.Undefined)) throw InvalidFrame();
        return values;
    }

    private static bool ClosedEnum<T>(JsonElement value, out T result) where T : struct, Enum
    {
        string? name = value.GetString();
        return Enum.TryParse(name, ignoreCase: false, out result) && Enum.IsDefined(result) && name == result.ToString();
    }

    internal static InvalidDataException InvalidFrame() => new("The NTFS metadata session contained an invalid frame.");
}
