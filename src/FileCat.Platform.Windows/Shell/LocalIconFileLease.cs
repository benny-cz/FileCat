using FileCat.Core.FileSystem;
using FileCat.Core.State;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Shell;

/// <summary>What a lease does with a link on its path. Reparse points that are no links (Cloud Files placeholders such as
/// OneDrive's, deduplicated or compressed files) are files and folders like any other.</summary>
public enum LeaseLinks
{
    /// <summary>Refuses any symbolic link, junction or mount point.</summary>
    Refuse,

    /// <summary>Follows junctions and mount points, which lead only to local folders; refuses symbolic links, which can
    /// lead to a network share.</summary>
    FollowLocal,

    /// <summary>Follows every link, as any program does.</summary>
    Follow,
}

/// <summary>
/// Holds every component of a fixed-drive file path. Denying delete sharing keeps those names bound while a path-taking
/// Shell API reads the file; an unverified name is never sent to that API. A link on the way is refused or followed as
/// <see cref="LeaseLinks"/> says, up to the folder the user chose; below it, and as the file itself, one is always
/// refused (<see cref="PortableFileOperations.FollowableComponents"/>). Where one is followed, the names held from there
/// on are those of its target.
/// </summary>
public sealed class LocalIconFileLease : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
    internal SafeFileHandle LastHandle => _handles[^1];

    /// <summary>
    /// The links icons and thumbnails go through. Junctions and mount points are followed, since they lead only to local
    /// folders; a symbolic link, which can lead to a network share that would see the user's sign-in, never is. An
    /// elevated FileCat follows none, so that a standard user's link cannot redirect what it reads.
    /// </summary>
    public static LeaseLinks ForIcons => WindowsTokenAuthority.IsHighIntegrity ? LeaseLinks.Refuse : LeaseLinks.FollowLocal;

    /// <summary>
    /// The links a copy, an extraction or an addition to an archive goes through. Without elevation every link is
    /// followed, as by any program: whoever could place one could do as much with the user's own rights. An elevated
    /// FileCat follows none, so that a standard user's link cannot redirect its writes (AC-20).
    /// </summary>
    public static LeaseLinks ForTransfers => WindowsTokenAuthority.IsHighIntegrity ? LeaseLinks.Refuse : LeaseLinks.Follow;

    private LocalIconFileLease(string path, bool directory, LeaseLinks links, bool removable = false, string? chosen = null)
    {
        string full = Path.GetFullPath(path);
        int followable = PortableFileOperations.FollowableComponents(full, chosen, file: !directory);
        if (!(removable ? WindowsFileOperations.IsReplaceVolume(full) : WindowsIcons.IsLocal(full)))
            throw new IOException("The icon is not on a fixed local drive.");
        string root = Path.GetPathRoot(full) ?? throw new IOException("The icon path has no drive root.");
        if (root.Length != 3 || root[1] != ':' || root[2] != '\\')
            throw new IOException("The icon path has no local drive root.");
        string[] parts = full[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if ((!directory && parts.Length == 0) || parts.Any(p => p is "." or ".." || p.Contains(':')))
            throw new IOException("The icon path has an invalid component.");

        try
        {
            var parent = OpenOrThrow(null, @"\??\" + root, Traverse | ReadAttributes | Synchronize,
                ShareRead | ShareWrite, Open, DirectoryFile | OpenReparsePoint, "Open icon drive");
            _handles.Add(parent);
            if (IsLink(parent)) throw new IOException("The icon drive is a link.");
            for (int i = 0; i < parts.Length; i++)
            {
                bool leaf = i == parts.Length - 1;
                bool file = leaf && !directory;
                uint access = (file ? GenericRead : Traverse | ReadAttributes) | Synchronize;
                uint share = file ? ShareRead : ShareRead | ShareWrite;
                uint kind = file ? NonDirectoryFile : DirectoryFile;
                var next = OpenOrThrow(parent, parts[i], access, share, Open, kind | OpenReparsePoint, "Open local path");
                uint tag;
                try { tag = GetReparseTag(next); }
                catch { next.Dispose(); throw; }
                if (IsLinkTag(tag))
                {
                    next.Dispose();
                    if (i >= followable || links == LeaseLinks.Refuse || (links == LeaseLinks.FollowLocal && tag != MountPointTag))
                        throw new IOException("The icon path contains a link.");
                    next = OpenOrThrow(parent, parts[i], access, share, Open, kind, "Open local path");
                }
                _handles.Add(next);
                parent = next;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>A file, for its icon or thumbnail (<see cref="ForIcons"/>).</summary>
    public static LocalIconFileLease? TryOpen(string path) => TryOpen(path, ForIcons);

    /// <param name="chosen">The folder the user chose to read from (a folder added to an archive): a link below it is
    /// refused. By default, the file's own folder.</param>
    public static LocalIconFileLease? TryOpen(string path, LeaseLinks links, string? chosen = null)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return new LocalIconFileLease(path, directory: false, links, chosen: chosen); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>A folder, refusing any link on the way (for reviewed replacement and deletion).</summary>
    public static LocalIconFileLease? TryOpenDirectory(string path) => TryOpenDirectory(path, LeaseLinks.Refuse);

    /// <param name="removable">Also on a removable drive (a USB stick, an SD card), where reviewed replacement and
    /// compared deletion work as on a fixed one; icons and thumbnails are read only from fixed drives.</param>
    /// <param name="chosen">The folder the user chose to write to: a link below it is refused. By default, this folder.</param>
    public static LocalIconFileLease? TryOpenDirectory(string path, LeaseLinks links, bool removable = false, string? chosen = null)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return new LocalIconFileLease(path, directory: true, links, removable, chosen); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        for (int i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose();
        _handles.Clear();
    }
}
