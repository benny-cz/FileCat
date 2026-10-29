using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows;

namespace FileCat.App.Tests;

public sealed class ElevatedReviewedReplacementTests
{
    private sealed class SignalMediumReplacement(string trigger, string result) : WindowsFileOperations
    {
        public bool Attempted { get; private set; }

        public override BoundReplacement? ReplaceReviewed(string source, string destination, string? expectedIdentity,
            string expectedSha256, long expectedLength, CancellationToken ct, string? recoveryPath = null)
        {
            Attempted = true;
            File.WriteAllText(trigger, "The high-token job reached bound replacement.");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(result))
            {
                if (DateTime.UtcNow >= deadline) throw new IOException("The medium writer did not respond at replacement.");
                Thread.Sleep(20);
            }
            return base.ReplaceReviewed(source, destination, expectedIdentity, expectedSha256,
                expectedLength, ct, recoveryPath);
        }
    }

    [Fact]
    public async Task Native_high_token_move_replace_keeps_the_medium_writers_new_destination()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_SECURITY_VM_LINK_FIXTURE");
        if (root is null) Assert.Skip("Run in the owned medium/high Windows fixture.");
        Assert.True(OperatingSystem.IsWindows() && WindowsTokenAuthority.IsHighIntegrity);
        string ready = Path.Combine(root!, "ac31-ready.txt");
        string trigger = Path.Combine(root, "ac31-trigger.txt");
        string writerResult = Path.Combine(root, "ac31-writer.txt");
        string source = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "ac31-source")).FullName, "incoming.bin");
        string destination = Directory.CreateDirectory(Path.Combine(root, "ac31-destination")).FullName;
        string target = Path.Combine(destination, "incoming.bin");
        string retired = Path.Combine(root, "ac31-retired-A.bin");
        byte[] incoming = Enumerable.Repeat((byte)'I', 4096).ToArray();
        byte[] a = Enumerable.Repeat((byte)'A', 4096).ToArray();
        byte[] b = Enumerable.Repeat((byte)'B', 4096).ToArray();
        File.WriteAllBytes(source, incoming);
        File.WriteAllBytes(target, a);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(ready))
        {
            Assert.True(DateTime.UtcNow < deadline, "The medium writer was not ready.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.Contains("S-1-16-8192", File.ReadAllText(ready));
        var files = new SignalMediumReplacement(trigger, writerResult);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, Path.Combine(root, "ac31-journal")) { MaxConcurrent = 0 };
        jobs.DecisionRequested += decision => decision.Resolve(new Decision(DecisionAction.Replace));
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Move,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = Location.FileSystem(destination),
        });
        jobs.MaxConcurrent = 1;
        jobs.Schedule();
        deadline = DateTime.UtcNow.AddSeconds(20);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, "The owned high-token Move job did not finish.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(files.Attempted);
        string writerEvidence = File.ReadAllText(writerResult);
        Assert.Contains("WriterIntegrity=S-1-16-8192", writerEvidence);
        Assert.Contains("Swapped=True", writerEvidence);
        Assert.NotEqual(JobState.Completed, job.State);
        Assert.Equal(b, File.ReadAllBytes(target));
        Assert.Equal(a, File.ReadAllBytes(retired));
        Assert.Equal(incoming, File.ReadAllBytes(source));
        Assert.Contains(job.Issues, i => i.Message.Contains("Not replaced", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(root, "ac31-result.txt"),
            $"HighIntegrity={WindowsTokenAuthority.IsHighIntegrity}\n{writerEvidence}\nJobState={job.State}\nTargetFirstByte={File.ReadAllBytes(target)[0]}\nRetiredFirstByte={File.ReadAllBytes(retired)[0]}\nSourceFirstByte={File.ReadAllBytes(source)[0]}\nIssues={string.Join(" | ", job.Issues.Select(i => i.Message))}\n");
    }
}
