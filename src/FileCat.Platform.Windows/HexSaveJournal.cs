using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Content;

namespace FileCat.Platform.Windows;

/// <summary>A durable journal of one in-place save: target identity, length, and original/replacement ranges.</summary>
/// <param name="WrittenRanges">Ranges the save recorded as written and flushed before it stopped (the next may be partial).</param>
public sealed record HexRecoveryRecord(string JournalPath, string TargetPath, WindowsFileIdentity Identity,
    long Length, IReadOnlyList<HexPatchRange> Ranges, int WrittenRanges, DateTime CreatedUtc);

/// <summary>What the target holds now, compared with a journal. <see cref="Blocker"/> is set when recovery is unsafe.</summary>
public sealed record HexRecoveryInspection(int OriginalRanges, int ReplacedRanges, int MixedRanges, string? Blocker)
{
    public bool AlreadyFinished => Blocker is null && OriginalRanges == 0 && MixedRanges == 0;
    public bool AlreadyRolledBack => Blocker is null && ReplacedRanges == 0 && MixedRanges == 0;
}

/// <summary>An in-place save stopped after its journal became durable; the journal stays for guarded recovery.</summary>
public sealed class HexSaveInterruptedException(string journalPath, string message, Exception? inner = null)
    : IOException(message, inner)
{
    public string JournalPath { get; } = journalPath;
}

/// <summary>Durable original/replacement ranges for explicit, non-atomic in-place hex saves.</summary>
public static class HexSaveJournal
{
    private static readonly byte[] Magic = "FCHXJ002"u8.ToArray();
    public const int MaxJournalBytes = 48 * 1024 * 1024;
    private const int RangeHeaderBytes = 12;

    public static IReadOnlyList<string> Pending(string directory) => Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "hex-*.fcj", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToArray()
        : [];

