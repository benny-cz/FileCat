using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// Review finding C7 of f134ba37: a rename answered with Replace moved over whatever had the name by then, unbound to the
/// destination the question showed, and was offered also where nothing may be replaced.
/// </summary>
public sealed class RenameReplaceTests
{
    private static async Task<Job> Rename(IFileSystemOperations files, TempDir root, string from, string to,
        Func<PendingDecision, DecisionAction> answer)
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, root.Dir("journal"));
        var job = jobs.Submit(new JobRequest { Kind = JobKind.Rename, Sources = [ItemRef.ForFileSystemPath(from, EntryKind.File)], NewName = to });
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, "The rename did not finish.");
            if (job.Decision is { Task.IsCompleted: false } question) question.Resolve(new Decision(answer(question)));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    [Fact]
    public async Task Replace_moves_over_the_destination_the_question_showed()
    {
        using var root = new TempDir();
        string a = root.File("a.txt", "renamed"), b = root.File("b.txt", "old");
        var job = await Rename(new ReviewedReplacementTestFiles(), root, a, "b.txt", _ => DecisionAction.Replace);
        Assert.True(job.State == JobState.Completed, string.Join("; ", job.Issues.Select(i => i.Message)));
        Assert.False(File.Exists(a));
        Assert.Equal("renamed", File.ReadAllText(b));
    }

    [Fact]
    public async Task Replace_keeps_a_destination_changed_after_the_question()
    {
        using var root = new TempDir();
        string a = root.File("a.txt", "renamed"), b = root.File("b.txt", "old");
        var job = await Rename(new ReviewedReplacementTestFiles(), root, a, "b.txt", _ =>
        {
            File.WriteAllText(b, "written meanwhile");
            return DecisionAction.Replace;
        });
        Assert.NotEqual(JobState.Completed, job.State);
        Assert.Equal("renamed", File.ReadAllText(a));
        Assert.Equal("written meanwhile", File.ReadAllText(b));
        Assert.Contains(job.Issues, i => i.Message.StartsWith("Not replaced:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replace_is_not_offered_where_nothing_can_be_replaced_bound_to_its_review()
    {
        using var root = new TempDir();
        string a = root.File("a.txt", "renamed"), b = root.File("b.txt", "old");
        ConflictRequest? asked = null;
        var job = await Rename(new PortableFileOperations(), root, a, "b.txt", question =>
        {
            asked = question.Request as ConflictRequest;
            return DecisionAction.Skip;
        });
        Assert.NotNull(asked);
        Assert.False(asked.CanReplace);
        Assert.Equal("old", File.ReadAllText(b));
        Assert.Equal("renamed", File.ReadAllText(a));
    }
}
