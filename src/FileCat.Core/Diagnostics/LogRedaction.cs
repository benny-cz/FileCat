using System.Text;
using System.Text.RegularExpressions;

namespace FileCat.Core.Diagnostics;

/// <summary>
/// Hashes the paths in text that FileCat does not compose itself: an error's own message, a server's or a helper's text, a
/// crash report (I643). Each path becomes the hash <see cref="AppLog.P"/> gives it, so support can still follow one path
/// from line to line. Two passes find them.
/// <list type="number">
/// <item><b>Quoted.</b> .NET quotes the whole path in its file errors ("Could not find file 'C:\Users\u\a b.txt'.",
/// "Access to the path '/home/u/x' is denied."). A span in quotes ('…', "…", ‘…’ or “…”) whose text begins as an absolute
/// path is hashed whole, spaces included. A quote inside a name (Bob's, Rock 'n' roll, Teachers' notes) does not end it:
/// the closing quote is the first one followed by a space, punctuation or the end after which the rest, up to the next
/// quoted path, holds its quotes in pairs; where none does, the last such quote. Either way more is hashed, never less: a
/// stray apostrophe later in the line (to the users' folder) takes the words before it into the hash.</item>
/// <item><b>Unquoted.</b> A server's error text ("550 /home/u/My files/a.txt: No such file") and FileCat's own messages
/// name paths without quotes. An absolute path that begins a word runs to the end of that word, then across single spaces
/// to the end of the last word, among the next <see cref="MaxBridgedWords"/>, that holds its folder separator, or that
/// ends in ':' or ';' within <see cref="MaxNameWords"/> words of it (C:\Users\jan\Shared Folder: reason). It stops at
/// another path, at marks alone (the " | " before an error's type), and after a word that ends in ':' or ';'; closing
/// punctuation stays outside.</item>
/// </list>
/// A path begins with a drive (C:\ or C:/); \\ (\\server\share, \\?\…, \\.\…); a single \ (\Device\…, \??\…); / (Unix);
/// ~/ or ~\; or an address with a scheme (file:/…, sftp://…, smb://…, https://…).
/// <para>Kept as they are:</para>
/// <list type="bullet">
/// <item>a root alone (C:\, \\?\C:\, /): it names nothing of the user's, and a message about a drive still says which;</item>
/// <item>the source location of a stack frame ("   at M() in D:\a\FileCat\src\X.cs:line 12"): where FileCat was built,
/// which a crash report needs (<see cref="HashPaths"/> only);</item>
/// <item>relative paths and bare names, which no rule tells from other words; so also the end of an unquoted path whose
/// last name holds a space and no ':' follows it ("notes.txt" of "C:\Docs\My notes.txt is missing"), or that runs on past
/// its bridged words;</item>
/// <item>in <see cref="HashPaths"/>, what follows a line break inside a path (a name can hold one on Linux and macOS).</item>
/// </list>
/// Hashing is idempotent: a hash is no path, so text read twice (an exported log) comes out the same.
/// </summary>
internal static partial class LogRedaction
{
    /// <summary>How many words after its first an unquoted path may run on across single spaces.</summary>
    internal const int MaxBridgedWords = 8;

    /// <summary>
    /// Within how many words of the last word with a separator a word ending in ':' or ';' still ends the path's last name
    /// ("…\Shared Folder:", "…\Shared Team Folder:"), not a phrase of the message ("C:\x cannot be opened:").
    /// </summary>
    internal const int MaxNameWords = 2;

    /// <summary>
    /// A crash report or a log file: each line is read on its own, so a quote that never closes cannot carry the hash
    /// into the lines after it, and a stack frame's line keeps its source location.
    /// </summary>
    internal static string HashPaths(string text)
    {
        if (!MayHoldPath(text)) return text;
        var result = new StringBuilder(text.Length + 16);
        int start = 0;
        while (true)
        {
            int newline = text.IndexOf('\n', start);
            int end = newline < 0 ? text.Length : newline;
            string line = text[start..end];
            result.Append(!MayHoldPath(line) || StackFrame().IsMatch(line) ? line : HashRecord(line));
            if (newline < 0) return result.ToString();
            result.Append('\n');
            start = newline + 1;
        }
    }

    /// <summary>One log record (a message and an error's text) as one line, however many lines its text holds.</summary>
    internal static string HashRecord(string text) =>
        MayHoldPath(text) ? Unquoted(Quoted(Quoted(text, IsSingleQuote), IsDoubleQuote)) : text;

