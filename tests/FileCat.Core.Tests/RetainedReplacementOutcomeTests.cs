using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// Review finding W9 of f134ba37: a replacement that moved the original destination aside and could not return it to its
/// name was journaled as canceled before any change, and the recovery name survived only in a message.
/// </summary>
public sealed class RetainedReplacementOutcomeTests
{
    /// <summary>Moves the destination aside and cannot return it, as the Windows adapter reports a failed restore.</summary>
    private sealed class KeptAside : ReviewedReplacementTestFiles
    {
        public string? Recovery;

        public override BoundReplacement? ReplaceReviewed(string source, string destination, string? expectedIdentity,
            string expectedSha256, long expectedLength, CancellationToken ct, string? recoveryPath = null,
            DateTime? expectedModifiedUtc = null)
        {
            Recovery = Path.Combine(Path.GetDirectoryName(destination)!, ".filecat-replaced-test.bak");
            File.Move(destination, Recovery);
            return new BoundReplacement(false, false, Recovery,
                "The incoming file was not published and the original destination could not return to its name: test.");
        }
    }

    [Theory]
    [InlineData(JobKind.Copy)]
    [InlineData(JobKind.Move)]
    [InlineData(JobKind.Rename)]
    public async Task An_original_kept_at_a_recovery_name_is_a_partial_change(JobKind kind)
    {
        using var root = new TempDir();
        string source = Path.GetFullPath(root.File("from/a.txt", "incoming"));
        string destination = Path.GetFullPath(root.File(kind == JobKind.Rename ? "from/b.txt" : "to/a.txt", "existing"));
        var files = new KeptAside();
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        string journals = root.Dir("journal");
        var jobs = new JobManager(files, providers, journals);
        var job = jobs.Submit(kind == JobKind.Rename
            ? new JobRequest { Kind = kind, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)], NewName = "b.txt" }
            : new JobRequest
            {
                Kind = kind,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(Path.GetDirectoryName(destination)!),
                Options = new TransferOptions { Conflicts = ConflictPolicy.Replace },
            });
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, "The job did not finish.");
            if (job.Decision is { Task.IsCompleted: false } question) question.Resolve(new Decision(DecisionAction.Replace));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        string issues = string.Join("; ", job.Issues.Select(i => $"{i.Path}: {i.Message} ({i.Outcome})"));
        string recovery = Assert.IsType<string>(files.Recovery);
        Assert.Equal("existing", File.ReadAllText(recovery));
        Assert.Equal("incoming", File.ReadAllText(source));
        Assert.True(job.Issues.Any(i => PathUtil.SafetyComparer.Equals(i.Path, recovery) && i.Outcome == StepOutcome.PartiallyApplied), issues);
        Assert.True(job.Issues.Any(i => PathUtil.SafetyComparer.Equals(i.Path, destination) && i.Outcome == StepOutcome.PartiallyApplied), issues);
        var records = Directory.GetFiles(journals, "job-*.fcj").SelectMany(JobJournal.ReadAll).ToList();
        var intent = Assert.Single(records, r => r.Get("t") == "intent" && r.Get("op") is "replace" or "move-replace" or "rename-replace");
        var done = Assert.Single(records, r => r.Get("t") == "done" && r.Step == intent.Step);
        Assert.Equal(nameof(StepOutcome.PartiallyApplied), done.Get("outcome"));
    }
}
