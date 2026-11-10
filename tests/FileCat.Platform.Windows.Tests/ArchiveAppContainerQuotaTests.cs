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
    // AT08 quota on a real NTFS volume with the real confined workers. FILECAT_ARCHIVE_QUOTA_VOLUME names a folder on an NTFS
    // volume with ample free space where the test user's enforced disk quota is about 100 MiB: room for a small archive and
    // the review's 64 MiB margin, not for 48 MiB more. Windows reports the free space available to the caller within that
    // quota, so the space review refuses creation and update before any object exists, with the original byte-identical and
    // the folder unchanged.
    [Fact]
    public async Task An_NTFS_disk_quota_refuses_before_writing_and_keeps_the_original()
    {
        _ = Probe();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Explicit native transactional job lane.");
        string? volume = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_QUOTA_VOLUME");
        if (string.IsNullOrEmpty(volume)) Assert.Skip("Set FILECAT_ARCHIVE_QUOTA_VOLUME to an NTFS folder where the user's enforced quota is about 100 MiB.");
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS"); Assert.True(assets is not null && Path.IsPathFullyQualified(assets));
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!); string executable = Path.Combine(assets!, "FileCat.exe");
        var ct = TestContext.Current.CancellationToken;
        string folder = Path.Combine(volume!, "at08q-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string root = Directory.CreateTempSubdirectory("filecat-native-at08q-").FullName;
        try
        {
            // The quota, not the volume, is what stops this job: the volume itself has far more room than the job needs.
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!);
            Assert.True(drive.TotalFreeSpace > (256L << 20), $"The quota volume needs more than 256 MiB free; it has {drive.TotalFreeSpace >> 20} MiB.");
            Assert.True(drive.AvailableFreeSpace is > (72L << 20) and < (110L << 20),
                $"This user's quota here should leave about 100 MiB; {drive.AvailableFreeSpace >> 20} MiB are available.");
            string small = Path.Combine(root, "small.txt"); File.WriteAllText(small, "small"); File.SetLastWriteTimeUtc(small, DateTime.UnixEpoch.AddSeconds(1700000000));
            string big = Path.Combine(root, "big.bin"); var bytes = new byte[48 << 20]; new Random(814).NextBytes(bytes); File.WriteAllBytes(big, bytes);
            File.SetLastWriteTimeUtc(big, DateTime.UnixEpoch.AddSeconds(1700000001));
            var profile = ArchiveNativeJobCases.Profile(ArchiveWriteFormat.SevenZip, 0);
            string journals = Path.Combine(root, "journals");
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([WindowsArchiveJobBackend.Create(profile.Format, executable)]));
            string target = Path.Combine(folder, "kept.7z");
            await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFile, "small.txt", small)], Profile: profile), null));
            byte[] original = File.ReadAllBytes(target);

            var reviewed = await manager.ReviewArchiveUpdateAsync(target, profile, null, ct);
            foreach (var job in new[] { manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "big.bin", big)]), null),
                manager.SubmitArchive(new(Path.Combine(folder, "new.7z"), null, [new(ArchiveChangeKind.AddFile, "big.bin", big)], Profile: profile), null) })
            {
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(180));
                    while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
                }
                string issues = string.Join(" | ", job.Issues.Select(issue => issue.Message + " [" + issue.Details + "]"));
                Assert.True(job.State == JobState.Failed, job.State + ": " + issues);
                Assert.True(job.Issues.Any(issue => issue.Message.StartsWith("Saving this archive needs up to", StringComparison.Ordinal)), issues);
            }
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.Equal(new[] { "kept.7z" }, Directory.GetFileSystemEntries(folder).Select(Path.GetFileName));
            Assert.Empty(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        }
        finally { TestCleanup.DeleteTree(folder); TestCleanup.DeleteTree(root); }
    }
}
