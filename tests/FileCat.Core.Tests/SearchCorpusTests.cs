using System.Text;
using System.Text.RegularExpressions;
using FileCat.Core.Content;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

/// <summary>
/// Release plan V13: content search checked against an independent reading of each file. A corpus of files in the
/// encodings FileCat reads — ASCII, UTF-8 with and without a byte-order mark, UTF-16 in both byte orders with and without
/// one, the system code page, binary — holds words planted in one encoding at chosen offsets: across FileCat's 1 MiB
/// reads, where a read's carried text starts and ends, at odd and even offsets, in another case, decomposed, beside word
/// characters or not. Every query's results are compared, found and not found alike, with what reading each whole file
/// at once in the readings <see cref="SearchQuery.Unicode"/> documents finds. FILECAT_SEARCH_CORPUS_FILES and
/// FILECAT_SEARCH_CORPUS_SEED make a longer or another run.
/// </summary>
public sealed class SearchCorpusTests : IDisposable
{
    private const int Read = 1024 * 1024;
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    // Words of different scripts; "naïve" is also planted decomposed (i, then a combining diaeresis).
    private static readonly string[] Words = ["Quixotic", "žluťoučký", "Straße", "日本語", "naïve"];

    private enum Kind { Ascii, Utf8, Utf8Bom, Utf16Le, Utf16LeBom, Utf16BeBom, Ansi, Binary }

    private enum Form { Own, Utf8, Utf16Le, Utf16Be }

    private sealed record Planted(string Shown, Form Form, int Offset, string Before, string After)
    {
        public override string ToString() => $"\"{Before}{Shown}{After}\" as {Form} at {Offset}";
    }

    private sealed record CorpusFile(string Name, Kind Kind, byte[] Data, List<Planted> Planted);

    [Fact]
    public void Content_search_finds_what_reading_each_whole_file_finds_and_nothing_else()
    {
        int files = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_SEARCH_CORPUS_FILES"), out var f) ? f : 16;
        int seed = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_SEARCH_CORPUS_SEED"), out var s) ? s : 1301;
        var log = TestContext.Current.TestOutputHelper;
        var ct = TestContext.Current.CancellationToken;
        var random = new Random(seed);
        string root = _dir.Dir("corpus");
        // A quarter of the files are larger than one read, with words placed where reads meet.
        var corpus = Enumerable.Range(0, files).Select(i => Make(i, random, atReadEdge: i % 4 == 3)).ToList();
        foreach (var file in corpus) File.WriteAllBytes(Path.Combine(root, file.Name), file.Data);
        var readings = corpus.ToDictionary(c => c.Name, c => Readings(c.Data));

        int found = 0, notFound = 0;
        var wrong = new List<string>();
        foreach (var query in Queries(root))
        {
            var set = new ResultSet("corpus", "corpus", "corpus");
            var session = new SearchSession(query, set);
            session.Run(ct);
            Assert.False(session.RegexTimedOut, query.Describe());
            var results = set.Snapshot().Select(r => r.Item.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var file in corpus)
            {
                bool expected = Expected(readings[file.Name], query);
                bool actual = results.Contains(file.Name);
                if (expected) found++;
                else notFound++;
                if (expected != actual)
                    wrong.Add($"{Describe(query)} in {file.Name} ({file.Data.Length:N0} bytes, read as {readings[file.Name].Main.Evidence}): " +
                              $"{(expected ? "not found, but it is there" : "found, but it is not there")}; planted {string.Join("; ", file.Planted)}");
            }
        }
        log?.WriteLine($"seed {seed}, {files} files ({corpus.Sum(c => (long)c.Data.Length):N0} bytes), {Queries(root).Count()} queries: " +
                       $"{found:N0} expected found, {notFound:N0} expected not found, {wrong.Count} answered otherwise");
        foreach (var line in wrong.Take(40)) log?.WriteLine(line);
        Assert.True(found > 0 && notFound > 0, "The corpus must hold both answers.");
        Assert.True(wrong.Count == 0, $"{wrong.Count} answers differ from reading the whole file; the first: {wrong.FirstOrDefault()}");
    }

