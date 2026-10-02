using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Search;

/// <summary>How a new search's matches combine with the items already found (plan §11: refine and append).</summary>
public enum RefineMode
{
    /// <summary>The new matches replace the list.</summary>
    Replace,
    /// <summary>Keep the found items that the new search matches too.</summary>
    Intersect,
    /// <summary>Remove the found items that the new search matches.</summary>
    Subtract,
    /// <summary>Add the new matches to the list.</summary>
    Append,
}

public static class Refine
{
    /// <summary>The list after a search in <paramref name="mode"/>; items keep their order and relative folders.</summary>
    public static List<(ItemRef Item, string Relative)> Combine(IReadOnlyList<(ItemRef Item, string Relative)> previous,
        IReadOnlyList<(ItemRef Item, string Relative)> found, RefineMode mode)
    {
        var matched = found.Select(f => f.Item).ToHashSet();
        return mode switch
        {
            RefineMode.Intersect => previous.Where(p => matched.Contains(p.Item)).ToList(),
            RefineMode.Subtract => previous.Where(p => !matched.Contains(p.Item)).ToList(),
            RefineMode.Append => Append(previous, found),
            _ => found.ToList(),
        };
    }

    private static List<(ItemRef Item, string Relative)> Append(IReadOnlyList<(ItemRef Item, string Relative)> previous,
        IReadOnlyList<(ItemRef Item, string Relative)> found)
    {
        var list = previous.ToList();
        var listed = previous.Select(p => p.Item).ToHashSet();
        foreach (var f in found)
            if (listed.Add(f.Item)) list.Add(f);
        return list;
    }
}

[Flags]
public enum DuplicateCriteria
{
    None = 0,
    Name = 1,
    Size = 2,
    /// <summary>Same bytes; implies the same size.</summary>
    Content = 4,
}

/// <summary>Groups of files alike by name, size, and content, and the files that could not be read.</summary>
public sealed record DuplicateResult(IReadOnlyList<IReadOnlyList<ItemRef>> Groups, IReadOnlyList<string> Unreadable)
{
    /// <summary>Names left out of the groups because they are other names of a file listed there (hard links, a folder
    /// reached through a junction): deleting one as a copy would delete the file itself.</summary>
    public IReadOnlyList<string> SameFile { get; init; } = [];

    /// <summary>Links to files, left out: a link is not a copy, and its target must not be taken for one.</summary>
    public IReadOnlyList<string> Links { get; init; } = [];
}

/// <summary>
/// Finds duplicate files among found items (plan §11): the same name, size, content, or any combination. Contents are
/// compared only among files of equal size: first their first 64 KiB, then, for those still alike, all their bytes by
/// SHA-256. Names compare as the file system does (without case on Windows). Given each file's identity, names of one
/// file count once (release issue I93): a hard link or a path through a junction is not a copy, and "all but one" must
/// never mark the file itself for deletion. Links to files are left out for the same reason.
/// </summary>
public static class DuplicateFinder
{
    private const int HeadBytes = 64 * 1024;

    public static DuplicateResult Find(IEnumerable<ItemRef> items, DuplicateCriteria criteria, CancellationToken ct, Func<string, string?>? identityOf = null)
    {
        if (criteria == DuplicateCriteria.None) throw new ArgumentException("Choose at least one of name, size, and content.", nameof(criteria));
        bool byName = (criteria & DuplicateCriteria.Name) != 0;
        bool byContent = (criteria & DuplicateCriteria.Content) != 0;
        bool bySize = byContent || (criteria & DuplicateCriteria.Size) != 0;
        var unreadable = new List<string>();
        var links = new List<string>();
        var files = new List<(ItemRef Item, string Path, long Size)>();
        foreach (var item in items.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            if (item.Kind != EntryKind.File || item.FileSystemPath is not { } path) continue;
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    links.Add(path);
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable.Add(path);
                continue;
            }
            long size = 0;
            if (bySize)
            {
                // The size now, not when the item was found: a changed file must not join the wrong group.
                try { size = new FileInfo(path).Length; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    unreadable.Add(path);
                    continue;
                }
            }
            files.Add((item, path, size));
        }
        var names = PathUtil.SafetyComparer;
        var candidates = files
            .GroupBy(f => (Name: byName ? f.Item.Name : string.Empty, f.Size), new KeyComparer(names))
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .ToList();
        var groups = new List<List<(ItemRef Item, string Path, long Size)>>();
        foreach (var candidate in candidates)
        {
            if (!byContent)
            {
                groups.Add(candidate);
                continue;
            }
            // Same first bytes, then the same bytes throughout.
            foreach (var head in SplitBy(candidate, f => Hash(f.Path, HeadBytes, unreadable, ct)))
            {
                if (head[0].Size <= HeadBytes) groups.Add(head);
                else groups.AddRange(SplitBy(head, f => Hash(f.Path, long.MaxValue, unreadable, ct)));
            }
        }
        // Names of one file count once: the first by path stays, the others are reported, and a group needs two files.
        var sameFile = new List<string>();
        if (identityOf is not null)
        {
            var distinct = new List<List<(ItemRef Item, string Path, long Size)>>();
            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var files2 = new List<(ItemRef Item, string Path, long Size)>();
                foreach (var f in group.OrderBy(f => f.Path, names))
                {
                    if (identityOf(f.Path) is { } id && !seen.Add(id)) sameFile.Add(f.Path);
                    else files2.Add(f);
                }
                if (files2.Count > 1) distinct.Add(files2);
            }
            groups = distinct;
        }
        var ordered = groups
            .Select(g => (IReadOnlyList<ItemRef>)g.OrderBy(f => f.Path, names).Select(f => f.Item).ToList())
            .OrderBy(g => g[0].Name, names).ThenByDescending(g => g[0].FileSystemPath is { } p ? SafeLength(p) : 0)
            .ToList();
        return new DuplicateResult(ordered, unreadable) { SameFile = sameFile, Links = links };
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>The files split into groups of two or more with the same key; files whose key is null are left out.</summary>
    private static IEnumerable<List<T>> SplitBy<T>(List<T> files, Func<T, string?> key) =>
        files.Select(f => (File: f, Key: key(f))).Where(x => x.Key is not null)
            .GroupBy(x => x.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Select(x => x.File).ToList());

    private static string? Hash(string path, long limit, List<string> unreadable, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long left = limit;
            int n;
            while (left > 0 && (n = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left))) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, n);
                left -= n;
            }
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (unreadable) unreadable.Add(path);
            return null;
        }
    }

    private sealed class KeyComparer(StringComparer names) : IEqualityComparer<(string Name, long Size)>
    {
        public bool Equals((string Name, long Size) x, (string Name, long Size) y) => x.Size == y.Size && names.Equals(x.Name, y.Name);

        public int GetHashCode((string Name, long Size) key) => HashCode.Combine(names.GetHashCode(key.Name), key.Size);
    }
}
