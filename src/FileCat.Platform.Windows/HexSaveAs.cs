using FileCat.Core.Content;

namespace FileCat.Platform.Windows;

/// <summary>Write a new file from a protected baseline plus sparse edits; never overwrite a target.</summary>
public static class HexSaveAs
{
    public static void CreateNew(ProtectedHexFile source, HexPatchOverlay overlay, string destination, CancellationToken ct = default)
    {
        string full = Path.GetFullPath(destination);
        if (File.Exists(full) || Directory.Exists(full)) throw new IOException("Save As requires a new destination name.");
        var ranges = overlay.FreezeForSave();
        string temp = Path.Combine(Path.GetDirectoryName(full)!, ".filecat-hex-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            source.ValidateForSave(ranges);
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                var buffer = new byte[1024 * 1024];
                for (long offset = 0; offset < source.Length;)
                {
                    ct.ThrowIfCancellationRequested();
                    int need = (int)Math.Min(buffer.Length, source.Length - offset);
                    int read = overlay.Read(offset, buffer.AsSpan(0, need));
                    if (read != need) throw new IOException($"Could not read the protected source at 0x{offset:X}.");
                    output.Write(buffer, 0, read);
                    offset += read;
                }
                output.Flush(flushToDisk: true);
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
