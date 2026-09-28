using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// P5 and P8 exit measurements (plan §23): scan cost, first member, extraction overhead, and scratch use of archives.
/// Set FILECAT_ARCHIVE_BENCH=1 to run; FILECAT_ARCHIVE_BENCH_MB sets the size of each of the four large files (64 by
/// default). Results go to the test output; docs/validation/P5-P8-archives.md records them. 7z runs where 7-Zip is installed.
/// </summary>
public sealed class ArchiveBenchmark : IDisposable
{
    private const int Folders = 200, NotesPerFolder = 100, LargeFiles = 4;
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly JobManager _jobs;
    private readonly string _spool;

    public ArchiveBenchmark()
    {
        _spool = _dir.Dir("spool");
        _providers.Register(new LocalFileSystemProvider());
        var zip = new ZipProvider(_spool);
        var archives = new ArchiveProvider(_spool, _providers);
        _providers.Register(zip);
        _providers.Register(archives);
        zip.OtherArchives = archives;
        zip.SpoolForeignMember = archives.Spool;
        _jobs = new JobManager(new PortableFileOperations(), _providers, Path.Combine(_dir.Path, "journal"));
    }

    public void Dispose() => _dir.Dispose();

    private static ITestOutputHelper? Log => TestContext.Current.TestOutputHelper;

