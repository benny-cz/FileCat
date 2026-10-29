using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class BoundDeleteTreeTests
{
    private sealed class SwapChildBeforeDelete(string child, string outside) : WindowsFileOperations
    {
        public override BoundTreeDeletion? DeleteTreeBound(string path, Action checkpoint, Func<string, bool>? deleteReadOnly = null)
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
            }, deleteReadOnly);
        }
    }

    private sealed class ReplaceChildDirectory(string child, string retired) : WindowsFileOperations
    {
        public override BoundTreeDeletion? DeleteTreeBound(string path, Action checkpoint, Func<string, bool>? deleteReadOnly = null)
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
            }, deleteReadOnly);
        }
    }

    private sealed class MoveParentBeforeDelete(string selected, string parent) : WindowsFileOperations
    {
        private bool _moved;

        public override BoundTreeDeletion? DeleteTreeBound(string path, Action checkpoint, Func<string, bool>? deleteReadOnly = null)
        {
            if (path == selected && !_moved)
            {
                _moved = true;
                Directory.Move(parent, parent + "-moved");
            }
            return base.DeleteTreeBound(path, checkpoint, deleteReadOnly);
        }
    }

    private static async Task Settle(Job job, Func<PendingDecision, DecisionAction> answer)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, "The delete did not finish.");
            if (job.Decision is { Task.IsCompleted: false } question) question.Resolve(new Decision(answer(question)));
            await Task.Delay(10, TestContext.Current.CancellationToken);
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

    [Fact]
    public async Task Permanent_delete_asks_before_a_read_only_file_and_retries_one_another_program_holds_open()
    {
        // Finding F-M4 of f134ba37: read-only files went silently, and a child antivirus or the indexer held open failed
        // the folder at once, with no retry.
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows handle-bound deletion is required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-delete-" + Guid.NewGuid().ToString("N"))).FullName;
        string readOnly = Path.Combine(root, "selected", "a-readonly.txt");
        try
        {
            string selected = Directory.CreateDirectory(Path.Combine(root, "selected")).FullName;
            File.WriteAllText(readOnly, "kept");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            string locked = Path.Combine(selected, "b-locked.txt"), free = Path.Combine(selected, "c-free.txt");
            File.WriteAllText(locked, "held");
            File.WriteAllText(free, "free");
            string sub = Directory.CreateDirectory(Path.Combine(selected, "sub")).FullName;
            File.WriteAllText(Path.Combine(sub, "d.txt"), "nested");
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(root, "journal"));
            var holder = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var asked = new List<string>();
            Job job;
            try
            {
                job = jobs.Submit(new JobRequest
                {
                    Kind = JobKind.Delete,
                    Sources = [ItemRef.ForFileSystemPath(selected, EntryKind.Directory)],
                });
                await Settle(job, question =>
                {
                    asked.Add(question.Request.ClassKey);
                    if (question.Request is ConfirmRequest { ConfirmClass: "readonly" }) return DecisionAction.Skip;
                    Assert.Equal("error-sharing", question.Request.ClassKey);
                    // Everything else went on the first attempt; only the held file and the kept one are left.
                    Assert.False(File.Exists(free));
                    Assert.False(Directory.Exists(sub));
                    Assert.True(File.Exists(locked));
                    holder.Dispose();
                    return DecisionAction.Retry;
                });
            }
            finally { holder.Dispose(); }
            Assert.Equal(["confirm-readonly", "error-sharing"], asked);
            Assert.False(File.Exists(locked));
            Assert.Equal("kept", File.ReadAllText(readOnly));
            Assert.True((File.GetAttributes(readOnly) & FileAttributes.ReadOnly) != 0);
            Assert.NotEqual(JobState.Completed, job.State);
            Assert.DoesNotContain(job.Issues, issue => issue.Message.Contains("not empty", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (File.Exists(readOnly)) File.SetAttributes(readOnly, FileAttributes.Normal);
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
        }
    }

    [Fact]
    public async Task Permanent_delete_deletes_a_read_only_file_the_user_agrees_to()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows handle-bound deletion is required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-delete-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string selected = Directory.CreateDirectory(Path.Combine(root, "selected", "deep")).Parent!.FullName;
            string readOnly = Path.Combine(selected, "deep", "readonly.txt");
            File.WriteAllText(readOnly, "agreed");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(root, "journal"));
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Delete,
                Sources = [ItemRef.ForFileSystemPath(selected, EntryKind.Directory)],
            });
            var asked = new List<string>();
            await Settle(job, question =>
            {
                asked.Add(question.Request.ClassKey + " " + ((ConfirmRequest)question.Request).Path);
                return DecisionAction.Proceed;
            });
            // The path as the volume names it, whose letter case may differ from the one asked for (C:\Windows\Temp).
            Assert.Equal("confirm-readonly " + readOnly, Assert.Single(asked), ignoreCase: true);
            Assert.Equal(JobState.Completed, job.State);
            Assert.False(Directory.Exists(selected));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Permanent_delete_asks_about_a_folder_it_cannot_reach_and_goes_on_with_the_next()
    {
        // Finding F-M4 of f134ba37: a folder the walk could not reach (here, one another program moved away after it was
        // selected) ended the whole job, and the next one was never deleted.
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows handle-bound deletion is required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-delete-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string parent = Directory.CreateDirectory(Path.Combine(root, "a")).FullName;
            string selected = Directory.CreateDirectory(Path.Combine(parent, "selected")).FullName;
            File.WriteAllText(Path.Combine(selected, "own.txt"), "own");
            string next = Directory.CreateDirectory(Path.Combine(root, "next")).FullName;
            File.WriteAllText(Path.Combine(next, "next.txt"), "next");
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new MoveParentBeforeDelete(selected, parent), providers, Path.Combine(root, "journal"));
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Delete,
                Sources = [ItemRef.ForFileSystemPath(selected, EntryKind.Directory), ItemRef.ForFileSystemPath(next, EntryKind.Directory)],
            });
            var asked = new List<string>();
            await Settle(job, question =>
            {
                asked.Add(question.Request.Title + ": " + ((ErrorRequest)question.Request).Path);
                return DecisionAction.Skip;
            });
            Assert.Equal(["Could not delete the folder: " + selected], asked);
            Assert.False(Directory.Exists(next));
            Assert.Equal("own", File.ReadAllText(Path.Combine(parent + "-moved", "selected", "own.txt")));
            Assert.NotEqual(JobState.Completed, job.State);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
