using FileCat.App.Controls;

namespace FileCat.App.Tests;

/// <summary>F3 and quick view wrap long lines at words, as a reader expects, and never lose or add a character.</summary>
public sealed class TextViewerWrapTests
{
    [Theory]
    [InlineData("the quick brown fox", 10, new[] { "the quick ", "brown fox" })]
    [InlineData("abcdefghij klm", 10, new[] { "abcdefghij ", "klm" })] // the space at the edge stays with its row
    [InlineData("ab cdefghijklmnop", 10, new[] { "ab cdefghi", "jklmnop" })] // a word longer than half a row is cut
    [InlineData("abcdefghijklmnopqrstuvwxyz", 10, new[] { "abcdefghij", "klmnopqrst", "uvwxyz" })]
    [InlineData("short", 10, new[] { "short" })]
    public void Long_lines_wrap_after_the_last_space_that_fits(string line, int columns, string[] rows)
    {
        var segments = TextViewer.Segments(line, columns, wrap: true).ToArray();
        Assert.Equal(rows, segments);
        Assert.Equal(line, string.Concat(segments));
        Assert.All(segments, s => Assert.True(s.TrimEnd(' ').Length <= columns, s));
    }

    [Fact]
    public void Without_wrapping_a_line_is_one_row()
    {
        const string line = "a line that is much longer than the ten columns it would wrap at";
        Assert.Equal([line], TextViewer.Segments(line, 10, wrap: false));
    }
}
