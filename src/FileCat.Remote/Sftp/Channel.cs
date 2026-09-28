namespace FileCat.Remote.Sftp;

/// <summary>
/// An item exactly as its folder's listing reports it (lstat): a link is the link itself, not its target. Deleting or
/// renaming through an entry never follows a link, which is why FileCat changes remote items only through entries
/// from a fresh listing of their folder.
/// </summary>
public interface IRemoteEntry
{
    string Name { get; }
    string FullPath { get; }
    bool IsDirectory { get; }
    bool IsLink { get; }
    long Size { get; }
    DateTime ModifiedUtc { get; }

    /// <summary>Removes exactly this entry: a file, a link (never its target), or an empty folder.</summary>
    void Delete();

    /// <summary>Renames exactly this entry to <paramref name="newPath"/>; fails when that name exists.</summary>
    void MoveTo(string newPath);
}

/// <summary>Attributes of an item with links followed (what reading its content would see).</summary>
public readonly record struct RemoteStat(bool IsDirectory, bool IsLink, long Size, DateTime ModifiedUtc);

/// <summary>
/// What FileCat asks of one SFTP connection: SSH.NET in the product, an in-memory server in tests. Paths are absolute
/// POSIX paths. Every failure surfaces as <see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>, or
/// <see cref="FileNotFoundException"/>, so jobs treat remote errors like local ones.
/// </summary>
public interface ISftpChannel : IDisposable
{
    bool IsConnected { get; }

    /// <summary>The folder the server starts in (usually the user's home folder).</summary>
    string HomeDirectory { get; }

    /// <summary>Entries of a folder, without "." and "..".</summary>
    IReadOnlyList<IRemoteEntry> List(string path, CancellationToken ct);

    /// <summary>Attributes with links followed; null when nothing is there (a dangling link reports itself).</summary>
    RemoteStat? Stat(string path);

    /// <summary>Seekable read-only content (links followed).</summary>
    Stream OpenRead(string path);

    /// <summary>Creates a new file; fails when the name exists.</summary>
    Stream CreateNew(string path);

    void CreateDirectory(string path);

    /// <summary>Renames a file this job created (never a link) to a name that does not exist.</summary>
    void Rename(string source, string target);

    /// <summary>
    /// Atomically replaces <paramref name="target"/> with <paramref name="source"/> (posix-rename@openssh.com). Returns
    /// false, changing nothing, when the server lacks the extension. The target must not be a link.
    /// </summary>
    bool TryReplace(string source, string target);

    void SetModified(string path, DateTime utc);
}

/// <summary>The connection broke; the work can continue after reconnecting only where it is provably safe.</summary>
public sealed class RemoteDisconnectedException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>Remote paths are POSIX: '/' separates, names are raw, and nothing is case-folded.</summary>
public static class RemotePath
{
    public static string Combine(string folder, string name) => folder.EndsWith('/') ? folder + name : folder + "/" + name;

    /// <summary>The parent folder, or null at the root.</summary>
    public static string? Parent(string path)
    {
        if (path == "/" || path.Length == 0) return null;
        string trimmed = path.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        return slash <= 0 ? "/" : trimmed[..slash];
    }

    public static string Name(string path)
    {
        string trimmed = path.TrimEnd('/');
        return trimmed[(trimmed.LastIndexOf('/') + 1)..];
    }

    /// <summary>Why a name a server reported cannot be used as one path part, or null.</summary>
    public static string? ProblemWithName(string name) =>
        name.Length == 0 ? "The server reported an empty name."
        : name is "." or ".." ? "The server reported a relative name."
        : name.Contains('/') || name.Contains('\0') ? "The server reported a name with a slash or NUL."
        : null;
}
