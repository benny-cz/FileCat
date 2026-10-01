using System.Buffers.Binary;
using System.Text;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// FileCat's read-only view of Windows' Recycle Bin (the owner's choice), on bins made here as Windows lays them out:
/// <c>$I</c> records beside the <c>$R</c> items they describe, on two drives, in both record formats.
/// </summary>
public sealed class RecycleBinProviderTests : IDisposable
{
    private static readonly DateTime First = new(2026, 9, 30, 8, 15, 0, DateTimeKind.Utc), Second = new(2026, 10, 1, 17, 45, 30, DateTimeKind.Utc);
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bin-tests", Guid.NewGuid().ToString("N")[..8])).FullName;
    private readonly string _binC, _binD;
    private readonly RecycleBinProvider _bin;

    public RecycleBinProviderTests()
    {
        _binC = Directory.CreateDirectory(Path.Combine(_root, "C-bin")).FullName;
        _binD = Directory.CreateDirectory(Path.Combine(_root, "D-bin")).FullName;
        _bin = new RecycleBinProvider(() => [('C', _binC), ('D', _binD)]);
        // Two files of one name from two folders, a folder with a folder in it, a file on another drive.
        File.WriteAllText(Path.Combine(_binC, "$RAAAAAA.txt"), "work notes");
        Record(_binC, "AAAAAA.txt", @"C:\Work\notes.txt", 10, First, 2);
        File.WriteAllText(Path.Combine(_binC, "$RBBBBBB.txt"), "home notes, longer");
        Record(_binC, "BBBBBB.txt", @"C:\Home\notes.txt", 18, Second, 2);
        Directory.CreateDirectory(Path.Combine(_binC, "$RCCCCCC", "2024"));
        File.WriteAllText(Path.Combine(_binC, "$RCCCCCC", "2024", "a.jpg"), "picture a");
        File.WriteAllText(Path.Combine(_binC, "$RCCCCCC", "b.jpg"), "picture b");
        Record(_binC, "CCCCCC", @"C:\Users\x\Photos", 18, First, 1);
        File.WriteAllText(Path.Combine(_binD, "$RGGGGGG.log"), "log");
        Record(_binD, "GGGGGG.log", @"D:\logs\x.log", 3, Second, 2);
        // Not shown: a record whose item is gone, an item without a record, and a damaged record.
        Record(_binC, "DDDDDD.txt", @"C:\gone.txt", 1, First, 2);
        File.WriteAllText(Path.Combine(_binC, "$REEEEEE.txt"), "no record");
        File.WriteAllText(Path.Combine(_binC, "$RFFFFFF.txt"), "damaged record");
        File.WriteAllBytes(Path.Combine(_binC, "$IFFFFFF.txt"), [2, 0, 0, 0, 0, 0, 0, 0, 1]);
    }

