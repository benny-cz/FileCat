using FileCat.Core.Resources;

namespace FileCat.Core.FileSystem;

/// <summary>Information shown for a drive row.</summary>
public sealed record DriveTag(string RootPath, string? Label, string DriveType, string? Format, long FreeBytes, long TotalBytes, bool Ready, string? RemoteName = null);

/// <summary>
/// "This PC": the list of drives/volumes (mount points on Unix). Each drive is queried with its own
/// timeout so one hung network or removable drive cannot block the list (§6.3).
/// </summary>
public class ComputerProvider : ResourceProvider
{
    private static readonly TimeSpan DriveQueryTimeout = TimeSpan.FromSeconds(2);

    public override string Scheme => Schemes.Computer;

    public override string GetDisplayPath(Location location) => PathUtil.IsWindows ? "This PC" : "Computer";

    public override string GetDisplayName(Location location) => GetDisplayPath(location);

    public override Location? GetParent(Location location) => null;

    public override string GetDeviceKey(Location location) => "computer";

    public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) => capability switch
    {
        LocationCapabilities.Delete or LocationCapabilities.Recycle => "Drives are not deleted from here: open a drive (Enter) to delete what is on it.",
        LocationCapabilities.Rename => "A drive's name is its label, which its properties change (Alt+Enter).",
        LocationCapabilities.MoveSource => "Drives cannot be moved: open a drive (Enter) to move what is on it.",
        LocationCapabilities.CreateDirectory or LocationCapabilities.CreateFile => "Open a drive (Enter) to create folders and files on it.",
        LocationCapabilities.TransferTarget => "Open a drive (Enter) to copy or move items onto it.",
        _ => base.ExplainUnavailable(location, capability),
    };

    public override bool TryParse(string text, Location? current, out Location? location)
    {
        var t = text.Trim();
        location = t.Equals("This PC", StringComparison.OrdinalIgnoreCase) || t.Equals("computer:", StringComparison.OrdinalIgnoreCase)
            ? new Location(Schemes.Computer, string.Empty)
            : null;
        return location is not null;
    }

    public override async Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        var drives = GetDriveRoots();
        var tasks = drives.Select(root => Task.Run(() => QueryListed(root), ct)).ToArray();
        for (int i = 0; i < drives.Count; i++)
        {
            DriveTag tag;
            try
            {
                if (await tasks[i].WaitAsync(DriveQueryTimeout, ct).ConfigureAwait(false) is not { } answered) continue;
                tag = answered;
            }
            catch (TimeoutException)
            {
                tag = new DriveTag(drives[i], null, "Not responding", null, -1, -1, false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                tag = new DriveTag(drives[i], null, "Unavailable", null, -1, -1, false);
            }
            var name = PathUtil.IsWindows ? drives[i].TrimEnd('\\') : drives[i];
            var e = new EntryData(name, EntryKind.Drive, tag.Ready ? tag.TotalBytes : -1)
            {
                Tag = tag,
                Flags = tag.Ready ? EntryFlags.None : EntryFlags.Unavailable,
            };
            sink.AddBatch([e]);
        }
    }

    /// <summary>
    /// The drives this list shows, each as it describes itself within <paramref name="budget"/>: a drive that takes
    /// longer (a dropped network share) is listed as not responding rather than holding up the caller.
    /// </summary>
    public async Task<IReadOnlyList<DriveTag>> QueryDrivesAsync(TimeSpan budget, CancellationToken ct)
    {
        var roots = GetDriveRoots();
        var tasks = roots.Select(root => Task.Run(() => QueryListed(root), ct)).ToArray();
        try
        {
            await Task.WhenAll(tasks).WaitAsync(budget, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Past the budget, or a drive failed: each one's own outcome is read below.
        }
        var tags = new List<DriveTag>(roots.Count);
        for (int i = 0; i < roots.Count; i++)
        {
            var task = tasks[i];
            if (task.IsCompletedSuccessfully)
            {
                if (task.Result is { } tag) tags.Add(tag);
            }
            else tags.Add(new DriveTag(roots[i], null, task.IsCompleted ? "Unavailable" : "Not responding", null, -1, -1, false));
        }
        return tags;
    }

    /// <summary>
    /// A drive's description, or null for a Unix mount point that is not one to list: not a folder (a file bound over
    /// another), or memory-backed scratch space (tmpfs). Runs in its own task: statfs can hang on a dead network mount.
    /// </summary>
    private DriveTag? QueryListed(string root)
    {
        if (PathUtil.IsWindows) return QueryDrive(root);
        if (!Directory.Exists(root)) return null;
        var tag = QueryDrive(root);
        return tag.DriveType == nameof(DriveType.Ram) && root != "/" ? null : tag;
    }

    protected virtual IReadOnlyList<string> GetDriveRoots()
    {
        try
        {
            var roots = DriveInfo.GetDrives().Select(d => d.Name);
            if (!PathUtil.IsWindows)
            {
                // Hide pseudo file systems on Unix; keep "/" and real mounts.
                roots = roots.Where(r => r == "/" || r.StartsWith("/media", StringComparison.Ordinal) ||
                                         r.StartsWith("/mnt", StringComparison.Ordinal) || r.StartsWith("/Volumes", StringComparison.Ordinal) ||
                                         r.StartsWith("/home", StringComparison.Ordinal));
            }
            return roots.ToList();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    protected virtual DriveTag QueryDrive(string root)
    {
        var d = new DriveInfo(root);
        var type = d.DriveType.ToString();
        if (!d.IsReady) return new DriveTag(root, null, type, null, -1, -1, false);
        string? label = null, format = null;
        long free = -1, total = -1;
        try { label = d.VolumeLabel; } catch (Exception) { }
        try { format = d.DriveFormat; } catch (Exception) { }
        try { free = d.AvailableFreeSpace; total = d.TotalSize; } catch (Exception) { }
        return new DriveTag(root, label, type, format, free, total, true);
    }

    public override Location? GetChildLocation(Location parent, in EntryData entry)
    {
        if (entry.Kind != EntryKind.Drive || entry.Has(EntryFlags.Unavailable)) return null;
        var root = entry.Tag is DriveTag t ? t.RootPath : entry.Name + Path.DirectorySeparatorChar;
        return Location.FileSystem(root);
    }

    public override string? GetNameInParent(Location location) => null;
}
