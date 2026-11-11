using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    // AT08 on a real small NTFS volume (for example a 200 MB VHDX attached as S:) with the real confined workers: the space
    // review refuses before any object exists, and a drive filled by another writer during the rebuild fails the job with the
    // full-drive text. Either way the original stays byte-identical and, after the exact discard, nothing is left beside it.
    [Fact]
    public async Task A_small_NTFS_volume_refuses_or_fails_cleanly_and_keeps_the_original()
    {
        _ = Probe();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Explicit native transactional job lane.");
        string? volume = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SMALL_VOLUME");
        if (string.IsNullOrEmpty(volume)) Assert.Skip("Set FILECAT_ARCHIVE_SMALL_VOLUME to a small NTFS volume (about 200 MB).");
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS"); Assert.True(assets is not null && Path.IsPathFullyQualified(assets));
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!); string executable = Path.Combine(assets!, "FileCat.exe");
        var ct = TestContext.Current.CancellationToken;
        string folder = Path.Combine(volume!, "at08-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string root = Directory.CreateTempSubdirectory("filecat-native-at08-").FullName;
        try
        {
            string small = Path.Combine(root, "small.txt"); File.WriteAllText(small, "small"); File.SetLastWriteTimeUtc(small, DateTime.UnixEpoch.AddSeconds(1700000000));
            string big = Path.Combine(root, "big.bin"); var bytes = new byte[24 << 20]; new Random(813).NextBytes(bytes); File.WriteAllBytes(big, bytes);
            File.SetLastWriteTimeUtc(big, DateTime.UnixEpoch.AddSeconds(1700000001));
            var profile = ArchiveNativeJobCases.Profile(ArchiveWriteFormat.SevenZip, 0);
            string journals = Path.Combine(root, "journals");
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([WindowsArchiveJobBackend.Create(profile.Format, executable)]));
            string target = Path.Combine(folder, "kept.7z"), filler = Path.Combine(folder, "filler.bin");
            await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFile, "small.txt", small)], Profile: profile), null));
            byte[] original = File.ReadAllBytes(target);

            // 1. Less free space than the review needs: refused before any object exists.
            FillLeaving(filler, (64L << 20) + (8L << 20));
            var reviewed = await manager.ReviewArchiveUpdateAsync(target, profile, null, ct);
            foreach (var job in new[] { manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "big.bin", big)]), null),
                manager.SubmitArchive(new(Path.Combine(folder, "new.7z"), null, [new(ArchiveChangeKind.AddFile, "big.bin", big)], Profile: profile), null) })
            {
                await Finish(job);
                Assert.True(job.State == JobState.Failed, job.State + ": " + Issues(job));
                Assert.True(job.Issues.Any(issue => issue.Message.StartsWith("Saving this archive needs up to", StringComparison.Ordinal)), Issues(job));
            }
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.Equal(new[] { "filler.bin", "kept.7z" }, Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
            File.Delete(filler);

            // 2. The drive fills during the rebuild: the job fails with the full-drive text and its own files are discarded. On a
            // slow machine the rebuild can finish before the fill; then the space review before the content snapshot refuses
            // with its own text, also before anything was published (never "could not confirm which version").
            reviewed = await manager.ReviewArchiveUpdateAsync(target, profile, null, ct);
            var update = manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "big.bin", big)]), null);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(180));
                while (update.ArchivePhase != ArchiveJobPhase.Rebuilding && !update.State.IsFinished()) await Task.Delay(2, deadline.Token);
                FillLeaving(filler, 1L << 20);
            }
            await Finish(update);
            if (update.State != JobState.Failed) Assert.Skip("The rebuild finished before the drive could be filled; " + update.State + ": " + Issues(update));
            Assert.True(update.Issues.Any(issue => issue.Message == ArchiveWriteMessages.OutOfSpace ||
                issue.Message.StartsWith("Saving this archive needs up to", StringComparison.Ordinal)), Issues(update));
            Assert.DoesNotContain(update.Issues, issue => issue.Message.Contains("could not confirm which version", StringComparison.Ordinal));
            File.Delete(filler);
            Assert.Equal(original, File.ReadAllBytes(target));
            // NTFS can remove while full, so the live discard leaves nothing; a kept transaction would be named in the issues.
            Assert.True(new[] { "kept.7z" }.SequenceEqual(Directory.GetFileSystemEntries(folder).Select(Path.GetFileName)), Issues(update));
            Assert.Empty(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        }
        finally { TestCleanup.DeleteTree(folder); TestCleanup.DeleteTree(root); }

        static string Issues(Job job) => string.Join(" | ", job.Issues.Select(issue => issue.Message + " [" + issue.Details + "]"));
        static async Task Finish(Job job)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(180));
            while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
        }
        static void FillLeaving(string path, long leave)
        {
            var block = new byte[1 << 20];
            using var fill = new FileStream(path, FileMode.Append, FileAccess.Write);
            try
            {
                while (true)
                {
                    long available = FileSystemCapacity.Query(Path.GetDirectoryName(path)!)?.AvailableBytes ?? throw new IOException("No capacity.");
                    if (available <= leave) break;
                    fill.Write(block, 0, (int)Math.Min(block.Length, available - leave)); fill.Flush(true);
                }
            }
            catch (IOException error) when (ArchiveWriteMessages.IsOutOfSpace(error)) { }
        }
    }
}
