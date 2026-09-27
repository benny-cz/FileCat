using FileCat.Core.Selection;

namespace FileCat.Core.Tests;

public class MaskTests
{
    [Theory]
    [InlineData("*.txt", "a.txt", true)]
    [InlineData("*.txt", "A.TXT", true)]
    [InlineData("*.txt", "a.txt.bak", false)]
    [InlineData("*.*", "noext", true)]
    [InlineData("*", "anything.x", true)]
    [InlineData("a?c", "abc", true)]
    [InlineData("a?c", "ac", false)]
    [InlineData("*.cs;*.axaml", "View.axaml", true)]
    [InlineData("*.cs,*.axaml", "x.cs", true)]
    [InlineData("*.cs|*Test*", "FooTests.cs", false)]
    [InlineData("*.cs|*Test*", "Foo.cs", true)]
    [InlineData("|*.bak", "a.txt", true)]
    [InlineData("|*.bak", "a.bak", false)]
    [InlineData("\"a;b.txt\"", "a;b.txt", true)]
    [InlineData("/^ab+c$/", "abbbc", true)]
    [InlineData("/^ab+c$/", "ABBC", false)]
    [InlineData("/^ab+c$/i", "ABBC", true)]
    [InlineData("*.", "README", true)]
    [InlineData("*.", "a.txt", false)]
    public void Name_matching(string mask, string name, bool expected)
    {
        Assert.Equal(expected, Mask.Parse(mask).IsMatch(name));
        Assert.Equal(expected, Mask.Parse(mask).IsMatch(name.AsSpan()));
    }

    [Fact]
    public void Star_only_fast_path_agrees_with_the_general_matcher()
    {
        // Random star patterns over a small alphabet (case and non-ASCII included) against random names.
        const string alphabet = "abAB.-7ßé";
        var random = new Random(11);
        string Random(int max)
        {
            var chars = new char[random.Next(max + 1)];
            for (int i = 0; i < chars.Length; i++) chars[i] = alphabet[random.Next(alphabet.Length)];
            return new string(chars);
        }
        for (int round = 0; round < 20_000; round++)
        {
            var parts = Enumerable.Range(0, random.Next(1, 5)).Select(_ => Random(3));
            var pattern = string.Join("*", parts);
            if (random.Next(4) == 0) pattern = "*" + pattern;
            var name = Random(8);
            Assert.True(Wildcard.IsStarMatch(name, pattern) == Wildcard.IsBacktrackingMatch(name, pattern), $"{pattern} vs {name}");
        }
        Assert.True(Wildcard.IsMatch("file-0000007-long-αβγ.txt", "*7-LONG*"));
        Assert.False(Wildcard.IsMatch("a", "a*a"));
    }

    [Fact]
    public void Directory_only_masks_skip_files()
    {
        var m = Mask.Parse("src\\");
        Assert.True(m.IsMatch("src", isDirectory: true));
        Assert.False(m.IsMatch("src", isDirectory: false));
    }

    [Theory]
    [InlineData("src/**/*.cs", "src/a/b/c.cs", true)]
    [InlineData("src/**/*.cs", "src/c.cs", true)]
    [InlineData("src/*.cs", "src/a/c.cs", false)]
    [InlineData("*.cs", "src/a/c.cs", true)]
    [InlineData("**/bin/**", "x/bin/y.dll", true)]
    public void Path_matching_with_double_star(string mask, string path, bool expected)
    {
        Assert.Equal(expected, Mask.Parse(mask).IsMatchPath(path));
    }

    [Fact]
    public void Invalid_masks_report_errors()
    {
        Assert.False(Mask.TryParse("\"unclosed", out _, out var e1));
        Assert.NotNull(e1);
        Assert.False(Mask.TryParse("/(unclosed/", out _, out var e2));
        Assert.NotNull(e2);
    }

    [Fact]
    public void Match_all_detection()
    {
        Assert.True(Mask.Parse("*.*").IsMatchAll);
        Assert.True(Mask.Parse("").IsMatchAll);
        Assert.False(Mask.Parse("*.txt").IsMatchAll);
        Assert.False(Mask.Parse("*|*.bak").IsMatchAll);
    }
}
