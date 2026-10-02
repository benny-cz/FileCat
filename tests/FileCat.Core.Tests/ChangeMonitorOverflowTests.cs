using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>
/// V12: a folder watcher's overflow observed as such. More changes at once than the system's notification buffer holds
/// lose which items changed; the watcher counts it and asks for the folder to be read again in full, after the overflow.
/// Unhindered, this machine's watcher kept up with 100,000 changes of long names (five rounds, no overflow), so the
/// handler is held up a little, as on a busy machine, and the system's buffer fills meanwhile.
/// </summary>
public sealed class ChangeMonitorOverflowTests
{
    [Fact]
    public async Task An_overflow_is_counted_and_the_folder_is_read_again_after_it()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Forcing an overflow is measured on Windows (64 KiB buffer, long names).");
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDir();
        string folder = Directory.CreateDirectory(Path.Combine(dir.Path, "churn")).FullName;
        long reads = 0, lastRead = 0;
        using var monitor = new ChangeMonitor(folder, () =>
        {
            Interlocked.Increment(ref reads);
            Interlocked.Exchange(ref lastRead, DateTime.UtcNow.Ticks);
        });
        Assert.True(monitor.IsActive);
        monitor.BeforeNotification = () => Thread.Sleep(2);

        // Long names make each notification record large (about 400 bytes): the 64 KiB buffer holds some 160 of them.
        string stem = new('n', 180);
        int rounds = 0;
        long churnEnded = 0;
        while (monitor.Overflows == 0 && rounds < 5)
        {
            int round = rounds++;
            // When the last change was made, as each thread saw it: the test's own wake-up after them can come seconds
            // later on a busy machine, after the reread they asked for (CI run 36946911728).
            var lastChange = await Task.WhenAll(Enumerable.Range(0, 4).Select(t => Task.Run(() =>
            {
                for (int i = 0; i < 2500; i++) File.WriteAllText(Path.Combine(folder, $"{stem}-{round}-{t}-{i:0000}.txt"), "c");
                for (int i = 0; i < 2500; i++) File.Delete(Path.Combine(folder, $"{stem}-{round}-{t}-{i:0000}.txt"));
                return DateTime.UtcNow.Ticks;
            }, ct)));
            churnEnded = lastChange.Max();
        }
        monitor.BeforeNotification = null;
        TestContext.Current.TestOutputHelper?.WriteLine($"{rounds} round(s) of 20,000 changes: {monitor.Overflows} overflow(s), {Interlocked.Read(ref reads)} reread(s) asked");
        Assert.True(monitor.Overflows > 0, "Five rounds of 20,000 changes with long names, handled slowly, did not overflow the notification buffer.");

        // Whatever was lost, the folder is asked to be read again once the churn is over.
        for (int i = 0; i < 100 && Interlocked.Read(ref lastRead) < churnEnded; i++) await Task.Delay(50, ct);
        Assert.True(Interlocked.Read(ref lastRead) >= churnEnded, "No reread was asked for after the churn and its overflow.");
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
    }
}
