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
    public bool Recursive { get; init; } = true;
    public int MaxDepth { get; init; } = int.MaxValue;
    public bool IncludeHidden { get; init; } = true;
    public bool IncludeDirectories { get; init; } = true;
    public long? MinSize { get; init; }
    public long? MaxSize { get; init; }
    public DateTime? ModifiedAfterUtc { get; init; }
    public DateTime? ModifiedBeforeUtc { get; init; }

    /// <summary>
    /// Searches these earlier results instead of folders: each item is tested against the criteria as it is now,
    /// nothing is entered, and matches keep their relative folder (plan §11: searching within results narrows the set).
    /// </summary>
    public IReadOnlyList<(ItemRef Item, string Relative)>? WithinResults { get; init; }

    public string Describe()
    {
        var parts = new List<string>();
        if (Names is { IsMatchAll: false }) parts.Add($"names \"{Names.Text}\"");
        if (!string.IsNullOrEmpty(Text)) parts.Add((Regex ? "regex " : "text ") + $"\"{Text}\"");
        if (MinSize is not null || MaxSize is not null) parts.Add("size filter");
        if (ModifiedAfterUtc is not null || ModifiedBeforeUtc is not null) parts.Add("date filter");
        var what = parts.Count == 0 ? "all items" : string.Join(", ", parts);
        if (WithinResults is { } within) return $"{what} within {within.Count:N0} earlier results";
        return $"{what} in {string.Join("; ", Roots)}{(Recursive ? "" : " (top level only)")}";
    }
}

/// <summary>
/// Recursive search as a session (plan §11): streams results into a result set of original references,
/// exposes visited/inaccessible scope, supports skipping the current subtree, never follows links, and
/// uses bounded, boundary-safe content scanning with regex timeouts.
/// </summary>
public sealed class SearchSession
{
    private const int ChunkBytes = 1024 * 1024;
    private static readonly EnumerationOptions Options = new() { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false };
    private readonly SearchQuery _query;
    private readonly ResultSet _results;
    private readonly Regex? _regex;
    private readonly bool _plainAsciiText;
    private volatile string? _skip;
    private readonly object _issuesLock = new();
    private readonly List<string> _inaccessible = [];
    private readonly List<string> _gone = [];

    public SearchSession(SearchQuery query, ResultSet results)
    {
        _query = query;
        _results = results;
        if (!string.IsNullOrEmpty(query.Text) && query.Regex)
            _regex = new Regex(query.Text, RegexOptions.CultureInvariant | RegexOptions.Multiline | (query.MatchCase ? 0 : RegexOptions.IgnoreCase), TimeSpan.FromSeconds(1));
        _plainAsciiText = query.Text is { Length: > 0 } t && IsPlainAscii(t);
    }

    public long FoldersVisited;
    public long FilesExamined;
    public long Matches;
    public volatile string? CurrentFolder;
    public volatile bool Finished;
    public volatile bool RegexTimedOut;

    public IReadOnlyList<string> Inaccessible
    {
        get
        {
            lock (_issuesLock) return _inaccessible.ToList();
        }
    }

    /// <summary>Skips the folder currently being searched (FAR/TC behavior).</summary>
    public void SkipCurrentFolder() => _skip = CurrentFolder;

    public static bool TryValidate(SearchQuery q, out string? error)
    {
        error = null;
        if (!string.IsNullOrEmpty(q.Text) && q.Regex)
        {
            try { _ = new Regex(q.Text); }
            catch (ArgumentException ex)
            {
                error = "Regular expression: " + ex.Message;
                return false;
            }
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
            lock (_issuesLock)
            {
                _results.Issues.Clear();
                _results.Issues.AddRange(_inaccessible.Take(50).Select(p => "Not searched (inaccessible): " + p));
                _results.Issues.AddRange(_gone.Take(50).Select(p => "No longer exists (not searched): " + p));
            }
            _results.NotifyChanged();
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
                lock (_issuesLock)
                {
                    if (_gone.Count < 1000) _gone.Add(path);
                }
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
            lock (_issuesLock)
            {
                if (_inaccessible.Count < 1000) _inaccessible.Add(dir);
            }
            return;
        }
        var subdirs = new List<string>();
        foreach (var info in children)
        {
            ct.ThrowIfCancellationRequested();
            if (_skip is { } skip && PathUtil.IsSameOrUnder(dir, skip))
            {
                if (string.Equals(skip, dir, StringComparison.OrdinalIgnoreCase)) _skip = null;
                return;
            }
            bool isDir = info is DirectoryInfo;
            bool isLink = (info.Attributes & FileAttributes.ReparsePoint) != 0;
            bool hidden = (info.Attributes & FileAttributes.Hidden) != 0;
            if (hidden && !_query.IncludeHidden) continue;
            if (isDir && !isLink && _query.Recursive && depth < _query.MaxDepth) subdirs.Add(info.FullName);
            if (IsMatch(info, isDir, ct)) Add(root, info, isDir);
        }
        foreach (var sub in subdirs)
        {
            if (_skip is { } skip && PathUtil.IsSameOrUnder(sub, skip)) continue;
            Walk(root, sub, depth + 1, ct);
        }
        if (_skip is { } s && string.Equals(s, dir, StringComparison.OrdinalIgnoreCase)) _skip = null;
    }

    private bool IsMatch(FileSystemInfo info, bool isDir, CancellationToken ct)
    {
        if (isDir && (!_query.IncludeDirectories || !string.IsNullOrEmpty(_query.Text))) return false;
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
        if (!isDir && !string.IsNullOrEmpty(_query.Text))
        {
            Interlocked.Increment(ref FilesExamined);
            if ((info.Attributes & (FileAttributes.Offline | (FileAttributes)0x440000)) != 0) return false; // never recall cloud files for a search
            return ContainsText(info.FullName, ct);
        }
        return true;
    }

    private bool ContainsText(string path, CancellationToken ct)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
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
            while ((n = fs.Read(bytes, 0, bytes.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                int c = decoder.GetChars(bytes, 0, n, chars, carried);
                var window = chars.AsSpan(0, carried + c);
                if (_regex is not null)
                {
                    try
                    {
                        if (_regex.IsMatch(window)) return true;
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        RegexTimedOut = true;
                        return false;
                    }
                }
                else if (Contains(window, text))
                {
                    return true;
                }
                // The end of this window starts the next one, so text split across two reads is found.
                carried = Math.Min(overlapChars, window.Length);
                window[^carried..].CopyTo(chars);
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_issuesLock)
            {
                if (_inaccessible.Count < 1000) _inaccessible.Add(path);
            }
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
}