    public void Dispose()
    {
        foreach (var d in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).Where(d => new DirectoryInfo(d).LinkTarget is not null).ToList())
            Directory.Delete(d);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static void Record(string bin, string id, string original, long size, DateTime deleted, int version)
    {
        byte[] data;
        if (version == 2)
        {
            data = new byte[28 + (original.Length + 1) * 2];
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), original.Length + 1);
            Encoding.Unicode.GetBytes(original).CopyTo(data, 28);
        }
        else
        {
            data = new byte[24 + 520];
            Encoding.Unicode.GetBytes(original).CopyTo(data, 24);
        }
        BinaryPrimitives.WriteInt64LittleEndian(data, version);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), deleted.ToFileTimeUtc());
        File.WriteAllBytes(Path.Combine(bin, "$I" + id), data);
    }

    private sealed class Sink : IEnumerationSink
    {
        public readonly List<EntryData> Entries = [];
        public readonly List<string> Issues = [];

        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries.AddRange(entries.ToArray());

        public void ReportIssue(string message) => Issues.Add(message);
    }

    private async Task<Sink> List(Location location)
    {
        var sink = new Sink();
        await _bin.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
        return sink;
    }

    private static RecycleBinTag Tag(EntryData e) => Assert.IsType<RecycleBinTag>(e.Tag);

    [Fact]
    public async Task The_bin_lists_each_deleted_item_by_the_name_it_had_and_where_it_was()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows' Recycle Bin."); return; }
        var bin = await List(RecycleBinProvider.Root);
        var rows = bin.Entries.OrderBy(e => Tag(e).OriginalPath, StringComparer.Ordinal).ToList();
        Assert.Equal([@"C:\Home\notes.txt", @"C:\Users\x\Photos", @"C:\Work\notes.txt", @"D:\logs\x.log"], rows.Select(r => Tag(r).OriginalPath));
        Assert.Equal(["notes.txt", "Photos", "notes.txt", "x.log"], rows.Select(r => r.Name.ToString()));
        Assert.Equal([EntryKind.File, EntryKind.Directory, EntryKind.File, EntryKind.File], rows.Select(r => r.Kind));
        // A file's size is the item's; a folder's the record's, which counts all it held.
        Assert.Equal([18L, 18L, 10L, 3L], rows.Select(r => r.Size));
        // The date shown is when it was deleted, and the folder it was deleted from is beside it.
        Assert.Equal([Second.Ticks, First.Ticks, First.Ticks, Second.Ticks], rows.Select(r => r.Modified));
        Assert.Equal([@"C:\Home", @"C:\Users\x", @"C:\Work", @"D:\logs"], rows.Select(r => Tag(r).DetailsText));
        // The damaged record is said, not guessed at; the orphans are not shown, as Windows does not show them.
        Assert.Contains(bin.Issues, i => i.Contains("1 of the Recycle Bin's records on C:", StringComparison.Ordinal));
        Assert.Equal("Recycle Bin", _bin.GetDisplayPath(RecycleBinProvider.Root));
        Assert.Null(_bin.GetParent(RecycleBinProvider.Root));
        Assert.True(_bin.TryParse("recycle bin", null, out var typed));
        Assert.Equal(RecycleBinProvider.Root, typed);
    }

    [Fact]
    public async Task A_deleted_folder_opens_and_leads_back_up()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows' Recycle Bin."); return; }
        var photos = (await List(RecycleBinProvider.Root)).Entries.Single(e => e.Name == "Photos");
        var inside = _bin.GetChildLocation(RecycleBinProvider.Root, photos)!;
        Assert.Equal(@"Recycle Bin\Photos", _bin.GetDisplayPath(inside));
        var rows = (await List(inside)).Entries.OrderBy(e => e.Name.ToString(), StringComparer.Ordinal).ToList();
        Assert.Equal(["2024", "b.jpg"], rows.Select(r => r.Name.ToString()));
        Assert.Equal(@"C:\Users\x\Photos\b.jpg", Tag(rows[1]).OriginalPath);
        // What a deleted folder held was deleted with it, at its time.
        Assert.Equal(First.Ticks, rows[1].Modified);
        var year = _bin.GetChildLocation(inside, rows[0])!;
        Assert.Equal(@"Recycle Bin\Photos\2024", _bin.GetDisplayPath(year));
        var deep = Assert.Single((await List(year)).Entries);
        Assert.Equal(@"C:\Users\x\Photos\2024\a.jpg", Tag(deep).OriginalPath);
        // Back up: the folder, then the bin, with the folder's row found by the name it had.
        Assert.Equal(inside, _bin.GetParent(year));
        Assert.Equal(RecycleBinProvider.Root, _bin.GetParent(inside));
        Assert.Equal("Photos", _bin.GetNameInParent(inside));
        Assert.Equal("2024", _bin.GetNameInParent(year));
    }

    [Fact]
    public async Task Two_deleted_files_of_one_name_stay_two_items()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows' Recycle Bin."); return; }
        var notes = (await List(RecycleBinProvider.Root)).Entries.Where(e => e.Name == "notes.txt").ToList();
        Assert.Equal(2, notes.Count);
        var items = notes.Select(n => _bin.GetItemRef(RecycleBinProvider.Root, n)).ToList();
        Assert.NotEqual(items[0].Parent, items[1].Parent);
        var texts = items.Select(item =>
        {
            using var content = _bin.OpenContent(item)!;
            Assert.Null(content.LocalPath);
            var bytes = new byte[content.Length];
            Assert.Equal(bytes.Length, content.Read(0, bytes));
            return (content.DisplayName, Encoding.UTF8.GetString(bytes));
        }).OrderBy(t => t.DisplayName, StringComparer.Ordinal).ToList();
        Assert.Equal([(@"C:\Home\notes.txt", "home notes, longer"), (@"C:\Work\notes.txt", "work notes")], texts);
    }

    [Fact]
    public async Task Nothing_outside_the_bin_is_reached()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows' Recycle Bin."); return; }
        File.WriteAllText(Path.Combine(_root, "outside.txt"), "not in the bin");
        foreach (var path in new[] { "C/$RCCCCCC/../../outside.txt", "C/../outside.txt", "C/$RCCCCCC/..", "X/$RCCCCCC", "C/notdata", "CC/$RCCCCCC" })
            await Assert.ThrowsAnyAsync<IOException>(() => List(new Location(Schemes.RecycleBin, path)));
        var inside = new Location(Schemes.RecycleBin, "C/$RCCCCCC");
        foreach (var name in new[] { @"..\..\outside.txt", "../outside.txt", "..", @"C:\Windows\win.ini" })
            Assert.Null(_bin.OpenContent(new ItemRef(inside, name, EntryKind.File)));
    }

    [Fact]
    public async Task Links_inside_a_deleted_folder_are_shown_not_followed()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows' Recycle Bin."); return; }
        string target = Directory.CreateDirectory(Path.Combine(_root, "elsewhere")).FullName;
        File.WriteAllText(Path.Combine(target, "secret.txt"), "outside the bin");
        string junction = Path.Combine(_binC, "$RCCCCCC", "jump");
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
               { ArgumentList = { "/c", "mklink", "/J", junction, target }, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
        {
            mklink.StandardOutput.ReadToEnd();
            mklink.WaitForExit();
        }
        Assert.True(Directory.Exists(junction));
        var inside = new Location(Schemes.RecycleBin, "C/$RCCCCCC");
        var listed = await List(inside);
        var link = listed.Entries.Single(e => e.Name == "jump");
        Assert.True(link.Has(EntryFlags.Unavailable));
        Assert.Null(_bin.GetChildLocation(inside, link));
        Assert.Contains(listed.Issues, i => i.Contains("not followed", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_reading_is_offered_and_the_rest_says_where_it_is_done()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows' Recycle Bin."); return; }
        foreach (var location in new[] { RecycleBinProvider.Root, new Location(Schemes.RecycleBin, "C/$RCCCCCC") })
        {
            Assert.Equal(LocationCapabilities.Enumerate | LocationCapabilities.ReadContent, _bin.GetCapabilities(location));
            foreach (var change in new[] { LocationCapabilities.Delete, LocationCapabilities.Recycle, LocationCapabilities.Rename, LocationCapabilities.TransferTarget, LocationCapabilities.CreateDirectory, LocationCapabilities.MoveSource })
                Assert.Contains("Windows' Recycle Bin", _bin.ExplainUnavailable(location, change));
        }
    }

    [Fact]
    public async Task Deleted_items_copy_out_with_their_names_and_bytes()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows' Recycle Bin."); return; }
        var rows = (await List(RecycleBinProvider.Root)).Entries;
        var work = rows.Single(e => e.Name == "notes.txt" && Tag(e).OriginalPath == @"C:\Work\notes.txt");
        var photos = rows.Single(e => e.Name == "Photos");
        var providers = new ProviderRegistry();
        providers.Register(new WindowsFileSystemProvider());
        providers.Register(_bin);
        var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(_root, "journal"));
        string destination = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [_bin.GetItemRef(RecycleBinProvider.Root, work), _bin.GetItemRef(RecycleBinProvider.Root, photos)],
            Destination = Location.FileSystem(destination),
        });
        for (int i = 0; i < 1000 && !job.State.IsFinished(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(job.State == JobState.Completed, $"{job.State}: {string.Join("; ", job.Issues.Select(x => x.Message))}");
        Assert.Equal("work notes", File.ReadAllText(Path.Combine(destination, "notes.txt")));
        Assert.Equal("picture a", File.ReadAllText(Path.Combine(destination, "Photos", "2024", "a.jpg")));
        Assert.Equal("picture b", File.ReadAllText(Path.Combine(destination, "Photos", "b.jpg")));
        // The copies keep the files' own times, not the time they were deleted.
        Assert.Equal(File.GetLastWriteTimeUtc(Path.Combine(_binC, "$RAAAAAA.txt")), File.GetLastWriteTimeUtc(Path.Combine(destination, "notes.txt")));
        // Nothing in the bin changed.
        Assert.True(File.Exists(Path.Combine(_binC, "$RAAAAAA.txt")) && Directory.Exists(Path.Combine(_binC, "$RCCCCCC")));
    }
}
