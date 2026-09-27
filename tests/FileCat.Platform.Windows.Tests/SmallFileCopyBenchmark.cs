using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Plan §21 budget: copying many 4 KiB files costs at most 25% more than CopyFile2 alone, journaling included.
/// Set FILECAT_COPY_BENCH=100000 to run the full fixture; by default a small smoke size runs. Rounds alternate the
/// order of the baseline and the job (real-time antivirus scanning and cache warmth favor whichever runs second) and
/// the median ratio is reported.
/// </summary>
public sealed class SmallFileCopyBenchmark
{
    [Fact]
    public async Task Small_file_copy_stays_close_to_copyfile2()
    {
        if (!OperatingSystem.IsWindows()) return;
        int n = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_COPY_BENCH"), out var v) ? v : 500;
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_COPY_BENCH_ROUNDS"), out var r) ? r : 1;
        var root = Path.Combine(Path.GetTempPath(), "fc-smallcopy-" + Guid.NewGuid().ToString("N"));
        var src = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
        var output = TestContext.Current.TestOutputHelper;
        try
        {
            var data = new byte[4096];
            new Random(1).NextBytes(data);
            for (int i = 0; i < n; i++) File.WriteAllBytes(Path.Combine(src, $"f{i:000000}.bin"), data);
            var ops = new WindowsFileOperations();
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(ops, providers, Path.Combine(root, "journal"));

            TimeSpan Baseline(int round)
            {
                var dst = Directory.CreateDirectory(Path.Combine(root, "base" + round)).FullName;
                var sw = Stopwatch.StartNew();
                foreach (var f in Directory.EnumerateFiles(src)) ops.CopyFile(f, Path.Combine(dst, Path.GetFileName(f)), new FileCopyOptions(), null, default);
                return sw.Elapsed;
            }

            async Task<TimeSpan> ViaJob(int round)
            {
                var dst = Directory.CreateDirectory(Path.Combine(root, "job" + round)).FullName;
                var sw = Stopwatch.StartNew();
                var job = jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(src, EntryKind.Directory)], Destination = Location.FileSystem(dst) });
                while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
                var elapsed = sw.Elapsed;
                Assert.Equal(JobState.Completed, job.State);
                Assert.Equal(n, Directory.GetFiles(Path.Combine(dst, "src")).Length);
                return elapsed;
            }

            var ratios = new List<double>();
            for (int round = 0; round < rounds; round++)
            {
                TimeSpan baseline, job;
                if (round % 2 == 0)
                {
                    baseline = Baseline(round);
                    job = await ViaJob(round);
                }
                else
                {
                    job = await ViaJob(round);
                    baseline = Baseline(round);
                }
                ratios.Add(job / baseline);
                output?.WriteLine($"round={round} files={n} baseline={baseline.TotalSeconds:F2}s job={job.TotalSeconds:F2}s overhead={(job / baseline - 1) * 100:F0}%");
            }
            ratios.Sort();
            output?.WriteLine($"median overhead={(ratios[ratios.Count / 2] - 1) * 100:F0}% over {rounds} round(s)");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
