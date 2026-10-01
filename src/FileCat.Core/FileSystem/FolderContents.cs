using System.IO.Enumeration;
using FileCat.Core.Records;

namespace FileCat.Core.FileSystem;

/// <summary>
/// What a local folder holds below it, read in one pass: its files and folders (links are counted, never followed), and
/// one fingerprint of their paths, sizes and modified times. A later reading that differs means something in it was
/// added, removed, renamed or written since, at any depth — which the folder's own time does not tell: only its direct
/// items change it, and only from one tick of the file system's clock to the next, and NTFS lists it late. A write that
/// kept a file's size and its time to the tick is not seen.
/// </summary>
public sealed record FolderContents(long Files, long Folders, long Bytes, ulong Fingerprint)
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    /// <summary>
    /// Reads what <paramref name="path"/> holds; null when any part of it cannot be read or it holds more than
    /// <paramref name="limit"/> items (a reading that leaves something out cannot vouch for it).
    /// </summary>
    public static FolderContents? Read(string path, CancellationToken ct, long limit = 1_000_000)
    {
        long files = 0, folders = 0, bytes = 0, items = 0;
        ulong sum = 0;
        var stack = new Stack<(string Path, string Relative)>();
        stack.Push((path, ""));
        try
        {
            while (stack.TryPop(out var folder))
            {
                ct.ThrowIfCancellationRequested();
                var entries = new FileSystemEnumerable<(string Name, bool Folder, bool Link, long Length, long Ticks)>(folder.Path,
                    (ref FileSystemEntry e) => (e.FileName.ToString(), e.IsDirectory, (e.Attributes & FileAttributes.ReparsePoint) != 0,
                        e.IsDirectory ? 0 : e.Length, e.LastWriteTimeUtc.UtcTicks), Options);
                foreach (var e in entries)
                {
                    if (++items > limit) return null;
                    string relative = folder.Relative.Length == 0 ? e.Name : folder.Relative + "/" + e.Name;
                    // A folder's own time is left out: it changes with what is in it, which is read anyway, and NTFS lists
                    // it late. A link counts as itself.
                    byte kind = e.Link ? (byte)2 : e.Folder ? (byte)1 : (byte)0;
                    sum += Hash(relative, kind, e.Link || e.Folder ? 0 : e.Length, e.Folder && !e.Link ? 0 : e.Ticks);
                    if (e.Folder)
                    {
                        folders++;
                        if (!e.Link) stack.Push((Path.Join(folder.Path, e.Name), relative));
                    }
                    else
                    {
                        files++;
                        if (!e.Link) bytes += e.Length;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return new FolderContents(files, folders, bytes, sum);
    }

    /// <summary>"12 files and 3 folders, 4.5 MiB"; "nothing" for an empty folder.</summary>
    public string Describe()
    {
        if (Files == 0 && Folders == 0) return "nothing";
        string folders = Folders == 1 ? "1 folder" : $"{Folders:N0} folders";
        if (Files == 0) return folders + " with no files";
        string files = Files == 1 ? "1 file" : $"{Files:N0} files";
        return (Folders == 0 ? files : $"{files} and {folders}") + ", " + (Bytes == 1 ? "1 byte" : RecordText.Short(Bytes));
    }

    /// <summary>One item's part of the fingerprint; the parts are added up, so the order a listing returns does not matter.</summary>
    private static ulong Hash(string relative, byte kind, long length, long ticks)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in relative) h = (h ^ c) * 1099511628211UL;
        h = Mix(h ^ kind);
        h = Mix(h ^ (ulong)length);
        return Mix(h ^ (ulong)ticks);
    }

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
