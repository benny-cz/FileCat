using FileCat.Core.Compare;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// Review finding F-M2 of f134ba37: the comparison's budget for reviewing one-sided items was shared by both sides, and a
/// review that gave up was charged nothing.
/// </summary>
public sealed class ComparisonReviewBudgetTests
{
    [Fact]
    public void Many_new_items_on_one_side_leave_the_other_sides_own_items_reviewed()
    {
        // 4,200 items new on the left used up the shared budget, so the right's own item went unreviewed, and a Mirror to
        // the right would have kept it.
        using var root = new TempDir();
        for (int i = 0; i < 4200; i++) root.File($"L/new{i:0000}.txt", "n");
        root.File("R/zzz-only-here.txt", "only on the right");
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var result = TreeCompare.Compare(providers, Location.FileSystem(Path.Combine(root.Path, "L")), Location.FileSystem(Path.Combine(root.Path, "R")),
            CompareCriteria.Size | CompareCriteria.Time, TimeSpan.FromSeconds(2), OperatingSystem.IsWindows(), TestContext.Current.CancellationToken);
        Assert.Equal(4200, result.Count(TreeDiffKind.LeftOnly));
        Assert.NotNull(Assert.Single(result.Entries, e => e.Kind == TreeDiffKind.RightOnly).RemovalReview);
    }

    [Fact]
    public void A_review_that_gives_up_says_what_it_read()
    {
        using var root = new TempDir();
        for (int i = 0; i < 12; i++) root.File($"big/f{i:00}.txt", new string('x', 1000));
        var spend = new FileTreeReview.Spend();
        Assert.Null(FileTreeReview.Capture(Path.Combine(root.Path, "big"), new PortableFileOperations(), new LocalFileSystemProvider(),
            byteLimit: 5000, itemLimit: 1000, allowLinks: true, spend: spend));
        // The folder, five files read whole, and the sixth, which no longer fits.
        Assert.Equal((7, 5000L), (spend.Items, spend.Bytes));
    }
}
