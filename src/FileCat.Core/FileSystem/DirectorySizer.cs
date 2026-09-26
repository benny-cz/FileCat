namespace FileCat.Core.FileSystem;

public readonly record struct SizeProgress(long Bytes, long Files, long Directories, int Inaccessible, bool Complete);

/// <summary>
/// Explicit per-directory size analysis (Space, plan §4.3): bounded, cancellable, never follows links,
/// and reports partial totals. Inaccessible subtrees are counted, not silently treated as empty.
/// </summary>
public static class DirectorySizer
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public static SizeProgress Compute(string root, Action<SizeProgress>? progress, CancellationToken ct, TimeSpan? reportEvery = null)
    {
        long bytes = 0, files = 0, dirs = 0;
        int inaccessible = 0;
        var every = reportEvery ?? TimeSpan.FromMilliseconds(250);
        var last = DateTime.UtcNow;
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            try
            {
                foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", Options))
                {
                    ct.ThrowIfCancellationRequested();
                    bool isLink = (info.Attributes & FileAttributes.ReparsePoint) != 0;
                    if (info is DirectoryInfo)
                    {
                        dirs++;
                        if (!isLink) stack.Push(info.FullName);
                    }
                    else
                    {
                        files++;
                        if (!isLink) bytes += ((FileInfo)info).Length;
                    }
                    if (progress is not null && DateTime.UtcNow - last > every)
                    {
                        last = DateTime.UtcNow;
                        progress(new SizeProgress(bytes, files, dirs, inaccessible, false));
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                inaccessible++;
            }
        }
        var result = new SizeProgress(bytes, files, dirs, inaccessible, true);
        progress?.Invoke(result);
        return result;
    }
}
