using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Paths longer than MAX_PATH (260 characters) on Windows. Windows itself refuses them to a program unless the
/// program prefixes them with \\?\ — or the program declares itself long-path aware (FileCat's manifest does) and the
/// computer allows it (LongPathsEnabled, off by default). So this is asked both on a computer that allows them and on
/// one that does not, of every operation FileCat offers on files, through the operations and jobs the app itself runs.
/// The Recycle Bin is the one exception, and it is Windows' own: it takes no such path from any program.
/// </summary>
public sealed class LongPathTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fc-long-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(@"\\?\" + _root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A folder whose own path is at least <paramref name="length"/> characters, made of ordinary names.</summary>
    private string Deep(string under, int length)
    {
        string path = under;
        int i = 0;
        while (path.Length < length) path = Path.Combine(path, $"folder-{i++:D2}-" + new string('x', 40));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class Sink : IEnumerationSink
    {
        public int Count;
        public readonly List<string> Issues = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Count += entries.Length;
        public void ReportIssue(string message) => Issues.Add(message);
    }

    private static async Task<Job> RunAsync(JobManager manager, JobRequest request)
    {
        var job = manager.Submit(request);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!job.State.IsFinished())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(job.State.ToString());
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    private static string Outcome(Job job) =>
        job.State == JobState.Completed && job.Issues.Count == 0 ? "ok"
        : $"{job.State}: {string.Join(" | ", job.Issues.Select(i => i.Message).Take(3))}";

    [Fact]
    public async Task Every_operation_works_on_paths_longer_than_MAX_PATH()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("MAX_PATH is Windows'."); return; }
        var log = TestContext.Current.TestOutputHelper;
        var ct = TestContext.Current.CancellationToken;
        object? enabled = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 0);
        log?.WriteLine($"LongPathsEnabled = {enabled}; temp {_root}");

        var ops = new WindowsFileOperations();
        var providers = new ProviderRegistry();
        providers.Register(new WindowsFileSystemProvider());
        var manager = new JobManager(ops, providers, Path.Combine(_root, "journal"));
        manager.DecisionRequested += d => d.Resolve(new Decision(DecisionAction.Replace));
        var results = new List<(string What, string Result)>();
        void Note(string what, string result)
        {
            results.Add((what, result));
            log?.WriteLine($"{what}: {result}");
        }

        // The tree: a folder 300 characters deep holding a file, and under it a folder past 600.
        string deep = Deep(Path.Combine(_root, "source"), 300);
        string deeper = Deep(deep, 620);
        string file = Path.Combine(deep, "a file.txt");
        string farFile = Path.Combine(deeper, "far.txt");
        File.WriteAllText(file, "near");
        File.WriteAllText(farFile, "far");
        log?.WriteLine($"file path {file.Length} characters; far file {farFile.Length}");

        // Listing, as a panel lists a folder.
        try
        {
            var provider = providers.Get(Schemes.FileSystem)!;
            var sink = new Sink();
            await provider.EnumerateAsync(Location.FileSystem(deep), sink, ct);
            Note("list a folder 300 deep", sink.Count == 2 && sink.Issues.Count == 0 ? "ok" : $"{sink.Count} entries (expected 2); {string.Join(" | ", sink.Issues)}");
        }
        catch (Exception ex) { Note("list a folder 300 deep", ex.GetType().Name + ": " + ex.Message); }

        // Copy the whole tree elsewhere.
        string copyTarget = Directory.CreateDirectory(Path.Combine(_root, "copy")).FullName;
        var copy = await RunAsync(manager, new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(Path.Combine(_root, "source"), EntryKind.Directory)], Destination = Location.FileSystem(copyTarget) });
        string copiedFar = farFile.Replace(Path.Combine(_root, "source"), Path.Combine(copyTarget, "source"));
        Note("copy a tree with a file 600 deep", Outcome(copy) + (File.Exists(copiedFar) && File.ReadAllText(copiedFar) == "far" ? "; the far file arrived intact" : "; the far file is NOT there"));

        // Rename a file 300 deep.
        var rename = await RunAsync(manager, new JobRequest { Kind = JobKind.Rename, Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)], NewName = "renamed.txt" });
        string renamed = Path.Combine(deep, "renamed.txt");
        Note("rename a file 300 deep", Outcome(rename) + (File.Exists(renamed) ? "" : "; not renamed"));

        // Attributes: read-only on, then off.
        var attrs = await RunAsync(manager, new JobRequest { Kind = JobKind.Attributes, Sources = [ItemRef.ForFileSystemPath(renamed, EntryKind.File)], Attributes = new AttributeChangeSet(FileAttributes.ReadOnly, 0, null, null, false) });
        Note("set read-only on a file 300 deep", Outcome(attrs) + ((File.GetAttributes(renamed) & FileAttributes.ReadOnly) != 0 ? "" : "; attribute not set"));
        await RunAsync(manager, new JobRequest { Kind = JobKind.Attributes, Sources = [ItemRef.ForFileSystemPath(renamed, EntryKind.File)], Attributes = new AttributeChangeSet(0, FileAttributes.ReadOnly, null, null, false) });

        // Move a file 600 deep to a folder 300 deep.
        var move = await RunAsync(manager, new JobRequest { Kind = JobKind.Move, Sources = [ItemRef.ForFileSystemPath(farFile, EntryKind.File)], Destination = Location.FileSystem(deep) });
        Note("move a file from 600 deep to 300 deep", Outcome(move) + (File.Exists(Path.Combine(deep, "far.txt")) && !File.Exists(farFile) ? "" : "; not moved"));

        // New folder and new file, 600 deep.
        var mkdir = await RunAsync(manager, new JobRequest { Kind = JobKind.CreateDirectory, Destination = Location.FileSystem(deeper), NewName = "made here" });
        Note("make a folder 600 deep", Outcome(mkdir) + (Directory.Exists(Path.Combine(deeper, "made here")) ? "" : "; not made"));
        var mkfile = await RunAsync(manager, new JobRequest { Kind = JobKind.CreateFile, Destination = Location.FileSystem(deeper), NewName = "made.txt" });
        Note("make a file 600 deep", Outcome(mkfile) + (File.Exists(Path.Combine(deeper, "made.txt")) ? "" : "; not made"));

        // Checksum of a file 300 deep, as checksum manifests and the files beside them are checked.
        try
        {
            string hash = FileCat.Core.Operations.Checksums.Compute(renamed, FileCat.Core.Operations.ChecksumKind.Sha256, ct);
            Note("checksum a file 300 deep", hash.Length == 64 ? "ok" : $"unexpected hash '{hash}'");
        }
        catch (Exception ex) { Note("checksum a file 300 deep", ex.GetType().Name + ": " + ex.Message); }

        // Alternate data streams of a file 300 deep, as the hidden-data view reads them.
        try
        {
            File.WriteAllText(renamed + ":extra", "stream");
            var streams = ops.GetAlternateStreams(renamed);
            Note("list the alternate streams of a file 300 deep", streams.Any(s => s.Contains("extra", StringComparison.Ordinal)) ? "ok" : $"{streams.Count} streams, 'extra' missing");
        }
        catch (Exception ex) { Note("list the alternate streams of a file 300 deep", ex.GetType().Name + ": " + ex.Message); }

        // The hex editor: open a file 300 deep, change two bytes, and save them as a new file beside it.
        try
        {
            string hexCopy = Path.Combine(deep, "patched.bin");
            using (var hex = new ProtectedHexFile(renamed))
            using (var overlay = new HexPatchOverlay(hex))
            {
                overlay.Write(0, [(byte)'N', (byte)'E']);
                HexSaveAs.CreateNew(hex, overlay, hexCopy, ct);
            }
            Note("hex-edit a file 300 deep and save as", File.ReadAllText(hexCopy) == "NEar" ? "ok" : $"saved '{File.ReadAllText(hexCopy)}'");
        }
        catch (Exception ex) { Note("hex-edit a file 300 deep and save as", ex.GetType().Name + ": " + ex.Message); }

        // To the Recycle Bin, then for good.
        var recycle = await RunAsync(manager, new JobRequest { Kind = JobKind.Recycle, Sources = [ItemRef.ForFileSystemPath(renamed, EntryKind.File)] });
        Note("recycle a file 300 deep", Outcome(recycle) + (File.Exists(renamed) ? "; still there" : ""));
        var delete = await RunAsync(manager, new JobRequest { Kind = JobKind.Delete, Sources = [ItemRef.ForFileSystemPath(Path.Combine(_root, "copy"), EntryKind.Directory)] });
        Note("delete a tree with files 600 deep for good", Outcome(delete) + (Directory.Exists(Path.Combine(_root, "copy")) ? "; still there" : ""));

        log?.WriteLine("---");
        foreach (var (what, result) in results) log?.WriteLine($"{(result.StartsWith("ok", StringComparison.Ordinal) ? "OK  " : "FAIL")} {what}");

        // Measured with LongPathsEnabled 1 (the host) and 0 (the lent VM, Windows' default) alike: everything works but
        // the Recycle Bin, which takes no path that long from anyone; FileCat says so and leaves the file as it was.
        var failed = results.Where(r => !r.Result.StartsWith("ok", StringComparison.Ordinal) && r.What != "recycle a file 300 deep").ToList();
        Assert.True(failed.Count == 0, string.Join("; ", failed.Select(f => $"{f.What}: {f.Result}")));
        var recycled = results.Single(r => r.What == "recycle a file 300 deep").Result;
        Assert.True(recycled.StartsWith("ok", StringComparison.Ordinal) || recycled.Contains("too long for the Recycle Bin", StringComparison.Ordinal) && recycled.Contains("still there", StringComparison.Ordinal), recycled);
    }
}
