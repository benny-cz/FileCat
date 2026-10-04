using System.Globalization;
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
    /// <summary>
    /// The text is also found where a file keeps it as UTF-16 (either byte order, at any offset) or UTF-8: in programs,
    /// fonts, the Registry's files, and other binary files, not only in text files in their own encoding.
    /// </summary>
    public bool Unicode { get; init; }
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
    /// When set, a match must carry a stream or attribute besides its download mark (D-55): what hidden payloads,
    /// WSL's metadata, or a program's own notes leave. Checked last, only for items that match everything else.
    /// </summary>
    public HiddenData.IHiddenData? CarriesHiddenData { get; init; }

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

    /// <summary>Revalidates archive members already in a result set, independently of discovering more archives.</summary>
    public IArchiveResultLookup? ResultArchives { get; init; }

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
        else if (!string.IsNullOrEmpty(Text))
        {
            string notes = string.Join(", ", new[] { WholeWords ? "whole words" : null, Unicode ? "UTF-16 and UTF-8 too" : null }.OfType<string>());
            parts.Add((Regex ? "regex " : "text ") + $"\"{Text}\"" + (notes.Length > 0 ? $" ({notes})" : ""));
        }
        if (MinSize is not null || MaxSize is not null) parts.Add("size filter");
        if (ModifiedAfterUtc is not null || ModifiedBeforeUtc is not null) parts.Add("date filter");
        if (CreatedAfterUtc is not null || CreatedBeforeUtc is not null) parts.Add("creation date filter");
        if ((AttributesSet | AttributesClear) != 0) parts.Add("attribute filter");
        if (CarriesHiddenData is not null) parts.Add("carrying streams or attributes");
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
    /// <summary>A provider reported a partial listing or another warning while earlier results were rechecked.</summary>
    Warning,
}

