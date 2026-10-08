using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class InterruptedRenameReviewTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void An_exact_reviewed_file_or_tree_can_finish_or_restore_its_original_name(bool directory, bool taken)
    {
        using var f = new Fixture(directory); if (taken) File.WriteAllText(f.Target, "occupant");
        var review = Assert.IsType<JournalRecovery.RenameReview>(JournalRecovery.ReviewRename(f.Intent, f.Files, f.Provider));
        var result = JournalRecovery.FinishReviewedRename(review, f.Files, f.Provider); var path = taken ? f.Original : f.Target;
        Emit("exact-rename", new { directory, taken, result.Resolved, result.Finished, review.Bytes, Items = review.Items.Count, ExactBytes = File.ReadAllText(directory ? Path.Join(path, "child") : path), Via = f.Exists, TargetBytes = taken ? File.ReadAllText(f.Target) : null });
        Assert.True(result.Resolved); Assert.Equal(taken ? 0 : 1, result.Finished); Assert.False(f.Exists); Assert.Equal("reviewed", File.ReadAllText(directory ? Path.Join(path, "child") : path)); if (taken) Assert.Equal("occupant", File.ReadAllText(f.Target));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(8, 8, true)]
    [InlineData(9, 8, false)]
    [InlineData(65536, 65536, true)]
    [InlineData(65537, 65536, false)]
    public void Whole_file_review_obeys_the_byte_budget(int length, long budget, bool accepted)
    {
        using var f = new Fixture(); File.WriteAllBytes(f.Via, Enumerable.Range(0, length).Select(i => (byte)i).ToArray());
        var review = JournalRecovery.ReviewRename(f.Intent, f.Files, f.Provider, byteLimit: budget);
        Emit("rename-byte-budget", new { length, budget, Reviewed = review is not null, Exists = f.Exists, ActualLength = new FileInfo(f.Via).Length });
        Assert.Equal(accepted, review is not null); Assert.True(f.Exists); Assert.Equal(length, new FileInfo(f.Via).Length);
    }

    [Theory]
    [InlineData(1, 8, false)]
    [InlineData(2, 7, false)]
    [InlineData(2, 8, true)]
    [InlineData(3, 8, true)]
    public void A_directory_review_requires_its_complete_bounded_tree(int entries, long bytes, bool accepted)
    {
        using var f = new Fixture(true); var review = JournalRecovery.ReviewRename(f.Intent, f.Files, f.Provider, byteLimit: bytes, itemLimit: entries);
        Emit("rename-tree-budget", new { entries, bytes, Reviewed = review is not null, Exists = f.Exists });
        Assert.Equal(accepted, review is not null); Assert.Equal("reviewed", File.ReadAllText(Path.Join(f.Via, "child")));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("identity")]
    [InlineData("final-path")]
    [InlineData("link")]
    [InlineData("info-access")]
    [InlineData("move-access")]
    [InlineData("move-changed")]
    [InlineData("both-taken")]
    [InlineData("parent-identity")]
    [InlineData("parent-path")]
    public void Changed_or_unavailable_items_and_parents_are_kept(string change)
    {
        using var f = new Fixture(); var files = new Files(f.Via, f.Root, change); var review = Assert.IsType<JournalRecovery.RenameReview>(JournalRecovery.ReviewRename(f.Intent, files, f.Provider)); files.Changed = true;
        if (change == "both-taken") { File.WriteAllText(f.Target, "target occupant"); File.WriteAllText(f.Original, "original occupant"); }
        JournalRecovery.RenameRecoveryResult? result = null;
        if (change == "cancel") Assert.Throws<OperationCanceledException>(() => JournalRecovery.FinishReviewedRename(review, files, f.Provider, () => throw new OperationCanceledException()));
        else result = JournalRecovery.FinishReviewedRename(review, files, f.Provider);
        Emit("rename-refusal", new { change, Resolved = result?.Resolved, ViaBytes = File.ReadAllText(f.Via), files.Moves, Target = File.Exists(f.Target) });
        if (result is not null) Assert.False(result.Resolved); Assert.Equal(change == "move-changed" ? "changed!" : "reviewed", File.ReadAllText(f.Via)); Assert.Equal(change == "move-access" ? 2 : change == "move-changed" ? 1 : 0, files.Moves); if (change != "both-taken") Assert.False(File.Exists(f.Target));
    }

    private void Emit(string control, object detail) => output.WriteLine("RENAME_REVIEW " + JsonSerializer.Serialize(new { control, detail, OwnedFileSystem = true, NativeDesktop = false, PhysicalSource = false, SyntheticUnavailableIdentityControls = true, AtomicRenameIdentityQualified = false }));
    private sealed class Fixture : IDisposable
    {
        public string Root = Directory.CreateDirectory(Path.Join(Path.GetTempPath(), "filecat-rename-review", Guid.NewGuid().ToString("N"))).FullName;
        public string Via => Path.Join(Root, "via"); public string Target => Path.Join(Root, "target"); public string Original => Path.Join(Root, "original");
        public PendingIntent Intent => new(1, JobJournal.RenameViaOp, Original, Target, null) { Via = Via };
        public bool Exists => File.Exists(Via) || Directory.Exists(Via); public readonly PortableFileOperations Files = new(); public readonly LocalFileSystemProvider Provider = new();
        public Fixture(bool directory = false) { if (directory) { Directory.CreateDirectory(Via); File.WriteAllText(Path.Join(Via, "child"), "reviewed"); } else File.WriteAllText(Via, "reviewed"); }
        public void Dispose() => Directory.Delete(Root, true);
    }
    private sealed class Files(string via, string parent, string outcome) : PortableFileOperations
    {
        public bool Changed; public int Moves;
        public override FileSystemItemInfo? TryGetInfo(string path) { if (Changed && path == via && outcome == "info-access") throw new UnauthorizedAccessException("Owned info denial"); var info = base.TryGetInfo(path); return Changed && path == via && outcome == "link" && info is not null ? info with { IsLink = true, LinkTarget = "other" } : info; }
        public override string? GetFileIdentity(string path) => path == via || path == parent ? Changed && (path == via && outcome == "identity" || path == parent && outcome == "parent-identity") ? "replacement" : "reviewed" : base.GetFileIdentity(path);
        public override string? GetFinalPath(string path) => Changed && (path == via && outcome == "final-path" || path == parent && outcome == "parent-path") ? path + "-changed" : path;
        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false) { Moves++; if (outcome == "move-access") throw new UnauthorizedAccessException("Owned rename denial"); if (outcome == "move-changed") { var time = File.GetLastWriteTimeUtc(source); File.WriteAllText(source, "changed!"); File.SetLastWriteTimeUtc(source, time); throw new IOException("Owned first move failed after replacement"); } base.Move(source, destination, replaceExisting, writeThrough); }
    }
}
