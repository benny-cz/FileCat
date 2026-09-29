using System.Text;
using System.Text.RegularExpressions;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Selection;

namespace FileCat.Core.Search;

public sealed class SearchQuery
{
    public required IReadOnlyList<string> Roots { get; init; }
    public Mask? Names { get; init; }
    public string? Text { get; init; }
    public bool MatchCase { get; init; }
    public bool Regex { get; init; }
    /// <summary>The text (or each regex match) only where it is a whole word: no letter, digit, or underscore beside it.</summary>
    public bool WholeWords { get; init; }
    /// <summary>Bytes to find in files instead of text (hex mode, <see cref="HexPattern"/>).</summary>
    public byte[]? Bytes { get; init; }
    public bool Recursive { get; init; } = true;
    public int MaxDepth { get; init; } = int.MaxValue;
    public bool IncludeHidden { get; init; } = true;
    public bool IncludeDirectories { get; init; } = true;
    public long? MinSize { get; init; }
    public long? MaxSize { get; init; }
    public DateTime? ModifiedAfterUtc { get; init; }
    public DateTime? ModifiedBeforeUtc { get; init; }
    public DateTime? CreatedAfterUtc { get; init; }
    public DateTime? CreatedBeforeUtc { get; init; }
    /// <summary>Attributes a match must have; <see cref="FileAttributes.Directory"/> finds folders only.</summary>
    public FileAttributes AttributesSet { get; init; }
    /// <summary>Attributes a match must not have; <see cref="FileAttributes.Directory"/> finds files only.</summary>
    public FileAttributes AttributesClear { get; init; }

    /// <summary>
    /// Folders whose contents are not searched (plan §11): a name or relative path ("node_modules", "bin\Debug") skips
    /// every folder whose path ends with it; a leading separator ("\build") anchors it to each searched folder; a full
    /// path skips that one folder.
    /// </summary>
    public IReadOnlyList<string> IgnoredFolders { get; init; } = [];

    /// <summary>
    /// Archives met while searching are searched too, by their members' names (never their contents, so a search with
    /// text or bytes leaves them alone). Null: archives are files like any other.
    /// </summary>
    public IArchiveMembers? Archives { get; init; }

    /// <summary>
    /// Searches these earlier results instead of folders: each item is tested against the criteria as it is now,
    /// nothing is entered, and matches keep their relative folder (plan §11: searching within results narrows the set).
    /// </summary>
    public IReadOnlyList<(ItemRef Item, string Relative)>? WithinResults { get; init; }

    internal bool HasContent => Bytes is { Length: > 0 } || !string.IsNullOrEmpty(Text);

    public string Describe()
    {
        var parts = new List<string>();
        if (Names is { IsMatchAll: false }) parts.Add($"names \"{Names.Text}\"");
        if (Bytes is { Length: > 0 } bytes) parts.Add($"bytes {HexPattern.Format(bytes)}");
        else if (!string.IsNullOrEmpty(Text)) parts.Add((Regex ? "regex " : "text ") + $"\"{Text}\"" + (WholeWords ? " (whole words)" : ""));
        if (MinSize is not null || MaxSize is not null) parts.Add("size filter");
        if (ModifiedAfterUtc is not null || ModifiedBeforeUtc is not null) parts.Add("date filter");
        if (CreatedAfterUtc is not null || CreatedBeforeUtc is not null) parts.Add("creation date filter");
        if ((AttributesSet | AttributesClear) != 0) parts.Add("attribute filter");
        var what = parts.Count == 0 ? "all items" : string.Join(", ", parts);
        if (WithinResults is { } within) return $"{what} within {within.Count:N0} earlier results";
        return $"{what} in {string.Join("; ", Roots)}{(Recursive ? "" : " (top level only)")}{(Archives is not null && !HasContent ? ", inside archives too" : "")}";
    }
}

