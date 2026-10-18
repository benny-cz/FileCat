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
}
