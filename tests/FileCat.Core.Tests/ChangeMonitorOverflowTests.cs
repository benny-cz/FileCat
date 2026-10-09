using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>
/// V12: native notification-buffer overflow must cause reconciliation of the actual final folder state.
/// A delayed writer-side timestamp is not evidence that an already completed reconciliation must repeat.
/// </summary>
public sealed class ChangeMonitorOverflowTests
{
    [Fact]
    public async Task An_overflow_is_counted_and_the_folder_is_read_again_after_it()
        => await ObserveOverflow(holdWriterAfterFinalState: false);

    [Fact]
    public async Task Completed_overflow_reconciliation_survives_a_delayed_writer_sample()
        => await ObserveOverflow(holdWriterAfterFinalState: true);

    private static async Task ObserveOverflow(bool holdWriterAfterFinalState)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Forcing an overflow is measured on Windows (64 KiB buffer, long names).");
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDir();
        string folder = Directory.CreateDirectory(Path.Combine(dir.Path, "churn")).FullName;
        long reads = 0;
        bool closed = false;
        RoundState? active = null;
        var gate = new object();
        var observations = new List<Reading>();
        using var monitor = new ChangeMonitor(folder, () =>
        {
            lock (gate)
            {
                if (closed) return;
                reads++;
                var state = active;
                if (state is null) return;
                string[] names = Directory.GetFiles(folder).Select(p => Path.GetFileName(p)).Order(StringComparer.Ordinal).ToArray();
                bool exact = names.SequenceEqual(state.ExpectedNames) &&
                    names.All(name => File.ReadAllBytes(Path.Combine(folder, name)).AsSpan().SequenceEqual("done"u8));
                var reading = new Reading(state.Round, DateTime.UtcNow.Ticks, Stopwatch.GetTimestamp(), names.Length, exact);
                observations.Add(reading);
                if (exact) state.FinalObserved.TrySetResult(reading);
            }
        });
        Assert.True(monitor.IsActive);
        monitor.BeforeNotification = () => Thread.Sleep(2);
        int rounds = 0;
        long writerSample = 0, earliestWriterMonotonicSample = 0;
        Reading? finalRead = null;
        RoundState? finalState = null;
        try
        {
            do
            {
                int round = rounds++;
                // Retire the previous round before removing its synthetic completion markers.
                lock (gate) active = null;
                foreach (string previous in Directory.GetFiles(folder)) File.Delete(previous);
                var state = new RoundState(round);
                lock (gate) active = state;
                string stem = new('n', 180);
                var samples = await Task.WhenAll(Enumerable.Range(0, 4).Select(t => Task.Run(async () =>
                {
                    for (int i = 0; i < 2500; i++) File.WriteAllText(Path.Combine(folder, $"{stem}-{round}-{t}-{i:0000}.txt"), "c");
                    for (int i = 0; i < 2500; i++) File.Delete(Path.Combine(folder, $"{stem}-{round}-{t}-{i:0000}.txt"));
                    // A positive final-state marker follows this writer's last mutation. All four markers and no
                    // churn files prove that the callback saw the completed namespace, even if writers resume later.
                    File.WriteAllText(Path.Combine(folder, $"done-{round}-{t}.txt"), "done");
                    if (holdWriterAfterFinalState)
                    {
                        await state.FinalObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
                        await Task.Delay(50, ct);
                    }
                    return new WriterSample(DateTime.UtcNow.Ticks, Stopwatch.GetTimestamp());
                }, ct)));
                writerSample = samples.Max(s => s.UTC);
                earliestWriterMonotonicSample = samples.Min(s => s.MonotonicTimestamp);
                monitor.BeforeNotification = null;
                finalRead = await state.FinalObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
                finalState = state;
                if (monitor.Overflows == 0) monitor.BeforeNotification = () => Thread.Sleep(2);
            } while (monitor.Overflows == 0 && rounds < 5);

            Reading[] captured;
            long asked;
            lock (gate) { captured = [.. observations]; asked = reads; }
            string[] actualNames = Directory.GetFiles(folder).Select(p => Path.GetFileName(p)).Order(StringComparer.Ordinal).ToArray();
            bool actualBytes = actualNames.All(name => File.ReadAllBytes(Path.Combine(folder, name)).AsSpan().SequenceEqual("done"u8));
            TestContext.Current.TestOutputHelper?.WriteLine(JsonSerializer.Serialize(new
            {
                HeldWriter = holdWriterAfterFinalState, ActualWindowsFileSystemWatcher = true,
                NativeChangeOperations = rounds * 20000, CompletionMarkers = rounds * 4,
                ActualNativeOverflows = monitor.Overflows, ActualRereads = asked,
                WriterSampleUTC = writerSample, FinalObservationUTC = finalRead!.UTC,
                FinalObservationBeforeHeldWriterSample = holdWriterAfterFinalState && finalRead.MonotonicTimestamp < earliestWriterMonotonicSample,
                FinalObservationMonotonicTimestamp = finalRead.MonotonicTimestamp,
                EarliestWriterMonotonicTimestamp = earliestWriterMonotonicSample,
                MonotonicFrequency = Stopwatch.Frequency,
                FinalNamesObservedByCallback = finalState!.ExpectedNames,
                ActualFinalNames = actualNames, AllFinalBytesExact = actualBytes,
                ActualReadings = captured, NoNotificationInjectionOrProductDeadlineChange = true,
            }));
            Assert.True(monitor.Overflows > 0, "Five rounds of 20,000 changes with long names, handled slowly, did not overflow the notification buffer.");
            Assert.True(finalRead.ExactFinalMarkers);
            Assert.Equal(finalState.ExpectedNames, actualNames);
            Assert.True(actualBytes);
            if (holdWriterAfterFinalState)
                Assert.True(finalRead.MonotonicTimestamp < earliestWriterMonotonicSample, "The controlled writer hold did not follow the exact final-state observation.");
        }
        finally
        {
            monitor.Dispose();
            // Finish an in-flight callback before TempDir removes the folder; later callbacks observe closed.
            lock (gate) closed = true;
        }
        foreach (string marker in Directory.GetFiles(folder)) File.Delete(marker);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
    }

    private sealed record Reading(int Round, long UTC, long MonotonicTimestamp, int FileCount, bool ExactFinalMarkers);
    private sealed record WriterSample(long UTC, long MonotonicTimestamp);
    private sealed class RoundState(int round)
    {
        public int Round { get; } = round;
        public string[] ExpectedNames { get; } = Enumerable.Range(0, 4).Select(t => $"done-{round}-{t}.txt").Order(StringComparer.Ordinal).ToArray();
        public TaskCompletionSource<Reading> FinalObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
