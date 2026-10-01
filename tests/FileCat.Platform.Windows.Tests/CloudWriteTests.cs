using System.Security.Cryptography;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Cloud providers' folders (E-CLOUD-1, writing): FileCat's own jobs in a folder a provider keeps in sync — copying in,
/// renaming, moving, deleting, the Recycle Bin, and copying out a file that is only in the cloud, which downloads it.
/// FILECAT_CLOUD_WRITE_ROOT names the provider's folder (on the owner's computer, OneDrive's); the test makes a folder
/// of its own there, works only inside it, and removes it — and from the Recycle Bin only what came from it — at the
/// end. The provider keeps the removed folder in its own online recycle bin for a while, as it does with anything
/// deleted there.
/// </summary>
public sealed class CloudWriteTests : IDisposable
{
    private const FileAttributes Pinned = (FileAttributes)0x80000, Unpinned = (FileAttributes)0x100000;
    private readonly string _local = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-cloud-write", Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_local, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Files_in_a_cloud_folder_are_copied_renamed_moved_deleted_and_downloaded_like_any_others()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Cloud Files are Windows'."); return; }
        if (Environment.GetEnvironmentVariable("FILECAT_CLOUD_WRITE_ROOT") is not { Length: > 0 } cloud || !Directory.Exists(cloud))
        { Assert.Skip("Set FILECAT_CLOUD_WRITE_ROOT to a cloud provider's folder where a test folder may be made and removed."); return; }
        var log = TestContext.Current.TestOutputHelper;
        var ct = TestContext.Current.CancellationToken;
        var before = Directory.EnumerateFileSystemEntries(cloud).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase).ToList();
        string test = Path.Combine(cloud, "FileCat-test-" + Guid.NewGuid().ToString("N")[..8]);

        // What goes in: a small text file, 4 MiB of random bytes, and a small tree.
        string src = Directory.CreateDirectory(Path.Combine(_local, "src")).FullName;
        byte[] small = Encoding.UTF8.GetBytes("FileCat in a cloud folder\n");
        byte[] big = RandomNumberGenerator.GetBytes(4 << 20);
        File.WriteAllBytes(Path.Combine(src, "a.txt"), small);
        File.WriteAllBytes(Path.Combine(src, "big.bin"), big);
        Directory.CreateDirectory(Path.Combine(src, "tree", "x"));
        File.WriteAllText(Path.Combine(src, "tree", "x", "y.txt"), "deep");

        var providers = new ProviderRegistry();
        var fsProvider = new WindowsFileSystemProvider();
        providers.Register(fsProvider);
        var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(_local, "journal"));
        try
        {
            await Run(jobs, new JobRequest { Kind = JobKind.CreateDirectory, Destination = Location.FileSystem(cloud), NewName = Path.GetFileName(test) });
            Assert.True(Directory.Exists(test));

            await Run(jobs, new JobRequest
            {
                Kind = JobKind.Copy, Destination = Location.FileSystem(test),
                Sources = [ItemRef.ForFileSystemPath(Path.Combine(src, "a.txt"), EntryKind.File), ItemRef.ForFileSystemPath(Path.Combine(src, "big.bin"), EntryKind.File),
                           ItemRef.ForFileSystemPath(Path.Combine(src, "tree"), EntryKind.Directory)],
            });
            Assert.Equal(small, File.ReadAllBytes(Path.Combine(test, "a.txt")));
            Assert.Equal(big, File.ReadAllBytes(Path.Combine(test, "big.bin")));
            Assert.Equal("deep", File.ReadAllText(Path.Combine(test, "tree", "x", "y.txt")));
            log?.WriteLine("copied in: a.txt, big.bin (4 MiB), tree/x/y.txt");

            await Run(jobs, new JobRequest { Kind = JobKind.Rename, Sources = [ItemRef.ForFileSystemPath(Path.Combine(test, "a.txt"), EntryKind.File)], NewName = "a-renamed.txt" });
            Assert.Equal(small, File.ReadAllBytes(Path.Combine(test, "a-renamed.txt")));
            Assert.False(File.Exists(Path.Combine(test, "a.txt")));

            await Run(jobs, new JobRequest { Kind = JobKind.Move, Sources = [ItemRef.ForFileSystemPath(Path.Combine(test, "big.bin"), EntryKind.File)], Destination = Location.FileSystem(Path.Combine(test, "tree")) });
            string moved = Path.Combine(test, "tree", "big.bin");
            Assert.Equal(big, File.ReadAllBytes(moved));
            Assert.False(File.Exists(Path.Combine(test, "big.bin")));
            log?.WriteLine("renamed and moved inside the folder");

            // "Free up space": asked for, FileCat shows the file as going to the cloud at once (the UNPINNED attribute); the
            // provider makes it a placeholder once it is uploaded, which the attributes that recall it say.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            File.SetAttributes(moved, (File.GetAttributes(moved) & ~Pinned) | Unpinned);
            Assert.Equal(CloudState.OnlineOnly, CloudFiles.StateOf(File.GetAttributes(moved), inSyncRoot: true));
            const FileAttributes recall = (FileAttributes)0x440000;
            while ((File.GetAttributes(moved) & (recall | FileAttributes.Offline)) == 0 && clock.Elapsed < TimeSpan.FromMinutes(4))
                await Task.Delay(1000, ct);
            var attributes = File.GetAttributes(moved);
            log?.WriteLine($"freed up by the provider after {clock.Elapsed.TotalSeconds:N0} s (attributes 0x{(int)attributes:X})");
            Assert.True((attributes & (recall | FileAttributes.Offline)) != 0, "The provider did not free the file up within four minutes.");
            var listed = new List<EntryData>();
            await fsProvider.EnumerateAsync(Location.FileSystem(Path.Combine(test, "tree")), new Sink(listed), ct);
            Assert.True(listed.Single(e => e.Name.ToString() == "big.bin").Has(EntryFlags.Offline), "The listing marks a file that is only in the cloud.");

            // Copying it out reads it, which downloads it: the copy is the file.
            string outDir = Directory.CreateDirectory(Path.Combine(_local, "out")).FullName;
            clock.Restart();
            await Run(jobs, new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(moved, EntryKind.File)], Destination = Location.FileSystem(outDir) });
            Assert.Equal(big, File.ReadAllBytes(Path.Combine(outDir, "big.bin")));
            log?.WriteLine($"copied out while only in the cloud, {clock.Elapsed.TotalSeconds:N1} s: the same 4 MiB; now {CloudFiles.StateOf(File.GetAttributes(moved), inSyncRoot: true)}");

            await Run(jobs, new JobRequest { Kind = JobKind.Delete, Sources = [ItemRef.ForFileSystemPath(Path.Combine(test, "a-renamed.txt"), EntryKind.File)] });
            Assert.False(File.Exists(Path.Combine(test, "a-renamed.txt")));
            string recycled = Path.Combine(test, "tree", "x", "y.txt");
            await Run(jobs, new JobRequest { Kind = JobKind.Recycle, Sources = [ItemRef.ForFileSystemPath(recycled, EntryKind.File)] });
            Assert.False(File.Exists(recycled));
            Assert.Contains(RecycledFrom(test), p => string.Equals(p, recycled, StringComparison.OrdinalIgnoreCase));
            log?.WriteLine("deleted one file, and sent one to the Recycle Bin (found there)");

            // The provider marks the folders it keeps in sync read-only, which once stopped FileCat's delete.
            string tree = Path.Combine(test, "tree");
            log?.WriteLine($"the provider's folder attributes: 0x{(int)File.GetAttributes(tree):X}");
            await Run(jobs, new JobRequest { Kind = JobKind.Delete, Sources = [ItemRef.ForFileSystemPath(tree, EntryKind.Directory)] });
            Assert.False(Directory.Exists(tree));
            await Run(jobs, new JobRequest { Kind = JobKind.Delete, Sources = [ItemRef.ForFileSystemPath(test, EntryKind.Directory)] });
            Assert.False(Directory.Exists(test));
            log?.WriteLine("FileCat deleted its folders there, and then the test folder itself");
        }
        finally
        {
            try
            {
                if (Directory.Exists(test))
                {
                    foreach (var f in Directory.EnumerateFiles(test, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                    foreach (var d in Directory.EnumerateDirectories(test, "*", SearchOption.AllDirectories).Append(test)) new DirectoryInfo(d).Attributes &= ~FileAttributes.ReadOnly;
                    Directory.Delete(test, recursive: true);
                }
            }
            finally
            {
                int purged = PurgeRecycledFrom(test);
                log?.WriteLine($"the test folder is gone; {purged} of its items purged from the Recycle Bin");
            }
        }
        var after = Directory.EnumerateFileSystemEntries(cloud).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(before, after);
        Assert.Empty(RecycledFrom(test));
    }

    private static async Task Run(JobManager jobs, JobRequest request)
    {
        var job = jobs.Submit(request);
        while (!job.State.IsFinished())
        {
            if (job.Decision is { } asked)
            {
                asked.Resolve(new Decision(DecisionAction.CancelJob));
                Assert.Fail($"The job asked \"{asked.Request.Title}: {asked.Request.Message}\"");
            }
            await Task.Delay(20);
        }
        Assert.True(job.State == JobState.Completed, $"{request.Kind}: {job.State}: {string.Join("; ", job.Issues.Select(i => i.Message))}");
    }

    /// <summary>The Recycle Bin's own records of this user on the folder's drive: each $I file names where its item was.</summary>
    private static IEnumerable<(string Info, string Original)> Recycled(string folder)
    {
        string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        string bin = Path.Combine(Path.GetPathRoot(folder)!, "$Recycle.Bin", sid);
        if (!Directory.Exists(bin)) yield break;
        foreach (var info in Directory.EnumerateFiles(bin, "$I*"))
        {
            byte[] d;
            try { d = File.ReadAllBytes(info); } catch (IOException) { continue; }
            // Version 2: header, size, deletion time, then the path's length in characters and the path.
            if (d.Length < 28 || BitConverter.ToInt64(d, 0) != 2) continue;
            int chars = BitConverter.ToInt32(d, 24);
            if (chars <= 0 || 28 + chars * 2 > d.Length) continue;
            yield return (info, Encoding.Unicode.GetString(d, 28, chars * 2).TrimEnd('\0'));
        }
    }

    private static List<string> RecycledFrom(string folder) =>
        Recycled(folder).Where(r => r.Original.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).Select(r => r.Original).ToList();

    /// <summary>Removes from the Recycle Bin exactly the items that came from <paramref name="folder"/>, nothing else.</summary>
    private static int PurgeRecycledFrom(string folder)
    {
        int purged = 0;
        foreach (var (info, original) in Recycled(folder).ToList())
        {
            if (!original.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            string data = Path.Combine(Path.GetDirectoryName(info)!, "$R" + Path.GetFileName(info)[2..]);
            if (File.Exists(data)) File.Delete(data);
            else if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
            File.Delete(info);
            purged++;
        }
        return purged;
    }

    private sealed class Sink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries) => list.AddRange(entries.ToArray());

        public void ReportIssue(string message) { }
    }
}