public enum SearchLogKind
{
    /// <summary>A folder or file that could not be read (access denied, a device error): it was not searched.</summary>
    Inaccessible,
    /// <summary>A folder on the ignore list: its contents were not searched.</summary>
    Ignored,
    /// <summary>A folder skipped while it was being searched (Skip current folder).</summary>
    Skipped,
    /// <summary>An earlier result that no longer exists (searching within results).</summary>
    Gone,
}

/// <summary>A line of a search's log (plan §11): what was not searched, and why.</summary>
public sealed record SearchLogEntry(SearchLogKind Kind, string Path, string? Detail = null)
{
    public string Describe() => Kind switch
    {
        SearchLogKind.Inaccessible => $"Not searched (inaccessible): {Path}" + (Detail is null ? string.Empty : $" · {Detail}"),
        SearchLogKind.Ignored => $"Not searched (on the ignore list): {Path}",
        SearchLogKind.Skipped => $"Not searched (skipped while searching): {Path}",
        _ => $"No longer exists (not searched): {Path}",
    };
}

/// <summary>
/// Recursive search as a session (plan §11): streams results into a result set of original references,
/// exposes visited/inaccessible scope, supports skipping the current subtree, never follows links, and
/// uses bounded, boundary-safe content scanning with regex timeouts.
/// </summary>
public sealed class SearchSession
{
    private const int ChunkBytes = 1024 * 1024;
    private const int MaxLog = 5000;
    private static readonly EnumerationOptions Options = new() { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false };
    private readonly SearchQuery _query;
    private readonly ResultSet _results;
    private readonly Regex? _regex;
    private readonly bool _plainAsciiText;
    private readonly IgnoredFolder[] _ignored;
    private volatile string? _skip;
    private readonly object _logLock = new();
    private readonly List<SearchLogEntry> _log = [];

    public SearchSession(SearchQuery query, ResultSet results)
    {
        _query = query;
        _results = results;
        if (query.Bytes is not { Length: > 0 } && !string.IsNullOrEmpty(query.Text) && (query.Regex || query.WholeWords))
            _regex = new Regex(Pattern(query), RegexOptions.CultureInvariant | RegexOptions.Multiline | (query.MatchCase ? 0 : RegexOptions.IgnoreCase), TimeSpan.FromSeconds(1));
        _plainAsciiText = query.Text is { Length: > 0 } t && IsPlainAscii(t);
        _ignored = query.IgnoredFolders.Select(IgnoredFolder.Parse).OfType<IgnoredFolder>().ToArray();
    }

    /// <summary>
    /// The regular expression a text search runs: the query's own, or its text taken literally; whole words guard the
    /// ends that are word characters (a literal "#include" may follow a letter).
    /// </summary>
    private static string Pattern(SearchQuery q)
    {
        string text = q.Text!;
        if (q.Regex) return q.WholeWords ? $@"(?<!\w)(?:{text})(?!\w)" : text;
        string escaped = System.Text.RegularExpressions.Regex.Escape(text);
        bool wordStart = char.IsLetterOrDigit(text[0]) || text[0] == '_';
        bool wordEnd = char.IsLetterOrDigit(text[^1]) || text[^1] == '_';
        return (wordStart ? @"(?<!\w)" : "") + escaped + (wordEnd ? @"(?!\w)" : "");
    }

    public long FoldersVisited;
    public long FilesExamined;
    public long Matches;
    public volatile string? CurrentFolder;
    public volatile bool Finished;
    public volatile bool RegexTimedOut;

    /// <summary>Folders and files that could not be read.</summary>
    public IReadOnlyList<string> Inaccessible
    {
        get
        {
            lock (_logLock) return _log.Where(e => e.Kind == SearchLogKind.Inaccessible).Select(e => e.Path).ToList();
        }
    }

    /// <summary>What was not searched, and why (the first 5,000 entries).</summary>
    public IReadOnlyList<SearchLogEntry> Log
    {
        get
        {
            lock (_logLock) return _log.ToList();
        }
    }

    /// <summary>Skips the folder currently being searched (FAR/TC behavior).</summary>
    public void SkipCurrentFolder() => _skip = CurrentFolder;

