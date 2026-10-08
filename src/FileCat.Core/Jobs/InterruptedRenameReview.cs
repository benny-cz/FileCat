using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Jobs;

public static partial class JournalRecovery
{
    public sealed record RenameParentReview(string Path, bool IsLink, string? LinkTarget, string? Identity, string? FinalPath);
    public sealed record RenameReview(PendingIntent Intent, IReadOnlyList<FileTreeReview.Item> Items, IReadOnlyList<RenameParentReview> Parents)
    {
        public long Bytes => Items.Sum(i => i.Content?.Length ?? 0);
    }
    public sealed record RenameRecoveryResult(bool Resolved, int Finished, IReadOnlyList<string> Report);

    /// <summary>
    /// Reviews a finite local temporary item, including complete ordinary-file bytes and directory descendants.
    /// Links, incomplete content and exhausted budgets stay unreviewed. Available identities and resolved paths are
    /// compared again before moving; this is not an atomic native handle-based rename or a filesystem snapshot.
    /// </summary>
    public static RenameReview? ReviewRename(PendingIntent intent, IFileSystemOperations files, ResourceProvider provider,
        Action? check = null, long byteLimit = StagedReviewByteLimit, int itemLimit = 1000)
    {
        if (intent.Operation != JobJournal.RenameViaOp || intent.Via is not { } via || intent.Target is null || itemLimit <= 0) return null;
        try
        {
            var tree = FileTreeReview.Capture(via, files, provider, check, byteLimit, itemLimit);
            if (tree is null) return null;
            var parents = new List<RenameParentReview>();
            foreach (var path in new[] { via, intent.Target, intent.Path }.Select(p => Path.GetDirectoryName(Path.GetFullPath(p))!).Distinct(PathUtil.SafetyComparer))
            {
                var parent = ReviewRenameParent(path, files, check); if (parent is null) return null; parents.Add(parent);
            }
            return new(intent, tree.Items, parents.OrderBy(p => p.Path, StringComparer.Ordinal).ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    private static RenameParentReview? ReviewRenameParent(string path, IFileSystemOperations files, Action? check)
    {
        var version = SourcePathReview.Capture(path, files, check);
        return version is null || !version.Info.IsDirectory ? null : new(path, version.Info.IsLink, version.Info.LinkTarget, version.Identity, version.FinalPath);
    }

    /// <summary>Rechecks the approved tree and parent paths before each destination attempt. Never replaces an occupant.</summary>
    public static RenameRecoveryResult FinishReviewedRename(RenameReview reviewed, IFileSystemOperations files, ResourceProvider provider, Action? check = null)
    {
        var intent = reviewed.Intent; var via = intent.Via!; var target = intent.Target!;
        foreach (var destination in new[] { target, intent.Path }.Distinct(PathUtil.SafetyComparer))
        {
            var current = ReviewRename(intent, files, provider, check);
            if (current is null || !current.Items.SequenceEqual(reviewed.Items) || !current.Parents.SequenceEqual(reviewed.Parents))
                return new(false, 0, [$"{via}: the temporary item or its parent path changed or could not be completely checked; it is kept for review."]);
            check?.Invoke();
            try
            {
                if (files.TryGetInfo(destination) is not null) continue;
                check?.Invoke(); files.Move(via, destination, replaceExisting: false, writeThrough: true);
                return destination == target ? new(true, 1, []) : new(true, 0, [$"{Path.GetFileName(destination)}: the new name \"{Path.GetFileName(target)}\" is taken, so it has its original name again."]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return new(false, 0, [$"{via}: neither \"{Path.GetFileName(target)}\" nor \"{Path.GetFileName(intent.Path)}\" could be used; it keeps this temporary name for review."]);
    }
}
