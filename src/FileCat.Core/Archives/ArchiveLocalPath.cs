using FileCat.Core.FileSystem;

namespace FileCat.Core.Archives;

/// <summary>
/// Unix native archive routes pin every directory component without following links. A folder that the user reached through
/// a symbolic link (macOS <c>/tmp</c> and <c>/var</c>, <c>/home</c> on Fedora Atomic desktops, a linked project folder) is
/// therefore resolved once, when a job is submitted or reviewed, to the folder it actually is. Only folders are resolved:
/// a link at the final archive or source name is never followed here; the native layer refuses it.
/// </summary>
internal static class ArchiveLocalPath
{
    /// <summary>The full path of a file whose parent folder is resolved on Unix; unchanged on Windows.</summary>
    internal static string ResolveParent(string path)
    {
        string full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) return full;
        string? directory = Path.GetDirectoryName(full), leaf = Path.GetFileName(full);
        if (directory is null || leaf.Length == 0) return full;
        return UnixFiles.RealPath(directory) is { } real ? Path.Combine(real, leaf) : full;
    }

    /// <summary>A folder resolved on Unix; unchanged on Windows. An unresolvable folder is returned as given for refusal later.</summary>
    internal static string ResolveDirectory(string directory)
    {
        string full = Path.GetFullPath(directory);
        if (OperatingSystem.IsWindows()) return full;
        return UnixFiles.RealPath(full) ?? full;
    }
}