    public static bool TryValidate(SearchQuery q, out string? error)
    {
        error = null;
        if (q.Bytes is not { Length: > 0 } && !string.IsNullOrEmpty(q.Text) && q.Regex)
        {
            try { _ = new Regex(q.Text); }
            catch (ArgumentException ex)
            {
                error = "Regular expression: " + ex.Message;
                return false;
            }
        }
        if (q.MinSize is { } min && q.MaxSize is { } max && min > max)
        {
            error = "The size at least is more than the size at most, so nothing could match.";
            return false;
        }
        if (q.ModifiedAfterUtc is { } after && q.ModifiedBeforeUtc is { } before && after > before
            || q.CreatedAfterUtc is { } createdAfter && q.CreatedBeforeUtc is { } createdBefore && createdAfter > createdBefore)
        {
            error = "The time range ends before it starts, so nothing could match.";
            return false;
        }
        if ((q.AttributesSet & q.AttributesClear) != 0)
        {
            error = "An attribute cannot be both required and excluded.";
            return false;
        }
        return true;
    }

    public void Run(CancellationToken ct)
    {
        try
        {
            if (_query.WithinResults is { } within)
            {
                Narrow(within, ct);
            }
            else
            {
                foreach (var root in _query.Roots)
                {
                    ct.ThrowIfCancellationRequested();
                    Walk(root, root, 0, ct);
                }
            }
            _results.IsComplete = true;
        }
        catch (OperationCanceledException)
        {
            _results.IsComplete = false;
        }
        finally
        {
            Finished = true;
            lock (_logLock)
            {
                _results.Issues.Clear();
                _results.Issues.AddRange(_log.Take(100).Select(e => e.Describe()));
            }
            _results.NotifyChanged();
        }
    }

    private void AddLog(SearchLogKind kind, string path, string? detail = null)
    {
        lock (_logLock)
        {
            if (_log.Count < MaxLog) _log.Add(new SearchLogEntry(kind, path, detail));
        }
    }

    private void Narrow(IReadOnlyList<(ItemRef Item, string Relative)> items, CancellationToken ct)
    {
        foreach (var (item, relative) in items)
        {
            ct.ThrowIfCancellationRequested();
            if (item.FileSystemPath is not { } path) continue;
            CurrentFolder = item.Parent.Path;
            FileSystemInfo info = item.IsContainer ? new DirectoryInfo(path) : new FileInfo(path);
            if (!info.Exists)
            {
                AddLog(SearchLogKind.Gone, path);
                continue;
            }
            if ((info.Attributes & FileAttributes.Hidden) != 0 && !_query.IncludeHidden) continue;
            if (IsMatch(info, item.IsContainer, ct)) AddResult(info, item.IsContainer, relative);
        }
    }

    private void Walk(string root, string dir, int depth, CancellationToken ct)
    {
        CurrentFolder = dir;
        Interlocked.Increment(ref FoldersVisited);
        List<FileSystemInfo> children;
        try
        {
            children = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", Options).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            AddLog(SearchLogKind.Inaccessible, dir, ex.Message);
            return;
        }
        var subdirs = new List<string>();
        foreach (var info in children)
        {
            ct.ThrowIfCancellationRequested();
            if (_skip is { } skip && PathUtil.IsSameOrUnder(dir, skip))
            {
                if (string.Equals(skip, dir, StringComparison.OrdinalIgnoreCase))
                {
                    _skip = null;
                    AddLog(SearchLogKind.Skipped, dir);
                }
                return;
            }
            bool isDir = info is DirectoryInfo;
            bool isLink = (info.Attributes & FileAttributes.ReparsePoint) != 0;
            bool hidden = (info.Attributes & FileAttributes.Hidden) != 0;
            if (hidden && !_query.IncludeHidden) continue;
            if (isDir && !isLink && _query.Recursive && depth < _query.MaxDepth)
            {
                if (IsIgnored(root, info.FullName)) AddLog(SearchLogKind.Ignored, info.FullName);
                else subdirs.Add(info.FullName);
            }
            if (IsMatch(info, isDir, ct)) Add(root, info, isDir);
            if (!isDir && _query.Archives is { } archives && !_query.HasContent && archives.IsArchive(info.Name))
                SearchArchive(root, (FileInfo)info, archives, ct);
        }
        foreach (var sub in subdirs)
        {
            if (_skip is { } skip && PathUtil.IsSameOrUnder(sub, skip)) continue;
            Walk(root, sub, depth + 1, ct);
        }
        if (_skip is { } s && string.Equals(s, dir, StringComparison.OrdinalIgnoreCase))
        {
            _skip = null;
            AddLog(SearchLogKind.Skipped, dir);
        }
    }