/// <summary>A line of a search's log (plan §11): skipped scope, provider warnings, and their original locations.</summary>
public sealed record SearchLogEntry(SearchLogKind Kind, string Path, string? Detail = null, Location? Parent = null, string? Name = null)
{
    public string Describe() => Kind switch
    {
        SearchLogKind.Inaccessible => $"Not searched (inaccessible): {Path}" + (Detail is null ? string.Empty : $" · {Detail}"),
        SearchLogKind.Ignored => $"Not searched (on the ignore list): {Path}",
        SearchLogKind.Skipped => $"Not searched (skipped while searching): {Path}",
        SearchLogKind.Warning => $"Search warning: {Path}" + (Detail is null ? string.Empty : $" · {Detail}"),
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
                foreach (var typed in _query.Roots)
                {
                    ct.ThrowIfCancellationRequested();
                    // As the disk spells it: a root typed in another case names the same folders on Windows, and its finds
                    // must be the same items as through any other spelling (appended searches list each file once).
                    string root = PathUtil.WithDiskCase(typed);
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

    private void AddLog(SearchLogKind kind, string path, string? detail = null, Location? parent = null, string? name = null)
    {
        lock (_logLock)
        {
            if (_log.Count < MaxLog) _log.Add(new SearchLogEntry(kind, path, detail, parent, name));
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
        // Batch each archive parent once. Only that parent's requested identities and current metadata are retained;
        // neither sibling folders nor members that were absent from the original result set become search results.
        foreach (var group in items.Where(r => !r.Item.Parent.IsFileSystem).GroupBy(r => r.Item.Parent))
        {
            ct.ThrowIfCancellationRequested();
            var parent = group.Key;
            CurrentFolder = parent.ToString();
            string? unavailable = parent.Scheme is not (Schemes.Zip or Schemes.Archive)
                ? "This result cannot be searched within: only local files and archive members are supported."
                : _query.HasContent ? "Archive member contents are not searched."
                : null;
            var lookup = _query.ResultArchives ?? _query.Archives as IArchiveResultLookup;
            if (unavailable is not null || lookup is null)
            {
                foreach (var (item, _) in group)
                {
                    ct.ThrowIfCancellationRequested();
                    AddLog(SearchLogKind.Inaccessible, item.ToString(), unavailable ?? "The archive member lookup is unavailable.", parent, item.Name);
                }
                continue;
            }
            IReadOnlyDictionary<ItemRef, ItemRef> current;
            bool partial = false;
            try
            {
                current = lookup.Revalidate(parent, group.Select(r => r.Item).ToHashSet(), issue =>
                {
                    partial = true;
                    AddLog(SearchLogKind.Warning, parent.ToString(), issue, parent);
                }, ct);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
            {
                var kind = ex is FileNotFoundException or DirectoryNotFoundException ? SearchLogKind.Gone : SearchLogKind.Inaccessible;
                AddLog(kind, parent.ToString(), ex.Message, parent);
                continue;
            }
            foreach (var (original, relative) in group)
            {
                ct.ThrowIfCancellationRequested();
                if (!current.TryGetValue(original, out var member))
                {
                    AddLog(partial ? SearchLogKind.Inaccessible : SearchLogKind.Gone, original.ToString(),
                        partial ? "The archive listing was partial; this member could not be rechecked." : null, parent, original.Name);
                    continue;
                }
                if (MemberMatches(member))
                {
                    _results.Add(member, relative);
                    long m = Interlocked.Increment(ref Matches);
                    if (m < 50 || m % 200 == 0) _results.NotifyChanged();
                }
            }
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
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var member in archives.List(archive.FullName, issue =>
            {
                // Providers repeat archive-wide warnings in each member folder. Keep the search log useful and bounded.
                if (warnings.Count < MaxLog && warnings.Add(issue))
                    AddLog(SearchLogKind.Warning, archive.FullName, issue, Location.FileSystem(archive.DirectoryName ?? root), archive.Name);
            }, ct))
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
        // An archive's members carry no streams or attributes of their own.
        if (_query.CarriesHiddenData is not null) return false;
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
            if (!ContainsContent(info.FullName, ct)) return false;
        }
        return _query.CarriesHiddenData is not { } hidden || CarriesHiddenData(hidden, info.FullName);
    }

    private static bool CarriesHiddenData(HiddenData.IHiddenData hidden, string path)
    {
        try
        {
            return hidden.List(path).Any(i => !HiddenData.DownloadMarks.IsDownloadMark(i.Name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
        var views = ViewsFor(guess);
        var text = _query.Text!;
        int overlapChars = _regex is null ? text.Length + 4 : 1024;
        fs.Position = 0;
        long at = 0;
        int n;
        while ((n = fs.Read(bytes, 0, bytes.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var view in views)
            {
                // A view starts where its text starts: after a byte-order mark, or one byte in for UTF-16 at odd offsets.
                int from = (int)Math.Clamp(view.Start - at, 0, n);
                if (from == n) continue;
                int c = view.Decoder.GetChars(bytes, from, n - from, view.Chars, view.Carried);
                var window = view.Chars.AsSpan(0, view.Carried + c);
                if (_regex is not null)
                {
                    if (RegexMatches(window, view.First, out view.MatchAtEnd)) return true;
                }
                else if (view.Ordinal ? window.IndexOf(text, _query.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) >= 0 : Contains(window, text, out view.MatchAtEnd))
                {
                    return true;
                }
                // The end of this window starts the next one, so text split across two reads is found.
                view.Carried = Math.Min(overlapChars, window.Length);
                window[^view.Carried..].CopyTo(view.Chars);
                view.First = false;
            }
            at += n;
        }
        return views.Any(v => v.MatchAtEnd);
    }

    /// <summary>
    /// How a file's bytes are read as text: in the encoding its start suggests, and with <see cref="SearchQuery.Unicode"/>
    /// also as UTF-16 in either byte order from even and from odd offsets (strings in programs and other binary files:
    /// .NET's user strings start at odd ones), and as UTF-8 where the file is not read so already (text other than
    /// ASCII inside a binary file, and any text inside a file read as UTF-16). Binary files and those views compare
    /// ordinally: a linguistic comparison skips ignorable characters, NUL among them, so "H", zero, "i" in bytes that
    /// are no text at all counted as "Hi".
    /// </summary>
    private List<TextView> ViewsFor(EncodingGuess guess)
    {
        var views = new List<TextView>(6);
        int overlap = _regex is null ? _query.Text!.Length + 4 : 1024;
        TextView View(int slot, Encoding encoding, int start, bool ordinal)
        {
            int need = overlap + encoding.GetMaxCharCount(ChunkBytes);
            var chars = _viewChars[slot] is { } kept && kept.Length >= need ? kept : _viewChars[slot] = new char[need];
            return new TextView(encoding.GetDecoder(), chars, start, ordinal);
        }
        views.Add(View(0, guess.Encoding, guess.PreambleLength, ordinal: guess.LooksBinary));
        if (!_query.Unicode) return views;
        bool mainUtf16 = guess.Encoding is UnicodeEncoding;
        bool mainBigEndian = mainUtf16 && guess.Encoding.CodePage == 1201;
        foreach (var (bigEndian, start, slot) in new[] { (false, 0, 1), (false, 1, 2), (true, 0, 3), (true, 1, 4) })
        {
            // The file's own UTF-16 at even offsets is the first view already.
            if (mainUtf16 && mainBigEndian == bigEndian && start == 0) continue;
            views.Add(View(slot, new UnicodeEncoding(bigEndian, byteOrderMark: false, throwOnInvalidBytes: false), start, ordinal: true));
        }
        // ASCII text reads the same in UTF-8 as in an 8-bit reading of the file, not in a UTF-16 one (release plan V13: a
        // UTF-16 file holding the word as UTF-8 was not found); a regular expression may match other text.
        if (guess.Encoding is not UTF8Encoding && (_query.Regex || !Ascii.IsValid(_query.Text!) || mainUtf16))
            views.Add(View(5, new UTF8Encoding(false, throwOnInvalidBytes: false), 0, ordinal: true));
        return views;
    }

    /// <summary>A file's bytes read as text one way (<see cref="ViewsFor"/>), with the end of its last window kept.</summary>
    private sealed class TextView(Decoder decoder, char[] chars, int start, bool ordinal)
    {
        public Decoder Decoder { get; } = decoder;
        public char[] Chars { get; } = chars;
        /// <summary>The file offset its text starts at.</summary>
        public int Start { get; } = start;
        /// <summary>Compared ordinally, not linguistically (see <see cref="ViewsFor"/>).</summary>
        public bool Ordinal { get; } = ordinal;
        public int Carried;
        public bool First = true;
        public bool MatchAtEnd;
    }

    /// <summary>
    /// A regex match in the window. A window's edges are not the file's: anchors and look-arounds there see nothing,
    /// so "^word" matched where a read happened to start and "word$" where one happened to end (release plan V13). A
    /// match at the window's first character is left to the window before, which saw what precedes it, and a match that
    /// ends with the window waits for the next read to show what follows (<paramref name="atEnd"/>: when the file ends
    /// there, it counts). Whole words rely on the same: their guards are look-arounds.
    /// </summary>
    private bool RegexMatches(ReadOnlySpan<char> window, bool first, out bool atEnd)
    {
        atEnd = false;
        try
        {
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
    /// <summary>The views' character buffers, kept for the next file (see <see cref="ViewsFor"/>).</summary>
    private readonly char[]?[] _viewChars = new char[6][];

    /// <summary>
    /// The query's text in decoded text. Ignoring case compares linguistically (the current culture), which is about ten
    /// times slower than ordinal comparison. An ordinal match counts at once, and plain ASCII text without control
    /// characters cannot match linguistically where it does not match ordinally, so only other text takes the slow path.
    /// </summary>
    private bool Contains(ReadOnlySpan<char> window, string text, out bool atEnd)
    {
        atEnd = false;
        if (_query.MatchCase) return window.IndexOf(text, StringComparison.Ordinal) >= 0;
        if (window.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (_plainAsciiText && IsPlainAscii(window)) return false;
        // A linguistic match compares characters as written, and what follows can still change the last one (an accent
        // written as a mark of its own: "naïve" matched where the file says "naïvë" and the mark began the next read). One
        // that ends with the window waits for the next read, as a regex match does.
        int at = CultureInfo.CurrentCulture.CompareInfo.IndexOf(window, text, CompareOptions.IgnoreCase, out int length);
        if (at < 0) return false;
        if (at + length < window.Length) return true;
        atEnd = true;
        return false;
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
