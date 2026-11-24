using System.Security.Cryptography;
using FileCat.Core.FileSystem;

namespace FileCat.Core.State;

/// <summary>
/// Where FileCat running as root on Linux or macOS keeps what it writes while it runs (I655): job journals, temporary
/// copies, the scratch of large listings, its instance files. A folder of its own under /tmp, made for this process under a
/// name nobody can guess, private to root (0700), and removed when the process ends. Never in a profile: in the caller's
/// (HOME under sudo -E, or under sudo where its configuration keeps HOME) the ordinary FileCat could not read or replace
/// what root made there, and root takes no state from its own either. Not under TMPDIR, which sudo -E leaves as the
/// caller's: whoever owns a folder can rename what is in it and put something else in its place. /tmp is sticky.
/// </summary>
internal static class RootSession
{
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private static readonly object Gate = new();
    private static string? _folder;

    /// <summary>The process's session folder, made on first use.</summary>
    internal static string Folder()
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Only root on Linux and macOS keeps a session folder.");
        lock (Gate)
        {
            if (_folder is not null) return _folder;
            // macOS's /tmp is a link to /private/tmp: the folder is named as it is, so that nothing has to follow a link.
            string parent = OperatingSystem.IsMacOS() ? "/private/tmp" : "/tmp";
            string folder = Path.Combine(parent, "filecat-root-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
            Directory.CreateDirectory(folder, Private);
            // Used only as made: a folder, not a link, of this account's alone.
            if (UnixFiles.Stat(folder) is not { IsDirectory: true, IsLink: false } made || made.Owner != UnixPermissions.CurrentUserId ||
                (made.Mode & 0x3F) != 0)
                throw new IOException("FileCat could not make a private folder for this session in " + parent + ".");
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Remove(folder);
            return _folder = folder;
        }
    }

    private static void Remove(string folder)
    {
        // Nothing but this process writes there (0700); a link inside is removed itself, never followed.
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