    private static string Describe(SearchQuery q) =>
        $"\"{q.Text}\"" + (q.Regex ? " (regex)" : "") + (q.MatchCase ? " match case" : "") + (q.WholeWords ? " whole words" : "") + (q.Unicode ? " Unicode" : "");

    private static IEnumerable<SearchQuery> Queries(string root)
    {
        foreach (string w in Words)
        {
            string e = Regex.Escape(w);
            yield return new() { Roots = [root], Text = w, IncludeDirectories = false };
            yield return new() { Roots = [root], Text = w, MatchCase = true, IncludeDirectories = false };
            yield return new() { Roots = [root], Text = w, Unicode = true, IncludeDirectories = false };
            yield return new() { Roots = [root], Text = w, WholeWords = true, Unicode = true, IncludeDirectories = false };
            yield return new() { Roots = [root], Text = w, MatchCase = true, WholeWords = true, IncludeDirectories = false };
            yield return new() { Roots = [root], Text = "^" + e, Regex = true, IncludeDirectories = false };
            yield return new() { Roots = [root], Text = e + "$", Regex = true, Unicode = true, IncludeDirectories = false };
            yield return new() { Roots = [root], Text = "(?<![ .])" + e, Regex = true, IncludeDirectories = false };
        }
    }

    // ---- the independent reading: each whole file decoded at once, in the readings SearchQuery documents ----

    private sealed record Reading(string Text, bool Ordinal);

    private sealed record FileReadings(EncodingGuess Main, Reading Own, Reading[] Unicode);

    private static FileReadings Readings(byte[] data)
    {
        var guess = TextDecoding.Detect(data.AsSpan(0, Math.Min(4096, data.Length)));
        var own = new Reading(Decode(guess.Encoding, data, guess.PreambleLength), guess.LooksBinary);
        // SearchQuery.Unicode: "also found where a file keeps it as UTF-16 (either byte order, at any offset) or UTF-8".
        var unicode = new[] { (false, 0), (false, 1), (true, 0), (true, 1) }
            .Select(v => new Reading(Decode(new UnicodeEncoding(v.Item1, false, false), data, v.Item2), true))
            .Append(new Reading(Decode(new UTF8Encoding(false, false), data, 0), true)).ToArray();
        return new FileReadings(guess, own, unicode);
    }

    private static string Decode(Encoding encoding, byte[] data, int start) => start >= data.Length ? string.Empty : encoding.GetString(data, start, data.Length - start);

    private static bool Expected(FileReadings file, SearchQuery q) =>
        Matches(file.Own, q) || q.Unicode && file.Unicode.Any(r => Matches(r, q));

