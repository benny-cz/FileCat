using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

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
