using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Compare;

/// <summary>One-way synchronization modes (plan §16.2; approved scope: no two-way sync, no stored state).</summary>
public enum SyncMode
{
    /// <summary>Copy items missing from the target and replace older target files; nothing is removed.</summary>
    Update,
    /// <summary>Make the target like the source: also replace differing files and remove items only in the target.</summary>
    Mirror,
}

public enum SyncAction
{
    /// <summary>Nothing can be done for this item (undecided, file against folder, or a letter-case collision).</summary>
    None,
    /// <summary>Copy an item the target lacks.</summary>
    Copy,
    /// <summary>Replace an older target file, still only if the source is newer when the job runs.</summary>
    ReplaceOlder,
    /// <summary>Replace the target file whatever its time.</summary>
    Replace,
    /// <summary>Remove an item only in the target (Recycle Bin unless deleting permanently was chosen).</summary>
    Remove,
}

/// <summary>A proposed step; <see cref="Include"/> is the user's per-item choice.</summary>
public sealed class SyncItem(TreeDiffEntry entry, SyncAction action, bool include, string reason)
{
    public TreeDiffEntry Entry { get; } = entry;
    public SyncAction Action { get; } = action;
    public string Reason { get; } = reason;
    public bool Include { get; set; } = include && action != SyncAction.None;
    public bool CanInclude => Action != SyncAction.None;
}

/// <summary>
/// Turns a recursive comparison into a one-way plan (Total Commander's Synchronize directories, plan §16.2): a proposal
/// per difference that the user can override, deletions only as explicit items of Mirror, names that differ only in
/// letter case excluded, and execution only through ordinary jobs.
/// </summary>
public static class SyncPlanner
{
    public static List<SyncItem> Propose(TreeCompareResult comparison, bool sourceIsLeft, SyncMode mode, bool targetIgnoresCase)
    {
        var differences = comparison.Entries.Where(e => e.IsDifference).ToList();
        var collisions = targetIgnoresCase ? CaseCollisions(comparison.Entries) : [];
        var items = new List<SyncItem>(differences.Count);
        foreach (var e in differences)
        {
            if (UnsafeSegment(e.RelativePath) is { } bad)
            {
                items.Add(new SyncItem(e, SyncAction.None, false, "Not synchronized: " + bad));
                continue;
            }
            if (collisions.Contains(e))
            {
                items.Add(new SyncItem(e, SyncAction.None, false, "Names that differ only in letter case would collide in the target; not synchronized"));
                continue;
            }
            var (sourceOnly, targetOnly) = sourceIsLeft ? (TreeDiffKind.LeftOnly, TreeDiffKind.RightOnly) : (TreeDiffKind.RightOnly, TreeDiffKind.LeftOnly);
            var (sourceNewer, targetNewer) = sourceIsLeft ? (TreeDiffKind.LeftNewer, TreeDiffKind.RightNewer) : (TreeDiffKind.RightNewer, TreeDiffKind.LeftNewer);
            SyncItem item = e.Kind switch
            {
                var k when k == sourceOnly => new(e, SyncAction.Copy, true, e.IsFolder ? "Only in the source: the folder is copied with its contents" : "Only in the source"),
                var k when k == sourceNewer => new(e, SyncAction.ReplaceOlder, true, "Newer in the source"),
                var k when k == targetOnly => mode == SyncMode.Mirror
                    ? new(e, SyncAction.Remove, true, "Only in the target: Mirror removes it")
                    : new(e, SyncAction.None, false, "Only in the target: Update keeps it"),
                var k when k == targetNewer => new(e, SyncAction.Replace, false, "Newer in the target: kept unless you choose to overwrite it"),
                TreeDiffKind.Different => mode == SyncMode.Mirror
                    ? new(e, SyncAction.Replace, true, "Different; Mirror replaces the target's copy" + (e.Detail is null ? "" : $" ({e.Detail})"))
                    : new(e, SyncAction.Replace, false, "Different, but the source is not newer: kept unless you choose to overwrite it"),
                TreeDiffKind.TypeMismatch => new(e, SyncAction.None, false, (e.Detail ?? "A file on one side, a folder on the other") + ": resolve it by hand"),
                _ => new(e, SyncAction.None, false, e.Detail ?? "Could not be compared"),
            };
            items.Add(item);
        }
        return items;
    }

