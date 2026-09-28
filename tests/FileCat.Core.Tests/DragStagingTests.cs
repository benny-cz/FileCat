using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>Dragging archive members to other programs (plan §4.2, P5): staged copies, marked, capped, and explained.</summary>
public sealed class DragStagingTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly MarkingOps _ops = new();

    private sealed class MarkingOps : PortableFileOperations
    {
        public Dictionary<string, string> Marks { get; } = new(StringComparer.OrdinalIgnoreCase);
        public override string? ReadOriginMark(string path) => Marks.GetValueOrDefault(path);
        public override bool WriteOriginMark(string path, string mark)
        {
            Marks[path] = mark;
            return true;
        }
    }

    public DragStagingTests()
    {
        _providers.Register(new LocalFileSystemProvider());
        _providers.Register(new ZipProvider(_dir.Dir("spool")));
    }

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir.Path, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        _dir.Dispose();
    }

    private string Zip(params (string Name, string Content)[] members)
    {
        string path = Path.Combine(_dir.Path, "download.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in members)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open());
            w.Write(content);
        }
        return path;
    }

    private List<ItemRef> Members(string zip, string folder = "")
    {
        var location = ZipProvider.ForFile(zip).WithPath(folder);
        var list = new List<EntryData>();
        _providers.Get(Schemes.Zip).EnumerateAsync(location, new Sink(list), CancellationToken.None).GetAwaiter().GetResult();
        return list.Select(e => _providers.Get(Schemes.Zip).GetItemRef(location, e)).ToList();
    }

    private sealed class Sink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries) => list.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }

    [Fact]
    public void Archive_members_are_staged_as_marked_read_only_copies()
    {
        string zip = Zip(("a.txt", "alpha"), ("b.txt", "beta"), ("sub/c.txt", "gamma"));
        _ops.Marks[zip] = "[ZoneTransfer]\r\nZoneId=3\r\n";
        var files = Members(zip).Where(m => !m.IsContainer).ToList();
        var (paths, refusal) = DragStaging.Stage(files, _providers, _ops, _dir.Path, TestContext.Current.CancellationToken);
        Assert.Null(refusal);
        Assert.Equal(["a.txt", "b.txt"], paths!.Select(Path.GetFileName).Order());
        Assert.Equal("alpha", File.ReadAllText(paths!.Single(p => p.EndsWith("a.txt", StringComparison.Ordinal))));
        Assert.All(paths!, p => Assert.Equal("[ZoneTransfer]\r\nZoneId=3\r\n", _ops.Marks[p])); // the download mark travels
        Assert.All(paths!, p => Assert.True((File.GetAttributes(p) & FileAttributes.ReadOnly) != 0));
        Assert.StartsWith(Path.Combine(_dir.Path, "drag"), paths![0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Folders_servers_and_large_selections_are_explained_instead()
    {
        string zip = Zip(("a.txt", "alpha"), ("sub/c.txt", "gamma"));
        var members = Members(zip);
        var (paths, refusal) = DragStaging.Stage(members, _providers, _ops, _dir.Path, TestContext.Current.CancellationToken);
        Assert.Null(paths);
        Assert.Contains("Folders", refusal, StringComparison.Ordinal);

        var remote = new ItemRef(new Location(Schemes.Sftp, "/home"), "x.txt", EntryKind.File, 10);
        (paths, refusal) = DragStaging.Stage([remote], _providers, _ops, _dir.Path, TestContext.Current.CancellationToken);
        Assert.Null(paths);
        Assert.Contains("F5", refusal, StringComparison.Ordinal);

        var huge = new ItemRef(ZipProvider.ForFile(zip), "a.txt", EntryKind.File, DragStaging.MaxBytes + 1);
        (paths, refusal) = DragStaging.Stage([huge], _providers, _ops, _dir.Path, TestContext.Current.CancellationToken);
        Assert.Null(paths);
        Assert.Contains("MiB", refusal, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "drag")) && Directory.EnumerateFileSystemEntries(Path.Combine(_dir.Path, "drag")).Any());
    }

    [Fact]
    public void Old_staged_folders_are_swept_and_recent_ones_kept()
    {
        string zip = Zip(("a.txt", "alpha"));
        var (paths, _) = DragStaging.Stage(Members(zip), _providers, _ops, _dir.Path, TestContext.Current.CancellationToken);
        string folder = Path.GetDirectoryName(paths![0])!;
        DragStaging.Sweep(_dir.Path, TimeSpan.FromDays(1));
        Assert.True(Directory.Exists(folder));
        Directory.SetCreationTimeUtc(folder, DateTime.UtcNow.AddDays(-2));
        DragStaging.Sweep(_dir.Path, TimeSpan.FromDays(1));
        Assert.False(Directory.Exists(folder));
    }
}
