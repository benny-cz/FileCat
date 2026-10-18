using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class UndoSafetyTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();
    private async Task<UndoReport> Run(UndoStep step, IFileSystemOperations? fs = null)
    {
        var original = new Job(new JobRequest { Kind = JobKind.CreateLink }, "Owned guard fixture", "fixture", [], []);
        original.AddUndo(step); original.SetState(JobState.Completed);
        using var claim = Assert.IsType<UndoReservation>(original.TryReserveUndo());
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
        var manager = new JobManager(fs ?? new PortableFileOperations(), providers, _dir.Dir("journals"));
        var inverse = manager.SubmitUndo(claim);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!inverse.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, "Inverse guard fixture timed out.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return Assert.IsType<UndoReport>(inverse.UndoReport);
    }

    [Theory]
    [InlineData("shared-content-edit", true)]
    [InlineData("original-missing", false)]
    [InlineData("original-replaced", false)]
    [InlineData("created-name-replaced", false)]
    public async Task Hard_link_removal_requires_the_recorded_identity_at_both_names_but_allows_shared_content_changes(string scenario, bool removed)
    {
        string original = _dir.File("original/data.txt", "before");
        string created = Path.Combine(_dir.Dir("created"), "data.txt");
        var fs = new IdentityFiles(original, created);
        fs.CreateLink(created, original, LinkKind.Hard, false);
        var step = new UndoStep(UndoKind.RemoveCreatedHardLink, created, original, 0, 0, "owned-file-identity");
        if (scenario == "shared-content-edit") { File.WriteAllText(created, "shared bytes edited after creation"); Assert.Equal(File.ReadAllText(original), File.ReadAllText(created)); }
        if (scenario == "original-missing") File.Delete(original);
        if (scenario == "original-replaced") { File.Delete(original); File.WriteAllText(original, "different file"); fs.OriginalIdentity = "replacement-identity"; }
        if (scenario == "created-name-replaced") { File.Delete(created); File.WriteAllText(created, "different created-name occupant"); fs.CreatedIdentity = "replacement-identity"; }
        string before = File.ReadAllText(created);
        var report = await Run(step, fs);
        Assert.Equal(removed ? UndoOutcomeKind.Removed : UndoOutcomeKind.Kept, Assert.Single(report.Outcomes).Outcome);
        Assert.Equal(!removed, File.Exists(created));
        if (!removed) Assert.Equal(before, File.ReadAllText(created));
        if (scenario == "shared-content-edit") Assert.Equal("shared bytes edited after creation", File.ReadAllText(original));
        string review = UndoActions.Describe(step);
        Assert.StartsWith("Remove created hard-link name:", review); Assert.Contains(created, review); Assert.Contains(original, review);
        Assert.Contains(created, report.ToText());
        if (removed) Assert.Contains(original, report.ToText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_created_empty_file_is_kept_when_it_now_contains_data_or_is_a_directory(bool directory)
    {
        string path = directory ? _dir.Dir("created") : _dir.File("created", "new content");
        var report = await Run(new UndoStep(UndoKind.RemoveCreatedFile, path, path, 0, File.GetLastWriteTimeUtc(path).Ticks));
        Assert.Equal(UndoOutcomeKind.Kept, Assert.Single(report.Outcomes).Outcome);
        Assert.True(directory ? Directory.Exists(path) : File.Exists(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_empty_folder_inverse_keeps_a_populated_folder_or_replacement_file(bool replacedByFile)
    {
        string path = replacedByFile ? _dir.File("created", "replacement") : _dir.Dir("created");
        if (!replacedByFile) File.WriteAllText(Path.Combine(path, "keep.txt"), "new contents");
        var report = await Run(new UndoStep(UndoKind.RemoveEmptyDirectory, path, path, 0, 0));
        Assert.Equal(UndoOutcomeKind.Kept, Assert.Single(report.Outcomes).Outcome);
        Assert.True(replacedByFile ? File.Exists(path) : File.Exists(Path.Combine(path, "keep.txt")));
    }

    [Fact]
    public async Task Moving_a_directory_back_keeps_its_current_contents_instead_of_requiring_an_unrecorded_tree_snapshot()
    {
        string current = _dir.Dir("current-folder"), original = Path.Combine(_dir.Path, "original-folder");
        var before = new PortableFileOperations().TryGetInfo(current)!;
        File.WriteAllText(Path.Combine(current, "added.txt"), "new child after the move");
        var report = await Run(new UndoStep(UndoKind.MoveBack, current, original, before.Size, before.ModifiedUtc.Ticks));
        Assert.Equal(UndoOutcomeKind.Restored, Assert.Single(report.Outcomes).Outcome);
        Assert.False(Directory.Exists(current)); Assert.Equal("new child after the move", File.ReadAllText(Path.Combine(original, "added.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recycle_restore_keeps_the_platform_refusal_and_never_overwrites_an_occupied_original(bool hasRecordedIdentity)
    {
        string target = _dir.File("original.txt", "occupant must survive"); var fs = new RefusedRecycleFiles();
        var report = await Run(new UndoStep(UndoKind.RestoreRecycled, "opaque-bin-id", target, 0, 0, hasRecordedIdentity ? "opaque-bin-id" : null), fs);
        Assert.Equal(UndoOutcomeKind.Kept, Assert.Single(report.Outcomes).Outcome);
        Assert.Equal(hasRecordedIdentity ? 1 : 0, fs.Attempts); Assert.Equal("occupant must survive", File.ReadAllText(target));
        string text = report.ToText(); Assert.Contains(target, text); Assert.DoesNotContain("opaque-bin-id", text);
    }

    [Fact]
    public void Link_review_and_result_name_the_created_entry_and_retained_target_with_the_correct_verbs()
    {
        string created = Path.Combine(_dir.Path, "created-link"), target = Path.Combine(_dir.Path, "target");
        var step = new UndoStep(UndoKind.RemoveCreatedLink, created, target, 0, 0);
        Assert.Equal($"Remove created link: \"{created}\" (target retained: \"{target}\")", UndoActions.Describe(step));
        string result = UndoReport.Describe(new UndoOutcome(step.Kind, created, target, UndoOutcomeKind.Removed, "removed"));
        Assert.StartsWith($"Removed the created link \"{created}\"", result);
        Assert.Contains($"target \"{target}\" was not removed", result);
    }

    // Portable real hard links plus controlled identity responses exercise the admission rule on all platforms.
    // Native Windows identity and last-name behavior also remain covered by WindowsFileOperationsTests.
    private sealed class IdentityFiles(string original, string created) : PortableFileOperations
    {
        public string OriginalIdentity = "owned-file-identity", CreatedIdentity = "owned-file-identity";
        public override string? GetFileIdentity(string path) => path == original ? OriginalIdentity : path == created ? CreatedIdentity : base.GetFileIdentity(path);
    }
    private sealed class RefusedRecycleFiles : PortableFileOperations
    {
        public int Attempts;
        public override bool TryRestoreRecycled(string recycledId, string originalPath, out string? error)
        { Attempts++; error = "The original name is occupied; the recycled item was kept."; return false; }
    }
}
