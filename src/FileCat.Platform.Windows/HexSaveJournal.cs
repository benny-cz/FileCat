using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Content;

namespace FileCat.Platform.Windows;

public sealed record HexRecoveryRecord(string JournalPath, string TargetPath, WindowsFileIdentity Identity,
    long Length, IReadOnlyList<HexPatchRange> Ranges);

/// <summary>Durable original/replacement ranges for explicit, non-atomic in-place hex saves.</summary>
public static class HexSaveJournal
{
    private static readonly byte[] Magic = "FCHXJ001"u8.ToArray();
    private const int MaxJournalBytes = 48 * 1024 * 1024;

    public static IReadOnlyList<string> Pending(string directory) => Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "hex-*.fcj", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToArray()
        : [];

    public static string Save(ProtectedHexFile file, HexPatchOverlay overlay, string journalDirectory,
        Action<int>? afterDurableStep = null)
    {
        var ranges = overlay.FreezeForSave();
        string? journalPath = null;
        try
        {
            if (ranges.Count == 0) { overlay.FinishSave(true); return ""; }
            file.ValidateForSave(ranges);
            Directory.CreateDirectory(journalDirectory);
            journalPath = Path.Combine(journalDirectory, "hex-" + Guid.NewGuid().ToString("N") + ".fcj");
            byte[] payload = Serialize(file, ranges);
            using (var stream = new FileStream(journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.WriteThrough))
            {
                Span<byte> size = stackalloc byte[4];
                BitConverter.TryWriteBytes(size, payload.Length);
                stream.Write(size);
                stream.Write(payload);
                stream.Write(SHA256.HashData(payload));
                stream.Flush(flushToDisk: true);
                afterDurableStep?.Invoke(0);
                Span<byte> progress = stackalloc byte[5];
                for (int i = 0; i < ranges.Count; i++)
                {
                    file.Write(ranges[i].Offset, ranges[i].Replacement);
                    file.Flush();
                    progress[0] = (byte)'P';
                    BitConverter.TryWriteBytes(progress[1..], i + 1);
                    stream.Write(progress);
                    stream.Flush(flushToDisk: true);
                    afterDurableStep?.Invoke(i + 1);
                }
            }
            // A durable journal can be removed only after the target has been flushed and read back.
            foreach (var range in ranges)
            {
                var actual = new byte[range.Replacement.Length];
                if (file.Read(range.Offset, actual) != actual.Length || !actual.AsSpan().SequenceEqual(range.Replacement))
                    throw new IOException($"Saved bytes at 0x{range.Offset:X} could not be verified; recovery is required.");
            }
            File.Delete(journalPath);
            overlay.FinishSave(true);
            return journalPath;
        }
        catch
        {
            overlay.FinishSave(false);
            throw;
        }
    }

    public static HexRecoveryRecord Read(string journalPath)
    {
        var info = new FileInfo(journalPath);
        if (info.Length < 44 || info.Length > MaxJournalBytes) throw new InvalidDataException("Invalid hex journal size.");
        var bytes = File.ReadAllBytes(journalPath);
        int payloadLength = BitConverter.ToInt32(bytes, 0);
        if (payloadLength < 0 || payloadLength > MaxJournalBytes - 36 || bytes.Length < payloadLength + 36)
            throw new InvalidDataException("Hex journal is truncated or oversized.");
        var payload = bytes.AsSpan(4, payloadLength);
        if (!SHA256.HashData(payload).AsSpan().SequenceEqual(bytes.AsSpan(4 + payloadLength, 32)))
            throw new InvalidDataException("Hex journal checksum failed.");
        using var reader = new BinaryReader(new MemoryStream(bytes, 4, payloadLength, writable: false), Encoding.UTF8);
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Unknown hex journal format.");
        int pathLength = reader.ReadInt32();
        if (pathLength < 1 || pathLength > 32768) throw new InvalidDataException("Invalid hex journal path.");
        string target = new UTF8Encoding(false, true).GetString(reader.ReadBytes(pathLength));
        var identity = new WindowsFileIdentity(reader.ReadUInt32(), reader.ReadUInt64());
        long length = reader.ReadInt64();
        int count = reader.ReadInt32();
        if (length < 0 || count < 0 || count > HexPatchOverlay.MaxTouchedBytes)
            throw new InvalidDataException("Invalid hex journal range count.");
        var ranges = new List<HexPatchRange>(Math.Min(count, 1024));
        long lastEnd = -1;
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            long offset = reader.ReadInt64();
            int size = reader.ReadInt32();
            total = checked(total + size);
            if (size <= 0 || size > HexPatchOverlay.MaxTouchedBytes || total > HexPatchOverlay.MaxTouchedBytes ||
                offset < 0 || offset < lastEnd || offset > length || size > length - offset)
                throw new InvalidDataException("Invalid or overlapping hex journal ranges.");
            var old = reader.ReadBytes(size);
            var replacement = reader.ReadBytes(size);
            if (old.Length != size || replacement.Length != size)
                throw new InvalidDataException("Truncated hex journal range.");
            ranges.Add(new HexPatchRange(offset, old, replacement));
            lastEnd = offset + size;
        }
        if (reader.BaseStream.Position != payloadLength) throw new InvalidDataException("Unexpected hex journal data.");
        return new HexRecoveryRecord(journalPath, target, identity, length, ranges);
    }

    /// <summary>Resume or roll back only when every current byte is one of the two journaled states.</summary>
    public static void Recover(HexRecoveryRecord record, bool rollback, Action<int>? afterDurableStep = null)
    {
        using var file = new ProtectedHexFile(record.TargetPath);
        if (file.FileIdentity != record.Identity || file.Length != record.Length)
            throw new IOException("The journal target's identity or length changed. Automatic recovery is blocked.");
        foreach (var range in record.Ranges)
        {
            var current = new byte[range.Original.Length];
            if (file.Read(range.Offset, current) != current.Length)
                throw new IOException("The journal target could not be read completely.");
            for (int i = 0; i < current.Length; i++)
                if (current[i] != range.Original[i] && current[i] != range.Replacement[i])
                    throw new IOException($"An unrelated byte change at 0x{range.Offset + i:X} blocks automatic recovery.");
        }
        for (int i = 0; i < record.Ranges.Count; i++)
        {
            var range = record.Ranges[i];
            file.Write(range.Offset, rollback ? range.Original : range.Replacement);
            file.Flush();
            afterDurableStep?.Invoke(i + 1);
        }
        File.Delete(record.JournalPath);
    }

    private static byte[] Serialize(ProtectedHexFile file, IReadOnlyList<HexPatchRange> ranges)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        var path = Encoding.UTF8.GetBytes(file.LocalPath!);
        writer.Write(path.Length);
        writer.Write(path);
        writer.Write(file.FileIdentity.VolumeSerial);
        writer.Write(file.FileIdentity.FileIndex);
        writer.Write(file.Length);
        writer.Write(ranges.Count);
        foreach (var range in ranges)
        {
            writer.Write(range.Offset);
            writer.Write(range.Original.Length);
            writer.Write(range.Original);
            writer.Write(range.Replacement);
        }
        writer.Flush();
        if (stream.Length + 36 > MaxJournalBytes) throw new IOException("Hex save journal exceeds its 48 MiB limit.");
        return stream.ToArray();
    }
}
