using System.Text;
using System.Text.RegularExpressions;

namespace FileCat.Core.Selection;

/// <summary>
/// The one mask language shared by selection, quick filter, search, copy filters, and compare (§11):
/// <c>;</c> or <c>,</c> separate masks, <c>|</c> starts exclusions, <c>/…/</c> (optionally <c>/…/i</c>) is a
/// regular expression, quotes protect separators, <c>**</c> spans directories in path matching, and a
/// trailing <c>\</c> or <c>/</c> restricts a mask to directories. Name matching is case-insensitive.
/// </summary>
public sealed class Mask
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private readonly MaskPart[] _include;
    private readonly MaskPart[] _exclude;

    private Mask(string text, MaskPart[] include, MaskPart[] exclude)
    {
        Text = text;
        _include = include;
        _exclude = exclude;
    }

    public string Text { get; }

    /// <summary>True when the mask matches every name ("*", "*.*", or empty).</summary>
    public bool IsMatchAll => _exclude.Length == 0 && (_include.Length == 0 || _include.Any(p => p.IsMatchAll));

    /// <summary>Set when a regular expression timed out; such names were treated as non-matches.</summary>
    public bool RegexTimedOut { get; private set; }

    public static Mask All { get; } = new("*", [], []);

    public static bool TryParse(string? text, out Mask mask, out string? error)
    {
        error = null;
        text ??= string.Empty;
        try
        {
            var (inc, exc) = SplitTopLevel(text);
            mask = new Mask(text, inc.Select(ParsePart).ToArray(), exc.Select(ParsePart).ToArray());
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            mask = All;
            return false;
        }
    }

    public static Mask Parse(string text) =>
        TryParse(text, out var m, out var error) ? m : throw new FormatException(error);

    /// <summary>Matches a single item name.</summary>
    public bool IsMatch(string name, bool isDirectory = false)
    {
        bool included = _include.Length == 0 || Any(_include, name, isDirectory, pathMode: false);
        return included && !Any(_exclude, name, isDirectory, pathMode: false);
    }

    /// <summary>Matches a relative path ("src/app/x.cs") so that <c>**</c> spans directories.</summary>
    public bool IsMatchPath(string relativePath, bool isDirectory = false)
    {
        var normalized = relativePath.Replace('\\', '/');
        bool included = _include.Length == 0 || Any(_include, normalized, isDirectory, pathMode: true);
        return included && !Any(_exclude, normalized, isDirectory, pathMode: true);
    }

    private bool Any(MaskPart[] parts, string value, bool isDirectory, bool pathMode)
    {
        foreach (var p in parts)
        {
            if (p.DirectoriesOnly && !isDirectory) continue;
            try
            {
                if (p.IsMatch(value, pathMode)) return true;
            }
            catch (RegexMatchTimeoutException)
            {
                RegexTimedOut = true;
            }
        }
        return false;
    }

    public override string ToString() => Text;

    private static (List<string> Include, List<string> Exclude) SplitTopLevel(string text)
    {
        var include = new List<string>();
        var exclude = new List<string>();
        var current = include;
        var sb = new StringBuilder();
        bool inQuotes = false, inRegex = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inRegex)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < text.Length) sb.Append(text[++i]);
                else if (c == '/') inRegex = false;
                continue;
            }
            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
                continue;
            }
            if (!inQuotes && c == '/' && sb.ToString().Trim().Length == 0)
            {
                inRegex = true;
                sb.Append(c);
                continue;
            }
            if (!inQuotes && (c == ';' || c == ','))
            {
                Flush(sb, current);
                continue;
            }
            if (!inQuotes && c == '|' && ReferenceEquals(current, include))
            {
                Flush(sb, current);
                current = exclude;
                continue;
            }
            sb.Append(c);
        }
        if (inQuotes) throw new ArgumentException("Unclosed quote in mask.");
        if (inRegex) throw new ArgumentException("Unclosed regular expression in mask.");
        Flush(sb, current);
        return (include, exclude);

        static void Flush(StringBuilder sb, List<string> list)
        {
            var s = sb.ToString().Trim();
            if (s.Length > 0) list.Add(s);
            sb.Clear();
        }
    }

    private static MaskPart ParsePart(string token)
    {
        if (token.Length >= 2 && token[0] == '/')
        {
            int close = token.LastIndexOf('/');
            if (close > 0)
            {
                var flags = token[(close + 1)..];
                if (flags.All(f => f is 'i' or 'I'))
                {
                    var options = RegexOptions.CultureInvariant | (flags.Length > 0 ? RegexOptions.IgnoreCase : 0);
                    return new MaskPart(null, new Regex(token[1..close], options, RegexTimeout), false);
                }
            }
        }
        var glob = token.Length >= 2 && token[0] == '"' && token[^1] == '"' ? token[1..^1] : token.Replace("\"", "");
        bool dirOnly = glob.Length > 1 && (glob.EndsWith('\\') || glob.EndsWith('/'));
        if (dirOnly) glob = glob[..^1];
        if (glob == "*.*") glob = "*";
        return new MaskPart(glob, null, dirOnly);
    }

    private sealed class MaskPart(string? glob, Regex? regex, bool directoriesOnly)
    {
        private Regex? _pathRegex;

        public bool DirectoriesOnly { get; } = directoriesOnly;

        public bool IsMatchAll => glob == "*" && !DirectoriesOnly;

        public bool IsMatch(string value, bool pathMode)
        {
            if (regex is not null) return regex.IsMatch(value);
            var g = glob!;
            if (!pathMode)
            {
                if (g == "*.") return !value.Contains('.');
                return Wildcard.IsMatch(value, g.Replace("**", "*"));
            }
            // A mask without a separator matches the last path segment, like a name mask.
            if (!g.Contains('/') && !g.Contains('\\'))
            {
                int slash = value.LastIndexOf('/');
                return Wildcard.IsMatch(slash >= 0 ? value[(slash + 1)..] : value, g.Replace("**", "*"));
            }
            _pathRegex ??= Wildcard.ToPathRegex(g.Replace('\\', '/'), RegexTimeout);
            return _pathRegex.IsMatch(value);
        }
    }
}

/// <summary>Case-insensitive wildcard matching: <c>*</c> any run, <c>?</c> one character.</summary>
public static class Wildcard
{
    /// <summary>Linear-time greedy matcher with single-star backtracking (names never contain separators here).</summary>
    public static bool IsMatch(string text, string pattern)
    {
        int t = 0, p = 0, starP = -1, starT = -1;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                starP = ++p;
                starT = t;
                continue;
            }
            if (p < pattern.Length && (pattern[p] == '?' || CharEq(pattern[p], text[t])))
            {
                p++;
                t++;
                continue;
            }
            if (starP >= 0)
            {
                t = ++starT;
                p = starP;
                continue;
            }
            return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    /// <summary>
    /// Path glob to regex: <c>**/</c> spans zero or more directories, <c>**</c> anything, <c>*</c> and <c>?</c>
    /// stay within one segment.
    /// </summary>
    public static Regex ToPathRegex(string glob, TimeSpan timeout)
    {
        var sb = new StringBuilder("^");
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                bool slash = i + 2 < glob.Length && glob[i + 2] == '/';
                sb.Append(slash ? "(?:.*/)?" : ".*");
                i += slash ? 2 : 1;
            }
            else if (c == '*') sb.Append("[^/]*");
            else if (c == '?') sb.Append("[^/]");
            else sb.Append(Regex.Escape(c.ToString()));
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, timeout);
    }

    private static bool CharEq(char a, char b) =>
        a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
}