    private static bool Matches(Reading reading, SearchQuery q)
    {
        string text = q.Text!;
        if (q.Regex || q.WholeWords)
        {
            var options = RegexOptions.CultureInvariant | RegexOptions.Multiline | (q.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            string pattern = q.Regex ? (q.WholeWords ? $@"(?<!\w)(?:{text})(?!\w)" : text)
                : (Word(text[0]) ? @"(?<!\w)" : "") + Regex.Escape(text) + (Word(text[^1]) ? @"(?!\w)" : "");
            return Regex.IsMatch(reading.Text, pattern, options);
        }
        if (q.MatchCase) return reading.Text.Contains(text, StringComparison.Ordinal);
        if (reading.Text.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        return !reading.Ordinal && reading.Text.Contains(text, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool Word(char c) => char.IsLetterOrDigit(c) || c == '_';

    // ---- the corpus ----

    private static CorpusFile Make(int index, Random r, bool atReadEdge)
    {
        var kinds = Enum.GetValues<Kind>();
        var kind = kinds[r.Next(kinds.Length)];
        int size = atReadEdge ? Read + r.Next(2048, 300_000) : r.Next(300, 24_000);
        var data = Noise(kind, size, r);
        var planted = new List<Planted>();
        int count = 1 + r.Next(3);
        for (int p = 0; p < count; p++)
        {
            string word = Words[r.Next(Words.Length)];
            string shown = r.Next(4) switch { 0 => word.ToUpperInvariant(), 1 => word.ToLowerInvariant(), _ => word };
            if (word == "naïve" && r.Next(2) == 0) shown = shown.Replace("ï", "i" + (char)0x0308).Replace("Ï", "I" + (char)0x0308);
            string before = Neighbour(r), after = Neighbour(r);
            var form = r.Next(3) == 0 ? (Form)(1 + r.Next(3)) : Form.Own;
            byte[] bytes = Encode(kind, form, before + shown + after);
            int header = kind switch { Kind.Utf8Bom => 3, Kind.Utf16LeBom or Kind.Utf16BeBom => 2, _ => 0 };
            if (data.Length - header < bytes.Length + 2) continue;
            int prefix = Encode(kind, form, before).Length;
            int offset = atReadEdge && p == 0
                ? r.Next(3) switch
                {
                    0 => Read - prefix - r.Next(1, Math.Max(2, bytes.Length - prefix)), // the word crosses the end of the first read
                    1 => Read - 1024 - prefix, // the word starts where the second read's carried text starts
                    _ => Read - bytes.Length + Encode(kind, form, after).Length, // the word ends where the first read ends
                }
                : header + r.Next(data.Length - header - bytes.Length);
            offset = Math.Clamp(offset, header, data.Length - bytes.Length);
            // The file's own UTF-16 is read in whole code units from its start.
            if (form == Form.Own && kind is Kind.Utf16Le or Kind.Utf16LeBom or Kind.Utf16BeBom && offset % 2 == 1) offset--;
            bytes.CopyTo(data, offset);
            planted.Add(new Planted(shown, form, offset + prefix, before, after));
        }
        return new CorpusFile($"f{index:000}-{kind}", kind, data, planted);
    }

    private static string Neighbour(Random r) => r.Next(9) switch
    {
        0 => "",
        1 => " ",
        2 => ".",
        3 => "\n",
        4 => "x",
        5 => "_",
        6 => "5",
        7 => "é",
        _ => ((char)0x0308).ToString(), // a combining mark: a word character to a regular expression
    };

    private static Encoding Own(Kind kind) => kind switch
    {
        Kind.Utf16Le or Kind.Utf16LeBom => new UnicodeEncoding(false, false),
        Kind.Utf16BeBom => new UnicodeEncoding(true, false),
        Kind.Ansi => TextDecoding.SystemAnsi,
        _ => new UTF8Encoding(false),
    };

    private static byte[] Encode(Kind kind, Form form, string text) => (form switch
    {
        Form.Utf8 => new UTF8Encoding(false),
        Form.Utf16Le => new UnicodeEncoding(false, false),
        Form.Utf16Be => new UnicodeEncoding(true, false),
        _ => Own(kind),
    }).GetBytes(text);

    private static byte[] Noise(Kind kind, int size, Random r)
    {
        if (kind == Kind.Binary)
        {
            var bytes = new byte[size];
            r.NextBytes(bytes);
            return bytes;
        }
        const string ascii = "abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789 ,.;:-\n";
        string extra = kind switch
        {
            Kind.Ascii => "",
            Kind.Ansi => "éěščřžýáíůúňťď",
            _ => "éěščřžýáíůúňťď日本語の文字",
        };
        var text = new StringBuilder(size);
        while (text.Length < size) text.Append(extra.Length > 0 && r.Next(8) == 0 ? extra[r.Next(extra.Length)] : ascii[r.Next(ascii.Length)]);
        byte[] body = Own(kind).GetBytes(text.ToString());
        byte[] bom = kind switch
        {
            Kind.Utf8Bom => [0xEF, 0xBB, 0xBF],
            Kind.Utf16LeBom => [0xFF, 0xFE],
            Kind.Utf16BeBom => [0xFE, 0xFF],
            _ => [],
        };
        var data = new byte[size];
        bom.CopyTo(data, 0);
        body.AsSpan(0, Math.Min(body.Length, size - bom.Length)).CopyTo(data.AsSpan(bom.Length));
        return data;
    }
}