    /// <summary>The most members one archive gives the search; a bigger one is searched that far and logged.</summary>
    internal const int MaxArchiveMembers = 200_000;

    /// <summary>
    /// An archive's members matched by name and by what they report (size, modification time, being a folder). They
    /// have no other attributes or creation times, so criteria on those leave them out.
    /// </summary>
    private void SearchArchive(string root, FileInfo archive, IArchiveMembers archives, CancellationToken ct)
    {
        CurrentFolder = archive.FullName;
        int listed = 0;
        try
        {
            foreach (var member in archives.List(archive.FullName, ct))
            {
                ct.ThrowIfCancellationRequested();
                if (++listed > MaxArchiveMembers)
                {
                    AddLog(SearchLogKind.Inaccessible, archive.FullName, $"only its first {MaxArchiveMembers:N0} members were searched");
                    break;
                }
                if (MemberMatches(member)) AddMember(root, archive, member);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            AddLog(SearchLogKind.Inaccessible, archive.FullName, "the archive could not be read: " + ex.Message);
        }
    }

    private bool MemberMatches(ItemRef member)
    {
        bool isDir = member.IsContainer;
        if (isDir && !_query.IncludeDirectories) return false;
        if ((_query.AttributesSet & ~FileAttributes.Directory) != 0) return false;
        if ((_query.AttributesSet & FileAttributes.Directory) != 0 && !isDir) return false;
        if ((_query.AttributesClear & FileAttributes.Directory) != 0 && isDir) return false;
        if (_query.CreatedAfterUtc is not null || _query.CreatedBeforeUtc is not null) return false;
        if (_query.Names is { } names && !names.IsMatch(member.Name, isDir))
        {
            if (names.RegexTimedOut) RegexTimedOut = true;
            return false;
        }
        if (!isDir && member.Size >= 0)
        {
            if (_query.MinSize is { } min && member.Size < min) return false;
            if (_query.MaxSize is { } max && member.Size > max) return false;
        }
        if (_query.ModifiedAfterUtc is not null || _query.ModifiedBeforeUtc is not null)
        {
            if (member.Modified <= 0) return false;
            var modified = new DateTime(member.Modified, DateTimeKind.Utc);
            if (_query.ModifiedAfterUtc is { } after && modified < after) return false;
            if (_query.ModifiedBeforeUtc is { } before && modified > before) return false;
        }
        return true;
    }

    /// <summary>A member found in an archive: its folder is the archive's, then its folder inside the archive.</summary>
    private void AddMember(string root, FileInfo archive, ItemRef member)
    {
        var archiveFolder = Path.GetRelativePath(root, archive.DirectoryName ?? root);
        if (archiveFolder == ".") archiveFolder = string.Empty;
        string inside = member.Parent.Path.Replace('/', Path.DirectorySeparatorChar);
        _results.Add(member, Path.Join(archiveFolder, archive.Name, inside));
        long m = Interlocked.Increment(ref Matches);
        if (m < 50 || m % 200 == 0) _results.NotifyChanged();
    }

    private bool IsIgnored(string root, string dir)
    {
        foreach (var entry in _ignored)
            if (entry.Matches(root, dir)) return true;
        return false;
    }

    private bool IsMatch(FileSystemInfo info, bool isDir, CancellationToken ct)
    {
        if (isDir && (!_query.IncludeDirectories || _query.HasContent)) return false;
        var attributes = info.Attributes;
        if ((attributes & _query.AttributesSet) != _query.AttributesSet || (attributes & _query.AttributesClear) != 0) return false;
        if (_query.Names is { } names && !names.IsMatch(info.Name, isDir))
        {
            if (names.RegexTimedOut) RegexTimedOut = true;
            return false;
        }
        if (!isDir)
        {
            long size = ((FileInfo)info).Length;
            if (_query.MinSize is { } min && size < min) return false;
            if (_query.MaxSize is { } max && size > max) return false;
        }
        if (_query.ModifiedAfterUtc is { } after && info.LastWriteTimeUtc < after) return false;
        if (_query.ModifiedBeforeUtc is { } before && info.LastWriteTimeUtc > before) return false;
        if (_query.CreatedAfterUtc is { } createdAfter && info.CreationTimeUtc < createdAfter) return false;
        if (_query.CreatedBeforeUtc is { } createdBefore && info.CreationTimeUtc > createdBefore) return false;
        if (!isDir && _query.HasContent)
        {
            Interlocked.Increment(ref FilesExamined);
            if ((attributes & (FileAttributes.Offline | (FileAttributes)0x440000)) != 0) return false; // never recall cloud files for a search
            return ContainsContent(info.FullName, ct);
        }
        return true;
    }

    private bool ContainsContent(string path, CancellationToken ct)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            return _query.Bytes is { Length: > 0 } pattern ? ContainsBytes(fs, pattern, ct) : ContainsText(fs, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog(SearchLogKind.Inaccessible, path, ex.Message);
            return false;
        }
    }