    /// <summary>
    /// Writes the overlay's ranges in place. The journal is flushed before the first target write; after that any
    /// failure raises <see cref="HexSaveInterruptedException"/> and keeps the journal. <paramref name="afterDurableStep"/>
    /// is a test hook called after each durable step (0 = journal written, n = range n written).
    /// </summary>
    public static string Save(ProtectedHexFile file, HexPatchOverlay overlay, string journalDirectory,
        Action<int>? afterDurableStep = null)
    {
        var ranges = overlay.FreezeForSave();
        bool committed = false;
        try
        {
            if (ranges.Count == 0) { committed = true; return ""; }
            file.ValidateForSave(ranges);
            byte[] payload = Serialize(file, ranges);
            Directory.CreateDirectory(journalDirectory);
            string journalPath = Path.Combine(journalDirectory, "hex-" + Guid.NewGuid().ToString("N") + ".fcj");
            FileStream stream;
            try
            {
                stream = new FileStream(journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough);
                try
                {
                    Span<byte> size = stackalloc byte[4];
                    BitConverter.TryWriteBytes(size, payload.Length);
                    stream.Write(size);
                    stream.Write(payload);
                    stream.Write(SHA256.HashData(payload));
                    stream.Flush(flushToDisk: true);
                }
                catch { stream.Dispose(); throw; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The target is untouched while the journal is incomplete, so there is nothing to recover.
                TryDelete(journalPath);
                throw new IOException("The recovery journal could not be written, so the file was not changed. " + ex.Message, ex);
            }
            using (stream)
            {
                try
                {
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
                    VerifyReadBack(file, ranges, rollback: false);
                }
                catch (Exception ex) when (ex is not HexSaveInterruptedException)
                {
                    throw new HexSaveInterruptedException(journalPath,
                        "The save stopped after the recovery journal was written: " + ex.Message, ex);
                }
            }
            committed = true;
            // The target is complete and verified; a journal that cannot be removed is harmless (recovery sees
            // every range already replaced) and is reported there.
            TryDelete(journalPath);
            return journalPath;
        }
        finally
        {
            overlay.FinishSave(committed);
        }
    }

    public static HexRecoveryRecord Read(string journalPath)
    {
        var info = new FileInfo(journalPath);
        if (info.Length < 44 || info.Length > MaxJournalBytes + 5L * (HexPatchOverlay.MaxTouchedBytes + 1))
            throw new InvalidDataException("Invalid hex journal size.");
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
        var identity = new WindowsFileIdentity(reader.ReadUInt64(), Convert.ToHexString(reader.ReadBytes(16)));
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
        // Progress records follow the checksum; a torn final record (crash while appending) is ignored.
        int written = 0;
        for (int at = payloadLength + 36; at + 5 <= bytes.Length && bytes[at] == (byte)'P'; at += 5)
        {
            int step = BitConverter.ToInt32(bytes, at + 1);
            if (step != written + 1 || step > ranges.Count) break;
            written = step;
        }
        return new HexRecoveryRecord(journalPath, target, identity, length, ranges, written, info.CreationTimeUtc);
    }

    /// <summary>
    /// Compares the target with the journal without changing anything. Opens the path with full sharing, so it also
    /// works while another program reads the file; recovery itself re-verifies under the protected handle.
    /// </summary>
    public static HexRecoveryInspection Inspect(HexRecoveryRecord record)
    {
        string path;
        try { path = ProtectedHexFile.LocalFullPath(record.TargetPath); }
        catch (NotSupportedException ex) { return new HexRecoveryInspection(0, 0, 0, ex.Message); }
        if (!File.Exists(path)) return new HexRecoveryInspection(0, 0, 0, "The file no longer exists at this path.");
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (ProtectedHexFile.Identity(handle) != record.Identity)
            return new HexRecoveryInspection(0, 0, 0, "A different file now has this name, so the journal no longer applies to it.");
        if (RandomAccess.GetLength(handle) != record.Length)
            return new HexRecoveryInspection(0, 0, 0, "The file's length changed after the save stopped, so another program modified it.");
        return Compare(record.Ranges, (offset, buffer) => RandomAccess.Read(handle, buffer, offset));
    }

    /// <summary>
    /// Resumes (writes replacements) or rolls back (writes originals) only when the target is the journaled file and
    /// each current byte is one of the two journaled states; then flushes, reads back, and removes the journal.
    /// <paramref name="openFile"/> lets an editor that still holds the protected handle recover through it.
    /// </summary>
    public static void Recover(HexRecoveryRecord record, bool rollback, ProtectedHexFile? openFile = null,
        Action<int>? afterDurableStep = null)
    {
        var owned = openFile is null ? new ProtectedHexFile(record.TargetPath) : null;
        var file = openFile ?? owned!;
        try
        {
            if (file.FileIdentity != record.Identity || file.Length != record.Length)
                throw new IOException("The file at this path is not the one that was being saved, or its length changed. Automatic recovery is blocked.");
            var state = Compare(record.Ranges, (offset, buffer) => file.Read(offset, buffer));
            if (state.Blocker is not null) throw new IOException(state.Blocker);
            for (int i = 0; i < record.Ranges.Count; i++)
            {
                var range = record.Ranges[i];
                file.Write(range.Offset, rollback ? range.Original : range.Replacement);
                file.Flush();
                afterDurableStep?.Invoke(i + 1);
            }
            VerifyReadBack(file, record.Ranges, rollback);
        }
        finally { owned?.Dispose(); }
        File.Delete(record.JournalPath);
    }

    /// <summary>Removes a journal without touching its target (the user accepts the file as it is).</summary>
    public static void Discard(HexRecoveryRecord record) => File.Delete(record.JournalPath);

    private static HexRecoveryInspection Compare(IReadOnlyList<HexPatchRange> ranges, Func<long, byte[], int> read)
    {
        int original = 0, replaced = 0, mixed = 0;
        foreach (var range in ranges)
        {
            var current = new byte[range.Original.Length];
            if (read(range.Offset, current) != current.Length)
                return new HexRecoveryInspection(original, replaced, mixed, "The file could not be read completely.");
            bool isOriginal = true, isReplacement = true;
            for (int i = 0; i < current.Length; i++)
            {
                bool o = current[i] == range.Original[i], r = current[i] == range.Replacement[i];
                if (!o && !r)
                    return new HexRecoveryInspection(original, replaced, mixed,
                        $"The byte at 0x{range.Offset + i:X} is neither the original nor the saved value, so another program changed the file. Automatic recovery is blocked.");
                isOriginal &= o;
                isReplacement &= r;
            }
            if (isReplacement && !isOriginal) replaced++;
            else if (isOriginal) original++;
            else mixed++;
        }
        return new HexRecoveryInspection(original, replaced, mixed, null);
    }

    private static void VerifyReadBack(ProtectedHexFile file, IReadOnlyList<HexPatchRange> ranges, bool rollback)
    {
        foreach (var range in ranges)
        {
            var expected = rollback ? range.Original : range.Replacement;
            var actual = new byte[expected.Length];
            if (file.Read(range.Offset, actual) != actual.Length || !actual.AsSpan().SequenceEqual(expected))
                throw new IOException($"Written bytes at 0x{range.Offset:X} read back differently.");
        }
    }

    private static void TryDelete(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { File.Delete(path); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Antivirus scanners briefly hold newly written files.
                if (attempt >= 4) return;
                Thread.Sleep(50 << attempt);
            }
        }
    }

    private static byte[] Serialize(ProtectedHexFile file, IReadOnlyList<HexPatchRange> ranges)
    {
        var path = Encoding.UTF8.GetBytes(file.LocalPath!);
        long estimate = 8 + 4 + path.Length + 8 + 16 + 8 + 4 + 36L + ranges.Sum(r => RangeHeaderBytes + 2L * r.Original.Length);
        if (estimate > MaxJournalBytes)
            throw new IOException($"These edits change too many scattered bytes for the {MaxJournalBytes / (1024 * 1024)} MiB recovery journal, so nothing was written. Use Save As or Export patch instead.");
        using var stream = new MemoryStream((int)estimate);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(path.Length);
        writer.Write(path);
        writer.Write(file.FileIdentity.VolumeSerial);
        writer.Write(Convert.FromHexString(file.FileIdentity.FileId));
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
        return stream.ToArray();
    }
}
