namespace FileCat.Core.FileSystem;

/// <summary>
/// The root item's metadata and, where available, native identity and resolved path at review time.
/// This does not hash file content, enumerate a directory's descendants, or make a later path-based mutation atomic.
/// </summary>
public sealed record SourcePathReview(FileSystemItemInfo Info, string? Identity, string? FinalPath)
{
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
