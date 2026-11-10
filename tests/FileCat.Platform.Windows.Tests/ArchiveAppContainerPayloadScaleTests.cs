using System.Diagnostics;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    // AT18 payload extremes on Windows (opt-in, native lane): an incompressible 512 MiB payload through TAR.GZ and a standalone
    // .zst, then replaced; 256 MiB of zeros, which Zstandard compresses far beyond the 1000:1 expansion rule, is refused by the
    // fresh verification with nothing published or left beside the inputs.
    [Fact]
    public async Task Incompressible_and_highly_repetitive_payloads_finish_or_refuse_within_their_budgets()
    {
        _ = Probe();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Explicit native transactional job lane.");
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SCALE") != "1") Assert.Skip("Set FILECAT_ARCHIVE_SCALE=1 for the AT18 scale measurement.");
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS"); Assert.True(assets is not null && Path.IsPathFullyQualified(assets));
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!); string executable = Path.Combine(assets!, "FileCat.exe");
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-native-at18p-").FullName;
        try
        {
            string noise = Path.Combine(root, "noise.bin"), zeros = Path.Combine(root, "zeros.bin"), other = Path.Combine(root, "other.bin");
            Write(noise, 512, seed: 1); Write(other, 64, seed: 2); Write(zeros, 256, seed: null);
            foreach (string file in new[] { noise, other, zeros }) File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch.AddSeconds(1700000000));
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(root, "journals"),
                new([WindowsArchiveJobBackend.Create(ArchiveWriteFormat.TarGzip, executable), WindowsArchiveJobBackend.Create(ArchiveWriteFormat.Zstd, executable)]));
            var gz = new ArchiveWriteProfile(ArchiveWriteFormat.TarGzip); var zst = new ArchiveWriteProfile(ArchiveWriteFormat.Zstd);
            var report = new List<string>(); var clock = Stopwatch.StartNew();

            string tar = Path.Combine(root, "noise.tar.gz");
            await Run("tar.gz create", manager.SubmitArchive(new(tar, null, [new(ArchiveChangeKind.AddFile, "noise.bin", noise)], Profile: gz), null), JobState.Completed);
            var reviewed = await manager.ReviewArchiveUpdateAsync(tar, gz, null, ct);
            await Run("tar.gz add", manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "other.bin", other)]), null), JobState.Completed);
            string single = Path.Combine(root, "noise.bin.zst");
            await Run("zst create", manager.SubmitArchive(new(single, null, [new(ArchiveChangeKind.AddFile, ArchiveStandalone.MemberName, noise)], Profile: zst), null), JobState.Completed);
            reviewed = await manager.ReviewArchiveUpdateAsync(single, zst, null, ct);
            Assert.Equal(512L << 20, Assert.Single(reviewed.Assessment.Manifest.Members).Length);
            await Run("zst replace", manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.Replace, ArchiveStandalone.MemberName, other)]), null), JobState.Completed);
            reviewed = await manager.ReviewArchiveUpdateAsync(single, zst, null, ct);
            Assert.Equal(64L << 20, Assert.Single(reviewed.Assessment.Manifest.Members).Length);

            string refusedPath = Path.Combine(root, "zeros.bin.zst");
            var refused = await Run("zeros refused", manager.SubmitArchive(new(refusedPath, null, [new(ArchiveChangeKind.AddFile, ArchiveStandalone.MemberName, zeros)], Profile: zst), null), JobState.Failed);
            Assert.Contains(refused.Issues, issue => issue.Message.Contains("expanded data", StringComparison.Ordinal));
            Assert.False(File.Exists(refusedPath));
            Assert.Equal(new[] { "journals", "noise.bin", "noise.bin.zst", "noise.tar.gz", "other.bin", "zeros.bin" },
                Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
            var process = Process.GetCurrentProcess(); process.Refresh();
            string summary = "AT18 payloads " + string.Join("; ", report) + $"; peakWorkingSet={process.PeakWorkingSet64 >> 20}MiB heap={GC.GetTotalMemory(false) >> 20}MiB";
            TestContext.Current.TestOutputHelper?.WriteLine(summary); Console.WriteLine(summary);

            async Task<Job> Run(string name, Job job, JobState expected)
            {
                clock.Restart();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(30));
                while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
                report.Add($"{name}={clock.Elapsed.TotalSeconds:F1}s");
                Assert.True(job.State == expected, name + ": " + job.State + ": " + string.Join(" | ", job.Issues.Select(issue => issue.Message)));
                return job;
            }
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(root); }

        static void Write(string path, int mebibytes, int? seed)
        {
            var random = seed is { } value ? new Random(value) : null; byte[] chunk = new byte[1 << 20];
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
            for (int i = 0; i < mebibytes; i++) { random?.NextBytes(chunk); output.Write(chunk); }
        }
    }
}
