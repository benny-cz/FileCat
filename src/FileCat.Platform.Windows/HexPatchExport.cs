using System.Text.Json;
using FileCat.Core.Content;

namespace FileCat.Platform.Windows;

/// <summary>Portable record of exact modified ranges for review or a future explicit patch workflow.</summary>
public static class HexPatchExport
{
    public static void Export(ProtectedHexFile source, HexPatchOverlay overlay, string destination)
    {
        string full = Path.GetFullPath(destination);
        if (File.Exists(full) || Directory.Exists(full)) throw new IOException("Patch export requires a new destination name.");
        string temp = Path.Combine(Path.GetDirectoryName(full)!, ".filecat-patch-" + Guid.NewGuid().ToString("N") + ".tmp");
        var ranges = overlay.FreezeForSave();
        try
        {
            if (ranges.Count == 0) throw new IOException("There are no modified bytes to export.");
            source.ValidateForSave(ranges);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, new
                {
                    format = "FileCat fixed-length hex patch v1",
                    sourcePath = source.LocalPath,
                    sourceIdentity = source.FileIdentity.ToString(),
                    sourceLength = source.Length,
                    ranges = ranges.Select(r => new { offset = r.Offset, originalBase64 = Convert.ToBase64String(r.Original),
                        replacementBase64 = Convert.ToBase64String(r.Replacement) }).ToArray(),
                }, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            source.ValidateForSave(ranges);
            File.Move(temp, full, overwrite: false);
        }
        finally
        {
            overlay.FinishSave(false);
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
