using FileCat.Core.Resources;

namespace FileCat.Core.Compare;

public enum TreeDiffKind
{
    Same,
    LeftOnly,
    RightOnly,
    LeftNewer,
    RightNewer,
    /// <summary>Different size or content with times that do not tell which is newer.</summary>
    Different,
    /// <summary>A file on one side, a folder on the other.</summary>
    TypeMismatch,
    /// <summary>Could not be decided: unreadable content or folder, or a link that is not followed.</summary>
    Unknown,
}

/// <param name="RelativePath">Path below both roots, with '/' separators.</param>
/// <param name="LeftFolder">The folder holding the left item (for acting on it); the right one likewise.</param>
public sealed record TreeDiffEntry(string RelativePath, TreeDiffKind Kind, EntryData? Left, EntryData? Right, string? Detail = null)
{
    public Location? LeftFolder { get; init; }
    public Location? RightFolder { get; init; }

    /// <summary>
    /// For a folder on one side only, on a disk or share: all it held when compared (what a synchronization would copy or
    /// remove, and the check that it is still so when that runs). Null elsewhere, and when it could not be read in full.
    /// </summary>
    public FileSystem.FolderContents? Contents { get; init; }

    public bool IsDifference => Kind != TreeDiffKind.Same;
    public bool IsFolder => (Left ?? Right)?.IsContainer == true;
}

/// <param name="Complete">False when stopped (canceled or over <see cref="TreeCompare.MaxEntries"/>): the rest was not compared.</param>
public sealed record TreeCompareResult(IReadOnlyList<TreeDiffEntry> Entries, bool Complete, int FoldersVisited, bool Canceled = false)
{
    public int Count(TreeDiffKind kind) => Entries.Count(e => e.Kind == kind);
}

/// <summary>
/// Recursive directory comparison (plan §16.2, P7): both trees are walked through their providers (disk, archives,
/// servers) in step. It is a preview: it never copies, synchronizes, or deletes. Folders present on one side are
/// reported once (not compared inside; on a disk or share, what they hold is read and stated); links to folders are never
/// followed; times compare at a tolerance (the coarser file system's precision); a content check reads both files.
/// Anything that could not be decided is Unknown, never Same.
/// </summary>
public static class TreeCompare
{
    public const int MaxEntries = 1_000_000;

