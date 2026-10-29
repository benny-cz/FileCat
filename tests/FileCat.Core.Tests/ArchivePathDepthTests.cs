using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchivePathDepthTests
{
    [Fact]
    public void A_folder_entry_at_the_deepest_level_is_not_too_deep()
    {
        // Review finding P6 of f134ba37: a folder entry's trailing '/' counted as one more segment, so FileCat's own empty
        // folder at depth 256 made every later update of its ZIP fail.
        string deepest = string.Concat(Enumerable.Repeat("d/", ArchivePaths.MaxMemberSegments));
        Assert.False(ArchivePaths.TooDeep(deepest));
        Assert.False(ArchivePaths.TooDeep(deepest[..^1]));
        Assert.True(ArchivePaths.TooDeep(deepest + "file.txt"));
        Assert.True(ArchivePaths.TooDeep(deepest + "d/"));
        Assert.Null(ArchivePaths.Problem(deepest[..^1]));
    }
}
