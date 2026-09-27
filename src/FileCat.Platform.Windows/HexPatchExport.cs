using System.Text.Json;
using FileCat.Core.Content;

namespace FileCat.Platform.Windows;

/// <summary>A parsed patch file: fixed-length ranges with the bytes they expect and the bytes they write.</summary>
public sealed record HexPatchFile(string SourcePath, string SourceIdentity, long SourceLength, IReadOnlyList<HexPatchRange> Ranges);

/// <summary>
/// Portable record of exact modified ranges. Export never changes the source; applying a patch only stages its
/// ranges in an editor's overlay after every expected original byte matches.
/// </summary>
public static class HexPatchExport
{
    public const string Format = "FileCat fixed-length hex patch v1";
    private const long MaxPatchFileBytes = 64L * 1024 * 1024;

    public static void Export(ProtectedHexFile source, HexPatchOverlay overlay, string destination)
    {
        string full = Path.GetFullPath(destination);
        if (File.Exists(full) || Directory.Exists(full)) throw new IOException("Patch export requires a new file name; this one already exists.");
        string temp = Path.Combine(Path.GetDirectoryName(full)!, ".filecat-patch-" + Guid.NewGuid().ToString("N") + ".tmp");
        var ranges = overlay.FreezeForSave();
        try
        {
            if (ranges.Count == 0) throw new IOException("There are no modified bytes to export.");
            source.ValidateForSave(ranges);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024))
            {
                JsonSerializer.Serialize(stream, new
                {
                    format = Format,
                    sourcePath = source.LocalPath,
                    sourceIdentity = source.FileIdentity.ToString(),
                    sourceLength = source.Length,
                    ranges = ranges.Select(r => new { offset = r.Offset, originalBase64 = Convert.ToBase64String(r.Original),
                        replacementBase64 = Convert.ToBase64String(r.Replacement) }).ToArray(),
                }, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, full, overwrite: false);
        }
        finally
        {
            overlay.FinishSave(false);
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>Parses and bounds-checks a patch file; content is untrusted input.</summary>
    public static HexPatchFile Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("The patch file no longer exists.", path);
        if (info.Length > MaxPatchFileBytes) throw new InvalidDataException("The patch file is larger than 64 MiB.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.GetString() != Format)
            throw new InvalidDataException("This is not a FileCat hex patch file.");
        long length = root.GetProperty("sourceLength").GetInt64();
        var ranges = new List<HexPatchRange>();
        long lastEnd = -1, total = 0;
        foreach (var item in root.GetProperty("ranges").EnumerateArray())
        {
            long offset = item.GetProperty("offset").GetInt64();
            var original = Convert.FromBase64String(item.GetProperty("originalBase64").GetString() ?? "");
            var replacement = Convert.FromBase64String(item.GetProperty("replacementBase64").GetString() ?? "");
            total += original.Length;
            if (original.Length == 0 || original.Length != replacement.Length || offset < lastEnd || offset < 0 ||
                offset > length - original.Length || total > HexPatchOverlay.MaxTouchedBytes)
                throw new InvalidDataException($"The patch has an invalid range at offset {offset}.");
            ranges.Add(new HexPatchRange(offset, original, replacement));
            lastEnd = offset + original.Length;
        }
        if (ranges.Count == 0) throw new InvalidDataException("The patch contains no ranges.");
        return new HexPatchFile(root.GetProperty("sourcePath").GetString() ?? "", root.GetProperty("sourceIdentity").GetString() ?? "",
            length, ranges);
    }

    /// <summary>
    /// Stages a patch in the overlay (unsaved) only if the file has the same length and every range currently holds
    /// its expected original bytes. Ranges that already hold the replacement are skipped. Returns the staged count.
    /// </summary>
    public static (int Staged, int AlreadyApplied) Apply(HexPatchFile patch, HexPatchOverlay overlay)
    {
        if (patch.SourceLength != overlay.Length)
            throw new InvalidDataException($"The patch was made for a file of {patch.SourceLength:N0} bytes; this file has {overlay.Length:N0} bytes.");
        var pending = new List<HexPatchRange>();
        int already = 0;
        foreach (var range in patch.Ranges)
        {
            var current = new byte[range.Original.Length];
            if (overlay.Read(range.Offset, current) != current.Length) throw new IOException($"The file could not be read at 0x{range.Offset:X}.");
            if (current.AsSpan().SequenceEqual(range.Replacement)) { already++; continue; }
            if (!current.AsSpan().SequenceEqual(range.Original))
                throw new InvalidDataException($"The bytes at 0x{range.Offset:X} differ from what the patch expects, so nothing was applied.");
            pending.Add(range);
        }
        const int MaxAction = 1024 * 1024;
        foreach (var range in pending)
            for (int at = 0; at < range.Replacement.Length; at += MaxAction)
                overlay.Write(range.Offset + at, range.Replacement.AsSpan(at, Math.Min(MaxAction, range.Replacement.Length - at)));
        return (pending.Count, already);
    }
}
