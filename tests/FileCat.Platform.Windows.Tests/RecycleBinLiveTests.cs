using System.Text;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// FileCat's view of the real Recycle Bin against Windows' own (FILECAT_RECYCLE_BIN_LIVE=1). Test items are deleted to the
/// bin with FileCat's Recycle job; FileCat's listing of the whole bin is compared, item by item, with the Shell's listing
/// of its Recycle Bin folder — where each item is kept, where it was, when it was deleted — and only counts are printed,
/// never a name; a test item is copied back out; and at the end exactly the test's own items are purged from the bin.
/// </summary>
public sealed class RecycleBinLiveTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bin-live", Guid.NewGuid().ToString("N")[..8])).FullName;
    private readonly string _out = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bin-live-out", Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose()
    {
        foreach (var dir in new[] { _root, _out })
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private sealed record ShellItem(string DataPath, string? DeletedFrom, DateTime? Deleted);

    /// <summary>The Shell's own listing of its Recycle Bin folder (CSIDL 10): where each item is kept, where it was, when.</summary>
    private static List<ShellItem> ShellListing()
    {
        var items = new List<ShellItem>();
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!)!;
        dynamic bin = shell.Namespace(10);
        foreach (dynamic item in bin.Items())
        {
            string path = item.Path;
            string? from = item.ExtendedProperty("System.Recycle.DeletedFrom") as string;
            DateTime? deleted = item.ExtendedProperty("System.Recycle.DateDeleted") is DateTime d ? d : null;
            items.Add(new ShellItem(path, from, deleted));
        }
        return items;
    }

    private static async Task<List<EntryData>> FileCatListing(RecycleBinProvider bin, List<string> issues)
    {
        var sink = new Sink();
        await bin.EnumerateAsync(RecycleBinProvider.Root, sink, TestContext.Current.CancellationToken);
        issues.AddRange(sink.Issues);
        return sink.Entries;
    }

    private sealed class Sink : IEnumerationSink
    {
        public readonly List<EntryData> Entries = [];
        public readonly List<string> Issues = [];

        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries.AddRange(entries.ToArray());

        public void ReportIssue(string message) => Issues.Add(message);
    }

    [Fact]
    public async Task FileCats_view_of_the_bin_is_Windows_own_and_its_items_copy_out()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows' Recycle Bin."); return; }
        if (Environment.GetEnvironmentVariable("FILECAT_RECYCLE_BIN_LIVE") != "1")
        { Assert.Skip("Set FILECAT_RECYCLE_BIN_LIVE=1: the test puts items of its own into this user's Recycle Bin and removes them."); return; }
        var log = TestContext.Current.TestOutputHelper;
        var ct = TestContext.Current.CancellationToken;
        string file = Path.Combine(_root, "deleted file ž.txt");
        File.WriteAllText(file, "a file FileCat deleted to the Recycle Bin", Encoding.UTF8);
        string folder = Directory.CreateDirectory(Path.Combine(_root, "deleted folder", "sub")).Parent!.FullName;
        File.WriteAllText(Path.Combine(folder, "inner.txt"), "inner");
        File.WriteAllText(Path.Combine(folder, "sub", "deep.txt"), "deep");
        var before = DateTime.UtcNow.AddSeconds(-2);

        var providers = new ProviderRegistry();
        providers.Register(new WindowsFileSystemProvider());
        var bin = new RecycleBinProvider();
        providers.Register(bin);
        var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(_out, ".journal"));
        try
        {
            var recycle = jobs.Submit(new JobRequest { Kind = JobKind.Recycle, Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File), ItemRef.ForFileSystemPath(folder, EntryKind.Directory)] });
            for (int i = 0; i < 3000 && !recycle.State.IsFinished(); i++) await Task.Delay(10, ct);
            Assert.True(recycle.State == JobState.Completed, $"{recycle.State}: {string.Join("; ", recycle.Issues.Select(x => x.Message))}");
            Assert.False(File.Exists(file) || Directory.Exists(folder));

            var issues = new List<string>();
            var rows = await FileCatListing(bin, issues);
            var shell = ShellListing();
            var fileCat = rows.Select(r => (Tag: (RecycleBinTag)r.Tag!, Row: r)).ToDictionary(r => r.Tag.DataPath, StringComparer.OrdinalIgnoreCase);
            var windows = shell.ToDictionary(s => s.DataPath, StringComparer.OrdinalIgnoreCase);
            log?.WriteLine($"the bin: FileCat lists {fileCat.Count} items, Windows' Recycle Bin {windows.Count}; FileCat's notes: {issues.Count}");
            // Every item Windows shows, FileCat shows, and nothing else; where it was and when it was deleted agree.
            var onlyWindows = windows.Keys.Except(fileCat.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            var onlyFileCat = fileCat.Keys.Except(windows.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            log?.WriteLine($"only Windows: {onlyWindows.Count} (on {string.Join(", ", onlyWindows.Select(p => p[..2]).Distinct())}); only FileCat: {onlyFileCat.Count}");
            Assert.Empty(onlyWindows);
            Assert.Empty(onlyFileCat);
            int placeDiffers = 0, timeDiffers = 0;
            foreach (var (dataPath, shellItem) in windows)
            {
                var tag = fileCat[dataPath].Tag;
                if (!string.Equals(Path.GetDirectoryName(tag.OriginalPath), shellItem.DeletedFrom?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) placeDiffers++;
                // The Shell gives the time in UTC, unmarked, and to the second.
                if (shellItem.Deleted is not { } at || Math.Abs((DateTime.SpecifyKind(at, DateTimeKind.Utc) - tag.DeletedUtc).TotalSeconds) > 2) timeDiffers++;
            }
            var deltas = windows.Where(w => w.Value.Deleted is not null)
                .Select(w => Math.Round((w.Value.Deleted!.Value - fileCat[w.Key].Tag.DeletedUtc).TotalSeconds)).Distinct().Order().Take(5);
            log?.WriteLine($"where it was: {placeDiffers} differ; when deleted: {timeDiffers} differ by over 2 s (the Shell's date as given minus FileCat's UTC, seconds: {string.Join(", ", deltas)}; this computer is UTC{TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalHours:+0;-0})");
            Assert.Equal(0, placeDiffers);
            Assert.Equal(0, timeDiffers);

            // The test's own items: by the names they had, where they were, deleted just now.
            var mine = rows.Where(r => ((RecycleBinTag)r.Tag!).OriginalPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Equal(2, mine.Count);
            var fileRow = mine.Single(r => r.Name == "deleted file ž.txt");
            var folderRow = mine.Single(r => r.Name == "deleted folder");
            Assert.Equal(EntryKind.File, fileRow.Kind);
            Assert.Equal(EntryKind.Directory, folderRow.Kind);
            Assert.InRange(new DateTime(fileRow.Modified, DateTimeKind.Utc), before, DateTime.UtcNow.AddSeconds(2));
            var inside = new Sink();
            await bin.EnumerateAsync(bin.GetChildLocation(RecycleBinProvider.Root, folderRow)!, inside, ct);
            Assert.Equal(["inner.txt", "sub"], inside.Entries.Select(e => e.Name.ToString()).Order(StringComparer.Ordinal));

            // Copied back out, as F5 copies it: the same names and bytes.
            var copy = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy, Destination = Location.FileSystem(_out),
                Sources = [bin.GetItemRef(RecycleBinProvider.Root, fileRow), bin.GetItemRef(RecycleBinProvider.Root, folderRow)],
            });
            for (int i = 0; i < 3000 && !copy.State.IsFinished(); i++) await Task.Delay(10, ct);
            Assert.True(copy.State == JobState.Completed, $"{copy.State}: {string.Join("; ", copy.Issues.Select(x => x.Message))}");
            Assert.Equal("a file FileCat deleted to the Recycle Bin", File.ReadAllText(Path.Combine(_out, "deleted file ž.txt"), Encoding.UTF8));
            Assert.Equal("deep", File.ReadAllText(Path.Combine(_out, "deleted folder", "sub", "deep.txt")));
            log?.WriteLine("the test's two items listed as they were, entered, and copied back out");
        }
        finally
        {
            log?.WriteLine($"purged from the bin: {Purge(_root)} of the test's own items");
        }
    }

    /// <summary>Removes from this user's bins exactly the items that were deleted from <paramref name="folder"/>.</summary>
    private static int Purge(string folder)
    {
        string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        string bin = Path.Combine(Path.GetPathRoot(folder)!, "$Recycle.Bin", sid);
        int purged = 0;
        foreach (var record in Directory.Exists(bin) ? Directory.GetFiles(bin, "$I*") : [])
        {
            var parsed = Core.FileSystem.RecycleBinRecords.Parse(File.ReadAllBytes(record), out _);
            if (parsed is null || !parsed.OriginalPath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            string data = Path.Combine(bin, Core.FileSystem.RecycleBinRecords.DataName(Path.GetFileName(record))!);
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
            else if (File.Exists(data)) File.Delete(data);
            File.Delete(record);
            purged++;
        }
        return purged;
    }
}
