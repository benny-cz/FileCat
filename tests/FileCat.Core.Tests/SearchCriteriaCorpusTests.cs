using System.Text.RegularExpressions;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

/// <summary>
/// V13's search criteria against a generated tree: every query's results compared with a filter written here from each
/// criterion's documented meaning, over the tree as enumerated directly, not through FileCat. Names (masks, lists,
/// exclusions, plain words, folders only), subfolders, hidden items, sizes in bytes and KB, modified and created ranges,
/// attributes set and clear, and ignored folders of all three kinds. FILECAT_FIND_CORPUS_QUERIES (default 400) and
/// FILECAT_FIND_CORPUS_SEED vary it.
/// </summary>
public sealed class SearchCriteriaCorpusTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose()
    {
        // Read-only and hidden items would stop the folder's removal on Windows.
        foreach (var path in Directory.EnumerateFileSystemEntries(_dir.Path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            try { File.SetAttributes(path, Directory.Exists(path) ? FileAttributes.Directory : FileAttributes.Normal); } catch (IOException) { }
        _dir.Dispose();
    }

    private static int Queries => int.TryParse(Environment.GetEnvironmentVariable("FILECAT_FIND_CORPUS_QUERIES"), out int n) && n > 0 ? n : 400;

    private static int Seed => int.TryParse(Environment.GetEnvironmentVariable("FILECAT_FIND_CORPUS_SEED"), out int s) ? s : 1313;

    private sealed record Item(string Path, string Relative, string Name, bool IsDir, FileAttributes Attributes, long Size, DateTime ModifiedUtc, DateTime CreatedUtc);

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The tree: folders up to three deep, files of tricky names, sizes on the KB boundaries, times over 400 days.</summary>
    private List<Item> Build(Random rng, DateTime nowUtc)
    {
        string[] stems = ["report", "Report", "data", "notes", "image", "a", "x y", "Ünïcödé", "čeština", "file.backup", "README", "log", ".dot"];
        string[] extensions = [".txt", ".log", ".TXT", ".md", ".bin", ".tmp", "", ".tar.gz"];
        string[] folderNames = ["src", "bin", "Debug", "build", "node_modules", "docs", "rebuild", "x", ".git"];
        long[] sizes = [0, 1, 1023, 1024, 1025, 2047, 2048, 10239, 10240, 10241, 65536];
        var dirs = new List<string> { _dir.Path };
        for (int i = 0; i < 40 && dirs.Count < 26; i++)
        {
            string parent = dirs[rng.Next(dirs.Count)];
            if (Path.GetRelativePath(_dir.Path, parent).Split(Path.DirectorySeparatorChar).Length >= 3 && parent != _dir.Path) continue;
            string path = Path.Combine(parent, folderNames[rng.Next(folderNames.Length)] + (rng.Next(3) == 0 ? "" : rng.Next(10).ToString()));
            if (Directory.Exists(path)) continue;
            Directory.CreateDirectory(path);
            dirs.Add(path);
        }
        var files = new List<string>();
        foreach (var dir in dirs)
            for (int k = rng.Next(2, 9); k > 0; k--)
            {
                string path = Path.Combine(dir, stems[rng.Next(stems.Length)] + (rng.Next(2) == 0 ? "" : rng.Next(100).ToString()) + extensions[rng.Next(extensions.Length)]);
                if (File.Exists(path) || Directory.Exists(path)) continue;
                long size = rng.Next(3) == 0 ? rng.Next(0, 70_000) : sizes[rng.Next(sizes.Length)];
                File.WriteAllBytes(path, new byte[size]);
                files.Add(path);
            }
        DateTime When() => nowUtc.AddSeconds(-rng.Next(0, 400 * 24 * 3600));
        foreach (var file in files)
        {
            File.SetLastWriteTimeUtc(file, When());
            File.SetCreationTimeUtc(file, When());
        }
        // Folders last, deepest first: making their children changed their times.
        foreach (var dir in dirs.Skip(1).OrderByDescending(d => d.Length))
        {
            Directory.SetLastWriteTimeUtc(dir, When());
            Directory.SetCreationTimeUtc(dir, When());
        }
        if (OperatingSystem.IsWindows())
            foreach (var path in files.Concat(dirs.Skip(1)))
            {
                var attributes = File.GetAttributes(path);
                if (rng.Next(8) == 0) attributes |= FileAttributes.Hidden;
                if (File.Exists(path) && rng.Next(8) == 0) attributes |= FileAttributes.ReadOnly;
                File.SetAttributes(path, attributes);
            }
        // The tree as the system lists it now, for the reference.
        var items = new List<Item>();
        foreach (var info in new DirectoryInfo(_dir.Path).EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            items.Add(new Item(info.FullName, Path.GetRelativePath(_dir.Path, info.FullName), info.Name, info is DirectoryInfo, info.Attributes,
                info is FileInfo f ? f.Length : 0, info.LastWriteTimeUtc, info.CreationTimeUtc));
        return items;
    }

    /// <summary>A mask from the parts the corpus uses, with what the reference makes of it.</summary>
    private static string RandomMask(Random rng)
    {
        string[] parts = ["*", "*.*", "*.txt", "*.TXT", "*.log", "re*", "?a*", "*.t?t", "report", "REPORT", "data", "notes.md", "x y*", "*.tar.gz", "čeština*",
            "src\\", "bin*\\", "*", "*.md", ".dot*", "*backup*"];
        string Part() => parts[rng.Next(parts.Length)];
        var include = Enumerable.Range(0, rng.Next(1, 3)).Select(_ => Part()).ToList();
        string mask = string.Join(";", include);
        if (rng.Next(4) == 0) mask += "|" + string.Join(";", Enumerable.Range(0, rng.Next(1, 3)).Select(_ => Part()));
        return mask;
    }

    /// <summary>One part of a mask by its documented meaning: wildcards over the whole name, a plain word anywhere in it.</summary>
    private static bool PartMatches(string part, string name, bool isDir)
    {
        bool foldersOnly = part.EndsWith('\\') || part.EndsWith('/');
        if (foldersOnly)
        {
            if (!isDir) return false;
            part = part[..^1];
        }
        if (part is "*" or "*.*" or "") return true;
        if (part.IndexOfAny(['*', '?', '.']) < 0) return name.Contains(part, StringComparison.OrdinalIgnoreCase);
        string pattern = "^" + Regex.Escape(part).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }

    private static bool MaskMatches(string mask, string name, bool isDir)
    {
        int bar = mask.IndexOf('|');
        string include = bar < 0 ? mask : mask[..bar], exclude = bar < 0 ? "" : mask[(bar + 1)..];
        var includes = include.Split([';', ','], StringSplitOptions.RemoveEmptyEntries);
        var excludes = exclude.Split([';', ','], StringSplitOptions.RemoveEmptyEntries);
        return (includes.Length == 0 || includes.Any(p => PartMatches(p, name, isDir))) && !excludes.Any(p => PartMatches(p, name, isDir));
    }

    /// <summary>An ignored folder by its documented meaning (<see cref="SearchQuery.IgnoredFolders"/>).</summary>
    private static bool Ignored(string entry, string root, string folder)
    {
        char sep = Path.DirectorySeparatorChar;
        if (Path.IsPathFullyQualified(entry)) return string.Equals(Path.TrimEndingDirectorySeparator(entry), folder, PathComparison);
        if (entry.StartsWith(sep)) return string.Equals(Path.Combine(root, entry.TrimStart(sep)), folder, PathComparison);
        return folder.EndsWith(sep + entry, PathComparison);
    }

    [Fact]
    public void Every_query_finds_exactly_what_its_criteria_mean()
    {
        var ct = TestContext.Current.CancellationToken;
        var nowUtc = DateTime.UtcNow;
        var rng = new Random(Seed);
        var tree = Build(rng, nowUtc);
        string root = _dir.Path;
        var dirs = tree.Where(i => i.IsDir).ToList();
        int refused = 0, empty = 0, total = 0;
        for (int index = 0; index < Queries; index++)
        {
            var c = new SearchCriteria { LookIn = root, Names = RandomMask(rng), Subfolders = rng.Next(5) != 0, IncludeHidden = rng.Next(3) != 0 };
            if (rng.Next(3) == 0)
            {
                var unit = rng.Next(2) == 0 ? SizeUnit.Bytes : SizeUnit.KB;
                double Value() => unit == SizeUnit.Bytes ? new long[] { 0, 1, 1023, 1024, 1025, 2048, 10240, 65536 }[rng.Next(8)] : new double[] { 0, 1, 1.5, 2, 10, 64 }[rng.Next(6)];
                if (rng.Next(2) == 0) (c.Advanced.SizeAtLeast, c.Advanced.SizeAtLeastUnit) = (Value(), unit);
                if (rng.Next(2) == 0) (c.Advanced.SizeAtMost, c.Advanced.SizeAtMostUnit) = (Value(), unit);
            }
            foreach (var time in new[] { c.Advanced.Modified, c.Advanced.Created })
                if (rng.Next(5) == 0)
                {
                    if (rng.Next(2) == 0)
                    {
                        time.Mode = TimeFilterMode.Within;
                        (time.Amount, time.Unit) = (rng.Next(1, 300), rng.Next(2) == 0 ? TimeUnit.Days : TimeUnit.Hours);
                    }
                    else
                    {
                        time.Mode = TimeFilterMode.Between;
                        var a = nowUtc.AddSeconds(-rng.Next(0, 400 * 24 * 3600)).ToLocalTime();
                        var b = nowUtc.AddSeconds(-rng.Next(0, 400 * 24 * 3600)).ToLocalTime();
                        if (a > b) (a, b) = (b, a);
                        time.From = rng.Next(4) == 0 ? null : DateTime.SpecifyKind(a, DateTimeKind.Unspecified);
                        time.To = rng.Next(4) == 0 ? null : DateTime.SpecifyKind(b, DateTimeKind.Unspecified);
                    }
                }
            if (rng.Next(4) == 0)
            {
                FileAttributes Pick() => new[] { FileAttributes.ReadOnly, FileAttributes.Hidden, FileAttributes.Directory }[rng.Next(3)];
                if (rng.Next(2) == 0) c.Advanced.AttributesSet = Pick();
                if (rng.Next(2) == 0) c.Advanced.AttributesClear = Pick();
            }
            char sep = Path.DirectorySeparatorChar;
            var ignored = new List<string>();
            if (rng.Next(4) == 0)
                ignored.Add(rng.Next(4) switch
                {
                    0 => "node_modules",
                    1 => "bin" + sep + "Debug",
                    2 => sep + "build",
                    _ => dirs.Count > 0 ? dirs[rng.Next(dirs.Count)].Path : "x",
                });
            string what = $"query {index} (seed {Seed}): names \"{c.Names}\", subfolders {c.Subfolders}, hidden {c.IncludeHidden}, {c.Advanced.Summary()}, ignored [{string.Join(", ", ignored)}]";
            bool built = c.TryBuildQuery(nowUtc, ignored, null, out var query, out var error);
            // Refused only for a reason the criteria give: a range that ends before it starts, or attributes both set and clear.
            long? least = c.Advanced.SizeAtLeast is { } l ? AdvancedSearchCriteria.Bytes(l, c.Advanced.SizeAtLeastUnit) : null;
            long? most = c.Advanced.SizeAtMost is { } m ? AdvancedSearchCriteria.Bytes(m, c.Advanced.SizeAtMostUnit) : null;
            var (mAfter, mBefore) = c.Advanced.Modified.Resolve(nowUtc);
            var (cAfter, cBefore) = c.Advanced.Created.Resolve(nowUtc);
            bool impossible = least > most || mAfter > mBefore || cAfter > cBefore || (c.Advanced.AttributesSet & c.Advanced.AttributesClear) != 0;
            Assert.True(built != impossible, $"{what}: {(built ? "built though it cannot match" : "refused: " + error)}");
            if (!built)
            {
                refused++;
                continue;
            }
            var set = new ResultSet("t", "t", "t");
            new SearchSession(query!, set).Run(ct);
            var found = set.Snapshot().Select(i => i.Item.FileSystemPath!).ToHashSet(StringComparer.Ordinal);

            // The reference: what the walk reaches, and of that what matches.
            bool Reached(Item item)
            {
                string? parent = Path.GetDirectoryName(item.Path);
                int depth = 0;
                for (string? dir = parent; dir is not null && !string.Equals(dir, root, PathComparison); dir = Path.GetDirectoryName(dir))
                {
                    depth++;
                    var folder = tree.First(t => string.Equals(t.Path, dir, PathComparison));
                    if (!c.IncludeHidden && (folder.Attributes & FileAttributes.Hidden) != 0) return false;
                    if (ignored.Any(e => Ignored(e, root, dir))) return false;
                }
                return c.Subfolders || depth == 0;
            }
            bool Matches(Item item)
            {
                if (!c.IncludeHidden && (item.Attributes & FileAttributes.Hidden) != 0) return false;
                if ((item.Attributes & c.Advanced.AttributesSet) != c.Advanced.AttributesSet || (item.Attributes & c.Advanced.AttributesClear) != 0) return false;
                if (!MaskMatches(c.Names, item.Name, item.IsDir)) return false;
                if (!item.IsDir && (item.Size < least || item.Size > most)) return false;
                if (item.ModifiedUtc < mAfter || item.ModifiedUtc > mBefore || item.CreatedUtc < cAfter || item.CreatedUtc > cBefore) return false;
                return true;
            }
            var expected = tree.Where(i => Reached(i) && Matches(i)).Select(i => i.Path).ToHashSet(StringComparer.Ordinal);
            var missing = expected.Except(found).ToList();
            var extra = found.Except(expected).ToList();
            string Show(string path) => tree.FirstOrDefault(t => t.Path == path) is { } t
                ? $"{t.Relative} ({(t.IsDir ? "folder" : t.Size + " bytes")}, {t.Attributes}, modified {t.ModifiedUtc:O}, created {t.CreatedUtc:O})" : path;
            Assert.True(missing.Count == 0 && extra.Count == 0,
                $"{what}: not found {string.Join("; ", missing.Take(5).Select(Show))}{(missing.Count > 5 ? $" and {missing.Count - 5} more" : "")}; " +
                $"found but not meant {string.Join("; ", extra.Take(5).Select(Show))}{(extra.Count > 5 ? $" and {extra.Count - 5} more" : "")}");
            if (expected.Count == 0) empty++;
            total += expected.Count;
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{Queries} queries (seed {Seed}) over {tree.Count} items ({tree.Count(t => t.IsDir)} folders, " +
            $"{tree.Count(t => (t.Attributes & FileAttributes.Hidden) != 0)} hidden): {refused} refused as impossible, {empty} found nothing, {total} results in all");
    }
}
