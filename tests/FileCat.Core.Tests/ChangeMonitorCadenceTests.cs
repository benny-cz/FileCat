using System.Diagnostics;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>
/// V12 (rapid change), release issue I87: a shown folder that keeps changing, as one does while a program saves a file
/// into it every moment (frames, build outputs, rotated logs), is read again every couple of seconds while it changes,
/// not only once the changes stop (ChangeMonitor coalesces bursts, but never delays a reread more than two seconds past
/// the first change). A single file growing does not show it on NTFS: the system reports its new size only when the
/// file is closed.
/// </summary>
public sealed class ChangeMonitorCadenceTests
{
    [Fact]
    public void Refresh_delay_coalesces_notifications_and_caps_the_burst_subject_to_the_scan_interval()
    {
        var first = DateTime.UnixEpoch;
        var interval = TimeSpan.FromMilliseconds(300);
        Assert.Equal(TimeSpan.FromMilliseconds(250), ChangeMonitor.RefreshDelay(first, first, DateTime.MinValue, interval));
        Assert.Equal(TimeSpan.FromMilliseconds(100), ChangeMonitor.RefreshDelay(first.AddMilliseconds(1900), first, DateTime.MinValue, interval));
        Assert.Equal(TimeSpan.Zero, ChangeMonitor.RefreshDelay(first.AddMilliseconds(2100), first, DateTime.MinValue, interval));
        Assert.Equal(TimeSpan.FromMilliseconds(100), ChangeMonitor.RefreshDelay(first.AddSeconds(2), first, first.AddMilliseconds(1800), interval));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_folder_that_keeps_changing_is_read_again_while_it_changes(bool holdLastWriter)
    {
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDir();
        string folder = Directory.CreateDirectory(Path.Combine(dir.Path, "growing")).FullName;
        var clock = Stopwatch.StartNew();
        var reads = new List<Reading>();
        var finalObserved = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool closed = false;
        Exception? readError = null;
        using var monitor = new ChangeMonitor(folder, () =>
        {
            lock (reads)
            {
                if (closed) return;
                try
                {
                    var files = Directory.GetFiles(folder).Select(p =>
                        (Name: Path.GetFileName(p), Length: new FileInfo(p).Length))
                        .OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
                    double time = clock.Elapsed.TotalSeconds;
                    reads.Add(new Reading(time, files));
                    if (files.Contains(("final.bin", 1L))) finalObserved.TrySetResult(time);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    readError = ex;
                }
            }
        });
        if (!monitor.IsActive) Assert.Skip("This folder cannot be watched here.");

        try
        {
            // Write for at least six seconds and allow a busy native watcher to catch up while writes still continue.
            // The delay-policy test covers the exact deadline without independent writer and timer queues.
            double started = clock.Elapsed.TotalSeconds;
            int count = 0;
            int IntermediateCounts()
            {
                lock (reads) return reads.Select(read => read.Files.Length).Where(length => length > 0).Distinct().Count();
            }
            while (clock.Elapsed.TotalSeconds - started < 25)
            {
                if (clock.Elapsed.TotalSeconds - started >= 6 && IntermediateCounts() >= 2) break;
                File.WriteAllText(Path.Combine(folder, $"frame-{count++:0000}.bin"), "f");
                await Task.Delay(50, ct);
            }
            double finalWriteStarted = clock.Elapsed.TotalSeconds;
            File.WriteAllText(Path.Combine(folder, "final.bin"), "f");
            double finalWriteReturned = clock.Elapsed.TotalSeconds;
            var expected = Enumerable.Range(0, count).Select(i => (Name: $"frame-{i:0000}.bin", Length: 1L))
                .Append((Name: "final.bin", Length: 1L)).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();

            // A writer can be descheduled after its write while the watcher has already read the completed file.
            // The held case forces that ordering; a later writer-side timestamp must not demand a redundant reread.
            if (holdLastWriter)
            {
                await finalObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
                await Task.Delay(50, ct);
            }
            double writerSample = clock.Elapsed.TotalSeconds;
            double finalRead = await finalObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Reading[] asked;
            lock (reads) asked = [.. reads];
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{count + 1} files; writes from {started:F2} s to {finalWriteStarted:F2} s; final write returned at {finalWriteReturned:F2} s, writer sampled at {writerSample:F2} s; " +
                $"rereads (time/file count): {string.Join(", ", asked.Select(r => $"{r.Time:F2}/{r.Files.Length}"))}");
            Assert.Null(readError);
            int during = asked.Where(r => r.Time < finalWriteStarted && r.Files.Length > 0).Select(r => r.Files.Length).Distinct().Count();
            Assert.True(during >= 2, $"{during} different folder snapshots while files kept arriving for {finalWriteStarted - started:F2} seconds");
            Assert.Contains(asked, r => r.Files.SequenceEqual(expected));
            if (holdLastWriter) Assert.True(finalRead < writerSample, "The controlled writer hold did not follow the final-file observation.");
        }
        finally
        {
            monitor.Dispose();
            // Finish an in-flight read before TempDir removes the folder; later callbacks observe closed.
            lock (reads) closed = true;
        }
    }

