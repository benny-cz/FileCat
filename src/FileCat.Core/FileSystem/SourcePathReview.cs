using FileCat.Core.Resources;

namespace FileCat.Core.FileSystem;

/// <summary>
/// The root item's metadata and, where available, native identity and resolved path at review time.
/// This does not hash file content, enumerate a directory's descendants, or make a later path-based mutation atomic.
/// </summary>
public sealed record SourcePathReview(FileSystemItemInfo Info, string? Identity, string? FinalPath)
{
    /// <summary>
    /// A result row describes the item found or last listed, not whichever item later occupies its path.
    /// This inexpensive check catches ordinary changes before a review and again when a queued job starts;
    /// it is not an object-bound or content-hash guarantee against a same-metadata substitution.
    /// </summary>
    public static bool MatchesSelectionMetadata(ItemRef item, IFileSystemOperations files)
    {
        if (item.FileSystemPath is not { } path) return false;
        try
        {
            var current = files.TryGetInfo(path);
            return current is not null && current.IsDirectory == item.IsContainer &&
                (current.IsDirectory || current.Size == item.Size) && current.ModifiedUtc.Ticks == item.Modified;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    public static SourcePathReview? Capture(string path, IFileSystemOperations files, Action? check = null)
    {
        check?.Invoke(); var info = files.TryGetInfo(path); check?.Invoke();
        if (info is null) return null;
        var identity = files.GetFileIdentity(path); check?.Invoke();
        var finalPath = files.GetFinalPath(path); check?.Invoke();
        // Reject metadata that moved while the separate identity/path calls were in progress.
        var after = files.TryGetInfo(path); check?.Invoke();
        if (info != after) throw new IOException("The source changed while its path was being reviewed; select it again.");
        return new(info, identity, finalPath);
    }

    public bool Matches(string path, IFileSystemOperations files, Action? check = null) =>
        Capture(path, files, check) == this;
}
