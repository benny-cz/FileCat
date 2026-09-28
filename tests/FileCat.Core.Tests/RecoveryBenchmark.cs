using System.Diagnostics;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>
/// P10's scan and preview benchmark on large images (FILECAT_RECOVERY_BENCH = a folder holding bench-ntfs.img and
/// bench-fat32.img from eng/make-recovery-bench.sh). The workflow "Recovery fixtures" builds them and runs this.
/// </summary>
public sealed class RecoveryBenchmark
{
    [Theory]
    [InlineData("bench-ntfs.img", "NTFS")]
    [InlineData("bench-fat32.img", "FAT32")]
    public void Scan_and_preview_a_large_image(string image, string fileSystem)
    {
        if (Environment.GetEnvironmentVariable("FILECAT_RECOVERY_BENCH") is not { Length: > 0 } folder) Assert.Skip("Set FILECAT_RECOVERY_BENCH to a folder with the benchmark images.");
        string path = Path.Combine(folder, image);
        if (!File.Exists(path)) Assert.Skip(image + " is not in " + folder);
        var log = TestContext.Current.TestOutputHelper;
        GC.Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);
        var clock = Stopwatch.StartNew();
        using var source = new ImageFileSource(path);
        var volume = Assert.Single(RecoveryScanner.Scan(source, TestContext.Current.CancellationToken));
        var scan = clock.Elapsed;
        long retained = GC.GetTotalMemory(forceFullCollection: true) - before;
        Assert.Equal(fileSystem, volume.FileSystem);
        var deleted = All(volume.Root).Where(i => i.IsDeleted && !i.IsDirectory).ToList();
        // 20,000 notes and 200 photos were written; half of each deleted.
        Assert.InRange(deleted.Count(i => i.Name.StartsWith("note-", StringComparison.Ordinal)), 9_000, 10_000);
        Assert.InRange(deleted.Count(i => i.Name == "photo.jpg"), 90, 100);

        var photo = deleted.First(i => i.Name == "photo.jpg" && i.State == RecoveryState.Recoverable);
        clock.Restart();
        using var content = new RecoveryContent(new WindowSource(source, volume.Offset, volume.Length, "volume"), photo);
        var buffer = new byte[64 * 1024];
        Assert.Equal(buffer.Length, content.Read(0, buffer));
        var preview = clock.Elapsed;
        clock.Restart();
        var whole = new byte[photo.Size];
        for (long at = 0; at < whole.Length;) at += content.Read(at, whole.AsSpan((int)at));
        var read = clock.Elapsed;

        log?.WriteLine($"{fileSystem}: scanned {new FileInfo(path).Length / (1024 * 1024)} MiB with {deleted.Count:N0} deleted files in {scan.TotalMilliseconds:F0} ms; " +
                       $"the scan result holds about {retained / (1024 * 1024.0):F1} MiB; first 64 KiB of a deleted photo in {preview.TotalMilliseconds:F1} ms, " +
                       $"all {photo.Size / 1024} KiB in {read.TotalMilliseconds:F1} ms.");
        // Budgets (docs/validation/P10-recovery.md): generous against CI machines, tight enough to catch a regression by an order of magnitude.
        Assert.True(scan < TimeSpan.FromSeconds(30), $"Scanning took {scan}.");
        Assert.True(preview < TimeSpan.FromSeconds(1), $"The first preview took {preview}.");
    }

    private static IEnumerable<RecoveryItem> All(RecoveryItem node) => node.Children.SelectMany(c => c.IsDirectory ? All(c).Prepend(c) : [c]);
}
