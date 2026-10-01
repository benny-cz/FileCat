using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Plan §9's large-copy row (FILECAT_LARGECOPY_BENCH=&lt;GiB per file&gt;, FILECAT_LARGECOPY_DIR for another volume than
/// the temporary folder's): F5 on large files costs at most 10% more than CopyFile2 alone with the same profile, on the
/// same volume. The same profile: the job copies files over 256 MiB unbuffered, so the baseline does too; a buffered
/// CopyFile2 returns while the data is still in the write cache, which measures the cache, not the copy. Four files are
/// copied by CopyFile2 and by a copy job in five pairs whose order alternates, so that a drift in the disk's state is
/// shared; every pair is reported, the median is graded. The job's time outside the copy engine (its own work) is
/// reported too, as the pairs' spread on a busy disk can exceed the budget either way. A strict copy (read back and
/// compared) is measured as well, and only reported: §9 keeps strict and default copies apart.
/// </summary>
public sealed class LargeFileCopyBenchmark
{
    [Fact]
    public async Task Large_file_copy_stays_close_to_copyfile2()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("CopyFile2 is Windows'.");
        if (!int.TryParse(Environment.GetEnvironmentVariable("FILECAT_LARGECOPY_BENCH"), out int gib) || gib < 1)
            Assert.Skip("Set FILECAT_LARGECOPY_BENCH to the size of each of four files in GiB to measure large copies.");
        var ct = TestContext.Current.CancellationToken;
        var output = TestContext.Current.TestOutputHelper;
        string root = Path.Combine(Environment.GetEnvironmentVariable("FILECAT_LARGECOPY_DIR") is { Length: > 0 } where ? where : Path.GetTempPath(),
                                   "fc-largecopy-" + Guid.NewGuid().ToString("N")[..8]);
        string src = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
        try
        {
            var rng = new Random(1602);
            var block = new byte[8 << 20];
            for (int f = 0; f < 4; f++)
            {
                using var stream = File.Create(Path.Combine(src, $"large-{f}.bin"));
                for (long done = 0; done < (long)gib << 30; done += block.Length)
                {
                    rng.NextBytes(block);
                    stream.Write(block);
                }
            }
            long total = 4L * gib << 30;
            var ops = new TimedOperations();
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(ops, providers, Path.Combine(root, "journal"));

            // Each file's time, to tell a slow disk from time spent around the copies; the job's time outside the copy
            // engine (its own work: discovery, the journal, staging and publishing) per measured job.
            var steps = new List<string>();
            var outside = new List<TimeSpan>();

            TimeSpan Baseline(string dst)
            {
                Directory.CreateDirectory(dst);
                steps.Clear();
                var clock = Stopwatch.StartNew();
                foreach (var f in Directory.EnumerateFiles(src))
                {
                    var one = Stopwatch.StartNew();
                    ops.CopyFile(f, Path.Combine(dst, Path.GetFileName(f)), new FileCopyOptions { NoBuffering = new FileInfo(f).Length > 256L << 20 }, null, ct);
                    steps.Add($"{one.Elapsed.TotalSeconds:F2}");
                }
                return clock.Elapsed;
            }

            async Task<TimeSpan> ViaJob(string dst, VerifyMode verify)
            {
                Directory.CreateDirectory(dst);
                steps.Clear();
                var clock = Stopwatch.StartNew();
                ops.Start(clock);
                var job = jobs.Submit(new JobRequest
                {
                    Kind = JobKind.Copy,
                    Sources = [.. Directory.EnumerateFiles(src).Select(f => ItemRef.ForFileSystemPath(f, EntryKind.File))],
                    Destination = Location.FileSystem(dst),
                    Options = new TransferOptions { Verify = verify },
                });
                // The moment the job says it ended (polling would add up to a timer tick).
                var ended = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
                job.Changed += j =>
                {
                    if (j.State.IsFinished()) ended.TrySetResult(clock.Elapsed);
                };
                if (job.State.IsFinished()) ended.TrySetResult(clock.Elapsed);
                var elapsed = await ended.Task.WaitAsync(ct);
                ops.Start(null);
                steps.Add("calls: " + ops.Calls());
                outside.Add(elapsed - ops.InCopies);
                Assert.Equal(JobState.Completed, job.State);
                Assert.Equal(total, Directory.EnumerateFiles(dst).Sum(f => new FileInfo(f).Length));
                return elapsed;
            }

            void Drop(string dst)
            {
                try { Directory.Delete(dst, true); } catch (IOException) { }
            }

            // Warm-up: one of each, not counted.
            Drop(Path.Combine(root, "warm-a"));
            Baseline(Path.Combine(root, "warm-a"));
            Drop(Path.Combine(root, "warm-a"));
            await ViaJob(Path.Combine(root, "warm-b"), new TransferOptions().Verify);
            Drop(Path.Combine(root, "warm-b"));

            var ratios = new List<double>();
            outside.Clear();
            for (int pair = 0; pair < 5; pair++)
            {
                string a = Path.Combine(root, $"base-{pair}"), b = Path.Combine(root, $"job-{pair}");
                TimeSpan baseline, job;
                string baseSteps, jobSteps;
                if (pair % 2 == 0)
                {
                    baseline = Baseline(a);
                    baseSteps = string.Join(", ", steps);
                    Drop(a);
                    job = await ViaJob(b, new TransferOptions().Verify);
                    jobSteps = string.Join(", ", steps);
                    Drop(b);
                }
                else
                {
                    job = await ViaJob(b, new TransferOptions().Verify);
                    jobSteps = string.Join(", ", steps);
                    Drop(b);
                    baseline = Baseline(a);
                    baseSteps = string.Join(", ", steps);
                    Drop(a);
                }
                ratios.Add(job / baseline);
                output?.WriteLine($"pair {pair} ({(pair % 2 == 0 ? "CopyFile2 first" : "the job first")}): {total >> 20:N0} MiB, CopyFile2 {baseline.TotalSeconds:F2} s " +
                                  $"({total / baseline.TotalSeconds / (1 << 20):N0} MiB/s), the job {job.TotalSeconds:F2} s, overhead {(job / baseline - 1) * 100:F1}%");
                output?.WriteLine($"  CopyFile2 per file (s): {baseSteps}; the job's {jobSteps}");
            }
            output?.WriteLine($"the job outside the copy engine: {string.Join(", ", outside.Select(t => $"{t.TotalMilliseconds:F0} ms"))}");
            var strict = await ViaJob(Path.Combine(root, "strict"), VerifyMode.ReadBack);
            Drop(Path.Combine(root, "strict"));
            output?.WriteLine($"strict copy (read back and compared): {strict.TotalSeconds:F2} s");
            var sorted = ratios.Order().ToList();
            double median = sorted[sorted.Count / 2];
            output?.WriteLine($"median overhead {(median - 1) * 100:F1}% over 5 pairs (lowest {(sorted[0] - 1) * 100:F1}%, highest {(sorted[^1] - 1) * 100:F1}%)");
            Assert.True(median <= 1.10, $"A large copy cost {(median - 1) * 100:F1}% more than CopyFile2 alone (median of 5 pairs).");
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

    /// <summary>The native operations, noting when the job's copies and renames begin and end.</summary>
    private sealed class TimedOperations : WindowsFileOperations
    {
        private Stopwatch? _clock;
        private readonly List<string> _calls = [];
        private TimeSpan _inCopies;

        /// <summary>Time spent inside the copy engine since <see cref="Start"/>.</summary>
        public TimeSpan InCopies
        {
            get { lock (_calls) return _inCopies; }
        }

        public void Start(Stopwatch? clock)
        {
            lock (_calls)
            {
                _clock = clock;
                if (clock is null) return;
                _calls.Clear();
                _inCopies = TimeSpan.Zero;
            }
        }

        public string Calls()
        {
            lock (_calls) return string.Join(" ", _calls);
        }

        private void Note(string what)
        {
            lock (_calls)
                if (_clock is { } c) _calls.Add($"{what}@{c.Elapsed.TotalSeconds:F3}");
        }

        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
        {
            Note("copy");
            var one = Stopwatch.StartNew();
            try { base.CopyFile(source, destination, options, progress, ct); }
            finally
            {
                Note("/copy");
                lock (_calls)
                    if (_clock is not null) _inCopies += one.Elapsed;
            }
        }

        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
        {
            Note("rename");
            try { base.Move(source, destination, replaceExisting, writeThrough); }
            finally { Note("/rename"); }
        }
    }
}
