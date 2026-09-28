using System.Globalization;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

/// <summary>The one line a finished job's notice shows: its most serious issue and how many others there are.</summary>
public sealed class JobIssueSummaryTests
{
    private static JobIssue Warning(string path, string message) => new(IssueSeverity.Warning, path, message, StepOutcome.Committed);

    [Fact]
    public void Repeated_warnings_are_counted_and_an_error_comes_first()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Null(JobIssue.Summarize([new JobIssue(IssueSeverity.Info, "a", "Skipped a link.", StepOutcome.Skipped)]));

            // One item: its name is said, unless the message already names it.
            Assert.Equal("Security metadata lost (photo.jpg).", JobIssue.Summarize([Warning(@"D:\out\photo.jpg", "Security metadata lost.")]));
            Assert.Equal("Could not read /srv/a.txt.", JobIssue.Summarize([Warning("/srv/a.txt", "Could not read /srv/a.txt.")]));

            // The same warning for many files is one message with a count, not the first file's alone.
            var many = Enumerable.Range(0, 1000).Select(i => Warning($"D:\\out\\f{i}.txt", "Security metadata lost.")).ToList();
            Assert.Equal("Security metadata lost (1,000 items).", JobIssue.Summarize(many));

            // An error outranks earlier warnings, and the rest are counted.
            many.Add(new JobIssue(IssueSeverity.Error, "/srv/b.txt", "Not copied: the disk is full.", StepOutcome.Failed));
            Assert.Equal("Not copied: the disk is full (b.txt); 1,000 other issues.", JobIssue.Summarize(many));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
