using FileCat.Core.Compare;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class BoundComparedDeleteJobTests
{
    private sealed class RecycleSpy : WindowsFileOperations
    {
        public bool Called { get; private set; }
        public override IReadOnlyList<RecycleResult> Recycle(IReadOnlyList<string> paths,
            Action<string>? itemStarted, CancellationToken ct)
        {
            Called = true;
            throw new InvalidOperationException("A compared object must not reach path-based Recycle.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mirror_permanent_file_removal_keeps_changed_bytes_and_removes_reviewed_bytes(bool substitute)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows handle-bound deletion required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-mirror-file-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string left = Directory.CreateDirectory(Path.Combine(root, "left")).FullName;
            string right = Directory.CreateDirectory(Path.Combine(root, "right")).FullName;
            string target = Path.Combine(right, "target.bin");
            byte[] a = Enumerable.Repeat((byte)'A', 4096).ToArray();
            byte[] b = Enumerable.Repeat((byte)'B', 4096).ToArray();
            File.WriteAllBytes(target, a);
            var files = new WindowsFileOperations();
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var compared = TreeCompare.Compare(providers, Location.FileSystem(left), Location.FileSystem(right),
                CompareCriteria.Size | CompareCriteria.Time, TimeSpan.Zero, true, TestContext.Current.CancellationToken,
                files: files);
            var plan = SyncPlanner.Propose(compared, sourceIsLeft: true, SyncMode.Mirror, targetIgnoresCase: true);
            Assert.True(Assert.Single(plan).CanInclude);
            var request = Assert.Single(SyncPlanner.BuildRequests(plan, sourceIsLeft: true, Location.FileSystem(right),
                providers, deletePermanently: true));
            if (substitute)
            {
                DateTime stamp = File.GetLastWriteTimeUtc(target);
                File.Move(target, Path.Combine(root, "retired-A.bin"));
                File.WriteAllBytes(target, b);
                File.SetLastWriteTimeUtc(target, stamp);
            }
            var jobs = new JobManager(files, providers, Path.Combine(root, "journal"));
            var job = jobs.Submit(request);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            if (substitute)
            {
                Assert.NotEqual(JobState.Completed, job.State);
                Assert.Equal(b, File.ReadAllBytes(target));
            }
            else
            {
                Assert.Equal(JobState.Completed, job.State);
                Assert.False(File.Exists(target));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mirror_permanent_folder_removal_binds_its_child_bytes(bool substitute)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows handle-bound deletion required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-mirror-tree-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string left = Directory.CreateDirectory(Path.Combine(root, "left")).FullName;
            string right = Directory.CreateDirectory(Path.Combine(root, "right")).FullName;
            string tree = Directory.CreateDirectory(Path.Combine(right, "tree")).FullName;
            string child = Path.Combine(tree, "child.bin");
            byte[] a = Enumerable.Repeat((byte)'A', 4096).ToArray();
            byte[] b = Enumerable.Repeat((byte)'B', 4096).ToArray();
            File.WriteAllBytes(child, a);
            var files = new WindowsFileOperations();
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var compared = TreeCompare.Compare(providers, Location.FileSystem(left), Location.FileSystem(right),
                CompareCriteria.Size | CompareCriteria.Time, TimeSpan.Zero, true, TestContext.Current.CancellationToken,
                files: files);
            var plan = SyncPlanner.Propose(compared, sourceIsLeft: true, SyncMode.Mirror, targetIgnoresCase: true);
            Assert.True(Assert.Single(plan).CanInclude);
            var request = Assert.Single(SyncPlanner.BuildRequests(plan, sourceIsLeft: true, Location.FileSystem(right),
                providers, deletePermanently: true));
            if (substitute)
            {
                DateTime childStamp = File.GetLastWriteTimeUtc(child);
                DateTime treeStamp = Directory.GetLastWriteTimeUtc(tree);
                File.Move(child, Path.Combine(root, "retired-A.bin"));
                File.WriteAllBytes(child, b);
                File.SetLastWriteTimeUtc(child, childStamp);
                Directory.SetLastWriteTimeUtc(tree, treeStamp);
            }
            var jobs = new JobManager(files, providers, Path.Combine(root, "journal"));
            var job = jobs.Submit(request);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            if (substitute)
            {
                Assert.NotEqual(JobState.Completed, job.State);
                Assert.Equal(b, File.ReadAllBytes(child));
            }
            else
            {
                Assert.Equal(JobState.Completed, job.State);
                Assert.False(Directory.Exists(tree));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Mirror_recycle_refuses_before_a_path_based_shell_call()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows Recycle route required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-mirror-recycle-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string left = Directory.CreateDirectory(Path.Combine(root, "left")).FullName;
            string right = Directory.CreateDirectory(Path.Combine(root, "right")).FullName;
            string target = Path.Combine(right, "target.bin");
            File.WriteAllText(target, "A");
            var files = new RecycleSpy();
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var compared = TreeCompare.Compare(providers, Location.FileSystem(left), Location.FileSystem(right),
                CompareCriteria.Size | CompareCriteria.Time, TimeSpan.Zero, true, TestContext.Current.CancellationToken,
                files: files);
            var plan = SyncPlanner.Propose(compared, sourceIsLeft: true, SyncMode.Mirror, targetIgnoresCase: true);
            var request = Assert.Single(SyncPlanner.BuildRequests(plan, sourceIsLeft: true, Location.FileSystem(right),
                providers, deletePermanently: false));
            DateTime stamp = File.GetLastWriteTimeUtc(target);
            File.Move(target, Path.Combine(root, "retired-A.bin"));
            File.WriteAllText(target, "B");
            File.SetLastWriteTimeUtc(target, stamp);
            var jobs = new JobManager(files, providers, Path.Combine(root, "journal"));
            var job = jobs.Submit(request);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.False(files.Called);
            Assert.NotEqual(JobState.Completed, job.State);
            Assert.Equal("B", File.ReadAllText(target));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
