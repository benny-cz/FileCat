using System.Globalization;
using System.Text;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

/// <summary>What content search finds (plan §11): across read boundaries, in any case, in any language.</summary>
public sealed class SearchContentTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private List<string> Find(string text, bool matchCase = false, bool regex = false)
    {
        var set = new ResultSet("t", "t", "t");
        new SearchSession(new SearchQuery { Roots = [_dir.Path], Text = text, MatchCase = matchCase, Regex = regex, IncludeDirectories = false }, set)
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
}
