using System.Buffers.Binary;
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
        public List<string> Progress { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries.AddRange(entries.ToArray());
        public void ReportIssue(string message) => Issues.Add(message);
        public void ReportProgress(string text) => Progress.Add(text);
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
    public async Task A_volume_without_deleted_items_says_so()
    {
        // A blank FAT12 floppy: a boot sector, two empty tables, and an empty root folder.
        var image = new byte[64 * 512];
        image[0] = 0xEB;
        image[1] = 0x3C;
        image[2] = 0x90;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(11), 512);
        image[13] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(14), 1);
        image[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(17), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(19), 64);
        image[21] = 0xF8;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(22), 1);
        image[510] = 0x55;
        image[511] = 0xAA;
        string path = Path.Combine(_dir.Path, "blank.img");
        File.WriteAllBytes(path, image);
        var rows = await ListAsync(RecoveryProvider.ForImage(path, 1));
        Assert.Empty(rows.Entries);
        Assert.StartsWith("No deleted items were found on this FAT12 volume.", Assert.Single(rows.Issues), StringComparison.Ordinal);
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
    public async Task A_file_whose_start_is_a_guess_is_recovered_with_a_warning_that_says_so()
    {
        // Windows erased half of its start, and nothing around it tells which place is right (ErasedFatStartTests).
        var image = new Fat32Image();
        var data = Fat32Image.Data(600, 6);
        image.Put(2, Fat32Image.Deleted("LONE    BIN", 0x10000 + 500, data));
        image.Put(0x10000 + 500, data);
        string path = Path.Combine(_dir.Path, "erased.img");
        image.Save(path);
        var root = RecoveryProvider.ForImage(path, 1);
        var row = (await ListAsync(root)).Entries.Single();
        Assert.Equal("Uncertain", ((IDisplayDetails)row.Tag!).KindText);
        Assert.False(row.Has(EntryFlags.Unavailable));

        var target = _dir.Dir("guessed");
        var job = await WaitAsync(_jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [_recovery.GetItemRef(root, row)], Destination = Location.FileSystem(target) }));
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(target, row.Name)));
        Assert.StartsWith(RecoveryItem.UncertainStart, Assert.Single(job.Issues).Message, StringComparison.Ordinal);

        // Dragging would hand the file over without that warning: it goes through F5.
        var (staged, refusal) = FileCat.Core.Operations.DragStaging.Stage([_recovery.GetItemRef(root, row)], _providers, new PortableFileOperations(), _dir.Dir("drag"), TestContext.Current.CancellationToken);
        Assert.Null(staged);
        Assert.Contains("is a guess", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Free_space_is_searched_when_asked_and_the_listing_says_how_far_it_got()
    {
        // ErasedFatStartTests.FarListing: a deleted folder whose listing goes on where nothing points.
        var image = new Fat32Image();
        uint folder = 0x10000 + 20_000, far = 120_000;
        image.Put(2, Fat32Image.Entry("BIG        ", 0x10, folder, 0, deleted: true));
        image.Folder(folder, 0, [.. Enumerable.Range(0, 14).Select(i => Fat32Image.Deleted($"F{i:D2}     BIN", folder + 1 + (uint)i, new byte[100]))]);
        var late = Fat32Image.Text(200, "late");
        image.Put(far, Fat32Image.Deleted("LATE    TXT", far + 1, late));
        image.Put(far + 1, late);
        string path = Path.Combine(_dir.Path, "far.img");
        image.Save(path);
        var root = RecoveryProvider.ForImage(path, 1);

        var quick = await ListAsync(root);
        Assert.Contains(quick.Issues, i => i.StartsWith("The lists of contents of 1 deleted folder may go on", StringComparison.Ordinal));
        Assert.Empty(quick.Progress);
        var offer = _recovery.DescribeFreeSpaceSearch(root)!;
        Assert.Equal((1, false), (offer.OpenListings, offer.Searched));
        Assert.Null(_recovery.DescribeFreeSpaceSearch(RecoveryProvider.ForImage(path))); // the list of volumes: none chosen yet

        _recovery.SearchFreeSpace(root);
        var deep = await ListAsync(root);
        Assert.DoesNotContain(deep.Issues, i => i.StartsWith("The lists of contents", StringComparison.Ordinal));
        Assert.StartsWith("Searching free space: 0%", deep.Progress[0], StringComparison.Ordinal);
        Assert.StartsWith("Searching free space: 100%", deep.Progress[^1], StringComparison.Ordinal);
        Assert.True(_recovery.DescribeFreeSpaceSearch(root)!.Searched);
        // Nothing names the folder the far piece belongs to (it has no subfolder), so it is listed apart.
        Assert.Equal(["Orphans", "_IG"], deep.Entries.Select(e => e.Name).Order(StringComparer.Ordinal));
        var lost = root.WithPath("Orphans/Lost folder 1");
        Assert.Equal("_ATE.TXT", (await ListAsync(lost)).Entries.Single().Name);
    }

    [Fact]
    public void Search_progress_reads_as_a_share_and_a_time_left()
    {
        Assert.StartsWith("Searching free space: 25% (", RecoveryProvider.SearchProgress(256L << 20, 1L << 30, TimeSpan.FromSeconds(1)), StringComparison.Ordinal);
        Assert.EndsWith("about 3 minutes left", RecoveryProvider.SearchProgress(256L << 20, 1L << 30, TimeSpan.FromSeconds(60)), StringComparison.Ordinal);
        Assert.EndsWith("about 30 seconds left", RecoveryProvider.SearchProgress(512L << 20, 1L << 30, TimeSpan.FromSeconds(30)), StringComparison.Ordinal);
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
    public async Task A_drive_is_recovered_only_to_another_disk()
    {
        // A stand-in for a drive read through the administrator helper: the same engine, a different source.
        string stick = RecoveryFixtures.Image("fat16");
        const string device = @"\\?\Volume{12345678-1234-1234-1234-123456789abc}";
        var opened = new List<string>();
        _recovery.OpenDevice = (d, name, _) =>
        {
            opened.Add(d);
            return new ImageFileSource(stick);
        };
        bool sameDisk = true;
        _recovery.SharesDisk = (_, _) => sameDisk;
        var root = _recovery.ForDevice(device, "drive E: (STICK)", 1);
        Assert.Contains("drive E: (STICK) › deleted items", _recovery.GetDisplayPath(root), StringComparison.Ordinal);
        Assert.Equal(new Location(Schemes.Computer, string.Empty), _recovery.GetParent(root));
        var sink = await ListAsync(root.WithPath("docs"));
        Assert.Contains(sink.Issues, i => i.Contains("in use while FileCat reads it", StringComparison.Ordinal));
        var report = sink.Entries.Single(e => e.Name == "report.txt");

        var target = _dir.Dir("same-disk");
        var refused = await WaitAsync(_jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [_recovery.GetItemRef(root.WithPath("docs"), report)], Destination = Location.FileSystem(target) }));
        Assert.Equal(JobState.Failed, refused.State);
        Assert.Contains("same physical disk", Assert.Single(refused.Issues).Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));

        sameDisk = false;
        var copied = await WaitAsync(_jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [_recovery.GetItemRef(root.WithPath("docs"), report)], Destination = Location.FileSystem(target) }));
        Assert.Equal(JobState.Completed, copied.State);
        Assert.Equal(RecoveryFixtures.Content("report.txt", 10000), File.ReadAllBytes(Path.Combine(target, "report.txt")));

        // Reread scans again through the same session: no second approval for the same drive.
        _recovery.Forget(root);
        await ListAsync(root);
        Assert.Equal([device], opened);
        _recovery.CloseAll();
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
