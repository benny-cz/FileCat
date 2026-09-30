using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// Release issue I26: progress never shows 100% while work remains, and the time left is realistic, honest about its
/// uncertainty (a likely and a pessimistic value), and steady. Jobs are simulated file by file with a known true time
/// left at every quarter-second tick of the display.
/// </summary>
public sealed class ProgressEstimatorTests
{
    /// <summary>A file of the simulated job: its bytes and how long it truly takes.</summary>
    private sealed record Piece(long Bytes, double Seconds);

    private sealed record Tick(double Time, ProgressEstimate Estimate, double TrueLeft, bool Advancing);

    /// <summary>Runs the job through the estimator; <paramref name="pauses"/> are (start, length) of time not advancing.</summary>
    private static List<Tick> Run(IReadOnlyList<Piece> pieces, bool totalsFinal = true, params (double Start, double Length)[] pauses)
    {
        var estimator = new ProgressEstimator();
        long workTotal = pieces.Sum(p => p.Bytes);
        double jobTime = pieces.Sum(p => p.Seconds);
        var ticks = new List<Tick>();
        double t = 0, worked = 0;
        int piece = 0;
        double intoPiece = 0;
        long doneBytes = 0;
        while (piece < pieces.Count)
        {
            t += 0.25;
            bool advancing = !pauses.Any(p => t > p.Start && t <= p.Start + p.Length);
            if (advancing)
            {
                double step = 0.25;
                worked += step;
                while (step > 0 && piece < pieces.Count)
                {
                    double need = pieces[piece].Seconds - intoPiece;
                    if (step >= need)
                    {
                        step -= need;
                        doneBytes += pieces[piece].Bytes;
                        piece++;
                        intoPiece = 0;
                    }
                    else
                    {
                        intoPiece += step;
                        step = 0;
                    }
                }
            }
            long partial = piece < pieces.Count && pieces[piece].Seconds > 0 ? (long)(pieces[piece].Bytes * intoPiece / pieces[piece].Seconds) : 0;
            var e = estimator.Update(new ProgressSample(TimeSpan.FromSeconds(t), doneBytes + partial, workTotal, piece, pieces.Count, totalsFinal, advancing));
            ticks.Add(new Tick(t, e, Math.Max(0, jobTime - worked), advancing));
        }
        return ticks;
    }