    /// <summary>Every form of path holds a slash or a backslash.</summary>
    private static bool MayHoldPath(string text) => text.AsSpan().IndexOfAny('\\', '/') >= 0;

    /// <summary>"   at Type.Method(…) in D:\a\FileCat\src\X.cs:line 12": a frame of a stack trace and its source file.</summary>
    [GeneratedRegex(@"^\s*at \S.* in .+:line \d+\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex StackFrame();

    private static bool IsSingleQuote(char c) => c is '\'' or (char)0x2018 or (char)0x2019;

    private static bool IsDoubleQuote(char c) => c is '"' or (char)0x201C or (char)0x201D;

    private static bool IsQuote(char c) => IsSingleQuote(c) || IsDoubleQuote(c);

    /// <summary>The quoted paths of one kind of quote, each hashed whole (rule 1).</summary>
    private static string Quoted(string text, Func<char, bool> isQuote)
    {
        // An opening quote stands after no letter or digit (not an apostrophe: it's, O'Brien) and before a path.
        List<int>? opening = null;
        for (int i = 0; i + 1 < text.Length; i++)
            if (isQuote(text[i]) && (i == 0 || !char.IsLetterOrDigit(text[i - 1])) && BeginsPath(text, i + 1, out _))
                (opening ??= []).Add(i);
        if (opening is null) return text;
        var result = new StringBuilder(text.Length);
        int copied = 0;
        for (int k = 0; k < opening.Count; k++)
        {
            int from = opening[k] + 1, limit = k + 1 < opening.Count ? opening[k + 1] : text.Length;
            int closing = Closing(text, from, limit, isQuote);
            // A quote that never closes hashes all up to the next quoted path, or to the end.
            int end = closing < 0 ? limit : closing;
            result.Append(text, copied, from - copied).Append(Hashed(text[from..end]));
            copied = end;
        }
        return result.Append(text, copied, text.Length - copied).ToString();
    }

    /// <summary>The quote that closes a path quoted from <paramref name="from"/> on, before <paramref name="limit"/>; -1 when none can.</summary>
    private static int Closing(string text, int from, int limit, Func<char, bool> isQuote)
    {
        int first = -1, last = -1, after = 0; // after: the quotes, apostrophes inside words aside, behind the one looked at
        for (int j = limit - 1; j >= from; j--)
        {
            if (!isQuote(text[j])) continue;
            if (EndsSpan(text, j + 1))
            {
                if (last < 0) last = j;
                if (after % 2 == 0) first = j;
            }
            if (!(j > 0 && j + 1 < text.Length && char.IsLetterOrDigit(text[j - 1]) && char.IsLetterOrDigit(text[j + 1]))) after++;
        }
        return first >= 0 ? first : last;
    }

    /// <summary>Whether a quote before <paramref name="i"/> can close a span: the text ends, or a space or punctuation follows (not a name or a separator).</summary>
    private static bool EndsSpan(string text, int i) => i >= text.Length || !(char.IsLetterOrDigit(text[i]) || text[i] is '\\' or '/' or '_');

    /// <summary>The unquoted paths, each hashed (rule 2).</summary>
    private static string Unquoted(string text)
    {
        StringBuilder? result = null;
        int copied = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (!BeginsWord(text, i) || !BeginsPath(text, i, out char separator)) continue;
            int end = PathEnd(text, i, separator);
            string path = text[i..end];
            if (!IsRootOnly(path))
            {
                result ??= new StringBuilder(text.Length);
                result.Append(text, copied, i - copied).Append(AppLog.Hash(path));
                copied = end;
            }
            i = end - 1;
        }
        return result is null ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    /// <summary>Whether a path can begin at <paramref name="i"/>: at the start, or after a space, a quote, an opening bracket, or = , ; : |.</summary>
    private static bool BeginsWord(string text, int i) =>
        i == 0 || char.IsWhiteSpace(text[i - 1]) || IsQuote(text[i - 1]) || text[i - 1] is '(' or '[' or '{' or '=' or ',' or ';' or ':' or '|';

    /// <summary>Whether an absolute path or an address begins at <paramref name="i"/>, and the folder separator it uses.</summary>
    private static bool BeginsPath(string text, int i, out char separator)
    {
        separator = '\0';
        if (i + 1 >= text.Length) return false;
        char a = text[i], b = text[i + 1];
        if (char.IsAsciiLetter(a) && b == ':')
        {
            // A drive: C:\ or C:/ (a scheme has two letters or more).
            if (i + 2 >= text.Length || text[i + 2] is not ('\\' or '/')) return false;
            separator = text[i + 2];
            return true;
        }
        if (a is '\\' or '/')
        {
            // \\server\share, \\?\C:\…, \\.\…, \??\…, \Device\…, /home/…: so long as a name or a separator follows.
            if (char.IsWhiteSpace(b) || IsQuote(b)) return false;
            separator = a;
            return true;
        }
        if (a == '~' && b is '\\' or '/')
        {
            separator = b;
            return true;
        }
        if (!char.IsAsciiLetter(a)) return false;
        int colon = i + 1;
        while (colon < text.Length && (char.IsAsciiLetterOrDigit(text[colon]) || text[colon] is '+' or '-' or '.')) colon++;
        // An address: scheme:// (sftp://host/…, smb://server/share), or file: with a path (file:/x, file:///C:/x).
        if (colon + 1 >= text.Length || text[colon] != ':' || text[colon + 1] != '/') return false;
        if (!(colon + 2 < text.Length && text[colon + 2] == '/') && !text.AsSpan(i, colon - i).Equals("file", StringComparison.OrdinalIgnoreCase))
            return false;
        separator = '/';
        return true;
    }

    /// <summary>Where an unquoted path that begins at <paramref name="start"/> ends (rule 2).</summary>
    private static int PathEnd(string text, int start, char separator)
    {
        int end = WordEnd(text, start), cursor = end, sinceSeparator = 0;
        for (int words = 0; words < MaxBridgedWords; words++)
        {
            // "C:\x: reason": a word that ends in ':' or ';' ends the path, or whatever runs on past it.
            if (text[cursor - 1] is ':' or ';') break;
            int next = cursor + 1;
            if (next >= text.Length || text[cursor] != ' ' || char.IsWhiteSpace(text[next]) || BeginsPath(text, next, out _)) break;
            var word = text.AsSpan(next, WordEnd(text, next) - next);
            // Marks alone (the " | " before an error's type, " · ", " - ") are no part of a name.
            if (!HasLetterOrDigit(word)) break;
            cursor = next + word.Length;
            if (word.Contains(separator)) (end, sinceSeparator) = (cursor, 0);
            // "C:\Users\jan\Shared Folder: reason": the words of a last name run to the ':' that follows them (a root has
            // none: "C:\ cannot be scanned: …" keeps its words).
            else if (text[cursor - 1] is ':' or ';' && sinceSeparator < MaxNameWords && !IsRootOnly(text[start..end])) end = cursor;
            else sinceSeparator++;
        }
        while (end > start + 1 && ClosesSentence(text, start, end)) end--;
        return end;
    }

    private static int WordEnd(string text, int i)
    {
        while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        return i;
    }

    private static bool HasLetterOrDigit(ReadOnlySpan<char> word)
    {
        foreach (char c in word)
            if (char.IsLetterOrDigit(c)) return true;
        return false;
    }

    /// <summary>Whether the path's last character belongs to the sentence: . , ; : ! ? a quote, or a bracket that closes nothing in the path.</summary>
    private static bool ClosesSentence(string text, int start, int end)
    {
        char last = text[end - 1];
        if (last is '.' or ',' or ';' or ':' or '!' or '?' || IsQuote(last)) return true;
        char open = last switch { ')' => '(', ']' => '[', '}' => '{', _ => '\0' };
        if (open == '\0') return false;
        var path = text.AsSpan(start, end - start);
        return path.Count(last) > path.Count(open);
    }

    /// <summary>A root alone, which names nothing of the user's: C:\, C:/, \\?\C:\, \\.\C:, \, /, ~/.</summary>
    private static bool IsRootOnly(string path)
    {
        ReadOnlySpan<char> rest = path;
        if (rest.StartsWith(@"\\?\", StringComparison.Ordinal) || rest.StartsWith(@"\\.\", StringComparison.Ordinal)) rest = rest[4..];
        if (rest.Length >= 2 && char.IsAsciiLetter(rest[0]) && rest[1] == ':') rest = rest[2..];
        else if (rest.Length >= 1 && rest[0] == '~') rest = rest[1..];
        foreach (char c in rest)
            if (c is not ('\\' or '/')) return false;
        return true;
    }

    private static string Hashed(string path) => IsRootOnly(path) ? path : AppLog.Hash(path);
}
