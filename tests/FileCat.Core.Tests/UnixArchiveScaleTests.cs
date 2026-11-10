using System.Diagnostics;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    // AT18 (opt-in, FILECAT_ARCHIVE_SCALE=1): a TAR at the 100,000-member policy limit is created and updated through the
    // native job route with real worker processes; one more member past the limit refuses before anything is written.
    // Time, peak parent working set, managed heap and open descriptors are reported for the scale record.
    [Fact]
    public async Task At_the_member_limit_creation_and_update_finish_within_their_resource_budgets()
    {
        Native();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SCALE") != "1") Assert.Skip("Set FILECAT_ARCHIVE_SCALE=1 for the AT18 scale measurement.");
        using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        // 499 folders x 200 tiny files, plus the folders and the tree itself: 99,999 members.
        string tree = Path.Combine(root, "tree"); int members = 1;
        for (int f = 0; f < 499; f++)
        {
            string folder = Directory.CreateDirectory(Path.Combine(tree, $"f{f:000}")).FullName; members++;
            for (int i = 0; i < 200; i++)
            {
                string file = Path.Combine(folder, $"n{i:000}.txt"); File.WriteAllText(file, $"{f}:{i}");
                File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch.AddSeconds(1700000000)); members++;
            }
        }
        Assert.Equal(ArchiveWritePolicy.MaxMembers - 1, members);
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        var backend = UnixArchiveJobBackend.Create(profile.Format, (nonce, _) => Child.Start(nonce, "archive-assess-v2"),
            (nonce, _) => Child.Start(nonce, "archive-transform-v2"), (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(root, "journals"), new([backend]));
        string target = Path.Combine(root, "scale.tar"); int descriptors = Descriptors();

        var clock = Stopwatch.StartNew();
        await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFolder, "tree", tree)], Profile: profile), null));
        var created = clock.Elapsed; long size = new FileInfo(target).Length;

        string extra = Path.Combine(root, "extra.txt"); File.WriteAllText(extra, "the last member"); File.SetLastWriteTimeUtc(extra, DateTime.UnixEpoch.AddSeconds(1700000001));
        var assessor = new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
        clock.Restart();
        var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
        var assessed = clock.Elapsed; Assert.Equal(members, reviewed.Assessment.Manifest.Members.Count);
        clock.Restart();
        await ArchiveNativeJobCases.Completed(manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "extra.txt", extra)]), null));
        var updated = clock.Elapsed;

        // One past the limit: refused before writing, the archive unchanged.
        byte[] before = File.ReadAllBytes(target);
        string over = Path.Combine(root, "over.txt"); File.WriteAllText(over, "one too many"); File.SetLastWriteTimeUtc(over, DateTime.UnixEpoch.AddSeconds(1700000002));
        reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
        var refused = manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "over.txt", over)]), null);
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            deadline.CancelAfter(TimeSpan.FromMinutes(10));
            while (!refused.State.IsFinished()) await Task.Delay(10, deadline.Token);
        }
        Assert.Equal(JobState.Failed, refused.State);
        Assert.Equal(before, File.ReadAllBytes(target));
        Assert.Equal(new[] { "extra.txt", "journals", "over.txt", "scale.tar", "tree" },
            Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int after = Descriptors();
        var process = Process.GetCurrentProcess(); process.Refresh();
        string report = $"AT18 members={members + 1} archiveBytes={size} create={created.TotalSeconds:F1}s assess={assessed.TotalSeconds:F1}s " +
            $"update={updated.TotalSeconds:F1}s peakWorkingSet={process.PeakWorkingSet64 >> 20}MiB heapAfter={GC.GetTotalMemory(false) >> 20}MiB " +
            $"descriptors={descriptors}->{after} refusal=\"{string.Join(" | ", refused.Issues.Select(issue => issue.Message))}\"";
        TestContext.Current.TestOutputHelper?.WriteLine(report); Console.WriteLine(report);
        Assert.True(after <= descriptors + 16, report); // No descriptor growth beyond the runner's own churn.

        static int Descriptors() => OperatingSystem.IsLinux() ? Directory.GetFileSystemEntries("/proc/self/fd").Length
            : Directory.Exists("/dev/fd") ? Directory.GetFileSystemEntries("/dev/fd").Length : -1;
    }
}
