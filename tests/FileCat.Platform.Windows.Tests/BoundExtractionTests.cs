using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class BoundExtractionTests
{
    private sealed class SwapAfterDirectoryLease(string child, string outside) : WindowsFileOperations
    {
        public bool Attempted { get; private set; }
        public bool Blocked { get; private set; }

        public override IDisposable HoldDirectory(string path)
        {
            var held = base.HoldDirectory(path);
            if (!Attempted && Path.GetFullPath(path).Equals(Path.GetFullPath(child), StringComparison.OrdinalIgnoreCase))
            {
                Attempted = true;
                try
                {
                    Directory.Move(child, child + ".retired");
                    Directory.CreateSymbolicLink(child, outside);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Blocked = true; }
            }
            return held;
        }
    }

    private sealed class SwapAtStreamPublication(string retired, byte[] replacement) : WindowsFileOperations
    {
        public bool Called { get; private set; }

        public override BoundReplacement? ReplaceReviewed(string source, string destination, string? expectedIdentity,
            string expectedSha256, long expectedLength, CancellationToken ct, string? recoveryPath = null)
        {
            Called = true;
            DateTime stamp = File.GetLastWriteTimeUtc(destination);
            File.Move(destination, retired);
            File.WriteAllBytes(destination, replacement);
            File.SetLastWriteTimeUtc(destination, stamp);
            return base.ReplaceReviewed(source, destination, expectedIdentity, expectedSha256, expectedLength, ct, recoveryPath);
        }
    }

    [Fact]
    public async Task Zip_extraction_refuses_a_linked_destination_child_and_preserves_the_outside_target()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows directory handles are required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-extract-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string archivePath = Path.Combine(root, "owned.zip");
            using (var file = File.Create(archivePath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("folder/inside.txt").Open()))
                writer.Write("archive bytes");
            string outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
            string marker = Path.Combine(outside, "marker.txt");
            File.WriteAllText(marker, "outside marker");
            string dest = Directory.CreateDirectory(Path.Combine(root, "dest")).FullName;
            Directory.CreateSymbolicLink(Path.Combine(dest, "folder"), outside);

            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var zip = new ZipProvider(Path.Combine(root, "spool"));
            providers.Register(zip);
            var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(root, "journal"));
            async Task<Job> Run(string destination)
            {
                var job = jobs.Submit(new JobRequest
                {
                    Kind = JobKind.Extract,
                    Sources = [new ItemRef(ZipProvider.ForFile(archivePath), "folder", EntryKind.Directory)],
                    Destination = Location.FileSystem(destination),
                });
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (!job.State.IsFinished())
                {
                    Assert.True(DateTime.UtcNow < deadline);
                    job.Decision?.Resolve(new Decision(DecisionAction.Skip));
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }
                return job;
            }

            var refused = await Run(dest);
            Assert.NotEqual(JobState.Completed, refused.State);
            Assert.Equal("outside marker", File.ReadAllText(marker));
            Assert.False(File.Exists(Path.Combine(outside, "inside.txt")));
            Assert.Contains(refused.Issues, i => i.Message.Contains("link", StringComparison.OrdinalIgnoreCase));

            string ordinary = Directory.CreateDirectory(Path.Combine(root, "ordinary")).FullName;
            var accepted = await Run(ordinary);
            Assert.True(accepted.State == JobState.Completed, string.Join("; ", accepted.Issues.Select(i => i.Message)));
            Assert.Equal("archive bytes", File.ReadAllText(Path.Combine(ordinary, "folder", "inside.txt")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Zip_replace_keeps_a_destination_swapped_after_the_stream_recheck()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows object-bound publication required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "filecat-bound-stream-replace-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            byte[] incoming = Enumerable.Repeat((byte)'I', 4096).ToArray();
            byte[] a = Enumerable.Repeat((byte)'A', 4096).ToArray();
            byte[] b = Enumerable.Repeat((byte)'B', 4096).ToArray();
            string archivePath = Path.Combine(root, "owned.zip");
            using (var file = File.Create(archivePath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            using (var stream = archive.CreateEntry("same.bin").Open()) stream.Write(incoming);
            string dest = Directory.CreateDirectory(Path.Combine(root, "dest")).FullName;
            string target = Path.Combine(dest, "same.bin");
            File.WriteAllBytes(target, a);
            var files = new SwapAtStreamPublication(Path.Combine(root, "retired-A.bin"), b);
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            providers.Register(new ZipProvider(Path.Combine(root, "spool")));
            var jobs = new JobManager(files, providers, Path.Combine(root, "journal"));
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Extract,
                Sources = [new ItemRef(ZipProvider.ForFile(archivePath), "same.bin", EntryKind.File)],
                Destination = Location.FileSystem(dest),
                Options = new TransferOptions { Conflicts = ConflictPolicy.Replace },
            });
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.True(files.Called);
            Assert.NotEqual(JobState.Completed, job.State);
            Assert.Equal(b, File.ReadAllBytes(target));
            Assert.Equal(a, File.ReadAllBytes(Path.Combine(root, "retired-A.bin")));
            Assert.Contains(job.Issues, i => i.Message.Contains("Not replaced", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Zip_extraction_holds_a_child_folder_through_the_publication_window()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows directory leases required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "filecat-bound-extract-window-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string archivePath = Path.Combine(root, "owned.zip");
            using (var file = File.Create(archivePath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("folder/inside.txt").Open()))
                writer.Write("archive bytes");
            string outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
            string marker = Path.Combine(outside, "marker.txt");
            File.WriteAllText(marker, "outside marker");
            string dest = Directory.CreateDirectory(Path.Combine(root, "dest")).FullName;
            string child = Directory.CreateDirectory(Path.Combine(dest, "folder")).FullName;
            var files = new SwapAfterDirectoryLease(child, outside);
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            providers.Register(new ZipProvider(Path.Combine(root, "spool")));
            var jobs = new JobManager(files, providers, Path.Combine(root, "journal"));
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Extract,
                Sources = [new ItemRef(ZipProvider.ForFile(archivePath), "folder", EntryKind.Directory)],
                Destination = Location.FileSystem(dest),
            });
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.True(files.Attempted);
            Assert.True(files.Blocked);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal("archive bytes", File.ReadAllText(Path.Combine(child, "inside.txt")));
            Assert.Equal("outside marker", File.ReadAllText(marker));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
