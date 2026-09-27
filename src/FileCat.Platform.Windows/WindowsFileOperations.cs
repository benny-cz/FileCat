using FileCat.Core.FileSystem;
using static FileCat.Platform.Windows.Native.NativeMethods;

namespace FileCat.Platform.Windows;

/// <summary>
/// Windows file mutations: native volume profiles, Recycle Bin classification, and Mark-of-the-Web
/// (<c>Zone.Identifier</c>) handling. Native copy and Shell recycle are layered on in WindowsFileOperations.Native.cs.
/// </summary>
public sealed partial class WindowsFileOperations : PortableFileOperations
{
    private const string ZoneStream = ":Zone.Identifier";

    public override unsafe VolumeInfo GetVolumeInfo(string path)
    {
        try
        {
            var root = VolumeRootOf(path);
            uint type = GetDriveType(root);
            var fsName = stackalloc char[64];
            if (!GetVolumeInformation(root, null, 0, out _, out _, out uint flags, fsName, 64))
                return VolumeInfo.Unknown(path) with { IsRemote = type == DRIVE_REMOTE || PathUtil.IsUncPath(path) };
            var fs = new string(fsName);
            bool fat = fs.StartsWith("FAT", StringComparison.OrdinalIgnoreCase) || fs.Equals("exFAT", StringComparison.OrdinalIgnoreCase);
            long free = GetDiskFreeSpaceEx(root, out var avail, out _, out _) ? (long)Math.Min(avail, long.MaxValue) : -1;
            bool remote = type == DRIVE_REMOTE || PathUtil.IsUncPath(path);
            return new VolumeInfo(
                PathUtil.GetDeviceKey(path),
                fs,
                SupportsNamedStreams: (flags & FILE_NAMED_STREAMS) != 0,
                CaseSensitive: (flags & FILE_CASE_SENSITIVE_SEARCH) != 0 && false, // per-directory case sensitivity is checked separately
                TimestampPrecision: fs.Equals("FAT", StringComparison.OrdinalIgnoreCase) || fs.Equals("FAT32", StringComparison.OrdinalIgnoreCase) ? TimeSpan.FromSeconds(2)
                    : fs.Equals("exFAT", StringComparison.OrdinalIgnoreCase) ? TimeSpan.FromMilliseconds(10)
                    : remote ? TimeSpan.FromSeconds(2) : TimeSpan.FromTicks(1),
                SupportsHardLinks: (flags & FILE_SUPPORTS_HARD_LINKS) != 0,
                SupportsSymbolicLinks: (flags & FILE_SUPPORTS_REPARSE_POINTS) != 0 && !fat,
                IsRemote: remote,
                IsRemovable: type is DRIVE_REMOVABLE or DRIVE_CDROM,
                SupportsRecycle: RecycleBinExists(path),
                FreeBytes: free);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return VolumeInfo.Unknown(path);
        }
    }

    public override string GetVolumeRoot(string path) => VolumeRootOf(path);

    /// <summary>CopyFile2 keeps attributes, the modification time, and alternate data streams itself.</summary>
    public override bool CopyPreservesMetadata => true;

    internal static unsafe string VolumeRootOf(string path)
    {
        var buf = stackalloc char[1024];
        if (GetVolumePathName(path, buf, 1024)) return new string(buf);
        return Path.GetPathRoot(path) ?? path;
    }

    /// <summary>Recycle Bin availability: fixed local volumes only (UNC and most removable media have none).</summary>
    public static bool RecycleBinExists(string path)
    {
        if (PathUtil.IsUncPath(path)) return false;
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return false;
            return GetDriveType(root) == DRIVE_FIXED;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public override RecycleClassification ClassifyRecycle(string path, long size)
    {
        var volume = RecycleVolume(path);
        if (!volume.HasBin) return RecycleClassification.NoRecycleBin;
        // The bin stores items under "$Recycle.Bin\<SID>\$R……": names that are already near MAX_PATH do not fit.
        if (path.Length > 240 && !path.StartsWith(@"\\?\", StringComparison.Ordinal)) return RecycleClassification.NameTooLong;
        // The default quota is a share of the volume; items larger than a conservative 5% are deleted permanently by the Shell.
        if (size > 0 && volume.FreeBytes > 0 && size > Math.Max(1L << 30, volume.TotalBytes / 20)) return RecycleClassification.TooLarge;
        return RecycleClassification.Recyclable;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Stamp, bool HasBin, long FreeBytes, long TotalBytes)> _recycleVolumes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-volume facts cached briefly, so classifying a million items does not cost millions of volume queries.</summary>
    private (bool HasBin, long FreeBytes, long TotalBytes) RecycleVolume(string path)
    {
        var root = Path.GetPathRoot(path) ?? path;
        long now = Environment.TickCount64;
        if (_recycleVolumes.TryGetValue(root, out var cached) && now - cached.Stamp < 10_000) return (cached.HasBin, cached.FreeBytes, cached.TotalBytes);
        bool bin = RecycleBinExists(path);
        long free = -1, total = long.MaxValue;
        if (bin && GetDiskFreeSpaceEx(root, out var available, out var totalBytes, out _))
        {
            free = (long)Math.Min(available, long.MaxValue);
            total = (long)Math.Min(totalBytes, long.MaxValue);
        }
        _recycleVolumes[root] = (now, bin, free, total);
        return (bin, free, total);
    }

    public override string? ReadOriginMark(string path)
    {
        try
        {
            var ads = path + ZoneStream;
            return File.Exists(ads) ? File.ReadAllText(ads) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public override bool WriteOriginMark(string path, string mark)
    {
        try
        {
            File.WriteAllText(path + ZoneStream, mark);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
