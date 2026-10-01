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

    /// <summary>
    /// How precisely the listing states <see cref="ModifiedUtc"/>: zero when exact; a minute or a day for an FTP server's
    /// LIST ("Oct 01 05:09", "Mar 04  2021"), where the time is the start of that minute or day (release issue I45).
    /// </summary>
    TimeSpan ModifiedPrecision => TimeSpan.Zero;

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

    /// <summary>
    /// The same, for a file the caller has just stat'ed at <paramref name="length"/> bytes: a channel that would otherwise
    /// ask the server for the length again (FTP) does not.
    /// </summary>
    Stream OpenRead(string path, long length) => OpenRead(path);

    /// <summary>Creates a new file; fails when the name exists.</summary>
    Stream CreateNew(string path);

    /// <summary>
    /// Writes <paramref name="input"/>, from its position to its end, into a new file (failing when the name exists), with
    /// as many requests in flight as the protocol allows: SFTP's one-at-a-time stream writes managed 0.29 MB/s at a 100 ms
    /// round trip (release issue I39). Reading <paramref name="input"/> may block or throw, to pace, pause or cancel.
    /// </summary>
    void UploadNew(Stream input, string path)
    {
        using var output = CreateNew(path);
        input.CopyTo(output, 256 * 1024);
    }

    /// <summary>
    /// Whether writing at an offset (<see cref="OpenWriteAt"/>) sends one request at a time, so continuing an upload is
    /// slower than <see cref="UploadNew"/> on a slow link (SFTP through SSH.NET).
    /// </summary>
    bool AppendsOneRequestAtATime => false;

    /// <summary>
    /// Whether the server renames a link itself when asked to rename it. ProFTPD's SFTP renames (and moves) the link's
    /// target instead, wherever it is (release issue I46); FTP servers and OpenSSH rename the link.
    /// </summary>
    bool RenamesLinksThemselves => true;

    /// <summary>
    /// Continues writing a file this job created, from <paramref name="offset"/> (an interrupted upload). Throws
    /// <see cref="NotSupportedException"/> where the server cannot, and the upload then starts again.
    /// </summary>
    Stream OpenWriteAt(string path, long offset);

    void CreateDirectory(string path);

    /// <summary>Renames a file this job created (never a link) to a name that does not exist.</summary>
    void Rename(string source, string target);

    /// <summary>
    /// Atomically replaces <paramref name="target"/> with <paramref name="source"/> (posix-rename@openssh.com). Returns
    /// false, changing nothing, when the server lacks the extension. The target must not be a link.
    /// </summary>
    bool TryReplace(string source, string target);

    /// <summary>
    /// Asks the server to give a file this modified time. A server that refuses or cannot is not an error (the content
    /// arrived); whether the time held shows in a <see cref="Stat"/> afterwards, which is how jobs report it (I43).
    /// </summary>
    void SetModified(string path, DateTime utc);
}

/// <summary>The connection broke; the work can continue after reconnecting only where it is provably safe.</summary>
public sealed class RemoteDisconnectedException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>FileCat will not do this on this server, whatever is tried again: the item fails with the reason.</summary>
public sealed class RefusedOperationException(string message) : IOException(message);

internal static class RemoteErrorText
{
    /// <summary>
    /// What a library failure says for the user: its own words, or, where those only point further down (.NET's TLS
    /// stream says "The read operation failed, see inner exception." when the server drops an FTPS connection), the
    /// first inner failure that says what happened.
    /// </summary>
    public static string Reason(Exception ex)
    {
        while (ex.InnerException is { } inner && ex.Message.Contains("inner exception", StringComparison.OrdinalIgnoreCase)) ex = inner;
        return ex.Message;
    }
}

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

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or inside it (ordinal: servers are case-sensitive).</summary>
    public static bool IsSameOrUnder(string path, string folder)
    {
        string f = folder.TrimEnd('/');
        return path == f || path.StartsWith(f + "/", StringComparison.Ordinal) || f.Length == 0;
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
