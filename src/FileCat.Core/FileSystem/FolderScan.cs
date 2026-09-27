namespace FileCat.Core.FileSystem;

/// <summary>
/// A bounded folder scan for "Find folder" (plan §11): breadth first, so near folders come first when it stops at
/// its limit; hidden and system folders are skipped as in listings; links are listed but never entered (no cycles).
/// </summary>
public static class FolderScan
{
    public const int DefaultLimit = 50_000;

    public sealed record Result(List<string> Folders, bool Stopped, int Unreadable);

    public static Result Run(string root, int limit, TimeSpan budget, CancellationToken ct = default)
    {
        var folders = new List<string>();
        int unreadable = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var pending = new Queue<string>();
        pending.Enqueue(root);
        var options = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        while (pending.TryDequeue(out var dir))
        {
            try
            {
                foreach (var child in new DirectoryInfo(dir).EnumerateDirectories("*", options))
                {
                    folders.Add(child.FullName);
                    if ((child.Attributes & FileAttributes.ReparsePoint) == 0) pending.Enqueue(child.FullName);
                    if (folders.Count >= limit || clock.Elapsed > budget || ct.IsCancellationRequested) return new Result(folders, true, unreadable);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                unreadable++;
            }
        }
        return new Result(folders, false, unreadable);
    }
}
