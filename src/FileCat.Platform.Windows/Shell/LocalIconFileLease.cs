using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Shell;

/// <summary>
/// Holds every component of a fixed-drive file path without following reparse points. Denying delete sharing keeps
/// those names bound while a path-taking Shell API reads the file; an unverified name is never sent to that API.
/// </summary>
public sealed class LocalIconFileLease : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
    internal SafeFileHandle LastHandle => _handles[^1];

    private LocalIconFileLease(string path, bool directory)
    {
        string full = Path.GetFullPath(path);
        if (!WindowsIcons.IsLocal(full)) throw new IOException("The icon is not on a fixed local drive.");
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
            if (IsReparsePoint(GetBasic(parent).FileAttributes)) throw new IOException("The icon drive is a link.");
            for (int i = 0; i < parts.Length; i++)
            {
                bool leaf = i == parts.Length - 1;
                bool file = leaf && !directory;
                var next = OpenOrThrow(parent, parts[i], (file ? GenericRead : Traverse | ReadAttributes) | Synchronize,
                    file ? ShareRead : ShareRead | ShareWrite, Open, (file ? NonDirectoryFile : DirectoryFile) | OpenReparsePoint, "Open local path");
                _handles.Add(next);
                if (IsReparsePoint(GetBasic(next).FileAttributes)) throw new IOException("The icon path contains a link.");
                parent = next;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public static LocalIconFileLease? TryOpen(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return new LocalIconFileLease(path, directory: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static LocalIconFileLease? TryOpenDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return new LocalIconFileLease(path, directory: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
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
