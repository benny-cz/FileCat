using System.IO.Compression;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// A ZIP is untrusted input (plan §17.3, V07/V24). Extraction must put every member inside the destination and
/// nowhere else: a member may not climb out with "..", land on an absolute path, or name an alternate data stream.
/// The TAR side of this is covered with the archive formats; ZIP is the format people actually receive.
/// </summary>
public sealed class ZipHostileNameExtractionTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private sealed class Sink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries) { foreach (var e in entries) list.Add(e); }
        public void ReportIssue(string message) { }
    }

    [Fact]
    public async Task No_member_of_a_hostile_zip_lands_outside_the_destination()
    {
        string path = Path.Combine(_dir.Path, "hostile.zip");
        // Written as raw entry names: the names a hostile archive carries, not what any tool would create.
        string[] hostile =
        [
            "../escape-up.txt",
            @"..\escape-win.txt",
            "deep/../../escape-deep.txt",
            "/abs-root.txt",
            "C:/abs-drive.txt",
            "host.txt:stream",
            "./dot-prefixed.txt",
        ];
        using (var file = File.Create(path))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            foreach (var name in hostile)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("HOSTILE-" + name);
            }
            using var ok = new StreamWriter(zip.CreateEntry("ok/inner.txt").Open());
            ok.Write("kept");
        }

        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var zipProvider = new ZipProvider(Path.Combine(_dir.Path, "tmp"));
        providers.Register(zipProvider);
        var archives = new ArchiveProvider(Path.Combine(_dir.Path, "tmp"), providers);
        providers.Register(archives);
        zipProvider.OtherArchives = archives;
        zipProvider.SpoolForeignMember = archives.Spool;

        var root = zipProvider.GetContainerLocation(path)!;
        var listed = new List<EntryData>();
        await zipProvider.EnumerateAsync(root, new Sink(listed), TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine("Listed at the root: " + string.Join(", ", listed.Select(e => $"{e.Name} ({e.Kind})")));

        string dest = _dir.Dir("out");
        var manager = new JobManager(new IdentityAwareTestFiles(), providers, Path.Combine(_dir.Path, "journal"));
        var job = manager.Submit(new JobRequest
        {
            Kind = JobKind.Extract,
            Sources = listed.Select(e => zipProvider.GetItemRef(root, e)).ToList(),
            Destination = Location.FileSystem(dest),
        });
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine($"Job {job.State}: " + string.Join(" | ", job.Issues.Select(i => i.Message)));

        // The one ordinary member is extracted, so the job really ran.
        Assert.Equal("kept", File.ReadAllText(Path.Combine(dest, "ok", "inner.txt")));

        // Nothing climbed out of the destination, in the temporary root or beside it.
        foreach (var escaped in new[] { "escape-up.txt", "escape-win.txt", "escape-deep.txt", "abs-root.txt", "abs-drive.txt" })
        {
            Assert.False(File.Exists(Path.Combine(_dir.Path, escaped)), $"{escaped} was written beside the destination.");
            Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), escaped)), $"{escaped} was written to the temporary folder.");
            if (OperatingSystem.IsWindows())
                Assert.False(File.Exists(Path.Combine(Path.GetPathRoot(dest)!, escaped)), $"{escaped} was written to the drive's root.");
        }

        // No member named an alternate data stream leaves one behind, under either half of its name.
        Assert.False(File.Exists(Path.Combine(dest, "host.txt")), "the ':' member was extracted as a file.");
        if (OperatingSystem.IsWindows())
            Assert.False(File.Exists(Path.Combine(dest, "host.txt") + ":stream"), "the ':' member was extracted as an alternate data stream.");

        // Everything the destination holds stays within it, whatever the archive called it.
        foreach (var written in Directory.GetFiles(dest, "*", SearchOption.AllDirectories))
            Assert.StartsWith(Path.GetFullPath(dest) + Path.DirectorySeparatorChar, Path.GetFullPath(written), StringComparison.Ordinal);
    }
}
