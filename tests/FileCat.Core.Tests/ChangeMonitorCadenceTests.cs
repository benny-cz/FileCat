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
    /// Each is judged on the monitor's own clock: the times it noticed each change, took each reread, and turned busy or
    /// calm at. The times its callbacks ran at, and the times this test wrote at, add the runner's scheduling: on Windows
    /// on Arm in CI two callbacks ran 0.87 s apart, and once the writes or their notices were held up for longer than the
    /// quiet spell, so the folder rightly counted as calm when the writes ended (I667).
    /// </summary>
    [Fact]
    public async Task A_busy_folder_is_read_again_at_the_churn_interval_until_it_calms()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDir();
        string folder = Directory.CreateDirectory(Path.Combine(dir.Path, "busy")).FullName;
        var clock = Stopwatch.StartNew();
        var interval = TimeSpan.FromSeconds(1);
        var window = TimeSpan.FromSeconds(5);
        var calmAfter = TimeSpan.FromSeconds(1.5);
        var taken = new List<DateTime>();
        var notices = new List<(DateTime At, DateTime Due)>();
        var turns = new List<(DateTime At, bool Busy)>();
        int churnChanges = 0;
        using var monitor = new ChangeMonitor(folder, () => { })
        {
            ChurnInterval = interval, ChurnWindow = window, CalmAfter = calmAfter,
            Fired = time => { lock (taken) taken.Add(time); },
            Noticed = (time, due) => { lock (notices) notices.Add((time, due)); },
            Turned = (time, busy) => { lock (turns) turns.Add((time, busy)); },
        };
        monitor.ChurnChanged += () => Interlocked.Increment(ref churnChanges);
        if (!monitor.IsActive) Assert.Skip("This folder cannot be watched here.");
        DateTime[] Taken() { lock (taken) return [.. taken.Order()]; }
        (DateTime At, DateTime Due)[] Notices() { lock (notices) return [.. notices.OrderBy(n => n.At)]; }
        (DateTime At, bool Busy)[] Turns() { lock (turns) return [.. turns.OrderBy(t => t.At)]; }
        // The rereads taken while the folder counted as busy, spell by spell: from the one that made it busy to the last
        // before it turned calm.
        List<DateTime[]> BusySpells()
        {
            var all = Taken();
            var turnedAt = Turns();
            var spells = new List<DateTime[]>();
            for (int i = 0; i < turnedAt.Length; i++)
            {
                if (!turnedAt[i].Busy) continue;
                var (start, end) = (turnedAt[i].At, i + 1 < turnedAt.Length ? turnedAt[i + 1].At : DateTime.MaxValue);
                spells.Add([.. all.Where(t => t >= start && t < end)]);
            }
            return spells;
        }
        double[] BusyGaps() => [.. BusySpells().SelectMany(spell => spell.Zip(spell.Skip(1), (a, b) => (b - a).TotalSeconds))];
        string Record()
        {
            var origin = Notices() is [var first, ..] ? first.At : DateTime.UtcNow;
            string Seconds(DateTime t) => (t - origin).TotalSeconds.ToString("F3");
            return $"noticed at: {string.Join(", ", Notices().Select(n => Seconds(n.At)))}; rereads taken at: " +
                $"{string.Join(", ", Taken().Select(Seconds))}; turned: {string.Join(", ", Turns().Select(t => $"{(t.Busy ? "busy" : "calm")} {Seconds(t.At)}"))}";
        }
        async Task Until(Func<bool> condition, TimeSpan limit)
        {
            var deadline = clock.Elapsed + limit;
            while (!condition() && clock.Elapsed < deadline) await Task.Delay(25, ct);
        }

        // A line every 400 ms, as a busy log is written: each change settles (a quarter second) before the next, so without
        // the churn interval every one was read again; after three rereads within the window the folder counts as busy.
        // (Changes closer together than a quarter second are already read every two seconds.) For six seconds at least,
        // and on until the folder counts as busy and two rereads have been taken apart while it did: a runner that holds up
        // the writes or their notices for longer than the quiet spell rightly makes the folder calm in between.
        double started = clock.Elapsed.TotalSeconds;
        for (int i = 0; clock.Elapsed.TotalSeconds - started < 25; i++)
        {
            if (clock.Elapsed.TotalSeconds - started >= 6 && monitor.IsChurning && BusyGaps().Length >= 2) break;
            File.WriteAllText(Path.Combine(folder, $"f{i:0000}.txt"), "x");
            await Task.Delay(400, ct);
        }
        Assert.True(monitor.IsChurning && BusyGaps().Length >= 2, Record());

        // Quiet for longer than CalmAfter: no longer busy, which it says once more.
        await Until(() => !monitor.IsChurning, TimeSpan.FromSeconds(10));
        Assert.False(monitor.IsChurning, Record());
        var turned = Turns();
        await Until(() => Volatile.Read(ref churnChanges) >= turned.Length, TimeSpan.FromSeconds(5));
        Assert.Equal(turned.Length, Volatile.Read(ref churnChanges));
        Assert.Equal(turned.Select((_, i) => i % 2 == 0), turned.Select(t => t.Busy)); // busy, calm, and so on
        TestContext.Current.TestOutputHelper?.WriteLine(Record() +
            $"; taken apart while busy: {string.Join(", ", BusyGaps().Select(g => g.ToString("F3")))}");

        var rereads = Taken();
        var noticed = Notices();
        foreach (var (at, busy) in turned)
        {
            if (busy)
            {
                // Busy from the third reread within the window, counted since the folder last turned calm.
                var since = turned.Where(t => !t.Busy && t.At < at).Select(t => t.At).DefaultIfEmpty(DateTime.MinValue).Max();
                Assert.Contains(at, rereads);
                Assert.Equal(ChangeMonitor.ChurnRereads, rereads.Count(t => t > since && t <= at && at - t <= window));
            }
            else
            {
                // Calm only after a quiet spell: no change noticed for CalmAfter.
                var last = noticed.Where(n => n.At <= at).Max(n => n.At);
                Assert.True(at - last >= calmAfter, $"calm {(at - last).TotalSeconds:F3} s after the last change; {Record()}");
            }
        }
        // While busy, rereads are taken an interval apart: it counts from the time the last one was taken at. A timer may
        // fall due a system timer tick (15.6 ms) early. Without the interval these rereads come 0.4 s apart.
        double floor = (interval - TimeSpan.FromMilliseconds(15.6)).TotalSeconds;
        Assert.All(BusyGaps(), gap => Assert.True(gap >= floor, $"rereads taken {gap:F3} s apart while the folder counted as busy; {Record()}"));

        // A change after the quiet spell is read again within the usual quarter second (a little more had the folder been
        // read moments before), not at the churn interval.
        var calm = turned[^1].At;
        File.WriteAllText(Path.Combine(folder, "late.txt"), "x");
        (DateTime At, DateTime Due)[] Late() => [.. Notices().Where(n => n.At > calm).Take(1)];
        await Until(() => Late() is [var change] && Taken().Any(t => t >= change.At), TimeSpan.FromSeconds(10));
        var late = Late();
        Assert.True(late.Length == 1 && Taken().Any(t => t >= late[0].At), "The change after the quiet spell was not read again. " + Record());
        var soon = late[0].Due - late[0].At;
        Assert.True(soon <= monitor.MinInterval, $"read again {soon.TotalSeconds:F3} s after the change; {Record()}");
    }

    /// <summary>
    /// A reread armed before the last one can still fall due after it: a timer does not withdraw a callback it has queued
    /// when it is armed again. A change noticed while that callback waits arms the timer for when its reread falls due;
    /// the waiting callback must not read the folder before the interval since the last reread ends. The held callbacks
    /// force that order; the interval is long here, so an early reread stands out.
    /// </summary>
    [Fact]
    public async Task A_reread_armed_before_the_last_one_waits_for_its_interval()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDir();
        string folder = Directory.CreateDirectory(Path.Combine(dir.Path, "stale")).FullName;
        var interval = TimeSpan.FromSeconds(2);
        var taken = new List<DateTime>();
        using var firstDue = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var firstTaken = new ManualResetEventSlim();
        using var staleDue = new ManualResetEventSlim();
        using var releaseStale = new ManualResetEventSlim();
        int due = 0;
        using var monitor = new ChangeMonitor(folder, () => { })
        {
            MinInterval = interval,
            Fired = time => { lock (taken) taken.Add(time); firstTaken.Set(); },
        };
        if (!monitor.IsActive) Assert.Skip("This folder cannot be watched here.");
        monitor.BeforeFire = () =>
        {
            switch (Interlocked.Increment(ref due))
            {
                case 1: firstDue.Set(); releaseFirst.Wait(TimeSpan.FromSeconds(10)); break;
                // Held until the first reread has been taken, however late its own thread runs.
                case 2: firstTaken.Wait(TimeSpan.FromSeconds(10)); staleDue.Set(); releaseStale.Wait(TimeSpan.FromSeconds(10)); break;
            }
        };
        DateTime[] Taken() { lock (taken) return [.. taken.Order()]; }

        monitor.Again(); // a change: the first reread falls due a quarter second later
        Assert.True(firstDue.Wait(TimeSpan.FromSeconds(5), ct), "The first reread never fell due.");
        monitor.Again(); // noticed as it falls due: arms the timer again, before any reread was taken
        releaseFirst.Set(); // the first reread is taken, and reads this change too
        Assert.True(staleDue.Wait(TimeSpan.FromSeconds(5), ct), "The timer armed before the first reread never fell due.");
        monitor.Again(); // a change after the first reread, noticed while that callback waits: due an interval after it
        releaseStale.Set();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (Taken().Length < 2 && DateTime.UtcNow < deadline) await Task.Delay(25, ct);
        await Task.Delay(500, ct); // and no reread after it

        var times = Taken();
        Assert.True(times.Length == 2, $"{times.Length} rereads");
        var apart = times[1] - times[0];
        Assert.True(apart >= interval - TimeSpan.FromMilliseconds(15.6), $"rereads taken {apart.TotalSeconds:F3} s apart");
        Assert.True(Volatile.Read(ref due) >= 3, "The second reread was not taken when the timer fell due again.");
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
