using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Edit;

/// <summary>
/// Risk R21: removal of FileCat's own temporary edit objects without trusting a bare pathname.
/// <list type="bullet">
/// <item>Windows: through a handle bound to the recorded identity (<see cref="IFileSystemOperations.DeleteOwnedFile"/>) and,
/// for a folder, through verified directory handles (<see cref="IFileSystemOperations.DeleteTreeBound"/>).</item>
/// <item>Unix has no conditional unlink of a held inode. A file is removed only in a folder only this user can change
/// (owned, not a link, no group or other write) while its name still binds the recorded identity (the argument of
/// UNIX_PRIVATE_CLEANUP_DECISION.md).</item>
/// <item>Anything else is kept, and its path is logged (hashed, I643) and returned. An adapter without identities or bound
/// operations (the portable adapter on Windows, used by tests) keeps the earlier removal only inside FileCat's own data
/// folders.</item>
/// </list>
/// </summary>
internal static class EditPrivateCleanup
{
    /// <summary>Null when the file was removed or is already gone; otherwise why it was kept (with its path).</summary>
    internal static string? DeleteOwnedFile(IFileSystemOperations files, string path, string? identity, bool inFileCatData = true,
        bool requireClearEfs = false)
    {
        try
        {
            if (!Present(path)) return null;
            if (identity is not null)
            {
                var bound = requireClearEfs ? files.DeleteClearOwnedFile(path, identity) : files.DeleteOwnedFile(path, identity);
                if (bound is not null) return bound.Deleted ? null : Kept(path, bound.Reason);
            }
            if (requireClearEfs) return Kept(path, "its clear protection state or exact identity could not be held for cleanup");
            if (OperatingSystem.IsWindows())
            {
                if (identity is null && inFileCatData) { File.Delete(path); return null; }
                return Kept(path, "no handle-bound deletion is available here");
            }
            if (identity is null) return Kept(path, "its identity was never recorded");
            if (!IsPrivateFolder(Path.GetDirectoryName(path)!)) return Kept(path, "it is outside a folder only FileCat can change");
            if (files.GetFileIdentity(path) != identity) return Kept(path, "another object now has its name");
            File.Delete(path);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return Kept(path, error.Message); }
    }

    /// <summary>
    /// A folder FileCat created for one operation, with whatever that operation left in it (a partial copy, say). Null when
    /// removed or already gone.
    /// </summary>
    internal static string? DeleteOwnedFolder(IFileSystemOperations files, string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return null;
            if (files.DeleteTreeBound(folder, () => { }) is { } bound)
                return bound.Failed == 0 ? null : Kept(folder, string.Join("; ", bound.Problems));
            // No verified-handle tree deletion: a folder only this user can change (Unix), or FileCat's own data folder
            // under an adapter without bound operations (Windows tests). .NET removes links found inside, never their targets.
            if (OperatingSystem.IsWindows() || IsPrivateFolder(folder)) { Directory.Delete(folder, recursive: true); return null; }
            return Kept(folder, "it is not a folder only FileCat can change");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return Kept(folder, error.Message); }
    }

    /// <summary>Removes an empty private folder FileCat created; a folder with anything left in it stays.</summary>
    internal static void RemoveEmptyFolder(string folder)
    {
        try { if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { AppLog.Warn("FileCat kept its temporary folder " + AppLog.P(folder) + ": " + error.Message); }
    }

    /// <summary>
    /// A folder only this user can change: owned by this user, not a link, and without group or other write permission (so
    /// no other user can rename, replace or add its entries). Readable folders qualify; the threat is a changed name.
    /// </summary>
    internal static bool IsPrivateFolder(string folder)
    {
        if (OperatingSystem.IsWindows()) return false;
        // Group write is accepted only for a user private group (Debian/Ubuntu/Fedora 002 umask), as for archive folders.
        return UnixFiles.Stat(folder) is { IsDirectory: true, IsLink: false } stat &&
            stat.Owner == Archives.Unix.UnixArchiveNative.GetEuid() && (stat.Mode & 0x02 /* other write */) == 0 &&
            ((stat.Mode & 0x10 /* group write */) == 0 || Archives.Unix.UnixArchiveNative.IsUserPrivateGroupFolder(folder));
    }

    private static bool Present(string path) => File.Exists(path) || new FileInfo(path).LinkTarget is not null;

    /// <summary>Why a path was kept, for the user; the log has it with the path hashed (I643).</summary>
    private static string Kept(string path, string? reason)
    {
        string Text(string shown) => "FileCat kept its temporary edit file " + shown + (string.IsNullOrEmpty(reason) ? "." : ": " + reason + ".");
        AppLog.Warn(Text(AppLog.P(path)));
        return Text(path);
    }
}
