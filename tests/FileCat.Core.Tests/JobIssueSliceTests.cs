using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class JobIssueSliceTests
{
    [Fact]
    public void Live_report_reads_only_the_requested_retained_slice()
    {
        var job = new Job(new JobRequest { Kind = JobKind.Copy }, "Copy", "local", [], []);
        for (int i = 0; i < 530; i++)
            job.AddIssue(new JobIssue(IssueSeverity.Warning, $"path-{i}", $"issue-{i}", StepOutcome.Skipped));

        Assert.Equal(530, job.IssueCount);
        Assert.Equal(Enumerable.Range(0, 256).Select(i => $"issue-{i}"), job.ReadIssues(0, 256).Select(issue => issue.Message));
        Assert.Equal(Enumerable.Range(256, 256).Select(i => $"issue-{i}"), job.ReadIssues(256, 256).Select(issue => issue.Message));
        Assert.Equal(Enumerable.Range(512, 18).Select(i => $"issue-{i}"), job.ReadIssues(512, 256).Select(issue => issue.Message));
        Assert.Empty(job.ReadIssues(530, 256));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.ReadIssues(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.ReadIssues(0, 0));
    }

    [Fact]
    public void Issue_totals_keep_counting_after_the_retained_detail_limit()
    {
        var job = new Job(new JobRequest { Kind = JobKind.Copy }, "Copy", "local", [], []);
        for (int i = 0; i < 10_005; i++)
            job.AddIssue(new JobIssue(i % 2 == 0 ? IssueSeverity.Warning : IssueSeverity.Error,
                $"path-{i}", $"issue-{i}", StepOutcome.Failed));

        var totals = job.IssueTotals;
        Assert.Equal(10_005, totals.Total);
        Assert.Equal(10_000, totals.Retained);
        Assert.Equal(5, totals.Omitted);
        Assert.Equal(5_003, totals.Warning);
        Assert.Equal(5_002, totals.Error);
        Assert.Equal(0, totals.Info);
        Assert.Equal(10_000, job.IssueCount);
        Assert.Contains(totals.Warning.ToString("N0") + " warnings", job.Summary);
        Assert.Equal("issue-9999", job.ReadIssues(9_999, 100)[0].Message);
    }
}
