using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>This PC and the location menu: one slow or failing drive never holds up the others (§6.3).</summary>
public sealed class ComputerProviderTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-drives", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class Drives(params (string Root, Func<DriveTag> Query)[] drives) : ComputerProvider
    {
        protected override IReadOnlyList<string> GetDriveRoots() => drives.Select(d => d.Root).ToList();

        protected override DriveTag QueryDrive(string root) => drives.Single(d => d.Root == root).Query();
    }

    /// <summary>Mount points stand in as real folders: on Linux and macOS only folders are listed.</summary>
    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private sealed class Sink : IEnumerationSink
    {
        public readonly List<EntryData> Entries = [];

        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries.AddRange(entries.ToArray());

        public void ReportIssue(string message) { }
    }

    /// <summary>
    /// This PC's Size column shows each drive's free space, and sorting by size follows it, though a drive's row keeps its
    /// capacity as its size. A full drive's 0 free is known; an empty disc drive shows none and comes last.
    /// </summary>
    [Fact]
    public async Task Sorting_by_size_follows_the_free_space_each_drive_shows()
    {
        const long GB = 1L << 30;
        string large = Folder("large"), disc = Folder("disc"), small = Folder("small"), full = Folder("full");
        var provider = new Drives(
            (large, () => new DriveTag(large, "LARGE", "Fixed", "NTFS", 10 * GB, 2000 * GB, true)),
            (disc, () => new DriveTag(disc, null, "CDRom", null, -1, -1, false)),
            (small, () => new DriveTag(small, "SMALL", "Fixed", "NTFS", 80 * GB, 100 * GB, true)),
            (full, () => new DriveTag(full, "FULL", "Fixed", "NTFS", 0, 500 * GB, true)));
        var sink = new Sink();
        await provider.EnumerateAsync(new Location(Schemes.Computer, string.Empty), sink, TestContext.Current.CancellationToken);
        Assert.Equal([2000 * GB, -1, 100 * GB, 500 * GB], sink.Entries.Select(e => e.Size));

        using var store = new EntryStore();
        store.Append(sink.Entries.ToArray());
        var order = Enumerable.Range(0, store.Count).ToArray();
        StableSort.Sort(order, EntrySorter.CreateComparison(store, new SortSpec(SortField.Size, Descending: true)));
        Assert.Equal([small, large, full, disc], order.Select(i => Assert.IsType<DriveTag>(store[i].Tag).RootPath));
    }

    [Fact]
    public async Task A_hung_or_failing_drive_is_listed_as_such_without_holding_up_the_rest()
    {
        using var release = new ManualResetEventSlim();
        string data = Folder("data"), network = Folder("network"), gone = Folder("gone");
        var provider = new Drives(
            (data, () => new DriveTag(data, "DATA", "Fixed", "NTFS", 1, 2, true)),
            (network, () =>
            {
                release.Wait(TimeSpan.FromSeconds(30));
                return new DriveTag(network, null, "Network", null, -1, -1, true);
            }),
            (gone, () => throw new IOException("The device is not ready.")));
        var clock = Stopwatch.StartNew();
        // A budget a busy machine meets for a drive that answers at once; the hung one is still waiting when it ends.
        var tags = await provider.QueryDrivesAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        clock.Stop();
        release.Set();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"The query waited {clock.Elapsed} for a hung drive.");
        Assert.Equal([data, network, gone], tags.Select(t => t.RootPath));
        Assert.Equal(["Fixed", "Not responding", "Unavailable"], tags.Select(t => t.DriveType));
        Assert.Equal([true, false, false], tags.Select(t => t.Ready));
    }

    [Fact]
    public async Task Unix_lists_folders_but_not_bound_files_or_memory_scratch_space()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Mount points that are files or tmpfs exist on Linux and macOS.");
        string disk = Folder("disk"), scratch = Folder("scratch"), file = Path.Combine(_root, "versions.txt");
        File.WriteAllText(file, "bound over a file");
        var provider = new Drives(
            (disk, () => new DriveTag(disk, disk, "Fixed", "ext4", 1, 2, true)),
            (scratch, () => new DriveTag(scratch, scratch, "Ram", "tmpfs", 1, 2, true)),
            (file, () => new DriveTag(file, file, "Fixed", "ext4", 1, 2, true)));
        var tags = await provider.QueryDrivesAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal([disk], tags.Select(t => t.RootPath));
    }
}
