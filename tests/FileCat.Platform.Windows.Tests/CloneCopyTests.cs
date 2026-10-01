using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Plan §9's large-copy row asks to account for ReFS block cloning (FILECAT_CLONE_DIR=&lt;a folder on a ReFS or Dev
/// Drive volume&gt;): where a volume clones, a copy shares the source's clusters, so it takes moments and no space. The
/// job copies files over 256 MiB unbuffered; this checks that its copies still clone, beside CopyFile2's own buffered and
/// unbuffered copies of the same file.
/// </summary>
public sealed partial class CloneCopyTests
{
    [Fact]
    public async Task Copies_on_a_cloning_volume_share_the_source_clusters()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Block cloning through CopyFile2 is Windows'.");
        if (Environment.GetEnvironmentVariable("FILECAT_CLONE_DIR") is not { Length: > 0 } where)
        {
            Assert.Skip("Set FILECAT_CLONE_DIR to a folder on a ReFS volume to check block cloning.");
            return;
        }
        var ct = TestContext.Current.CancellationToken;
        var log = TestContext.Current.TestOutputHelper;
        string volume = Path.GetPathRoot(Path.GetFullPath(where))!;
        Assert.True(GetVolumeInformation(volume, null, 0, out _, out _, out uint flags, null, 0));
        if ((flags & FILE_SUPPORTS_BLOCK_REFCOUNTING) == 0) Assert.Skip($"{volume} does not clone blocks.");
        string root = Directory.CreateDirectory(Path.Combine(where, "fc-clone-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        try
        {
            const long Size = 1L << 30;
            string src = Path.Combine(root, "source.bin");
            using (var stream = new FileStream(src, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
            {
                var rng = new Random(1603);
                var block = new byte[8 << 20];
                for (long done = 0; done < Size; done += block.Length)
                {
                    rng.NextBytes(block);
                    stream.Write(block);
                }
                stream.Flush(flushToDisk: true);
            }
            var ops = new WindowsFileOperations();
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(ops, providers, Path.Combine(root, "journal"));

            var failures = new List<string>();
            foreach (var (name, copy) in new (string, Func<string, Task>)[]
                     {
                         ("CopyFile2, buffered", dst =>
                         {
                             ops.CopyFile(src, Path.Combine(dst, "source.bin"), new FileCopyOptions(), null, ct);
                             return Task.CompletedTask;
                         }),
                         ("CopyFile2, unbuffered", dst =>
                         {
                             ops.CopyFile(src, Path.Combine(dst, "source.bin"), new FileCopyOptions { NoBuffering = true }, null, ct);
                             return Task.CompletedTask;
                         }),
                         ("the copy job", async dst =>
                         {
                             var job = jobs.Submit(new JobRequest
                             {
                                 Kind = JobKind.Copy,
                                 Sources = [ItemRef.ForFileSystemPath(src, EntryKind.File)],
                                 Destination = Location.FileSystem(dst),
                             });
                             while (!job.State.IsFinished()) await Task.Delay(5, ct);
                             Assert.Equal(JobState.Completed, job.State);
                         }),
                     })
            {
                string dst = Directory.CreateDirectory(Path.Combine(root, name.Replace(",", "").Replace(' ', '-'))).FullName;
                long before = Free(volume);
                var clock = Stopwatch.StartNew();
                await copy(dst);
                var took = clock.Elapsed;
                string copied = Path.Combine(dst, "source.bin");
                using (var handle = File.OpenHandle(copied, FileMode.Open, FileAccess.ReadWrite))
                    Assert.True(FlushFileBuffers(handle.DangerousGetHandle()));
                long used = before - Free(volume);
                Assert.Equal(Size, new FileInfo(copied).Length);
                log?.WriteLine($"{name}: {took.TotalMilliseconds:F0} ms, free space down by {used / (1 << 20):N0} MiB");
                if (used > Size / 4) failures.Add($"{name} took {used / (1 << 20):N0} MiB of space: its copy was not a clone");
            }
            Assert.True(failures.Count == 0, string.Join("; ", failures));
        }
        finally
        {
            for (int i = 0; i < 5 && Directory.Exists(root); i++)
            {
                try { Directory.Delete(root, true); }
                catch (IOException) { Thread.Sleep(300); }
            }
        }
    }

    private const uint FILE_SUPPORTS_BLOCK_REFCOUNTING = 0x08000000;

    private static long Free(string volume)
    {
        Assert.True(GetDiskFreeSpaceEx(volume, out _, out _, out ulong free));
        return (long)free;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformation(string root, char[]? name, uint nameSize, out uint serial, out uint maxComponent, out uint flags, char[]? fileSystem, uint fileSystemSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong total, out ulong free);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushFileBuffers(nint handle);
}
