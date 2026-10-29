using System.Buffers.Binary;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.State;

if (args is ["--matroska-depth"])
{
    const int depth = 5_000;
    var bytes = new byte[depth * 8];
    for (int i = 0; i < depth; i++)
    {
        int at = i * 8;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at, 4), 0x1A45DFA3);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at + 4, 4), 0x10000000u | (uint)((depth - i - 1) * 8));
    }
    var report = MediaInspector.Inspect(new MemoryContentSource("owned.mkv", bytes), CancellationToken.None);
    bool bounded = report?.Warnings.Any(w => w.Contains("nesting limit", StringComparison.Ordinal)) == true;
    Console.WriteLine($"bytes={bytes.Length} depth={depth} bounded={bounded} warnings={string.Join(" | ", report?.Warnings ?? [])}");
    return bounded ? 0 : 6;
}

if (args is ["--search-depth", var depthText] && int.TryParse(depthText, out int requestedDepth) && requestedDepth is >= 1 and <= 5_000)
{
    string ownedRoot = Path.Combine(Path.GetTempPath(), "filecat-owned-search-depth-" + Guid.NewGuid().ToString("N"));
    var created = new List<string>();
    try
    {
        string current = ownedRoot;
        for (int i = 0; i < requestedDepth; i++)
        {
            current = Path.Combine(current, "d");
            Directory.CreateDirectory(current);
            created.Add(current);
        }
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var results = new ResultSet("owned-depth", "owned-depth", "test");
        var session = new SearchSession(new SearchQuery { Roots = [ownedRoot] }, results);
        session.Run(cancel.Token);
        bool expectedComplete = requestedDepth <= SearchSession.MaxTraversalDepth;
        bool bounded = session.Finished && results.IsComplete == expectedComplete &&
            session.FoldersVisited == Math.Min(requestedDepth + 1, SearchSession.MaxTraversalDepth + 1);
        Console.WriteLine($"depth={requestedDepth} visited={session.FoldersVisited} complete={results.IsComplete} bounded={bounded}");
        return bounded ? 0 : 7;
    }
    finally
    {
        for (int i = created.Count - 1; i >= 0; i--)
            try { Directory.Delete(created[i]); } catch (IOException) { }
        try { Directory.Delete(ownedRoot); } catch (IOException) { }
    }
}

if (args is ["--job-depth"] or ["--job-depth", _] &&
    (args.Length == 1 || int.TryParse(args[1], out _)))
{
    int jobDepth = args.Length == 1 ? 520 : int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
    if (jobDepth is < 1 or > 520) return 15;
    // macOS's ordinary temporary path is long enough to hit PATH_MAX before a useful deep
    // fallback control. /tmp is an owned disposable base with the same local filesystem.
    string ownedRoot = Path.Combine(OperatingSystem.IsMacOS() ? "/tmp" : Path.GetTempPath(),
        "filecat-owned-job-depth-" + Guid.NewGuid().ToString("N"));
    string source = Path.Combine(ownedRoot, "source");
    string destination = Path.Combine(ownedRoot, "destination");
    var created = new List<string>();
    string current = source;
    try
    {
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        for (int i = 0; i < jobDepth; i++)
        {
            current = Path.Combine(current, "d");
            Directory.CreateDirectory(current);
            created.Add(current);
        }
        File.WriteAllText(Path.Combine(current, "end.txt"), "owned");
        var jobProviders = new ProviderRegistry();
        jobProviders.Register(new LocalFileSystemProvider());
        var manager = new JobManager(new PortableFileOperations(), jobProviders, Path.Combine(ownedRoot, "journal"));
        var copy = manager.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.Directory)],
            Destination = Location.FileSystem(destination),
        });
        var jobDeadline = DateTime.UtcNow.AddSeconds(60);
        while (!copy.State.IsFinished() && DateTime.UtcNow < jobDeadline) await Task.Delay(10);
        bool copyBounded = copy.State.IsFinished() && (jobDepth > 512
            ? copy.Issues.Any(i => i.Message.Contains("level copy limit", StringComparison.Ordinal))
            : copy.State == JobState.Completed);
        var delete = manager.Submit(new JobRequest
        {
            Kind = JobKind.Delete,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.Directory)],
        });
        jobDeadline = DateTime.UtcNow.AddSeconds(60);
        while (!delete.State.IsFinished() && DateTime.UtcNow < jobDeadline) await Task.Delay(10);
        // This harness deliberately uses PortableFileOperations, exercising the recursive fallback
        // even on Windows; production Windows deletion uses its separate native bound adapter.
        bool deleteBounded = delete.State.IsFinished() && (jobDepth > 512
            ? delete.Issues.Any(i => i.Message.Contains("level delete limit", StringComparison.Ordinal))
            : delete.State == JobState.Completed && !Directory.Exists(source));
        Console.WriteLine($"depth={jobDepth} copy={copy.State} copyBounded={copyBounded} copyIssues={string.Join(" | ", copy.Issues.Select(i => i.Message))} delete={delete.State} deleteBounded={deleteBounded} deleteIssues={string.Join(" | ", delete.Issues.Select(i => i.Message))}");
        return copyBounded && deleteBounded ? 0 : 8;
    }
    finally
    {
        try { File.Delete(Path.Combine(current, "end.txt")); } catch (IOException) { }
        for (int i = created.Count - 1; i >= 0; i--)
            try { Directory.Delete(created[i]); } catch (IOException) { }
        try { Directory.Delete(ownedRoot, recursive: true); } catch (IOException) { }
    }
}

