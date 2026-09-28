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
/// reported once (not descended); links to folders are never followed; times compare at a tolerance (the coarser
/// file system's precision); a content check reads both files. Anything that could not be decided is Unknown, never Same.
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
        var comparer = caseInsensitiveNames ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
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
            void Add(TreeDiffEntry e) => entries.Add(e with { LeftFolder = e.Left is null ? null : l, RightFolder = e.Right is null ? null : r });
            var rightByName = new Dictionary<string, EntryData>(comparer);
            foreach (var e in rightItems) rightByName.TryAdd(e.Name, e);
            var seen = new HashSet<string>(comparer);
            foreach (var le in leftItems.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                string path = relative.Length == 0 ? le.Name : relative + "/" + le.Name;
                if (!rightByName.TryGetValue(le.Name, out var re))
                {
                    Add(new TreeDiffEntry(path, TreeDiffKind.LeftOnly, le, null));
                    continue;
                }
                seen.Add(re.Name);
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
            foreach (var re in rightItems.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                if (!seen.Contains(re.Name)) Add(new TreeDiffEntry(relative.Length == 0 ? re.Name : relative + "/" + re.Name, TreeDiffKind.RightOnly, null, re));
            }
        }

        TreeDiffEntry CompareFiles(string path, Location l, EntryData le, Location r, EntryData re)
        {
            bool sizeDiffers = (criteria & (CompareCriteria.Size | CompareCriteria.Content)) != 0 && le.Size >= 0 && re.Size >= 0 && le.Size != re.Size;
            long dt = le.Modified - re.Modified;
            bool timeKnown = le.Modified > 0 && re.Modified > 0;
            bool timeDiffers = (criteria & CompareCriteria.Time) != 0 && timeKnown && Math.Abs(dt) > tolerance.Ticks;
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
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    return new TreeDiffEntry(path, TreeDiffKind.Unknown, le, re, "The content could not be read: " + ex.Message);
                }
            }
            if (!sizeDiffers && !timeDiffers && !contentDiffers) return new TreeDiffEntry(path, TreeDiffKind.Same, le, re);
            if (sizeDiffers) detail = $"Sizes {le.Size:N0} and {re.Size:N0} bytes";
            else if (contentDiffers) detail = "Same size, different content";
            if (timeDiffers) return new TreeDiffEntry(path, dt > 0 ? TreeDiffKind.LeftNewer : TreeDiffKind.RightNewer, le, re, detail);
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
