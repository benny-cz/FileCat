using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Tests;

public sealed class ZipSourceLinkTests
{
    private sealed class SwapAtRead(string source, string protectedBytes) : WindowsFileOperations
    {
        public override IDisposable HoldRegularFile(string path, string? root = null)
        {
            if (path == source)
            {
                File.Move(source, source + ".held");
                File.CreateSymbolicLink(source, protectedBytes);
            }
            return base.HoldRegularFile(path, root);
        }
    }

    [Fact]
    public async Task A_source_with_an_active_writer_requires_a_retry_when_packing_needs_an_Efs_hold()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows file handles are required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-zip-live-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string log = Path.Combine(root, "live.log"), zip = Path.Combine(root, "packed.zip");
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(root, "journal"));
            var request = new JobRequest
            {
                Kind = JobKind.ArchiveUpdate,
                Sources = [],
                Archive = new ArchivePlan(zip, null, [new ArchiveChange(ArchiveChangeKind.AddFile, "live.log", log)]),
            };
            Job job;
            using (var writer = new FileStream(log, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
            {
                Assert.SkipUnless(WindowsFileTransaction.CanEncrypt(writer.SafeFileHandle), "Requires an EFS-capable NTFS source volume.");
                writer.Write("line 1\n"u8);
                writer.Flush();
                job = await PackAsync();
            }
            Assert.Equal(JobState.Failed, job.State);
            Assert.Contains(job.Issues, issue => issue.Message.Contains("Close any program writing the file, then retry", StringComparison.Ordinal));
            Assert.False(File.Exists(zip));
            Assert.Equal("line 1\n", File.ReadAllText(log));

            job = await PackAsync();
            Assert.True(job.State == JobState.Completed, string.Join("; ", job.Issues.Select(i => i.Message)));
            using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
            using var entry = new StreamReader(Assert.Single(archive.Entries).Open());
            Assert.Equal("line 1\n", entry.ReadToEnd());

            async Task<Job> PackAsync()
            {
                var pack = jobs.Submit(request);
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (!pack.State.IsFinished())
                {
                    Assert.True(DateTime.UtcNow < deadline);
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }
                return pack;
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Zip_pack_refuses_a_source_swapped_to_a_link_before_content_read()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows file handles are required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-zip-link-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string source = Path.Combine(root, "source.txt");
            string target = Path.Combine(root, "outside.txt");
            string zip = Path.Combine(root, "packed.zip");
            File.WriteAllText(source, "ordinary");
            File.WriteAllText(target, "protected marker");
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new SwapAtRead(source, target), providers, Path.Combine(root, "journal"));
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.ArchiveUpdate,
                Sources = [],
                Archive = new ArchivePlan(zip, null, [new ArchiveChange(ArchiveChangeKind.AddFile, "source.txt", source)]),
            });
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.Equal(JobState.Failed, job.State);
            Assert.Equal("protected marker", File.ReadAllText(target));
            Assert.False(File.Exists(zip));
            Assert.Contains(job.Issues, i => i.Message.Contains("link", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Recursive_zip_pack_refuses_a_child_swapped_to_a_link_after_folder_enumeration()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows file handles are required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "filecat-zip-child-link-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
            string child = Path.Combine(folder, "child.txt");
            string outside = Path.Combine(root, "outside.txt");
            string zip = Path.Combine(root, "packed.zip");
            File.WriteAllText(child, "ordinary child");
            File.WriteAllText(outside, "outside marker");
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new SwapAtRead(child, outside), providers, Path.Combine(root, "journal"));
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.ArchiveUpdate,
                Sources = [],
                Archive = new ArchivePlan(zip, null,
                    [new ArchiveChange(ArchiveChangeKind.AddFolder, "folder", folder)]),
            });
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.Equal(JobState.Failed, job.State);
            Assert.Equal("outside marker", File.ReadAllText(outside));
            Assert.False(File.Exists(zip));
            Assert.Contains(job.Issues, i => i.Message.Contains("link", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
