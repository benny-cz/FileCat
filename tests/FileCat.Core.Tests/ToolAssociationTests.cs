using FileCat.Core.State;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

public sealed class ToolAssociationTests
{
    [Fact]
    public void Associations_parse_validate_and_pick_the_first_match_per_intent()
    {
        var list = Associations.Parse("""
            # comment
            *.pdf | view | C:\Tools\SumatraPDF.exe | {file}
            *.log;*.txt | edit | C:\Tools\notepad++.exe
            *.* | open | C:\Tools\opener.exe | --open {file}
            """, out var error);
        Assert.Null(error);
        Assert.Equal(3, list.Count);
        Assert.Equal("SumatraPDF", Associations.Find(list, Associations.View, "Manual.PDF")!.Name);
        Assert.Null(Associations.Find(list, Associations.Edit, "Manual.pdf"));
        Assert.Equal(["{file}"], Associations.Find(list, Associations.Edit, "app.log")!.Arguments);
        Assert.Equal(["--open", "{file}"], Associations.Find(list, Associations.Open, "x.bin")!.Arguments);
        Assert.Equal(list.Count, Associations.Parse(Associations.Format(list), out _).Count);

        Associations.Parse("*.pdf | print | a.exe", out error);
        Assert.Contains("view (F3), edit (F4), or open", error);
        Associations.Parse("*.pdf | view", out error);
        Assert.NotNull(error);
        Associations.Parse("\"unclosed | view | a.exe", out error);
        Assert.Contains("not a valid mask", error);
    }

    [Theory]
    [InlineData("0.1.0-preview", "v0.1.0", true)]
    [InlineData("0.1.0-preview", "0.1.0-preview", false)]
    [InlineData("0.1.0", "v0.2.0-beta", true)]
    [InlineData("0.2.0", "v0.1.9", false)]
    [InlineData("1.0.0+abc", "1.0.0", false)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", true)]
    [InlineData("1.0.0", "garbage", false)]
    [InlineData("1.2", "1.2.1", true)]
    public void Release_versions_compare_like_semver(string current, string candidate, bool newer) =>
        Assert.Equal(newer, ReleaseVersion.IsNewer(current, candidate));
}
