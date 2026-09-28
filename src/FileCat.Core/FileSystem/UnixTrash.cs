using System.Text;

namespace FileCat.Core.FileSystem;

/// <summary>
/// The trash on Linux (freedesktop.org Trash specification 1.0) and macOS. An item goes to a trash on its own volume, so
/// it is renamed there and never copied: the home trash for the home volume; elsewhere, on Linux, <c>$topdir/.Trash/$uid</c>
/// when the administrator made a shared <c>.Trash</c> (sticky, not a link), otherwise <c>$topdir/.Trash-$uid</c>, and on
/// macOS the volume's <c>.Trashes/$uid</c>. A trash directory made by someone else, or replaced by a link, is never used:
/// it could hand the item to another user.
/// </summary>
public static partial class UnixTrash
{
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Where a trash lives: its root, where items go, and (Linux) where their .trashinfo files go.</summary>
    public sealed record Location(string Root, string Files, string? Info, string? TopDirectory);

    /// <summary>
    /// The trash for a path without creating anything (cheap enough for capability checks); null where there is none,
    /// such as a read-only volume or a volume whose trash belongs to someone else.
    /// </summary>
    public static Location? For(string path)
    {
        if (OperatingSystem.IsWindows()) return null;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) return null;
        string full = Path.GetFullPath(path);
        string volume = PortableFileOperations.UnixVolumeRoot(full);
        if (OperatingSystem.IsMacOS())
        {
            var homeTrash = Path.Combine(home, ".Trash");
            if (volume == PortableFileOperations.UnixVolumeRoot(home)) return new Location(homeTrash, homeTrash, null, null);
            // Finder makes .Trashes on a volume; FileCat uses it once it exists.
            var trashes = Path.Combine(volume, ".Trashes");
            if (!IsRealDirectory(trashes)) return null;
            var mine = Path.Combine(trashes, UserId);
            return Usable(mine, mustExist: false) ? new Location(mine, mine, null, volume) : null;
        }
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".local", "share");
        if (volume == PortableFileOperations.UnixVolumeRoot(data)) return Linux(Path.Combine(data, "Trash"), null);
        // Another volume: the administrator's shared .Trash (sticky, not a link) first, then the user's own .Trash-$uid.
        var shared = Path.Combine(volume, ".Trash");
        if (IsRealDirectory(shared) && UnixPermissions.Stat(shared) is { } s && (s.Mode & UnixFileMode.StickyBit) != 0)
        {
            var mine = Path.Combine(shared, UserId);
            if (Usable(mine, mustExist: false) && (Directory.Exists(mine) || Writable(shared))) return Linux(mine, volume);
        }
        var own = Path.Combine(volume, ".Trash-" + UserId);
        if (Present(own) ? Usable(own, mustExist: true) : Writable(volume)) return Linux(own, volume);
        return null;

        static Location Linux(string root, string? top) => new(root, Path.Combine(root, "files"), Path.Combine(root, "info"), top);
    }

    /// <summary>
    /// Moves an item into its trash and returns where it went. On Linux the .trashinfo file is written first, and its
    /// exclusive creation reserves the name, as the specification asks.
    /// </summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public static string Put(string path, Location trash)
    {
        Prepare(trash);
        string full = Path.GetFullPath(path).TrimEnd('/');
        bool isDirectory = Directory.Exists(full) && new DirectoryInfo(full).LinkTarget is null;
        string baseName = Path.GetFileName(full);
        for (int attempt = 0; ; attempt++)
        {
            string name = attempt == 0 ? baseName : PathUtil.MakeUniqueName(baseName,
                n => File.Exists(Path.Combine(trash.Files, n)) || Directory.Exists(Path.Combine(trash.Files, n)) ||
                     trash.Info is not null && File.Exists(Path.Combine(trash.Info, n + ".trashinfo")), isDirectory);
            var destination = Path.Combine(trash.Files, name);
            if (Present(destination))
            {
                if (attempt > 100) throw new IOException("The trash holds too many items with this name.");
                continue;
            }
            string? info = null;
            if (trash.Info is not null)
            {
                info = Path.Combine(trash.Info, name + ".trashinfo");
                try
                {
                    using var stream = new FileStream(info, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
                    // Paths in a volume's own trash are relative to the volume, so they stay right wherever it is mounted.
                    string recorded = trash.TopDirectory is { } top ? Path.GetRelativePath(top, full) : full;
                    var text = "[Trash Info]\nPath=" + Escape(recorded) + "\nDeletionDate=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "\n";
                    stream.Write(Encoding.UTF8.GetBytes(text));
                }
                catch (IOException) when (File.Exists(info) && attempt <= 100)
                {
                    continue; // another program took this name first
                }
            }
            try
            {
                if (isDirectory) Directory.Move(full, destination);
                else File.Move(full, destination);
                return destination;
            }
            catch
            {
                if (info is not null) TryDelete(info);
                throw;
            }
        }
    }

    /// <summary>The .trashinfo file that belongs to an item in a Linux trash; null on macOS.</summary>
    public static string? InfoFileFor(string trashedPath)
    {
        if (!OperatingSystem.IsLinux()) return null;
        var files = Path.GetDirectoryName(trashedPath);
        if (files is null || Path.GetFileName(files) != "files") return null;
        return Path.Combine(Path.GetDirectoryName(files)!, "info", Path.GetFileName(trashedPath) + ".trashinfo");
    }

    /// <summary>The URL escaping the specification asks for, keeping slashes.</summary>
    internal static string Escape(string path)
    {
        var sb = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(path))
        {
            char c = (char)b;
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' || "/-_.~!*'()".Contains(c)) sb.Append(c);
            else sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void Prepare(Location trash)
    {
        // Private from the start: other users must not even see the names of what you delete.
        if (!Directory.Exists(trash.Root)) Directory.CreateDirectory(trash.Root, Private);
        if (!Usable(trash.Root, mustExist: true)) throw new IOException("The trash folder belongs to someone else or is a link, so FileCat does not use it.");
        if (!Directory.Exists(trash.Files)) Directory.CreateDirectory(trash.Files, Private);
        if (trash.Info is not null && !Directory.Exists(trash.Info)) Directory.CreateDirectory(trash.Info, Private);
    }

    private static string UserId => UnixPermissions.CurrentUserId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Anything at all by this name, a dangling link included.</summary>
    private static bool Present(string path) => Directory.Exists(path) || File.Exists(path) || new FileInfo(path).LinkTarget is not null;

    /// <summary>A directory (not a link) owned by this user; with <paramref name="mustExist"/> false, a missing one is fine.</summary>
    private static bool Usable(string path, bool mustExist)
    {
        if (!Present(path)) return !mustExist;
        return IsRealDirectory(path) && UnixPermissions.Stat(path) is { } s && s.Uid == UnixPermissions.CurrentUserId;
    }

    private static bool IsRealDirectory(string path) => Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is null;

    private static bool Writable(string directory) => UnixPermissions.CanWrite(directory);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
