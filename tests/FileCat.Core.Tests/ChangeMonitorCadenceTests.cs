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
            // For six seconds, a new file every 50 ms, then one known final file.
            double started = clock.Elapsed.TotalSeconds;
            int count = 0;
            while (clock.Elapsed.TotalSeconds - started < 6)
            {
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
            int during = asked.Count(r => r.Time < finalWriteStarted);
            Assert.True(during >= 2, $"{during} reread(s) asked while files kept arriving for six seconds");
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

    private sealed record Reading(double Time, (string Name, long Length)[] Files);
}
