using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class NestedArchiveTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private sealed class Sink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }

        public void ReportIssue(string message) { }
    }

    private sealed class MarkingOps : PortableFileOperations
    {
        public readonly Dictionary<string, string> Marks = new(StringComparer.OrdinalIgnoreCase);
        public override string? ReadOriginMark(string path) => Marks.GetValueOrDefault(path);
        public override bool WriteOriginMark(string path, string mark)
        {
            Marks[path] = mark;
            return true;
        }
        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
        {
            base.Move(source, destination, replaceExisting, writeThrough);
            if (Marks.Remove(source, out var m)) Marks[destination] = m;
        }
    }

    private static List<EntryData> List(ZipProvider provider, Location location)
    {
        var list = new List<EntryData>();
        provider.EnumerateAsync(location, new Sink(list), CancellationToken.None).GetAwaiter().GetResult();
        return list;
    }

    /// <summary>outer.zip: docs/inner.zip (a.txt, dir/b.txt), readme.txt.</summary>
    private string MakeNested()
    {
        var innerBytes = new MemoryStream();
        using (var inner = new ZipArchive(innerBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(inner.CreateEntry("a.txt").Open())) w.Write("inner a");
            using (var w = new StreamWriter(inner.CreateEntry("dir/b.txt").Open())) w.Write("inner b");
        }
        string outerPath = Path.Combine(_dir.Path, "outer.zip");
        using (var outer = ZipFile.Open(outerPath, ZipArchiveMode.Create))
        {
            using (var s = outer.CreateEntry("docs/inner.zip").Open()) s.Write(innerBytes.ToArray());
            using (var w = new StreamWriter(outer.CreateEntry("readme.txt").Open())) w.Write("outer");
        }
        return outerPath;
    }

    [Fact]
    public void Archives_inside_archives_open_read_only_with_readable_paths()
    {
        string outerPath = MakeNested();
        var zip = new ZipProvider(Path.Combine(_dir.Path, "tmp"));
        var docs = zip.GetContainerLocation(outerPath)!.WithPath("docs");
        var innerRow = Assert.Single(List(zip, docs));
        Assert.True(innerRow.Has(EntryFlags.Container));
        var inner = zip.GetChildLocation(docs, innerRow);
        Assert.NotNull(inner);
        Assert.Equal(Path.Combine(outerPath, "docs", "inner.zip"), zip.GetDisplayPath(inner!));
        var rows = List(zip, inner!);
        Assert.Equal(["a.txt", "dir"], rows.Select(r => r.Name).Order());
        var a = zip.GetItemRef(inner!, rows.Single(r => r.Name == "a.txt"));
        using (var content = zip.OpenContent(a)!)
        {
            var buffer = new byte[64];
            int n = content.Read(0, buffer);
            Assert.Equal("inner a", System.Text.Encoding.UTF8.GetString(buffer, 0, n));
        }
        var sub = zip.GetChildLocation(inner!, rows.Single(r => r.Name == "dir"))!;
        Assert.Equal(Path.Combine(outerPath, "docs", "inner.zip", "dir"), zip.GetDisplayPath(sub));
        Assert.Equal(inner, zip.GetParent(sub));
        Assert.Equal(docs, zip.GetParent(inner!));
        Assert.Equal(0, (int)(zip.GetCapabilities(inner!) & (LocationCapabilities.Delete | LocationCapabilities.TransferTarget)));
        Assert.Contains("inside archives are read-only", zip.ExplainUnavailable(inner!, LocationCapabilities.Delete));
        Assert.True(zip.TryParse(Path.Combine(outerPath, "docs", "inner.zip", "dir"), null, out var parsed));
        Assert.Equal(sub, parsed);
        Assert.Equal(PathUtil.GetDeviceKey(outerPath), zip.GetDeviceKey(sub));
    }

    [Fact]
    public async Task Extracting_from_a_nested_archive_keeps_the_outer_download_mark()
    {
        string outerPath = MakeNested();
        var providers = new ProviderRegistry();
        var ops = new MarkingOps();
        providers.Register(new LocalFileSystemProvider());
        var zip = new ZipProvider(Path.Combine(_dir.Path, "tmp"));
        providers.Register(zip);
        ops.Marks[outerPath] = "[ZoneTransfer]\nZoneId=3";
        var manager = new JobManager(ops, providers, Path.Combine(_dir.Path, "journal"));
        var docs = zip.GetContainerLocation(outerPath)!.WithPath("docs");
        var inner = zip.GetChildLocation(docs, Assert.Single(List(zip, docs)))!;
        var dest = _dir.Dir("out");
        var job = manager.Submit(new JobRequest
        {
            Kind = JobKind.Extract,
            Sources = List(zip, inner).Select(e => zip.GetItemRef(inner, e)).ToList(),
            Destination = Location.FileSystem(dest),
        });
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("inner b", File.ReadAllText(Path.Combine(dest, "dir", "b.txt")));
        Assert.True(ops.Marks.ContainsKey(Path.Combine(dest, "a.txt")));
    }
}