    /// <summary>
    /// A folder that keeps changing (a log written every second, in C:\Windows say) is read again at most every
    /// <see cref="ChangeMonitor.ChurnInterval"/> once it counts as busy, which it says (for the status line); a quiet
    /// spell ends that, and the next change is read again promptly. The knobs are shortened here.
    /// </summary>
    [Fact]
    public async Task A_busy_folder_is_read_again_at_the_churn_interval_until_it_calms()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDir();
        string folder = Directory.CreateDirectory(Path.Combine(dir.Path, "busy")).FullName;
        var clock = Stopwatch.StartNew();
        var reads = new List<double>();
        var fired = new List<DateTime>();
        int churnChanges = 0;
        using var monitor = new ChangeMonitor(folder, () => { lock (reads) reads.Add(clock.Elapsed.TotalSeconds); })
        {
            ChurnInterval = TimeSpan.FromSeconds(1), ChurnWindow = TimeSpan.FromSeconds(5), CalmAfter = TimeSpan.FromSeconds(1.5),
            Fired = time => { lock (fired) fired.Add(time); },
        };
        monitor.ChurnChanged += () => Interlocked.Increment(ref churnChanges);
        if (!monitor.IsActive) Assert.Skip("This folder cannot be watched here.");
        double[] Reads() { lock (reads) return [.. reads]; }
        async Task Until(Func<bool> condition, TimeSpan limit)
        {
            var deadline = clock.Elapsed + limit;
            while (!condition() && clock.Elapsed < deadline) await Task.Delay(25, ct);
        }

        // A line every 400 ms for six seconds, as a busy log is written: each change settles (a quarter second) before
        // the next, so without the churn interval every one was read again; after three rereads within the window the
        // folder counts as busy. (Changes closer together than a quarter second are already read every two seconds.)
        double started = clock.Elapsed.TotalSeconds;
        for (int i = 0; clock.Elapsed.TotalSeconds - started < 6; i++)
        {
            File.WriteAllText(Path.Combine(folder, $"f{i:0000}.txt"), "x");
            await Task.Delay(400, ct);
        }
        Assert.True(monitor.IsChurning, $"rereads: {string.Join(", ", Reads().Select(t => t.ToString("F2")))}");
        // From the reread that made the folder count as busy (the third), on the monitor's own clock: the interval counts
        // from the time a reread was taken at. The times its callbacks ran at add the runner's scheduling (on Windows on Arm
        // in CI two of them ran 0.87 s apart). A timer may fall due a system timer tick (15.6 ms) early. Without the
        // interval these rereads come 0.4 s apart.
        DateTime[] taken;
        lock (fired) taken = [.. fired.Order()];
        var gaps = taken.Skip(ChangeMonitor.ChurnRereads - 1).Zip(taken.Skip(ChangeMonitor.ChurnRereads), (a, b) => (b - a).TotalSeconds).ToArray();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"rereads: {string.Join(", ", Reads().Select(t => t.ToString("F2")))}; taken apart while busy: {string.Join(", ", gaps.Select(g => g.ToString("F3")))}");
        Assert.NotEmpty(gaps);
        Assert.All(gaps, gap => Assert.True(gap >= 0.95, $"rereads taken {gap:F3} s apart while the folder counted as busy"));

        // Quiet for longer than CalmAfter: no longer busy, and a change is read again within the usual quarter second.
        await Until(() => !monitor.IsChurning, TimeSpan.FromSeconds(5));
        Assert.False(monitor.IsChurning);
        Assert.Equal(2, Volatile.Read(ref churnChanges));
        int before = Reads().Length;
        double written = clock.Elapsed.TotalSeconds;
        File.WriteAllText(Path.Combine(folder, "late.txt"), "x");
        await Until(() => Reads().Length > before, TimeSpan.FromSeconds(3));
        Assert.True(Reads().Length > before, "The change after the quiet spell was not read again.");
        Assert.True(Reads()[before] - written < 0.9, $"read again {Reads()[before] - written:F2} s after the change");
    }

    /// <summary>
    /// A change noticed as a reread falls due, before that reread takes the monitor's lock, is read by that reread: the
    /// timer it armed again from the reread before must not read the folder a second time a quarter second later (on macOS
    /// in CI a busy folder was read 0.4 s after the last time this way). The held timer callback forces that order.
    /// </summary>
    [Fact]
    public async Task A_change_noticed_as_a_reread_falls_due_is_not_read_again_on_its_own()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDir();
        string folder = Directory.CreateDirectory(Path.Combine(dir.Path, "race")).FullName;
        var clock = Stopwatch.StartNew();
        var reads = new List<double>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int due = 0;
        using var monitor = new ChangeMonitor(folder, () => { lock (reads) reads.Add(clock.Elapsed.TotalSeconds); });
        if (!monitor.IsActive) Assert.Skip("This folder cannot be watched here.");
        monitor.BeforeFire = () =>
        {
            if (Interlocked.Increment(ref due) != 1) return;
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        monitor.Again(); // a change: a reread falls due a quarter second later
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), ct), "The reread never fell due.");
        monitor.Again(); // noticed while that reread is held before the lock: it will read this change too
        release.Set();
        await Task.Delay(1500, ct); // well past the quarter second the second notice armed the timer for

        double[] times;
        lock (reads) times = [.. reads];
        Assert.True(times.Length == 1, $"rereads: {string.Join(", ", times.Select(t => t.ToString("F2")))}");
        Assert.True(Volatile.Read(ref due) >= 2, "The timer the second notice armed never fell due.");
    }

    private sealed record Reading(double Time, (string Name, long Length)[] Files);
}
