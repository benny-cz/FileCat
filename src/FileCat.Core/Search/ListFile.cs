using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Search;

/// <summary>
/// A list file (plan §19.1, Total Commander's LOADLIST; FileCat's --list): paths, one per line, in UTF-8 or the encoding
/// its byte order mark names, opened as a result set. A relative path is taken from the list's own folder. Lines that
/// name nothing are counted; network paths are counted and left out, never contacted on the list's behalf (a list can
/// come from anywhere, and asking a server whether a path exists hands it the user's sign-in; release issue I16's rule).
/// </summary>
public static class ListFile
{
    /// <summary>The largest list read (a million paths fit several times over).</summary>
    public const long MaxBytes = 64L << 20;

    /// <summary>The most members one list adds.</summary>
    public const int MaxItems = 500_000;

    /// <summary>What a list gave: members added, lines naming nothing, network paths left out, and whether it went on past <see cref="MaxItems"/>.</summary>
    public sealed record Outcome(int Added, int Missing, int Network, bool Truncated);

    public static Outcome Load(string path, ResultSet set, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("The list file does not exist.", path);
        if (info.Length > MaxBytes) throw new IOException($"The list file is larger than {MaxBytes >> 20} MiB.");
        string folder = Path.GetDirectoryName(info.FullName)!;
        int added = 0, missing = 0, network = 0;
        bool truncated = false;
        var batch = new List<(ItemRef Item, string Relative)>(1024);
        using (var reader = new StreamReader(info.FullName, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
        {
            for (string? line; (line = reader.ReadLine()) is not null;)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (IsNetwork(line))
                {
                    network++;
                    continue;
                }
                string full;
                try { full = Path.GetFullPath(line, folder); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    missing++;
                    continue;
                }
                // A relative line in a list kept on a share is a network path too.
                if (IsNetwork(full))
                {
                    network++;
                    continue;
                }
                EntryKind? kind = Directory.Exists(full) ? EntryKind.Directory : File.Exists(full) ? EntryKind.File : null;
                if (kind is null)
                {
                    missing++;
                    continue;
                }
                if (added + batch.Count >= MaxItems)
                {
                    truncated = true;
                    break;
                }
                batch.Add((ItemRef.ForFileSystemPath(full, kind.Value), ""));
                if (batch.Count == batch.Capacity)
                {
                    added += set.AddRange(batch);
                    batch.Clear();
                }
            }
        }
        added += set.AddRange(batch);
        return new Outcome(added, missing, network, truncated);
    }

    private static bool IsNetwork(string path) =>
        PathUtil.IsUncPath(path) || path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) || path.StartsWith("//?/UNC/", StringComparison.OrdinalIgnoreCase);
}
