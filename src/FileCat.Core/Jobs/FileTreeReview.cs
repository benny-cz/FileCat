using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Jobs;

/// <summary>
/// A finite ordinary-file content and descendant-membership review. Available metadata, identity and resolved
/// paths are retained for every item. This does not hold native mutation handles or freeze a filesystem snapshot.
/// When links are allowed, only their literal item/link metadata is reviewed; target content is not followed.
/// </summary>
public sealed record FileTreeReview(string Root, IReadOnlyList<FileTreeReview.Item> Items, bool AllowLinks)
{
    public sealed record Item(string Path, SourcePathReview Version, JournalRecovery.StagedFileReview? Content);
    public long Bytes => Items.Sum(i => i.Content?.Length ?? 0);

    public static FileTreeReview? Capture(string root, IFileSystemOperations files, ResourceProvider provider,
        Action? check = null, long byteLimit = JournalRecovery.StagedReviewByteLimit, int itemLimit = 1000, bool allowLinks = false)
        => Capture(root, files, provider, check, byteLimit, itemLimit, allowLinks, expected: null);

    private static FileTreeReview? Capture(string root, IFileSystemOperations files, ResourceProvider provider,
        Action? check, long byteLimit, int itemLimit, bool allowLinks, IReadOnlyDictionary<string, Item>? expected)
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
                if (version is null) return null;
                // Do not open content belonging to a changed identity/version or a newly discovered descendant.
                // Complete hashes are compared afterward, but known path changes must stop before that read.
                if (expected is not null && (!expected.TryGetValue(next.Path, out var approved) || approved.Version != version)) return null;
                bool link = version.Info.IsLink || (version.Info.Attributes & FileAttributes.ReparsePoint) != 0;
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
                else if (!link)
                {
                    content = JournalRecovery.ReviewStagedFile(next.Path, provider, check, remaining);
                    if (content is null) return null;
                    remaining -= content.Length;
                }
                if (!version.Matches(next.Path, files, check)) return null;
                items.Add(new(next.Path, version, content));
            }
            return new(root, items.OrderBy(i => i.Path, StringComparer.Ordinal).ToArray(), allowLinks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    public bool Matches(IFileSystemOperations files, ResourceProvider provider, Action? check = null)
    {
        var expected = Items.ToDictionary(i => i.Path, StringComparer.Ordinal);
        var current = Capture(Root, files, provider, check, JournalRecovery.StagedReviewByteLimit, 1000, AllowLinks, expected);
        return current is not null && current.Items.SequenceEqual(Items);
    }
}

/// <summary>The provider that supplied the complete reviewed ordinary-file bytes is retained for later checks.</summary>
public sealed record ReviewedSourceTree(FileTreeReview Tree, ResourceProvider Provider);