    /// <summary>20,000 notes of about 1 KiB in 200 folders, and large files: half random, half text.</summary>
    private string MakeTree(int largeMiB)
    {
        string root = _dir.Dir("tree");
        for (int f = 0; f < Folders; f++)
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, $"folder-{f:000}")).FullName;
            for (int i = 0; i < NotesPerFolder; i++)
                File.WriteAllText(Path.Combine(folder, $"note-{i:000}.txt"), string.Concat(Enumerable.Repeat($"folder {f} note {i} ", 50)));
        }
        string large = Directory.CreateDirectory(Path.Combine(root, "large")).FullName;
        var random = new Random(1);
        var buffer = new byte[1024 * 1024];
        for (int n = 0; n < LargeFiles; n++)
        {
            using var file = File.Create(Path.Combine(large, $"large-{n}.bin"));
            for (int mb = 0; mb < largeMiB; mb++)
            {
                if (n % 2 == 0) random.NextBytes(buffer);
                else for (int b = 0; b < buffer.Length; b++) buffer[b] = (byte)"log line with some repeating text\n"[(b + mb) % 34];
                file.Write(buffer);
            }
        }
        return root;
    }

    private async Task<(Job Job, TimeSpan Elapsed, long PeakScratch)> RunAsync(JobRequest request, string watchFolder, string scratchPattern)
    {
        var clock = Stopwatch.StartNew();
        var job = _jobs.Submit(request);
        long peak = 0;
        while (!job.State.IsFinished())
        {
            if (Directory.Exists(watchFolder))
            {
                long scratch = 0;
                try
                {
                    // A fresh query per file: a folder listing reports the size an open file had when it was created.
                    foreach (var f in Directory.EnumerateFiles(watchFolder, scratchPattern, SearchOption.TopDirectoryOnly)) scratch += new FileInfo(f).Length;
                }
                catch (IOException) { }
                peak = Math.Max(peak, scratch);
            }
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        return (job, clock.Elapsed, peak);
    }

    private static long Size(string folder) => new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    private static string MiB(long bytes) => (bytes / (1024 * 1024.0)).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " MiB";

    private sealed class Sink : IEnumerationSink
    {
        public int Count;
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Count += entries.Length;
        public void ReportIssue(string message) { }
    }

    /// <summary>Opening a fresh provider and listing every folder: the cost of reading the archive's structure.</summary>
    private static (TimeSpan First, TimeSpan All, int Entries) Scan(ResourceProvider provider, Location root)
    {
        var clock = Stopwatch.StartNew();
        var sink = new Sink();
        provider.EnumerateAsync(root, sink, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        var first = clock.Elapsed;
        for (int f = 0; f < Folders; f++)
            provider.EnumerateAsync(root.WithPath($"folder-{f:000}"), sink, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        provider.EnumerateAsync(root.WithPath("large"), sink, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        return (first, clock.Elapsed, sink.Count);
    }

    /// <summary>
    /// The first 64 KiB of the last large member (what F3 waits for): from a fresh provider, which reads the archive's
    /// structure first, and again once it has.
    /// </summary>
    private static (TimeSpan Cold, TimeSpan Warm) FirstMember(ResourceProvider provider, Location root, long size)
    {
        var item = new ItemRef(root.WithPath("large"), $"large-{LargeFiles - 1}.bin", EntryKind.File, size);
        TimeSpan Once()
        {
            var clock = Stopwatch.StartNew();
            using var content = provider.OpenContent(item)!;
            var buffer = new byte[64 * 1024];
            Assert.Equal(buffer.Length, content.Read(0, buffer));
            return clock.Elapsed;
        }
        var cold = Once();
        return (cold, Once());
    }

    private static string Ms(TimeSpan t) => t.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " ms";

    [Fact]
    public async Task Archives_scan_open_extract_and_update_within_budgets()
    {
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_BENCH") is not { Length: > 0 }) Assert.Skip("Set FILECAT_ARCHIVE_BENCH=1 to measure archives.");
        int largeMiB = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_BENCH_MB"), out var m) ? m : 64;
        long largeBytes = largeMiB * 1024L * 1024;
        string tree = MakeTree(largeMiB);
        long treeBytes = Size(tree);
        Log?.WriteLine($"Payload: {Folders * NotesPerFolder:N0} notes of about 1 KiB and {LargeFiles} files of {largeMiB} MiB (half random), {MiB(treeBytes)} in all.");
        var results = new List<string>();
        // FILECAT_ARCHIVE_BENCH_FORMATS narrows a run, for example to "7z" (default: zip,tgz,7z,iso).
        var formats = (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_BENCH_FORMATS") ?? "zip,tgz,7z,iso").Split(',', StringSplitOptions.TrimEntries);
        var clock = new Stopwatch();
        var archiveProvider = _providers.Get(Schemes.Archive);

        // ---- ZIP: pack, scan, first member, extract, update ------------------------------------------------------
        if (formats.Contains("zip"))
        {
            string baselineZip = Path.Combine(_dir.Path, "baseline.zip");
            clock.Restart();
            ZipFile.CreateFromDirectory(tree, baselineZip, CompressionLevel.Optimal, includeBaseDirectory: false);
            var packBaseline = clock.Elapsed;

            string zip = Path.Combine(_dir.Path, "packed.zip");
            var changes = Directory.EnumerateFileSystemEntries(tree).Select(p => Directory.Exists(p)
                ? new ArchiveChange(ArchiveChangeKind.AddFolder, Path.GetFileName(p), p)
                : new ArchiveChange(ArchiveChangeKind.AddFile, Path.GetFileName(p), p)).ToList();
            var pack = await RunAsync(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = new ArchivePlan(zip, null, changes) }, _dir.Path, ".filecat-zip-*");
            Assert.Equal(JobState.Completed, pack.Job.State);
            results.Add($"ZIP pack (Alt+F5, verified): {pack.Elapsed.TotalSeconds:F1} s; ZipFile.CreateFromDirectory {packBaseline.TotalSeconds:F1} s; archive {MiB(new FileInfo(zip).Length)}.");

            var zipRoot = ZipProvider.ForFile(zip);
            var (zipFirst, zipAll, zipEntries) = Scan(new ZipProvider(_spool), zipRoot);
            Assert.Equal(Folders * NotesPerFolder + LargeFiles + Folders + 1, zipEntries);
            var zipMember = FirstMember(new ZipProvider(_spool), zipRoot, largeBytes);
            results.Add($"ZIP scan: root listed in {zipFirst.TotalMilliseconds:F0} ms, all {Folders + 2} folders in {zipAll.TotalMilliseconds:F0} ms; first 64 KiB of a {largeMiB} MiB member in {Ms(zipMember.Cold)} cold, {Ms(zipMember.Warm)} warm.");
            // Budgets: generous for shared machines, tight enough to catch a regression like the per-file flushes found here.
            Assert.True(zipMember.Warm < TimeSpan.FromMilliseconds(250), $"First page of a large member: {zipMember.Warm}."); // plan §21.2

            string zipOut = _dir.Dir("zip-out");
            var zipProvider = _providers.Get(Schemes.Zip);
            var zipSources = new List<EntryData>();
            await zipProvider.EnumerateAsync(zipRoot, new CollectSink(zipSources), TestContext.Current.CancellationToken);
            var extract = await RunAsync(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = zipSources.Select(e => zipProvider.GetItemRef(zipRoot, e)).ToList(),
                Destination = Location.FileSystem(zipOut),
            }, _spool, "*");
            Assert.Equal(JobState.Completed, extract.Job.State);
            Assert.Equal(treeBytes, Size(zipOut));
            string zipBaselineOut = Path.Combine(_dir.Path, "zip-baseline-out");
            clock.Restart();
            ZipFile.ExtractToDirectory(zip, zipBaselineOut);
            var extractBaseline = clock.Elapsed;
            results.Add($"ZIP extract (F5): {extract.Elapsed.TotalSeconds:F1} s, ZipFile.ExtractToDirectory {extractBaseline.TotalSeconds:F1} s ({extract.Elapsed / extractBaseline:F2}×); peak private spool {MiB(extract.PeakScratch)}.");
            Assert.True(extract.Elapsed < extractBaseline * 4, $"Extracting took {extract.Elapsed}, the in-box extractor {extractBaseline}.");
            Assert.Equal(0, extract.PeakScratch); // copies decompress straight into their files

            string note = _dir.File("added.txt", "one more note");
            long zipBytes = new FileInfo(zip).Length;
            var update = await RunAsync(new JobRequest
            {
                Kind = JobKind.ArchiveUpdate,
                Archive = new ArchivePlan(zip, ArchiveBaseline.Of(zip), [new ArchiveChange(ArchiveChangeKind.AddFile, "added.txt", note)]),
            }, _dir.Path, ".filecat-zip-*");
            Assert.Equal(JobState.Completed, update.Job.State);
            results.Add($"ZIP update (add one small file to {MiB(zipBytes)}): {update.Elapsed.TotalSeconds:F1} s; peak scratch beside the archive {MiB(update.PeakScratch)}.");
            Assert.True(update.PeakScratch <= zipBytes + 1024 * 1024, $"Updating used {update.PeakScratch} bytes of scratch.");
        }

        // ---- TAR.GZ: a forward-only format -------------------------------------------------------------------------
        if (formats.Contains("tgz"))
        {
            string tgz = Path.Combine(_dir.Path, "packed.tar.gz");
            using (var file = File.Create(tgz))
            using (var gz = new GZipStream(file, CompressionLevel.Fastest))
                TarFile.CreateFromDirectory(tree, gz, includeBaseDirectory: false);
            var tgzRoot = ArchiveProvider.ForFile(tgz);
            var (tgzFirst, tgzAll, tgzEntries) = Scan(new ArchiveProvider(_spool, _providers), tgzRoot);
            Assert.Equal(Folders * NotesPerFolder + LargeFiles + Folders + 1, tgzEntries);
            var tgzMember = FirstMember(new ArchiveProvider(_spool, _providers), tgzRoot, largeBytes);
            string tgzOut = _dir.Dir("tgz-out");
            var tgzSources = new List<EntryData>();
            await archiveProvider.EnumerateAsync(tgzRoot, new CollectSink(tgzSources), TestContext.Current.CancellationToken);
            var tgzExtract = await RunAsync(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = tgzSources.Select(e => archiveProvider.GetItemRef(tgzRoot, e)).ToList(),
                Destination = Location.FileSystem(tgzOut),
            }, _spool, "*");
            Assert.Equal(JobState.Completed, tgzExtract.Job.State);
            Assert.Equal(treeBytes, Size(tgzOut));
            string tgzBaselineOut = _dir.Dir("tgz-baseline-out");
            clock.Restart();
            using (var file = File.OpenRead(tgz))
            using (var gz = new GZipStream(file, CompressionMode.Decompress))
                TarFile.ExtractToDirectory(gz, tgzBaselineOut, overwriteFiles: false);
            var tgzBaseline = clock.Elapsed;
            results.Add($"TAR.GZ ({MiB(new FileInfo(tgz).Length)}): root listed in {tgzFirst.TotalMilliseconds:F0} ms, all folders in {tgzAll.TotalMilliseconds:F0} ms; first 64 KiB of the last member in {Ms(tgzMember.Cold)} cold, {Ms(tgzMember.Warm)} warm; " +
                        $"extract {tgzExtract.Elapsed.TotalSeconds:F1} s, TarFile.ExtractToDirectory {tgzBaseline.TotalSeconds:F1} s ({tgzExtract.Elapsed / tgzBaseline:F2}×); peak spool {MiB(tgzExtract.PeakScratch)}.");
            Assert.True(tgzExtract.Elapsed < tgzBaseline * 4, $"Extracting took {tgzExtract.Elapsed}, the in-box extractor {tgzBaseline}.");
            Assert.Equal(0, tgzExtract.PeakScratch);
        }

        // ---- 7z (solid), when 7-Zip is installed ----------------------------------------------------------------
        if (formats.Contains("7z"))
        {
            string? sevenZip = new[] { Environment.GetEnvironmentVariable("FILECAT_7Z"), @"C:\Program Files\7-Zip\7z.exe", "/usr/bin/7z", "/usr/bin/7za" }
                .FirstOrDefault(p => p is { Length: > 0 } && File.Exists(p));
            if (sevenZip is not null)
            {
                string solid = Path.Combine(_dir.Path, "packed.7z");
                var psi = new ProcessStartInfo(sevenZip, ["a", "-bd", "-mx=1", "-ms=on", solid, "."]) { WorkingDirectory = tree, RedirectStandardOutput = true };
                using (var p = Process.Start(psi)!)
                {
                    await p.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                    await p.WaitForExitAsync(TestContext.Current.CancellationToken);
                }
                var root7 = ArchiveProvider.ForFile(solid);
                var (first7, all7, entries7) = Scan(new ArchiveProvider(_spool, _providers), root7);
                Assert.Equal(Folders * NotesPerFolder + LargeFiles + Folders + 1, entries7);
                var member7 = FirstMember(new ArchiveProvider(_spool, _providers), root7, largeBytes);
                string out7 = _dir.Dir("7z-out");
                var sources7 = new List<EntryData>();
                await archiveProvider.EnumerateAsync(root7, new CollectSink(sources7), TestContext.Current.CancellationToken);
                var extract7 = await RunAsync(new JobRequest
                {
                    Kind = JobKind.Copy,
                    Sources = sources7.Select(e => archiveProvider.GetItemRef(root7, e)).ToList(),
                    Destination = Location.FileSystem(out7),
                }, _spool, "*");
                Assert.Equal(JobState.Completed, extract7.Job.State);
                Assert.Equal(treeBytes, Size(out7));
                clock.Restart();
                using (var p = Process.Start(new ProcessStartInfo(sevenZip, ["x", "-bd", "-o" + _dir.Dir("7z-native-out"), solid]) { RedirectStandardOutput = true })!)
                {
                    await p.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                    await p.WaitForExitAsync(TestContext.Current.CancellationToken);
                }
                var native7 = clock.Elapsed;
                results.Add($"7z solid ({MiB(new FileInfo(solid).Length)}): root listed in {first7.TotalMilliseconds:F0} ms, all folders in {all7.TotalMilliseconds:F0} ms; first 64 KiB of the last member in {Ms(member7.Cold)} cold, {Ms(member7.Warm)} warm; " +
                            $"extract {extract7.Elapsed.TotalSeconds:F1} s, native 7-Zip {native7.TotalSeconds:F1} s; peak spool {MiB(extract7.PeakScratch)}.");
                Assert.True(extract7.Elapsed < native7 * 5, $"Extracting took {extract7.Elapsed}, native 7-Zip {native7}.");
                Assert.Equal(0, extract7.PeakScratch);
            }
        }

        // ---- ISO 9660 / Joliet ------------------------------------------------------------------------------------
        if (formats.Contains("iso"))
        {
            string iso = Path.Combine(_dir.Path, "packed.iso");
            var builder = new DiscUtils.Iso9660.CDBuilder { UseJoliet = true, VolumeIdentifier = "BENCH" };
            foreach (var f in Directory.EnumerateFiles(tree, "*", SearchOption.AllDirectories)) builder.AddFile(Path.GetRelativePath(tree, f), f);
            builder.Build(iso);
            var isoRoot = ArchiveProvider.ForFile(iso);
            var (isoFirst, isoAll, isoEntries) = Scan(new ArchiveProvider(_spool, _providers), isoRoot);
            Assert.Equal(Folders * NotesPerFolder + LargeFiles + Folders + 1, isoEntries);
            var isoMember = FirstMember(new ArchiveProvider(_spool, _providers), isoRoot, largeBytes);
            results.Add($"ISO ({MiB(new FileInfo(iso).Length)}): root listed in {isoFirst.TotalMilliseconds:F0} ms, all folders in {isoAll.TotalMilliseconds:F0} ms; first 64 KiB of the last member in {Ms(isoMember.Cold)} cold, {Ms(isoMember.Warm)} warm.");
            Assert.True(isoMember.Warm < TimeSpan.FromMilliseconds(250), $"First page of a disc image's file: {isoMember.Warm}.");
        }

        foreach (var line in results) Log?.WriteLine(line);
    }

    private sealed class CollectSink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries) => list.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }
}