    /// <summary>
    /// Entries whose paths would collide in a target that ignores letter case: "a.txt" against "A.txt", whether both
    /// come from a case-sensitive source or one of them is already in the target.
    /// </summary>
    private static HashSet<TreeDiffEntry> CaseCollisions(IReadOnlyList<TreeDiffEntry> entries) =>
        entries.GroupBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).SelectMany(g => g).ToHashSet();

    /// <summary>Why a path segment cannot be written below the target (a server may list a name like "..\x"), or null.</summary>
    private static string? UnsafeSegment(string relativePath)
    {
        foreach (var segment in relativePath.Split('/'))
            if (Jobs.SafeNames.Validate(segment) is { } bad) return $"\"{segment}\": {bad}";
        return null;
    }

    /// <summary>
    /// The jobs for the included items, in the order they should run: copies of new items (asking about anything that
    /// appeared meanwhile), replacements of older files (skipped if the target became newer), unconditional replacements,
    /// then removals. Items keep their folders below the target root. Empty groups make no job.
    /// </summary>
    public static List<JobRequest> BuildRequests(IEnumerable<SyncItem> items, bool sourceIsLeft, Location targetRoot, ProviderRegistry providers, bool deletePermanently)
    {
        var copy = new List<ItemRef>();
        var replaceOlder = new List<ItemRef>();
        var replace = new List<ItemRef>();
        var remove = new List<ItemRef>();
        // The target each replacement replaces, as compared: one edited meanwhile is not overwritten.
        var expected = new Dictionary<ItemRef, (long Size, long ModifiedTicks)>();
        foreach (var item in items.Where(i => i.Include && i.CanInclude))
        {
            var e = item.Entry;
            int slash = e.RelativePath.LastIndexOf('/');
            string relative = slash < 0 ? string.Empty : e.RelativePath[..slash].Replace('/', Path.DirectorySeparatorChar);
            if (item.Action == SyncAction.Remove)
            {
                var (folder, data) = sourceIsLeft ? (e.RightFolder, e.Right) : (e.LeftFolder, e.Left);
                if (folder is null || data is not { } d) continue;
                remove.Add(providers.For(folder).GetItemRef(folder, d));
                continue;
            }
            var (sourceFolder, sourceData) = sourceIsLeft ? (e.LeftFolder, e.Left) : (e.RightFolder, e.Right);
            if (sourceFolder is null || sourceData is not { } s) continue;
            var reference = providers.For(sourceFolder).GetItemRef(sourceFolder, s);
            var withFolder = new ItemRef(reference.Parent, reference.Name, reference.Kind, reference.Size, reference.Modified)
            {
                Flags = reference.Flags,
                Ordinal = reference.Ordinal,
                RelativeFolder = relative,
            };
            (item.Action switch { SyncAction.Copy => copy, SyncAction.ReplaceOlder => replaceOlder, _ => replace }).Add(withFolder);
            if (item.Action is SyncAction.Replace or SyncAction.ReplaceOlder && (sourceIsLeft ? e.Right : e.Left) is { } target && target.Modified > 0)
                expected[withFolder] = (target.Size, target.Modified);
        }
        var requests = new List<JobRequest>();
        void Transfer(List<ItemRef> sources, ConflictPolicy conflicts, string description)
        {
            if (sources.Count == 0) return;
            requests.Add(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = sources,
                Destination = targetRoot,
                Options = new TransferOptions { Conflicts = conflicts },
                Mode = QueueMode.Queue,
                Description = description,
                ExpectedTargets = conflicts is ConflictPolicy.Replace or ConflictPolicy.ReplaceIfNewer ? expected : null,
            });
        }
        Transfer(copy, ConflictPolicy.Ask, "Synchronize: copy new items");
        Transfer(replaceOlder, ConflictPolicy.ReplaceIfNewer, "Synchronize: replace older files");
        Transfer(replace, ConflictPolicy.Replace, "Synchronize: replace files");
        if (remove.Count > 0)
        {
            requests.Add(new JobRequest
            {
                Kind = deletePermanently ? JobKind.Delete : JobKind.Recycle,
                Sources = remove,
                Mode = QueueMode.Queue,
                Description = "Synchronize: remove items only in the target",
                OnlyAsCompared = true,
            });
        }
        return requests;
    }

    /// <summary>"Copy 3 items, replace 2, remove 1" for the included steps.</summary>
    public static string Describe(IEnumerable<SyncItem> items)
    {
        var chosen = items.Where(i => i.Include && i.CanInclude).ToList();
        int copy = chosen.Count(i => i.Action == SyncAction.Copy);
        int replace = chosen.Count(i => i.Action is SyncAction.ReplaceOlder or SyncAction.Replace);
        int remove = chosen.Count(i => i.Action == SyncAction.Remove);
        var parts = new List<string>();
        if (copy > 0) parts.Add($"copy {copy:N0}");
        if (replace > 0) parts.Add($"replace {replace:N0}");
        if (remove > 0) parts.Add($"remove {remove:N0}");
        return parts.Count == 0 ? "nothing to do" : string.Join(", ", parts);
    }
}