if (args is ["--undo-high-guard", var fixtureRoot] && WindowsTokenAuthority.IsHighIntegrity)
{
    string current = Path.Combine(fixtureRoot, "medium", "moved");
    string originalPath = Path.Combine(fixtureRoot, "protected", "moved");
    if (!Directory.Exists(originalPath) || Directory.Exists(current)) return 9;
    var fs = new PortableFileOperations();
    var guardProviders = new ProviderRegistry();
    guardProviders.Register(new LocalFileSystemProvider());
    var guardManager = new JobManager(fs, guardProviders, Path.Combine(fixtureRoot, "journals"));
    var move = guardManager.Submit(new JobRequest
    {
        Kind = JobKind.Move,
        Sources = [ItemRef.ForFileSystemPath(originalPath, EntryKind.Directory)],
        Destination = Location.FileSystem(Path.Combine(fixtureRoot, "medium")),
    });
    var guardDeadline = DateTime.UtcNow.AddSeconds(30);
    while (!move.State.IsFinished() && DateTime.UtcNow < guardDeadline) await Task.Delay(10);
    if (!move.CanUndo || !Directory.Exists(current)) return 10;
    File.WriteAllText(Path.Combine(fixtureRoot, "ready.txt"), "medium writer may replace the moved directory");
    guardDeadline = DateTime.UtcNow.AddSeconds(30);
    while (!File.Exists(Path.Combine(fixtureRoot, "done.txt")) && DateTime.UtcNow < guardDeadline) await Task.Delay(20);
    bool substitution = File.Exists(Path.Combine(current, "substituted.txt")) &&
        File.ReadAllText(Path.Combine(current, "substituted.txt")) == "MEDIUM-SUBSTITUTION";
    bool childEdit = File.Exists(Path.Combine(current, "original.txt")) &&
        File.ReadAllText(Path.Combine(current, "original.txt")) == "MEDIUM-EDIT";
    if (!substitution && !childEdit) return 11;
    using var guardClaim = move.TryReserveUndo();
    if (guardClaim is null) return 12;
    var undo = guardManager.SubmitUndo(guardClaim);
    guardDeadline = DateTime.UtcNow.AddSeconds(30);
    while (!undo.State.IsFinished() && DateTime.UtcNow < guardDeadline) await Task.Delay(10);
    var result = undo.UndoReport?.Outcomes;
    bool kept = Directory.Exists(current) && !Directory.Exists(originalPath) &&
        (substitution || childEdit) &&
        result?.Any(o => o.Outcome == UndoOutcomeKind.Kept && o.Reason.Contains("Elevated directory Undo is refused", StringComparison.Ordinal)) == true;
    Console.WriteLine($"high={WindowsTokenAuthority.IsHighIntegrity} moved={move.State} undo={undo.State} kept={kept} originalExists={Directory.Exists(originalPath)} result={string.Join(" | ", result?.Select(o => o.Reason) ?? [])}");
    return kept ? 0 : 13;
}

