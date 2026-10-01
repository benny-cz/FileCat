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
    public async Task A_folder_that_keeps_changing_is_read_again_while_it_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDir();
        string folder = Directory.CreateDirectory(Path.Combine(dir.Path, "growing")).FullName;
        var clock = Stopwatch.StartNew();
        var reads = new List<double>();
        using var monitor = new ChangeMonitor(folder, () =>
        {
            lock (reads) reads.Add(clock.Elapsed.TotalSeconds);
        });
        if (!monitor.IsActive) Assert.Skip("This folder cannot be watched here.");

        // For six seconds, a new file every 50 ms.
        double started = clock.Elapsed.TotalSeconds, last = started;
        for (int i = 0; clock.Elapsed.TotalSeconds - started < 6; i++)
        {
            File.WriteAllText(Path.Combine(folder, $"frame-{i:0000}.bin"), "f");
            last = clock.Elapsed.TotalSeconds;
            await Task.Delay(50, ct);
        }
        await Task.Delay(1000, ct);
        double[] asked;
        lock (reads) asked = [.. reads];
        TestContext.Current.TestOutputHelper?.WriteLine($"files made from {started:F2} s to {last:F2} s; rereads asked at {string.Join(", ", asked.Select(t => t.ToString("F2")))} s");
        // Six seconds of changes: a reread at least every three seconds while they last, and one after the last change.
        int during = asked.Count(t => t < last);
        Assert.True(during >= 2, $"{during} reread(s) asked while files kept arriving for six seconds");
        Assert.True(asked.Length > 0 && asked[^1] >= last, "no reread asked after the last change");
    }
}
