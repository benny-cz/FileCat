using System.Globalization;
using System.Text;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

/// <summary>What content search finds (plan §11): across read boundaries, in any case, in any language.</summary>
public sealed class SearchContentTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private List<string> Find(string text, bool matchCase = false, bool regex = false, bool unicode = false, bool wholeWords = false)
    {
        var set = new ResultSet("t", "t", "t");
        new SearchSession(new SearchQuery { Roots = [_dir.Path], Text = text, MatchCase = matchCase, Regex = regex, Unicode = unicode, WholeWords = wholeWords, IncludeDirectories = false }, set)
            .Run(TestContext.Current.CancellationToken);
        Assert.True(set.IsComplete);
        return [.. set.Snapshot().Select(i => i.Item.Name).Order()];
    }

    [Fact]
    public void Text_split_across_two_reads_is_found_in_either_case_mode()
    {
        // The needle straddles the 1 MiB read boundary.
        var content = new StringBuilder(new string('x', 1024 * 1024 - 3)).Append("Needle").Append('y', 100).ToString();
        File.WriteAllText(Path.Combine(_dir.Path, "split.txt"), content);
        Assert.Equal(["split.txt"], Find("needle"));
        Assert.Equal(["split.txt"], Find("Needle", matchCase: true));
        Assert.Empty(Find("needle", matchCase: true));
        Assert.Equal(["split.txt"], Find("Nee+dle", matchCase: true, regex: true));
    }

    [Fact]
    public void Ignoring_case_finds_letters_inside_any_language_and_accented_words()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "czech.txt"), "Na horách stojí chata. PŘÍLIŠ ŽLUŤOUČKÝ KŮŇ.", Encoding.UTF8);
        File.WriteAllText(Path.Combine(_dir.Path, "utf16.txt"), "Unicode Needle here", Encoding.Unicode);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            // Czech collation treats "ch" as one letter, so a linguistic search alone did not find "hata" in "chata".
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
            Assert.Equal(["czech.txt"], Find("HATA"));
            Assert.Equal(["czech.txt"], Find("příliš žluťoučký"));
            Assert.Empty(Find("prilis")); // accents are letters, not case
            Assert.Equal(["utf16.txt"], Find("NEEDLE"));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    /// <summary>
    /// Bytes of a binary file with <paramref name="inside"/> at <paramref name="offset"/>: zeros around it, as string
    /// tables keep their strings, and a byte here and there otherwise. (Bytes read as UTF-16 make characters, often
    /// letters, which would join the text in a whole-word search.)
    /// </summary>
    private void Binary(string name, byte[] inside, int offset, int length = 8192)
    {
        var bytes = new byte[Math.Max(length, offset + inside.Length + 16)];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i % 64 == 5 ? 3 : i % 64 == 9 ? 0x90 : 0);
        inside.CopyTo(bytes, offset);
        File.WriteAllBytes(Path.Combine(_dir.Path, name), bytes);
    }

    [Fact]
    public void Unicode_finds_text_that_programs_and_other_binary_files_keep_as_UTF16_or_UTF8()
    {
        Binary("resources.bin", Encoding.Unicode.GetBytes("Needle Příliš"), 1000);
        // .NET's user strings, among others, start at odd offsets.
        Binary("odd.bin", Encoding.Unicode.GetBytes("Needle"), 1001);
        // Fonts name themselves in UTF-16 big-endian.
        Binary("font.bin", Encoding.BigEndianUnicode.GetBytes("Needle"), 2000);
        Binary("utf8.bin", Encoding.UTF8.GetBytes("Příliš žluťoučký kůň"), 3000);
        // UTF-16 split across the 1 MiB read boundary, at an odd offset.
        Binary("split.bin", Encoding.Unicode.GetBytes("Needle"), 1024 * 1024 - 5, 1024 * 1024 + 4096);
        File.WriteAllText(Path.Combine(_dir.Path, "plain.txt"), "nothing to find here");

        // Without it, the text is only looked for as each file reads: binary files as single bytes, compared exactly
        // (a linguistic comparison, skipping the zeros, found "Needle" in UTF-16 when case was ignored).
        Assert.Empty(Find("Needle", matchCase: true));
        Assert.Empty(Find("needle"));
        Assert.Empty(Find("příliš"));
        string[] utf16 = ["font.bin", "odd.bin", "resources.bin", "split.bin"];
        Assert.Equal(utf16, Find("Needle", matchCase: true, unicode: true));
        Assert.Equal(utf16, Find("NEEDLE", unicode: true));
        Assert.Empty(Find("NEEDLE", matchCase: true, unicode: true));
        Assert.Equal(["resources.bin", "utf8.bin"], Find("PŘÍLIŠ", unicode: true));
        Assert.Equal(utf16, Find("Nee+dle", matchCase: true, regex: true, unicode: true));
        Assert.Equal(utf16, Find("needle", wholeWords: true, unicode: true));
        Assert.Equal(["utf8.bin"], Find(@"k\w{2}\b", regex: true, unicode: true));
    }

    [Fact]
    public void Letters_with_zero_bytes_between_them_are_not_the_text()
    {
        // "H", zero, "i": neither the text "Hi" in single bytes nor in UTF-16 (a linguistic comparison skips zeros).
        Binary("apart.bin", [(byte)'H', 0, 0, 0, (byte)'i', 0], 500);
        Assert.Empty(Find("hi"));
        Assert.Empty(Find("hi", unicode: true));
        Assert.Empty(Find("Hi", matchCase: true, unicode: true));
    }
}