    /// <summary>The bytes anywhere in the file; the end of each read starts the next, so bytes split across two are found.</summary>
    private bool ContainsBytes(FileStream fs, byte[] pattern, CancellationToken ct)
    {
        var buffer = pattern.Length <= ChunkBytes / 2 ? _bytes ??= new byte[ChunkBytes] : new byte[pattern.Length * 2];
        int carried = 0, n;
        while ((n = fs.Read(buffer, carried, buffer.Length - carried)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            var window = buffer.AsSpan(0, carried + n);
            if (window.IndexOf(pattern) >= 0) return true;
            carried = Math.Min(pattern.Length - 1, window.Length);
            window[^carried..].CopyTo(buffer);
        }
        return false;
    }

    private bool ContainsText(FileStream fs, CancellationToken ct)
    {
        // A session reads one file at a time, so its buffers are made once (per file, they allocated gigabytes).
        var bytes = _bytes ??= new byte[ChunkBytes];
        int hn = fs.Read(bytes, 0, (int)Math.Min(4096, fs.Length));
        var guess = TextDecoding.Detect(bytes.AsSpan(0, hn));
        var encoding = guess.Encoding;
        fs.Position = guess.PreambleLength;
        var text = _query.Text!;
        int overlapChars = _regex is null ? text.Length + 4 : 1024;
        int need = overlapChars + encoding.GetMaxCharCount(ChunkBytes);
        if (_chars is null || _chars.Length < need) _chars = new char[need];
        var chars = _chars;
        var decoder = encoding.GetDecoder();
        int carried = 0, n;
        bool first = true, matchAtEnd = false;
        while ((n = fs.Read(bytes, 0, bytes.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            int c = decoder.GetChars(bytes, 0, n, chars, carried);
            var window = chars.AsSpan(0, carried + c);
            if (_regex is not null)
            {
                if (RegexMatches(window, first, out matchAtEnd)) return true;
            }
            else if (Contains(window, text))
            {
                return true;
            }
            // The end of this window starts the next one, so text split across two reads is found.
            carried = Math.Min(overlapChars, window.Length);
            window[^carried..].CopyTo(chars);
            first = false;
        }
        return matchAtEnd;
    }

    /// <summary>
    /// A regex match in the window. With whole words, a match at the window's first character is left to the window
    /// before, which saw what precedes it, and a match that ends with the window waits for the next read to show what
    /// follows (<paramref name="atEnd"/>: when the file ends there, it counts).
    /// </summary>
    private bool RegexMatches(ReadOnlySpan<char> window, bool first, out bool atEnd)
    {
        atEnd = false;
        try
        {
            if (!_query.WholeWords) return _regex!.IsMatch(window);
            foreach (var match in _regex!.EnumerateMatches(window, first ? 0 : 1))
            {
                if (match.Index + match.Length < window.Length) return true;
                atEnd = true;
            }
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            RegexTimedOut = true;
            atEnd = false;
            return false;
        }
    }

    private byte[]? _bytes;
    private char[]? _chars;

    /// <summary>
    /// The query's text in decoded text. Ignoring case compares linguistically (the current culture), which is about ten
    /// times slower than ordinal comparison. An ordinal match counts at once, and plain ASCII text without control
    /// characters cannot match linguistically where it does not match ordinally, so only other text takes the slow path.
    /// </summary>
    private bool Contains(ReadOnlySpan<char> window, string text)
    {
        if (_query.MatchCase) return window.IndexOf(text, StringComparison.Ordinal) >= 0;
        if (window.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (_plainAsciiText && IsPlainAscii(window)) return false;
        return window.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private static bool IsPlainAscii(ReadOnlySpan<char> text) =>
        System.Text.Ascii.IsValid(text) && text.IndexOfAnyInRange('\0', '\u0008') < 0 && text.IndexOfAnyInRange('\u000E', '\u001F') < 0 && !text.Contains('\u007F');

    private void Add(string root, FileSystemInfo info, bool isDir)
    {
        var parentPath = Path.GetDirectoryName(info.FullName) ?? root;
        var rel = Path.GetRelativePath(root, parentPath);
        if (rel == ".") rel = string.Empty;
        AddResult(info, isDir, rel);
    }

    private void AddResult(FileSystemInfo info, bool isDir, string relativeFolder)
    {
        var parentPath = Path.GetDirectoryName(info.FullName) ?? info.FullName;
        var item = new ItemRef(Location.FileSystem(parentPath), info.Name, isDir ? EntryKind.Directory : EntryKind.File,
            isDir ? -1 : ((FileInfo)info).Length, info.LastWriteTimeUtc.Ticks);
        _results.Add(item, relativeFolder);
        long m = Interlocked.Increment(ref Matches);
        if (m < 50 || m % 200 == 0) _results.NotifyChanged();
    }

    /// <summary>An entry of the ignore list, as <see cref="SearchQuery.IgnoredFolders"/> describes.</summary>
    private sealed record IgnoredFolder(string Folder, bool Anchored, bool FullPath)
    {
        private static readonly char Sep = Path.DirectorySeparatorChar;

        public static IgnoredFolder? Parse(string entry)
        {
            string e = entry.Trim().Replace('/', Sep);
            bool unc = e.StartsWith(new string(Sep, 2), StringComparison.Ordinal);
            bool full = Path.IsPathFullyQualified(e);
            bool anchored = !unc && e.StartsWith(Sep);
            string folder = Path.TrimEndingDirectorySeparator(e);
            if (anchored && !full) folder = folder.TrimStart(Sep);
            // On Linux and macOS "/build" is both a full path and anchored: it skips either folder.
            return folder.Length == 0 ? null : new IgnoredFolder(folder, anchored, full);
        }

        public bool Matches(string root, string dir)
        {
            var comparison = PathUtil.SafetyComparison;
            string path = Path.TrimEndingDirectorySeparator(dir);
            if (FullPath && string.Equals(path, Folder, comparison)) return true;
            if (Anchored) return string.Equals(path, Path.TrimEndingDirectorySeparator(Path.Join(root, Folder.TrimStart(Sep))), comparison);
            if (FullPath) return false;
            return path.EndsWith(Folder, comparison) && (path.Length == Folder.Length || path[path.Length - Folder.Length - 1] == Sep);
        }
    }
}
