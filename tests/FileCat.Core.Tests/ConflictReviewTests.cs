using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// Review finding F-M1 of f134ba37: an existing destination was reviewed (read whole and hashed, up to 64 MiB) before the
/// conflict answer, so Skip and Keep both read every one of them, and a replaced file was read three times.
/// </summary>
public sealed class ConflictReviewTests
{
    /// <summary>Counts the review's own question about a destination: where it really is.</summary>
    private sealed class Watching(params string[] watched) : ReviewedReplacementTestFiles
    {
        public int Reviews;
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> Reviewed = new(PathUtil.SafetyComparer);

        public override string? GetFinalPath(string path)
        {
            if (watched.Any(w => PathUtil.SafetyComparer.Equals(path, w)))
            {
                Interlocked.Increment(ref Reviews);
                Reviewed[path] = true;
            }
            return base.GetFinalPath(path);
        }
    }

    [Theory]
    [InlineData(ConflictPolicy.Skip)]
    [InlineData(ConflictPolicy.KeepBothRenameIncoming)]
    [InlineData(ConflictPolicy.Replace)]
    public async Task Only_a_destination_that_is_to_be_replaced_is_reviewed(ConflictPolicy policy)
    {
        using var root = new TempDir();
        string source = Path.GetFullPath(root.File("from/a.txt", "incoming"));
        string destination = Path.GetFullPath(root.File("to/a.txt", "existing"));
        var files = new Watching(destination);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, root.Dir("journal"));
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = Location.FileSystem(Path.GetDirectoryName(destination)!),
            Options = new TransferOptions { Conflicts = policy },
        });
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, "The copy did not finish.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        if (policy == ConflictPolicy.Replace)
        {
            Assert.True(files.Reviews > 0);
            Assert.Equal("incoming", File.ReadAllText(destination));
        }
        else
        {
            Assert.Equal(0, files.Reviews);
            Assert.Equal("existing", File.ReadAllText(destination));
        }
    }

    [Fact]
    public async Task An_answer_given_for_all_reviews_no_further_destination()
    {
        // A question offering Replace reviews the destination it shows before it is asked, so that a file put in its place
        // meanwhile is never replaced. The answers "Skip all" then gives without asking review nothing.
        using var root = new TempDir();
        string[] names = ["a.txt", "b.txt", "c.txt"];
        var sources = names.Select(n => Path.GetFullPath(root.File("from/" + n, "incoming"))).ToArray();
        var destinations = names.Select(n => Path.GetFullPath(root.File("to/" + n, "existing"))).ToArray();
        var files = new Watching(destinations);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, root.Dir("journal"));
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [.. sources.Select(s => ItemRef.ForFileSystemPath(s, EntryKind.File))],
            Destination = Location.FileSystem(Path.GetDirectoryName(destinations[0])!),
            Options = new TransferOptions { Conflicts = ConflictPolicy.Ask },
        });
        int questions = 0;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, "The copy did not finish.");
            if (job.Decision is { Task.IsCompleted: false } question)
            {
                questions++;
                question.Resolve(new Decision(DecisionAction.Skip, ApplyToAll: true));
            }
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.Equal(1, questions);
        Assert.Single(files.Reviewed);
        Assert.All(destinations, d => Assert.Equal("existing", File.ReadAllText(d)));
    }
}
