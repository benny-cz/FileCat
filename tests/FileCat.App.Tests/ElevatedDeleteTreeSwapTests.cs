using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows;

namespace FileCat.App.Tests;

public sealed class ElevatedDeleteTreeSwapTests
{
    private sealed class SignalMediumTree(string trigger, string writerResult) : WindowsFileOperations
    {
        public override BoundTreeDeletion? DeleteTreeBound(string path, Action checkpoint, Func<string, bool>? deleteReadOnly = null)
        {
            bool signaled = false;
            return base.DeleteTreeBound(path, () =>
            {
                if (!signaled)
                {
                    signaled = true;
                    File.WriteAllText(trigger, "The high-token tree walker enumerated the selected root.");
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!File.Exists(writerResult))
                    {
                        if (DateTime.UtcNow >= deadline) throw new IOException("The medium writer did not respond at the tree seam.");
                        Thread.Sleep(20);
                    }
                }
                checkpoint();
            }, deleteReadOnly);
        }
    }

    [Fact]
    public async Task Native_high_token_tree_delete_refuses_a_medium_planted_child_link()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_SECURITY_VM_LINK_FIXTURE");
        if (root is null) Assert.Skip("Run in the owned medium/high Windows fixture.");
        Assert.True(OperatingSystem.IsWindows() && WindowsTokenAuthority.IsHighIntegrity);
        string selected = Directory.CreateDirectory(Path.Combine(root!, "ac33-selected")).FullName;
        string child = Directory.CreateDirectory(Path.Combine(selected, "child")).FullName;
        string original = Path.Combine(child, "ordinary.txt");
        File.WriteAllText(original, "OWNED-AC33-ORDINARY");
        string outsideMarker = Path.Combine(root, "extract-outside", "marker.txt");
        string markerBefore = File.ReadAllText(outsideMarker);
        string ready = Path.Combine(root, "ac33-ready.txt");
        string writerResult = Path.Combine(root, "ac33-writer.txt");
        string trigger = Path.Combine(root, "ac33-trigger.txt");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(ready))
        {
            Assert.True(DateTime.UtcNow < deadline, "The medium tree writer was not ready.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.Contains("S-1-16-8192", File.ReadAllText(ready));
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        string journalRoot = Path.Combine(root, "ac33-journal");
        var jobs = new JobManager(new SignalMediumTree(trigger, writerResult), providers, journalRoot);
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Delete,
            Sources = [ItemRef.ForFileSystemPath(selected, EntryKind.Directory)],
        });
        deadline = DateTime.UtcNow.AddSeconds(20);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, "The high-token tree deletion did not finish.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        string writerEvidence = File.ReadAllText(writerResult);
        Assert.Contains("WriterIntegrity=S-1-16-8192", writerEvidence);
        Assert.Contains("Swapped=True", writerEvidence);
        Assert.NotEqual(JobState.Completed, job.State);
        Assert.Contains(job.Issues, issue => issue.Message.Contains("became a link", StringComparison.Ordinal));
        Assert.True((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0);
        Assert.Equal("OWNED-AC33-ORDINARY", File.ReadAllText(Path.Combine(root, "ac33-retired-child", "ordinary.txt")));
        Assert.Equal(markerBefore, File.ReadAllText(outsideMarker));
        string journal = string.Join("\n", Directory.GetFiles(journalRoot, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.Contains("PartiallyApplied", journal, StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(root, "ac33-result.txt"),
            $"HighIntegrity={WindowsTokenAuthority.IsHighIntegrity}\n{writerEvidence}\nJobState={job.State}\n" +
            $"ChildIsLink=True\nRetiredBytes={File.ReadAllText(Path.Combine(root, "ac33-retired-child", "ordinary.txt"))}\n" +
            $"OutsideMarker={File.ReadAllText(outsideMarker)}\nJournalPartiallyApplied=True\n" +
            $"Issues={string.Join(" | ", job.Issues.Select(i => i.Message))}\n");
    }
}
