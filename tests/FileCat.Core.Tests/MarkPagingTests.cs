using FileCat.Core.Listing;

namespace FileCat.Core.Tests;

public sealed class MarkPagingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(64)]
    public void Pages_cover_sparse_and_word_boundary_marks_once_without_changing_selection(int pageSize)
    {
        int[] expected = [0, 1, 63, 64, 65, 127, 128, 999_999];
        var marks = new MarkSet();
        foreach (int index in expected) marks.Set(index, true);
        var result = new List<int>();
        var page = new int[pageSize];
        int after = -1;
        while (marks.CopyAfter(after, page) is var count && count > 0)
        {
            result.AddRange(page.Take(count));
            after = page[count - 1];
        }
        Assert.Equal(expected, result);
        Assert.Equal(expected.Length, marks.Count);
        Assert.Equal(0, marks.CopyAfter(int.MaxValue, page));
        Assert.Equal(0, marks.CopyAfter(-1, Span<int>.Empty));
    }

    [Fact]
    public void Bounded_page_query_of_a_million_marks_allocates_no_selection_array()
    {
        var marks = new MarkSet();
        for (int i = 0; i < 1_000_000; i++) marks.Set(i, true);
        Span<int> page = stackalloc int[64];
        marks.CopyAfter(-1, page); // JIT warmup
        long before = GC.GetAllocatedBytesForCurrentThread();
        int count = marks.CopyAfter(999_935, page);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(64, count);
        Assert.Equal(0, allocated);
        Assert.Equal(999_936, page[0]);
        Assert.Equal(999_999, page[^1]);
    }

    [Fact]
    public void Invalid_cursor_is_rejected_and_cleared_marks_leave_no_stale_page()
    {
        var marks = new MarkSet();
        marks.Set(64, true);
        Assert.Throws<ArgumentOutOfRangeException>(() => marks.CopyAfter(-2, new int[4]));
        marks.Clear();
        Assert.Equal(0, marks.CopyAfter(-1, new int[4]));
        marks.Set(65, true);
        var page = new int[4];
        Assert.Equal(1, marks.CopyAfter(-1, page));
        Assert.Equal(65, page[0]);
    }
}