if (args is ["--list-network-guard", var server] && System.Net.IPAddress.TryParse(server, out _))
{
    string ownedRoot = Path.Combine(Path.GetTempPath(), "filecat-owned-list-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(ownedRoot);
    try
    {
        string unc = @"\\" + server + @"\filecat-owned\item.txt";
        string listSource = Path.Combine(ownedRoot, "source.lst");
        string sourceLink = Path.Combine(ownedRoot, "source-link.lst");
        string entryLink = Path.Combine(ownedRoot, "entry-link.txt");
        string local = Path.Combine(ownedRoot, "local.txt");
        File.WriteAllText(local, "LOCAL");
        File.CreateSymbolicLink(sourceLink, unc);
        File.CreateSymbolicLink(entryLink, unc);
        File.WriteAllText(listSource, unc + "\n" + entryLink + "\n" + local + "\n");
        var set = new ResultSet("owned-list", "owned-list", "test");
        bool directRefused = false, sourceRefused = false;
        try { ListFile.Load(unc, set, CancellationToken.None); }
        catch (IOException) { directRefused = true; }
        try { ListFile.Load(sourceLink, set, CancellationToken.None); }
        catch (IOException) { sourceRefused = true; }
        var outcome = ListFile.Load(listSource, set, CancellationToken.None);
        bool bounded = directRefused && sourceRefused && outcome.Added == 1 && outcome.Network == 2 && set.Count == 1;
        Console.WriteLine($"server={server} directRefused={directRefused} sourceRefused={sourceRefused} added={outcome.Added} unsafe={outcome.Network} bounded={bounded}");
        return bounded ? 0 : 14;
    }
    finally { Directory.Delete(ownedRoot, recursive: true); }
}

// The parent Core test or native VM acceptance owns this exact disposable root
// and kills only this child after the first dependent inverse rename.
// --app-layout puts the journal where FileCat's isolated --data profile reads it.
// No user files are selected here.
if (args.Length is not (1 or 2) || !Path.IsPathFullyQualified(args[0]) || !Directory.Exists(args[0]) ||
    (args.Length == 2 && args[1] != "--app-layout")) return 2;
string root = args[0];
string items = Directory.CreateDirectory(Path.Combine(root, "items")).FullName;
string journal = Directory.CreateDirectory(args.Length == 2
    ? Path.Combine(root, "data", "local", "journal")
    : Path.Combine(root, "journals")).FullName;
string marker = Path.Combine(root, "inverse-held.json");
string[] paths = [Path.Combine(items, "a.txt"), Path.Combine(items, "b.txt"), Path.Combine(items, "c.txt")];
for (int i = 0; i < paths.Length; i++) File.WriteAllText(paths[i], ((char)('A' + i)).ToString());

var files = new HoldFirstInverseMove(marker);
var providers = new ProviderRegistry();
providers.Register(new LocalFileSystemProvider());
var jobs = new JobManager(files, providers, journal);
var original = jobs.Submit(new JobRequest
{
    Kind = JobKind.Rename,
    Sources = paths.Select(path => ItemRef.ForFileSystemPath(path, EntryKind.File)).ToArray(),
    NewNames = ["b.txt", "c.txt", "a.txt"],
});
var deadline = DateTime.UtcNow.AddSeconds(20);
while (!original.State.IsFinished() && DateTime.UtcNow < deadline) await Task.Delay(10);
if (!original.CanUndo || !paths.Select(File.ReadAllText).SequenceEqual(["C", "A", "B"])) return 3;
using var claim = original.TryReserveUndo();
if (claim is null) return 4;
files.Hold = true;
var inverse = jobs.SubmitUndo(claim);
while (!inverse.State.IsFinished()) await Task.Delay(100);
return 5; // The parent must kill us while the inverse rename is held.

file sealed class HoldFirstInverseMove(string marker) : PortableFileOperations
{
    private int _held;
    public bool Hold { get; set; }

    public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
    {
        base.Move(source, destination, replaceExisting, writeThrough);
        if (!Hold || Interlocked.Exchange(ref _held, 1) != 0) return;
        // Publish only after the writer closes the file. The parent kills this
        // process as soon as the marker appears, including on Windows where an
        // in-progress WriteAllText denies concurrent reads.
        string pendingMarker = marker + ".writing";
        File.WriteAllText(pendingMarker, JsonSerializer.Serialize(new { source, destination }));
        File.Move(pendingMarker, marker);
        Thread.Sleep(TimeSpan.FromMinutes(2));
    }
}
