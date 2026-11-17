using System.Buffers;
using System.Security.Cryptography;

namespace FileCat.Core.Jobs;

internal sealed record StorageJournalReadResult(StorageTransformJournalCollector Collector, StorageJournalPrefix Prefix,
    long BytesRead, bool LimitReached);

/// <summary>Reads a strict bounded sequence; damaged records are latched, never skipped to reach a later end.</summary>
internal static class StorageTransformJournalReader
{
    internal static StorageJournalReadResult Read(Stream input, Guid expectedJob = default, long? prefixLength = null,
        string? prefixDigest = null, CancellationToken ct = default) => Read(input, expectedJob, prefixLength, prefixDigest, ct, null);

    internal static StorageJournalReadResult Read(Stream input, Guid expectedJob, long? prefixLength,
        string? prefixDigest, CancellationToken ct, Action<StorageTransformJournalCollector>? acceptedRecord)
    {
        var collector = new StorageTransformJournalCollector();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] chunk = new byte[64 * 1024]; var line = new ArrayBufferWriter<byte>(1024);
        long bytes = 0; int records = 0; bool limit = false;
        long maximum = prefixLength ?? StorageJournalPolicy.JournalBytes;
        if (maximum is < 0 or > StorageJournalPolicy.JournalBytes) throw new ArgumentOutOfRangeException(nameof(prefixLength));
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int wanted = (int)Math.Min(chunk.Length, maximum - bytes);
                if (wanted == 0)
                {
                    if (prefixLength is null && input.ReadByte() != -1) { limit = true; collector.Fault(); }
                    break;
                }
                int got = input.Read(chunk, 0, wanted);
                if (got == 0) break;
                digest.AppendData(chunk, 0, got); bytes += got;
                for (int i = 0; i < got; i++)
                {
                    if (line.WrittenCount >= StorageJournalPolicy.RecordBytes) { limit = true; collector.Fault(); return Result(); }
                    line.GetSpan(1)[0] = chunk[i]; line.Advance(1);
                    if (chunk[i] != '\n') continue;
                    if (++records > StorageJournalPolicy.RecordLimit) { limit = true; collector.Fault(); return Result(); }
                    using var document = StorageTransformJournalWriter.Decode(line.WrittenSpan);
                    collector.Read(document.RootElement); line.Clear();
                    if (collector.Damaged) return Result();
                    acceptedRecord?.Invoke(collector);
                }
            }
            if (line.WrittenCount != 0 || prefixLength is { } expected && bytes != expected) collector.Fault();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        { collector.Fault(); }
        return Result();

        StorageJournalReadResult Result()
        {
            string hash = Convert.ToHexString(digest.GetCurrentHash());
            Guid job = collector.Header?.JobId ?? expectedJob;
            if (expectedJob != Guid.Empty && collector.Header?.JobId != expectedJob || prefixDigest is not null && !hash.Equals(prefixDigest, StringComparison.Ordinal)) collector.Fault();
            return new(collector, new(job, collector.Sequence, bytes, hash), bytes, limit);
        }
    }
}