    public static TreeCompareResult Compare(ProviderRegistry providers, Location left, Location right, CompareCriteria criteria, TimeSpan tolerance,
        bool caseInsensitiveNames, CancellationToken ct, Action<string>? progress = null)
    {
        var entries = new List<TreeDiffEntry>();
        int folders = 0;
        bool complete = true;
        var lp = providers.For(left);
        var rp = providers.For(right);

        void Walk(Location l, Location r, string relative)
        {
            ct.ThrowIfCancellationRequested();
            if (entries.Count >= MaxEntries)
            {
                complete = false;
                return;
            }
            folders++;
            progress?.Invoke(relative);
            var (leftItems, leftError) = List(lp, l, ct);
            var (rightItems, rightError) = List(rp, r, ct);
            if (leftError is not null || rightError is not null)
            {
                entries.Add(new TreeDiffEntry(relative.Length == 0 ? "." : relative, TreeDiffKind.Unknown, null, null,
                    "The folder could not be read: " + (leftError ?? rightError)));
                return;
            }
            // Both folders are recorded, also for one-sided items: synchronization copies into the other side's folder.
            void Add(TreeDiffEntry e) => entries.Add(e with { LeftFolder = l, RightFolder = r });
            var pairs = NamePairing.Pair([.. leftItems.OrderBy(e => e.Name, StringComparer.Ordinal)], [.. rightItems.OrderBy(e => e.Name, StringComparer.Ordinal)], caseInsensitiveNames);
            foreach (var (left0, right0) in pairs)
            {
                ct.ThrowIfCancellationRequested();
                if (left0 is not { } le)
                {
                    var only = right0!.Value;
                    Add(OneSided(new TreeDiffEntry(relative.Length == 0 ? only.Name : relative + "/" + only.Name, TreeDiffKind.RightOnly, null, only), rp, r, only));
                    continue;
                }
                string path = relative.Length == 0 ? le.Name : relative + "/" + le.Name;
                if (right0 is not { } re)
                {
                    Add(OneSided(new TreeDiffEntry(path, TreeDiffKind.LeftOnly, le, null), lp, l, le));
                    continue;
                }
                if (le.IsContainer != re.IsContainer)
                {
                    Add(new TreeDiffEntry(path, TreeDiffKind.TypeMismatch, le, re, le.IsContainer ? "A folder on the left, a file on the right" : "A file on the left, a folder on the right"));
                    continue;
                }
                if (le.IsContainer)
                {
                    if (le.Has(EntryFlags.Link) || re.Has(EntryFlags.Link))
                    {
                        Add(new TreeDiffEntry(path, TreeDiffKind.Unknown, le, re, "A link to a folder: not followed"));
                        continue;
                    }
                    var cl = lp.GetChildLocation(l, le);
                    var cr = rp.GetChildLocation(r, re);
                    if (cl is null || cr is null)
                    {
                        Add(new TreeDiffEntry(path, TreeDiffKind.Unknown, le, re, "The folder cannot be opened"));
                        continue;
                    }
                    Walk(cl, cr, path);
                    continue;
                }
                Add(CompareFiles(path, l, le, r, re));
            }
        }

        // A folder on one side only, on a disk or share: all it holds, for a plan to state and a removal to check.
        TreeDiffEntry OneSided(TreeDiffEntry entry, ResourceProvider provider, Location folder, EntryData data)
        {
            if (data.Kind != EntryKind.Directory || data.Has(EntryFlags.Link) || provider.GetChildLocation(folder, data) is not { IsFileSystem: true } inside)
                return entry;
            progress?.Invoke(entry.RelativePath);
            var contents = FileSystem.FolderContents.Read(inside.Path, ct);
            return entry with { Contents = contents, Detail = contents is null ? "What it holds could not be read in full" : "Holds " + contents.Describe() };
        }

        TreeDiffEntry CompareFiles(string path, Location l, EntryData le, Location r, EntryData re)
        {
            bool sizesKnown = le.Size >= 0 && re.Size >= 0;
            bool sizeDiffers = (criteria & (CompareCriteria.Size | CompareCriteria.Content)) != 0 && sizesKnown && le.Size != re.Size;
            // A time a listing states only to the minute or the day stands for all of it (an FTP server's LIST, I45).
            int? compared = (criteria & CompareCriteria.Time) != 0 ? EntryTimes.Compare(le, re, tolerance) : 0;
            int newer = compared ?? 0;
            bool timeDiffers = newer != 0;
            // A size or time a listing does not give decides nothing (V13: undecided never counts as the same).
            bool sizeOpen = (criteria & CompareCriteria.Size) != 0 && !sizesKnown, timeOpen = compared is null;
            string? detail = null;
            bool contentDiffers = false;
            if (!sizeDiffers && (criteria & CompareCriteria.Content) != 0)
            {
                try
                {
                    using var a = lp.OpenContent(lp.GetItemRef(l, le));
                    using var b = rp.OpenContent(rp.GetItemRef(r, re));
                    bool? equal = a is null || b is null ? null : DirectoryCompare.ContentEqual(a, b, ct);
                    if (equal is null) return new TreeDiffEntry(path, TreeDiffKind.Unknown, le, re, "The content could not be read");
                    contentDiffers = !equal.Value;
                    if (equal.Value) sizeOpen = false; // the same content is the same size
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    return new TreeDiffEntry(path, TreeDiffKind.Unknown, le, re, "The content could not be read: " + ex.Message);
                }
            }
            if (!sizeDiffers && !timeDiffers && !contentDiffers)
                return sizeOpen || timeOpen
                    ? new TreeDiffEntry(path, TreeDiffKind.Unknown, le, re, sizeOpen && timeOpen ? "Neither the size nor the time is known on both sides"
                        : sizeOpen ? "The size is not known on both sides" : "The time is not known on both sides")
                    : new TreeDiffEntry(path, TreeDiffKind.Same, le, re);
            if (sizeDiffers) detail = $"Sizes {le.Size:N0} and {re.Size:N0} bytes";
            else if (contentDiffers) detail = "Same size, different content";
            if (timeDiffers) return new TreeDiffEntry(path, newer > 0 ? TreeDiffKind.LeftNewer : TreeDiffKind.RightNewer, le, re, detail);
            return new TreeDiffEntry(path, TreeDiffKind.Different, le, re, detail);
        }

        try
        {
            Walk(left, right, "");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Stopped by the user: what was compared so far is still true, and the result says it is partial.
            return new TreeCompareResult(entries, false, folders, Canceled: true);
        }
        return new TreeCompareResult(entries, complete, folders);
    }

    private static (List<EntryData> Items, string? Error) List(ResourceProvider provider, Location location, CancellationToken ct)
    {
        var items = new List<EntryData>();
        try
        {
            provider.EnumerateAsync(location, new Sink(items), ct).GetAwaiter().GetResult();
            items.RemoveAll(e => e.Kind == EntryKind.Parent);
            return (items, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
        {
            return (items, ex.Message);
        }
    }

    private sealed class Sink(List<EntryData> into) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries) => into.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }
}
