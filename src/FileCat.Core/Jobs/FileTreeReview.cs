using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Jobs;

/// <summary>
/// A finite ordinary-file content and descendant-membership review. Available metadata, identity and resolved
/// paths are retained for every item. This does not hold native mutation handles or freeze a filesystem snapshot.
/// When links are allowed, only their literal item/link metadata is reviewed; target content is not followed.
/// </summary>
public sealed record FileTreeReview(string Root, IReadOnlyList<FileTreeReview.Item> Items, bool AllowLinks, bool BindByIdentity = false)
{
    public sealed record Item(string Path, SourcePathReview Version, JournalRecovery.StagedFileReview? Content);
    public long Bytes => Items.Sum(i => i.Content?.Length ?? 0);

    /// <param name="bindByIdentity">For a destination about to be replaced: a file beyond the remaining byte budget, or
    /// one a cloud provider keeps (a reparse point that is no link, whose bytes would have to be downloaded), is reviewed
    /// by its native identity and metadata alone, without content. Replacement then checks those through the handle it
    /// holds, so a different or rewritten file is still never destroyed.</param>
    /// <param name="spend">Told what the capture read, also when it gave up: what a budget is charged for it.</param>
    public static FileTreeReview? Capture(string root, IFileSystemOperations files, ResourceProvider provider,
        Action? check = null, long byteLimit = JournalRecovery.StagedReviewByteLimit, int itemLimit = 1000, bool allowLinks = false,
        bool bindByIdentity = false, Spend? spend = null)
        => Capture(root, files, provider, check, byteLimit, itemLimit, allowLinks, bindByIdentity, expected: null, spend);

    /// <summary>The items a capture looked at and the bytes it read, also when it gave up (review finding F-M2 of f134ba37).</summary>
    public sealed class Spend
    {
        public int Items { get; internal set; }
        public long Bytes { get; internal set; }
    }

    private static FileTreeReview? Capture(string root, IFileSystemOperations files, ResourceProvider provider,
        Action? check, long byteLimit, int itemLimit, bool allowLinks, bool bindByIdentity, IReadOnlyDictionary<string, Item>? expected,
        Spend? spend = null)
    {
        if (itemLimit <= 0 || byteLimit < 0) return null;
        try
        {
            var items = new List<Item>(); var pending = new Stack<(string Path, int Depth)>(); pending.Push((root, 0));
            long remaining = byteLimit;
            while (pending.TryPop(out var next))
            {
                check?.Invoke(); if (items.Count == itemLimit || next.Depth > 128) return null;
                var version = SourcePathReview.Capture(next.Path, files, check);
                if (spend is not null) spend.Items++;
                if (version is null) return null;
                // Do not open content belonging to a changed identity/version or a newly discovered descendant.
                // Complete hashes are compared afterward, but known path changes must stop before that read.
                if (expected is not null && (!expected.TryGetValue(next.Path, out var approved) || approved.Version != version)) return null;
                bool reparse = (version.Info.Attributes & FileAttributes.ReparsePoint) != 0;
                bool link = version.Info.IsLink || (reparse && !bindByIdentity);
                if (link && !allowLinks) return null;
                JournalRecovery.StagedFileReview? content = null;
                if (version.Info.IsDirectory && !link)
                {
                    foreach (var path in Directory.EnumerateFileSystemEntries(next.Path))
                    {
                        check?.Invoke(); if (items.Count + pending.Count + 1 >= itemLimit) return null;
                        pending.Push((path, next.Depth + 1));
                    }
                }
                else if (!link && !(bindByIdentity && version.Identity is not null && (reparse || version.Info.Size > remaining)))
                {
                    content = JournalRecovery.ReviewStagedFile(next.Path, provider, check, remaining);
                    // A file over what is left is read up to it before it fails.
                    if (spend is not null) spend.Bytes += content?.Length ?? Math.Min(remaining, Math.Max(0, version.Info.Size));
                    if (content is null) return null;
                    remaining -= content.Length;
                }
                if (!version.Matches(next.Path, files, check)) return null;
                items.Add(new(next.Path, version, content));
            }
            return new(root, items.OrderBy(i => i.Path, StringComparer.Ordinal).ToArray(), allowLinks, bindByIdentity);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    public bool Matches(IFileSystemOperations files, ResourceProvider provider, Action? check = null)
    {
        var expected = Items.ToDictionary(i => i.Path, StringComparer.Ordinal);
        var current = Capture(Root, files, provider, check, JournalRecovery.StagedReviewByteLimit, 1000, AllowLinks, BindByIdentity, expected);
        return current is not null && current.Items.SequenceEqual(Items);
    }

    internal Item? FindItem(string path) => Items.FirstOrDefault(i => PathUtil.SafetyComparer.Equals(i.Path, path));

    // A root can take time to transfer. Check each still-pending member against its own approved version without
    // rechecking siblings that this move has already removed.
    internal bool MatchesItem(string path, IFileSystemOperations files, ResourceProvider provider, Action? check)
    {
        var item = FindItem(path);
        if (item is null || !item.Version.Matches(path, files, check)) return false;
        if (item.Content is { } content && JournalRecovery.ReviewStagedFile(path, provider, check, content.Length) != content) return false;
        return item.Version.Matches(path, files, check);
    }

    internal bool MatchesChildren(string directory, IReadOnlyList<FileSystemItemInfo> children)
    {
        var expected = Items.Where(i => PathUtil.SafetyComparer.Equals(Path.GetDirectoryName(i.Path), directory))
            .Select(i => i.Path).ToHashSet(PathUtil.SafetyComparer);
        return expected.Count == children.Count && children.All(i => expected.Contains(i.Path));
    }

    internal bool MatchesSubtree(string path, IFileSystemOperations files, ResourceProvider provider, Action? check)
    {
        var pending = Items.Where(i => PathUtil.IsSameOrUnder(i.Path, path)).ToArray();
        return pending.Any(i => PathUtil.SafetyComparer.Equals(i.Path, path)) &&
            new FileTreeReview(path, pending, AllowLinks, BindByIdentity).Matches(files, provider, check);
    }
}

/// <summary>The provider that supplied the complete reviewed ordinary-file bytes is retained for later checks.</summary>
public sealed record ReviewedSourceTree(FileTreeReview Tree, ResourceProvider Provider);
