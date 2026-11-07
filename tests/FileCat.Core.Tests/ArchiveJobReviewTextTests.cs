using System.Text;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveJobReviewTextTests
{
    [Fact]
    public void Large_names_and_selections_have_a_bounded_preview_with_visible_truncation()
    {
        string name = new('a', ArchiveWritePolicy.MaxNameChars);
        string preview = ArchiveJobReviewText.Samples(Enumerable.Repeat(name, ArchiveWritePolicy.MaxMembers));
        Assert.True(preview.Length <= ArchiveJobReviewText.MaximumCharacters);
        Assert.Contains("preview shortened", preview); Assert.Contains("additional names omitted", preview);
    }
    [Fact]
    public void Control_characters_and_bidirectional_overrides_cannot_inject_dialog_text()
    {
        string preview = ArchiveJobReviewText.Samples(["file\nReplace everything\r\t", "name\u202Eexe"]);
        Assert.Equal(1, preview.Count(character => character == '\n'));
        Assert.DoesNotContain('\r', preview); Assert.DoesNotContain('\t', preview); Assert.DoesNotContain('\u202E', preview);
        Assert.Contains("\\nReplace", preview); Assert.Contains("\\u202E", preview);
    }
    [Fact]
    public void Canonical_and_combining_spellings_remain_distinguishable_in_the_preview()
    {
        Assert.Contains("é", ArchiveJobReviewText.Samples(["é"]));
        Assert.Contains("e\\u0301", ArchiveJobReviewText.Samples(["é"]));
    }
    [Fact]
    public void Clipped_supplementary_characters_stay_valid_and_the_filename_end_remains_visible()
    {
        string name = new string('a', 127) + "🔐" + new string('b', 1000) + "🔐TAIL";
        string preview = ArchiveJobReviewText.Samples([name]);
        Assert.NotEmpty(new UTF8Encoding(false, true).GetBytes(preview)); Assert.Contains("TAIL", preview);
        Assert.Contains("preview shortened", preview);
    }
    [Fact]
    public void A_preview_does_not_walk_an_unbounded_source_of_names()
    {
        int fetched = 0;
        IEnumerable<string> Names() { while (true) { fetched++; yield return "file"; } }
        string preview = ArchiveJobReviewText.Samples(Names());
        Assert.InRange(fetched, 1, ArchiveJobReviewText.MaximumSamples + 1); Assert.Contains("additional names omitted", preview);
    }
}
