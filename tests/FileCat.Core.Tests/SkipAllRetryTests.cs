using System.Collections.Concurrent;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class SkipAllRetryTests
{
    [Fact]
    public async Task Locked_Windows_temp_files_are_left_untouched_after_skip_for_all()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows delete-sharing semantics are required.");
        string root = Directory.CreateTempSubdirectory("filecat-skip-lock-").FullName;
        var locks = new List<FileStream>();
        try
        {
            var paths = Enumerable.Range(0, 3).Select(i => Path.Combine(root, $"locked-{i}.tmp")).ToArray();
            foreach (string path in paths)
            {
                File.WriteAllText(path, "keep");
                locks.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            }
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var manager = new JobManager(new PortableFileOperations(), providers, Path.Combine(root, "journal"));
            int asked = 0;
            manager.DecisionRequested += decision =>
            {
                Interlocked.Increment(ref asked);
                decision.Resolve(new Decision(DecisionAction.Skip, ApplyToAll: true));
            };
            var job = manager.Submit(new JobRequest
            {
                Kind = JobKind.Delete,
                Sources = paths.Select(path => ItemRef.ForFileSystemPath(path, EntryKind.File)).ToArray(),
            });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            while (!job.State.IsFinished()) await Task.Delay(10, timeout.Token);

            Assert.Equal(1, asked);
            Assert.Equal(3, job.ItemsFailed);
            Assert.All(paths, path => Assert.Equal("keep", File.ReadAllText(path)));
        }
        finally
        {
            foreach (var handle in locks) handle.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Skip_all_sharing_errors_does_not_retry_each_later_file()
    {
        string root = Directory.CreateTempSubdirectory("filecat-skip-all-").FullName;
        try
        {
            var fs = new SharingDeleteOperations();
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var manager = new JobManager(fs, providers, Path.Combine(root, "journal"));
            var paths = Enumerable.Range(0, 3).Select(i => Path.Combine(root, $"locked-{i}.tmp")).ToArray();
            foreach (string path in paths) File.WriteAllText(path, "keep");
            int asked = 0;
            manager.DecisionRequested += decision =>
            {
                Interlocked.Increment(ref asked);
                decision.Resolve(new Decision(DecisionAction.Skip, ApplyToAll: true));
            };
            var job = manager.Submit(new JobRequest
            {
                Kind = JobKind.Delete,
                Sources = paths.Select(path => ItemRef.ForFileSystemPath(path, EntryKind.File)).ToArray(),
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!job.State.IsFinished()) await Task.Delay(10, timeout.Token);

            Assert.Equal(1, asked);
            Assert.Equal(new[] { 4, 1, 1 }, paths.Select(fs.Attempts).ToArray());
            Assert.Equal(3, job.ItemsFailed);
            Assert.All(paths, path => Assert.True(File.Exists(path)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class SharingDeleteOperations : PortableFileOperations
    {
        private readonly ConcurrentDictionary<string, int> _attempts = new();

        public int Attempts(string path) => _attempts.GetValueOrDefault(path);

        public override void DeleteFile(string path)
        {
            _attempts.AddOrUpdate(path, 1, (_, n) => n + 1);
            throw new IOException("The file is in use", unchecked((int)0x80070020));
        }
    }
}
