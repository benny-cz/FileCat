using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class BoundDeleteTreeTests
{
    private sealed class SwapChildBeforeDelete(string child, string outside) : WindowsFileOperations
    {
        public override BoundTreeDeletion? DeleteTreeBound(string path, Action checkpoint)
        {
            bool swapped = false;
            return base.DeleteTreeBound(path, () =>
            {
                if (!swapped)
                {
                    swapped = true;
                    Directory.Move(child, outside + "-held");
                    Directory.CreateSymbolicLink(child, outside);
                }
                checkpoint();
            });
        }
    }

    private sealed class ReplaceChildDirectory(string child, string retired) : WindowsFileOperations
    {
        public override BoundTreeDeletion? DeleteTreeBound(string path, Action checkpoint)
        {
            bool swapped = false;
            return base.DeleteTreeBound(path, () =>
            {
                if (!swapped)
                {
                    swapped = true;
                    Directory.Move(child, retired);
                    Directory.CreateDirectory(child);
                    File.WriteAllText(Path.Combine(child, "new.txt"), "new child");
                }
                checkpoint();
            });
        }
    }

    [Fact]
    public async Task Permanent_delete_refuses_a_new_ordinary_child_with_the_same_name()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows handle-bound deletion is required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-delete-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string selected = Directory.CreateDirectory(Path.Combine(root, "selected")).FullName;
            string child = Directory.CreateDirectory(Path.Combine(selected, "child")).FullName;
            File.WriteAllText(Path.Combine(child, "old.txt"), "old child");
            string retired = Path.Combine(root, "retired-child");
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new ReplaceChildDirectory(child, retired), providers, Path.Combine(root, "journal"));
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Delete,
                Sources = [ItemRef.ForFileSystemPath(selected, EntryKind.Directory)],
            });
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.NotEqual(JobState.Completed, job.State);
            Assert.Equal("new child", File.ReadAllText(Path.Combine(child, "new.txt")));
            Assert.Equal("old child", File.ReadAllText(Path.Combine(retired, "old.txt")));
            Assert.Contains(job.Issues, issue => issue.Message.Contains("identity changed", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Permanent_delete_does_not_follow_a_child_swapped_to_an_outside_directory_link()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows handle-bound deletion is required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-delete-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string selected = Directory.CreateDirectory(Path.Combine(root, "selected")).FullName;
            string child = Directory.CreateDirectory(Path.Combine(selected, "child")).FullName;
            File.WriteAllText(Path.Combine(child, "ordinary.txt"), "ordinary");
            string outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
            string marker = Path.Combine(outside, "marker.txt");
            File.WriteAllText(marker, "out-of-tree marker");
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new SwapChildBeforeDelete(child, outside), providers, Path.Combine(root, "journal"));
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Delete,
                Sources = [ItemRef.ForFileSystemPath(selected, EntryKind.Directory)],
            });
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.Equal("out-of-tree marker", File.ReadAllText(marker));
            Assert.True(Directory.Exists(selected));
            Assert.True((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0);
            Assert.True(Directory.Exists(outside + "-held"));
            Assert.NotEqual(JobState.Completed, job.State);
            Assert.Contains(job.Issues, issue => issue.Message.Contains("became a link", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