    private static List<Piece> Files(int count, long bytes, double seconds, int seed = 1, double noise = 0)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count).Select(_ => new Piece(bytes, seconds * (1 + noise * (2 * random.NextDouble() - 1)))).ToList();
    }

    private static void FractionIsHonest(List<Tick> ticks)
    {
        double last = 0;
        foreach (var tick in ticks)
        {
            Assert.True(tick.Estimate.Fraction >= last, $"The bar went back at {tick.Time} s.");
            Assert.True(tick.Estimate.Fraction <= 0.99, $"{tick.Estimate.Fraction:P1} shown at {tick.Time} s while work remained.");
            last = tick.Estimate.Fraction;
        }
    }

    [Fact]
    public void One_large_file_gets_a_steady_accurate_time_left()
    {
        var ticks = Run([new Piece(3_000_000_000, 30)]);
        FractionIsHonest(ticks);
        var shown = ticks.Where(t => t.Estimate.Likely is not null).ToList();
        Assert.NotEmpty(shown);
        Assert.All(ticks.Where(t => t.Time < 2), t => Assert.Null(t.Estimate.Likely)); // measuring first
        foreach (var t in shown.Where(t => t.Time >= 4))
            Assert.InRange(t.Estimate.Likely!.Value.TotalSeconds, t.TrueLeft * 0.85 - 1.5, t.TrueLeft * 1.15 + 1.5);
        for (int i = 1; i < shown.Count; i++)
            Assert.True(shown[i].Estimate.Likely <= shown[i - 1].Estimate.Likely + TimeSpan.FromSeconds(0.5), $"The time left rose at {shown[i].Time} s.");
    }

    [Fact]
    public void Many_small_files_are_timed_by_the_file()
    {
        var ticks = Run(Files(20_000, 4096, 0.0025));
        FractionIsHonest(ticks);
        foreach (var t in ticks.Where(t => t.Time >= 5 && t.Estimate.Likely is not null))
            Assert.InRange(t.Estimate.Likely!.Value.TotalSeconds, t.TrueLeft * 0.8 - 1.5, t.TrueLeft * 1.2 + 1.5);
        // The bar follows the files, not their few bytes.
        var half = ticks.First(t => t.Time >= 25);
        Assert.InRange(half.Estimate.Fraction, 0.4, 0.6);
    }

    [Fact]
    public void Small_files_after_large_ones_are_within_the_pessimistic_time_and_learned_soon()
    {
        var pieces = Files(10, 1_000_000_000, 10).Concat(Files(20_000, 4096, 0.005)).ToList();
        var ticks = Run(pieces);
        FractionIsHonest(ticks);
        // While only large files moved, the likely time cannot know the small files' pace, but the pessimistic one covers it.
        var large = ticks.Where(t => t.Time is >= 10 and < 95 && t.Estimate.Pessimistic is not null).ToList();
        Assert.NotEmpty(large);
        Assert.True(large.Count(t => t.Estimate.Pessimistic!.Value.TotalSeconds >= t.TrueLeft) >= large.Count * 0.9,
            "The pessimistic time left was below the truth while the work left differed from the work measured.");
        // Half a minute into the small files, the likely time is right.
        foreach (var t in ticks.Where(t => t.Time >= 135 && t.Estimate.Likely is not null))
            Assert.InRange(t.Estimate.Likely!.Value.TotalSeconds, t.TrueLeft * 0.75 - 2, t.TrueLeft * 1.25 + 2);
    }

    [Fact]
    public void Noisy_progress_gives_a_time_left_that_does_not_jump()
    {
        var ticks = Run(Files(600, 10_000_000, 0.1, seed: 7, noise: 0.4));
        FractionIsHonest(ticks);
        var shown = ticks.Where(t => t.Estimate.Likely is not null && t.Time >= 5).ToList();
        for (int i = 1; i < shown.Count; i++)
        {
            double before = shown[i - 1].Estimate.Likely!.Value.TotalSeconds, now = shown[i].Estimate.Likely!.Value.TotalSeconds;
            Assert.True(now <= before + Math.Max(1, before * 0.1), $"The time left jumped from {before:0.0} s to {now:0.0} s at {shown[i].Time} s.");
            Assert.InRange(now, shown[i].TrueLeft * 0.75 - 2, shown[i].TrueLeft * 1.25 + 2);
        }
    }

    [Fact]
    public void Time_paused_or_waiting_for_an_answer_is_not_the_jobs_pace()
    {
        var ticks = Run(Files(300, 10_000_000, 0.1), true, (10, 30));
        FractionIsHonest(ticks);
        Assert.All(ticks.Where(t => !t.Advancing), t => Assert.Null(t.Estimate.Likely));
        foreach (var t in ticks.Where(t => t.Time >= 45 && t.Estimate.Likely is not null))
            Assert.InRange(t.Estimate.Likely!.Value.TotalSeconds, t.TrueLeft * 0.8 - 1.5, t.TrueLeft * 1.2 + 1.5);
    }

    [Fact]
    public void A_stall_shows_no_countdown_and_a_growing_total_no_time_left()
    {
        // Nothing moves for 12 seconds (an item without bytes that takes that long): no time left is claimed then.
        var pieces = Files(50, 10_000_000, 0.2).Append(new Piece(0, 12)).Concat(Files(50, 10_000_000, 0.2)).ToList();
        var ticks = Run(pieces);
        Assert.Contains(ticks, t => t.Estimate.Note == ProgressNote.Stalled && t.Estimate.Likely is null);
        Assert.Contains(ticks.Where(t => t.Time > 25), t => t.Estimate.Likely is not null); // back once it moves again

        var counting = Run(Files(100, 10_000_000, 0.1), totalsFinal: false);
        Assert.All(counting, t => Assert.Null(t.Estimate.Likely));
        Assert.Contains(counting, t => t.Estimate.Note == ProgressNote.Counting);
    }

    [Fact]
    public void The_pessimistic_time_is_never_below_the_likely_one()
    {
        foreach (var ticks in new[] { Run(Files(600, 10_000_000, 0.1, seed: 3, noise: 0.5)), Run(Files(5_000, 4096, 0.004, seed: 5, noise: 0.3)) })
            Assert.All(ticks.Where(t => t.Estimate.Likely is not null), t => Assert.True(t.Estimate.Pessimistic >= t.Estimate.Likely));
    }

    [Fact]
    public async Task A_verified_copy_counts_its_reading_back_as_work()
    {
        // The copy's bytes are all written long before the job ends: reading both files back takes as long again.
        using var dir = new TempDir();
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(dir.Path, "journal"));
        string file = Path.Combine(dir.Dir("src"), "big.bin");
        using (var fs = File.Create(file))
        {
            var block = new byte[1 << 20];
            new Random(1).NextBytes(block);
            for (int i = 0; i < 256; i++) fs.Write(block);
        }
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)],
            Destination = Location.FileSystem(dir.Dir("dst")),
            Options = new TransferOptions { Verify = VerifyMode.ReadBack },
        });
        // When the last byte is copied (where progress used to show 100%), about a third of the work is done.
        double? workWhenCopied = null;
        while (!job.State.IsFinished())
        {
            if (workWhenCopied is null && job.BytesTotal > 0 && job.BytesDone == job.BytesTotal)
                workWhenCopied = (double)job.WorkBytesDone / job.WorkBytesTotal;
            await Task.Delay(2, TestContext.Current.CancellationToken);
        }
        Assert.Equal(JobState.Completed, job.State);
        Assert.NotNull(workWhenCopied);
        Assert.InRange(workWhenCopied.Value, 0.3, 0.6);
        Assert.Equal(job.WorkBytesTotal, job.WorkBytesDone);
        Assert.Equal(3L * 256 << 20, job.WorkBytesTotal);
    }
}
