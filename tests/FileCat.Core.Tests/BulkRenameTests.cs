using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class BulkRenameTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly JobManager _jobs;

    public BulkRenameTests()
    {
        _providers.Register(new LocalFileSystemProvider());
        _jobs = new JobManager(new PortableFileOperations(), _providers, Path.Combine(_dir.Path, "journal"));
    }

    public void Dispose() => _dir.Dispose();

    private ItemRef Item(string name) => ItemRef.ForFileSystemPath(Path.Combine(_dir.Path, name),
        Directory.Exists(Path.Combine(_dir.Path, name)) ? EntryKind.Directory : EntryKind.File);

    private bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    [Fact]
    public void Masks_counters_ranges_search_and_case_compose_in_order()
    {
        _dir.File("Holiday Photo.JPG", "1");
        _dir.File("scan.tar.gz", "2");
        _dir.Dir("Folder.Name");
        var items = new[] { Item("Holiday Photo.JPG"), Item("scan.tar.gz"), Item("Folder.Name") };
        var rows = BulkRenamePlanner.Preview(items, new RenameRules("[N1-3]_[C]", "[E]", " ", "-", CounterStart: 7, CounterStep: 3, CounterDigits: 3, Case: RenameCase.Lower), Exists);
        Assert.Equal(["hol_007.jpg", "sca_010.gz", "fol_013"], rows.Select(r => r.NewName));
        Assert.All(rows, r => Assert.Null(r.Problem));
        var regex = BulkRenamePlanner.Preview(items[..1], new RenameRules(Search: @"(\w+) (\w+)", Replace: "$2 $1", Regex: true), Exists);
        Assert.Equal("Photo Holiday.JPG", regex[0].NewName);
        var badRegex = BulkRenamePlanner.Preview(items[..1], new RenameRules(Search: "(", Regex: true), Exists);
        Assert.NotNull(badRegex[0].Problem);
        Assert.Equal("Holiday Photo.JPG", BulkRenamePlanner.Preview(items[..1], new RenameRules("[N][unknown]"), Exists)[0].NewName.Replace("[unknown]", ""));
    }

    [Fact]
    public void Collisions_invalid_names_and_taken_names_block_the_plan()
    {
        _dir.File("a.txt");
        _dir.File("b.txt");
        _dir.File("keep.txt");
        var items = new[] { Item("a.txt"), Item("b.txt") };
        var same = BulkRenamePlanner.Preview(items, new RenameRules("same"), Exists);
        Assert.All(same, r => Assert.Contains("same name", r.Problem));
        var taken = BulkRenamePlanner.Preview(items[..1], new RenameRules("keep"), Exists);
        Assert.Contains("exists", taken[0].Problem);
        var invalid = BulkRenamePlanner.Preview(items[..1], new RenameRules("a|b"), Exists);
        Assert.NotNull(invalid[0].Problem);
        // A swap is a valid plan: each target is leaving.
        var swap = BulkRenamePlanner.Preview(items, new RenameRules(), Exists, explicitNames: ["b.txt", "a.txt"]);
        Assert.All(swap, r => Assert.Null(r.Problem));
    }

    [Fact]
    public async Task Swaps_and_chains_run_through_temporary_names_and_undo_exactly()
    {
        _dir.File("a.txt", "A");
        _dir.File("b.txt", "B");
        _dir.File("c.txt", "C");
        var items = new[] { Item("a.txt"), Item("b.txt"), Item("c.txt") };
        // a→b, b→c, c→a: a cycle through all three.
        var job = _jobs.Submit(new JobRequest { Kind = JobKind.Rename, Sources = items, NewNames = ["b.txt", "c.txt", "a.txt"] });
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("A", File.ReadAllText(Path.Combine(_dir.Path, "b.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(_dir.Path, "c.txt")));
        Assert.Equal("C", File.ReadAllText(Path.Combine(_dir.Path, "a.txt")));
        Assert.Empty(Directory.GetFiles(_dir.Path, BulkRenameRunner.TempPrefix + "*"));
        Assert.True(job.CanUndo);
        var report = UndoService.Undo(job, new PortableFileOperations());
        Assert.Equal(3, report.Count(r => r.StartsWith("Renamed", StringComparison.Ordinal)));
        Assert.Equal("A", File.ReadAllText(Path.Combine(_dir.Path, "a.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(_dir.Path, "b.txt")));
        Assert.Equal("C", File.ReadAllText(Path.Combine(_dir.Path, "c.txt")));
    }

    private BulkRenameRunner.Pair Pair(string from, string to) => new(Path.Combine(_dir.Path, from), Path.Combine(_dir.Path, to));

    private string Read(string name) => File.ReadAllText(Path.Combine(_dir.Path, name));

    [Fact]
    public void A_cancel_before_every_item_has_its_temporary_name_puts_every_original_name_back()
    {
        _dir.File("a.txt", "A");
        _dir.File("b.txt", "B");
        _dir.File("c.txt", "C");
        int calls = 0;
        Assert.Throws<OperationCanceledException>(() => BulkRenameRunner.Run([Pair("a.txt", "b.txt"), Pair("b.txt", "c.txt"), Pair("c.txt", "a.txt")],
            new PortableFileOperations(), () =>
            {
                if (++calls == 3) throw new OperationCanceledException();
            }));
        Assert.Equal(["A", "B", "C"], new[] { Read("a.txt"), Read("b.txt"), Read("c.txt") });
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path, BulkRenameRunner.TempPrefix + "*"));
    }

    private sealed class CrashingFs(int crashAtMove) : PortableFileOperations
    {
        private int _moves;

        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
        {
            if (++_moves == crashAtMove) throw new InvalidProgramException("The process died.");
            base.Move(source, destination, replaceExisting, writeThrough);
        }
    }

    [Fact]
    public void After_a_crash_recovery_finishes_the_renames_that_were_under_way()
    {
        _dir.File("a.txt", "A");
        _dir.File("b.txt", "B");
        _dir.File("c.txt", "C");
        var items = new[] { Item("a.txt"), Item("b.txt"), Item("c.txt") };
        var request = new JobRequest { Kind = JobKind.Rename, Sources = items, NewNames = ["b.txt", "c.txt", "a.txt"] };
        var (title, device, reads, writes) = _jobs.Describe(request);
        var job = new Job(request, title, device, reads, writes);
        string journalDir = Path.Combine(_dir.Path, "journal-crash");
        // Three moves to temporary names, one final rename, then the process dies.
        using (var journal = JobJournal.Create(journalDir, job))
        {
            Assert.Throws<InvalidProgramException>(() => BulkRenameRunner.Run([Pair("a.txt", "b.txt"), Pair("b.txt", "c.txt"), Pair("c.txt", "a.txt")],
                new CrashingFs(crashAtMove: 5), () => { }, journal: journal));
        }
        Assert.Equal("A", Read("b.txt"));
        var interrupted = Assert.Single(JournalRecovery.Scan(journalDir));
        var leftovers = JournalRecovery.FindRenameLeftovers(interrupted);
        Assert.Equal(2, leftovers.Count);
        var report = JournalRecovery.FinishRenames(leftovers, out int finished);
        Assert.Empty(report);
        Assert.Equal(2, finished);
        Assert.Equal(["C", "A", "B"], new[] { Read("a.txt"), Read("b.txt"), Read("c.txt") });
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path, BulkRenameRunner.TempPrefix + "*"));

        // A swap interrupted after both items have temporary names, and a stranger takes one of the new names
        // meanwhile: the first item gets its original name back; the second finds both names taken and keeps the
        // temporary name, and the report says so.
        JournalRecovery.Close(interrupted, "test");
        _dir.File("d.txt", "D");
        _dir.File("e.txt", "E");
        var job2 = new Job(new JobRequest { Kind = JobKind.Rename, Sources = [Item("d.txt"), Item("e.txt")], NewNames = ["e.txt", "d.txt"] }, title, device, reads, writes);
        using (var journal = JobJournal.Create(journalDir, job2))
        {
            Assert.Throws<InvalidProgramException>(() => BulkRenameRunner.Run([Pair("d.txt", "e.txt"), Pair("e.txt", "d.txt")],
                new CrashingFs(crashAtMove: 3), () => { }, journal: journal));
        }
        _dir.File("e.txt", "stranger");
        var second = Assert.Single(JournalRecovery.Scan(journalDir));
        var lines = JournalRecovery.FinishRenames(JournalRecovery.FindRenameLeftovers(second), out int done);
        Assert.Equal(0, done);
        Assert.Equal("D", Read("d.txt"));
        Assert.Equal("stranger", Read("e.txt"));
        Assert.Equal(2, lines.Count);
        Assert.Contains("original name again", lines[0], StringComparison.Ordinal);
        Assert.Contains("keeps this temporary name", lines[1], StringComparison.Ordinal);
        Assert.Equal("E", File.ReadAllText(Assert.Single(Directory.GetFiles(_dir.Path, BulkRenameRunner.TempPrefix + "*"))));
    }

    [Fact]
    public async Task Failures_keep_original_names_and_undo_refuses_changed_items()
    {
        _dir.File("one.txt", "1");
        _dir.File("two.txt", "2");
        var items = new[] { Item("one.txt"), Item("two.txt") };
        var job = _jobs.Submit(new JobRequest { Kind = JobKind.Rename, Sources = items, NewNames = ["uno.txt", "dos.txt"] });
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(JobState.Completed, job.State);
        File.WriteAllText(Path.Combine(_dir.Path, "dos.txt"), "changed afterwards");
        var report = UndoService.Undo(job, new PortableFileOperations());
        Assert.Contains(report, r => r.StartsWith("Not undone", StringComparison.Ordinal) && r.Contains("changed", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(_dir.Path, "one.txt")));
        Assert.True(File.Exists(Path.Combine(_dir.Path, "dos.txt")));

        // An item that vanished before the rename ran is reported; the others still rename.
        _dir.File("x.txt");
        _dir.File("y.txt");
        var vanished = new[] { Item("x.txt"), Item("y.txt") };
        File.Delete(Path.Combine(_dir.Path, "x.txt"));
        var partial = _jobs.Submit(new JobRequest { Kind = JobKind.Rename, Sources = vanished, NewNames = ["x2.txt", "y2.txt"] });
        while (!partial.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(JobState.CompletedWithIssues, partial.State);
        Assert.True(File.Exists(Path.Combine(_dir.Path, "y2.txt")));
    }
}
