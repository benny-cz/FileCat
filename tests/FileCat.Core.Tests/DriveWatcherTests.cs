using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>Drives coming and going (plan §8.2): reported once each, only while someone listens.</summary>
public sealed class DriveWatcherTests
{
    [Fact]
    public async Task Drives_that_come_and_go_are_reported_while_someone_listens()
    {
        var ct = TestContext.Current.CancellationToken;
        var roots = new List<string> { "A", "B" };
        IReadOnlyList<string> Read() { lock (roots) return [.. roots]; }
        using var watcher = new DriveWatcher(Read, TimeSpan.FromMilliseconds(30));
        var changes = new List<DriveChange>();
        var listener = watcher.Listen(c => { lock (changes) changes.Add(c); });

        lock (roots) { roots.Add("C"); roots.Remove("A"); }
        // The watcher's own check finds it.
        for (int i = 0; i < 200 && changes.Count == 0; i++) await Task.Delay(10, ct);
        var first = Assert.Single(changes);
        Assert.Equal(["C"], first.Added);
        Assert.Equal(["A"], first.Removed);
        Assert.False(first.MediaChanged);

        // Nothing changed: nothing is reported; a medium changed in a drive that stayed: that is.
        watcher.Check();
        Assert.Single(changes);
        watcher.Check(mediaChanged: true);
        Assert.Equal(2, changes.Count);
        Assert.True(changes[1].MediaChanged);
        Assert.Empty(changes[1].Added);

        // No listener, no checks.
        listener.Dispose();
        lock (roots) roots.Add("D");
        watcher.Check();
        await Task.Delay(100, ct);
        Assert.Equal(2, changes.Count);
    }
}
