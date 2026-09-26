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

    public string Describe()
    {
        var parts = new List<string>();
        if (Names is { IsMatchAll: false }) parts.Add($"names \"{Names.Text}\"");
        if (!string.IsNullOrEmpty(Text)) parts.Add((Regex ? "regex " : "text ") + $"\"{Text}\"");
        if (MinSize is not null || MaxSize is not null) parts.Add("size filter");
        if (ModifiedAfterUtc is not null || ModifiedBeforeUtc is not null) parts.Add("date filter");
        var what = parts.Count == 0 ? "all items" : string.Join(", ", parts);
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
    private volatile string? _skip;
    private readonly object _issuesLock = new();
    private readonly List<string> _inaccessible = [];

    public SearchSession(SearchQuery query, ResultSet results)
    {
        _query = query;
        _results = results;
        if (!string.IsNullOrEmpty(query.Text) && query.Regex)
            _regex = new Regex(query.Text, RegexOptions.CultureInvariant | RegexOptions.Multiline | (query.MatchCase ? 0 : RegexOptions.IgnoreCase), TimeSpan.FromSeconds(1));
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
            foreach (var root in _query.Roots)
            {
                ct.ThrowIfCancellationRequested();
                Walk(root, root, 0, ct);
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
            }
            _results.NotifyChanged();
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
            var head = new byte[Math.Min(4096, (int)Math.Min(fs.Length, int.MaxValue))];
            int hn = fs.Read(head, 0, head.Length);
            var guess = TextDecoding.Detect(head.AsSpan(0, hn));
            var encoding = guess.Encoding;
            fs.Position = guess.PreambleLength;
            var text = _query.Text!;
            int overlapChars = _regex is null ? text.Length + 4 : 1024;
            var buffer = new byte[ChunkBytes];
            var decoder = encoding.GetDecoder();
            var chars = new char[encoding.GetMaxCharCount(ChunkBytes)];
            string carry = string.Empty;
            var comparison = _query.MatchCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;
            int n;
            while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                int c = decoder.GetChars(buffer, 0, n, chars, 0);
                var window = carry + new string(chars, 0, c);
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
                else if (window.Contains(text, comparison))
                {
                    return true;
                }
                carry = window.Length > overlapChars ? window[^overlapChars..] : window;
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

    private void Add(string root, FileSystemInfo info, bool isDir)
    {
        var parentPath = Path.GetDirectoryName(info.FullName) ?? root;
        var rel = Path.GetRelativePath(root, parentPath);
        if (rel == ".") rel = string.Empty;
        var item = new ItemRef(Location.FileSystem(parentPath), info.Name, isDir ? EntryKind.Directory : EntryKind.File,
            isDir ? -1 : ((FileInfo)info).Length, info.LastWriteTimeUtc.Ticks);
        _results.Add(item, rel);
        long m = Interlocked.Increment(ref Matches);
        if (m < 50 || m % 200 == 0) _results.NotifyChanged();
    }
}
