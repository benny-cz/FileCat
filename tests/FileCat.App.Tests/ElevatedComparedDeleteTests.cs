using FileCat.Core.Compare;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows;

namespace FileCat.App.Tests;

public sealed class ElevatedComparedDeleteTests
{
    private sealed class SignalMediumDelete(string trigger, string result) : WindowsFileOperations
    {
        public bool Attempted { get; private set; }

        public override BoundDeletion? DeleteReviewedFile(string path, string? expectedIdentity,
            string expectedSha256, long expectedLength, CancellationToken ct)
        {
            Attempted = true;
            File.WriteAllText(trigger, "The high-token Mirror job reached bound deletion.");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(result))
            {
                if (DateTime.UtcNow >= deadline) throw new IOException("The medium writer did not respond at deletion.");
                Thread.Sleep(20);
            }
            return base.DeleteReviewedFile(path, expectedIdentity, expectedSha256, expectedLength, ct);
        }
    }

    [Fact]
    public async Task Native_high_token_Mirror_keeps_a_medium_writers_new_same_metadata_file()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_SECURITY_VM_LINK_FIXTURE");
        if (root is null) Assert.Skip("Run in the owned medium/high Windows fixture.");
        Assert.True(OperatingSystem.IsWindows() && WindowsTokenAuthority.IsHighIntegrity);
        string ready = Path.Combine(root!, "ac15-ready.txt");
        string trigger = Path.Combine(root, "ac15-trigger.txt");
        string writerResult = Path.Combine(root, "ac15-writer.txt");
        string left = Directory.CreateDirectory(Path.Combine(root, "ac15-left")).FullName;
        string right = Directory.CreateDirectory(Path.Combine(root, "ac15-right")).FullName;
        string target = Path.Combine(right, "target.bin");
        string retired = Path.Combine(root, "ac15-retired-A.bin");
        byte[] a = Enumerable.Repeat((byte)'A', 4096).ToArray();
        byte[] b = Enumerable.Repeat((byte)'B', 4096).ToArray();
        File.WriteAllBytes(target, a);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(ready))
        {
            Assert.True(DateTime.UtcNow < deadline, "The medium writer was not ready.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.Contains("S-1-16-8192", File.ReadAllText(ready));
        var files = new SignalMediumDelete(trigger, writerResult);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var compared = TreeCompare.Compare(providers, Location.FileSystem(left), Location.FileSystem(right),
            CompareCriteria.Size | CompareCriteria.Time, TimeSpan.Zero, true, TestContext.Current.CancellationToken,
            files: files);
        var plan = SyncPlanner.Propose(compared, sourceIsLeft: true, SyncMode.Mirror, targetIgnoresCase: true);
        Assert.True(Assert.Single(plan).CanInclude);
        var request = Assert.Single(SyncPlanner.BuildRequests(plan, sourceIsLeft: true,
            Location.FileSystem(right), providers, deletePermanently: true));
        var jobs = new JobManager(files, providers, Path.Combine(root, "ac15-journal"));
        var job = jobs.Submit(request);
        deadline = DateTime.UtcNow.AddSeconds(20);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, "The owned high-token Mirror job did not finish.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(files.Attempted);
        string writerEvidence = File.ReadAllText(writerResult);
        Assert.Contains("WriterIntegrity=S-1-16-8192", writerEvidence);
        Assert.Contains("Swapped=True", writerEvidence);
        Assert.NotEqual(JobState.Completed, job.State);
        Assert.Equal(b, File.ReadAllBytes(target));
        Assert.Equal(a, File.ReadAllBytes(retired));
        Assert.Contains(job.Issues, issue => issue.Message.Contains("Not removed", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(root, "ac15-result.txt"),
            $"HighIntegrity={WindowsTokenAuthority.IsHighIntegrity}\n{writerEvidence}\nJobState={job.State}\nTargetFirstByte={File.ReadAllBytes(target)[0]}\nRetiredFirstByte={File.ReadAllBytes(retired)[0]}\nIssues={string.Join(" | ", job.Issues.Select(i => i.Message))}\n");
    }
}
