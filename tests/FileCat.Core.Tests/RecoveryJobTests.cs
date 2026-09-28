using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>F5 from a recovery location: an ordinary copy job that never passes lost bytes off as data.</summary>
public sealed class RecoveryJobTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly RecoveryProvider _recovery = new();
    private readonly JobManager _jobs;

    public RecoveryJobTests()
    {
        _providers.Register(new LocalFileSystemProvider());
        _providers.Register(_recovery);
        _jobs = new JobManager(new PortableFileOperations(), _providers, Path.Combine(_dir.Path, "journal"));
    }

    public void Dispose() => _dir.Dispose();

    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public List<string> Issues { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries.AddRange(entries.ToArray());
        public void ReportIssue(string message) => Issues.Add(message);
    }

    private async Task<Sink> ListAsync(Location location)
    {
        var sink = new Sink();
        await _recovery.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
        return sink;
    }

    private static async Task<Job> WaitAsync(Job job)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!job.State.IsFinished())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Job still {job.State}");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    [Fact]
    public async Task Recovered_folders_and_files_arrive_whole_and_partial_ones_say_what_is_lost()
    {
        var root = RecoveryProvider.ForImage(RecoveryFixtures.Image("fat32"), 1);
        var rows = await ListAsync(root);
        Assert.Contains(rows.Entries, e => e.Name == "docs" && e.Kind == EntryKind.Directory);
        var photos = rows.Entries.Single(e => e.Name == "photos");
        var frag = rows.Entries.Single(e => e.Name == "frag-a.bin");
        Assert.Equal("Partly lost", ((IDisplayDetails)frag.Tag!).KindText);
        Assert.Contains("one continuous run", ((IDisplayDetails)frag.Tag!).DetailsText, StringComparison.Ordinal);

        var target = _dir.Dir("recovered");
        var job = await WaitAsync(_jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [_recovery.GetItemRef(root, photos), _recovery.GetItemRef(root, frag)],
            Destination = Location.FileSystem(target),
        }));
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Equal(RecoveryFixtures.Content("a.jpg", 70000), File.ReadAllBytes(Path.Combine(target, "photos", "a.jpg")));
        Assert.Equal(RecoveryFixtures.Content("b.jpg", 12345), File.ReadAllBytes(Path.Combine(target, "photos", "b.jpg")));
        var copied = File.ReadAllBytes(Path.Combine(target, "frag-a.bin"));
        Assert.Equal(RecoveryFixtures.Content("frag-a.bin", 16384), copied[..16384]);
        Assert.All(copied[16384..], b => Assert.Equal(0, b));
        var warning = Assert.Single(job.Issues);
        Assert.Equal("frag-a.bin", warning.Path);
        // "24 KiB of 40 KiB are lost (bytes 16,384–40,959) …", in the user's number format.
        Assert.StartsWith(PartialContent.Describe([(16384, 24576)], 40960), warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overwritten_items_are_listed_but_never_copied()
    {
        var root = RecoveryProvider.ForImage(RecoveryFixtures.Image("fat16"), 1);
        var old = root.WithPath("old");
        var row = (await ListAsync(old)).Entries.Single(e => e.Name == "overwritten.txt");
        Assert.True(row.Has(EntryFlags.Unavailable));
        Assert.Equal("Overwritten", ((IDisplayDetails)row.Tag!).KindText);
        var target = _dir.Dir("out");
        var job = await WaitAsync(_jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [_recovery.GetItemRef(old, row)], Destination = Location.FileSystem(target) }));
        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("overwritten", Assert.Single(job.Issues).Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Fact]
    public async Task A_partitioned_disk_lists_its_volumes_and_nothing_changes_the_image()
    {
        var image = RecoveryFixtures.Image("disk-gpt");
        var before = File.ReadAllBytes(image);
        var volumes = await ListAsync(RecoveryProvider.ForImage(image));
        Assert.Equal(["Volume 1", "Volume 2"], volumes.Entries.Select(e => e.Name));
        Assert.Equal(["FAT16", "exFAT"], volumes.Entries.Select(e => ((IDisplayDetails)e.Tag!).KindText));
        var second = _recovery.GetChildLocation(RecoveryProvider.ForImage(image), volumes.Entries[1])!;
        Assert.Equal("2", second.Session);
        Assert.Equal("second.txt", Assert.Single((await ListAsync(second)).Entries).Name);
        Assert.Equal(RecoveryProvider.ForImage(image), _recovery.GetParent(second));
        Assert.Contains("deleted items", _recovery.GetDisplayPath(second), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(image));
        Assert.Equal(LocationCapabilities.Enumerate | LocationCapabilities.ReadContent, _recovery.GetCapabilities(second));
    }
}
