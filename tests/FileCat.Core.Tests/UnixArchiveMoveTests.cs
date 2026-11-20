using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

/// <summary>
/// Plan §11.6.1 on Unix: a move removes only sources whose archived bytes, metadata and identity still match, after a
/// verified publication; a folder goes only once empty. Each removal re-reviews the held source, sets it aside under an
/// exclusive name in the same folder and unlinks only that held object.
/// </summary>
public sealed partial class UnixArchiveStorageTests
{
    private static (JobManager Manager, string Journals) MoveManager(string root)
    {
        var backend = UnixArchiveJobBackend.Create(ArchiveWriteFormat.Tar, (nonce, _) => Child.Start(nonce, "archive-assess-v2"),
            (nonce, _) => Child.Start(nonce, "archive-transform-v2"), (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
        string journals = Path.Combine(root, "journals");
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([backend]));
        manager.DecisionRequested += pending => Assert.Fail("The fixture has no conflict or exclusion: " + pending.Request.Message);
        return (manager, journals);
    }
    private static void Source(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
        File.SetUnixFileMode(path, (UnixFileMode)0x1a4); File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddSeconds(1700000000));
    }

    [Fact]
    public async Task A_verified_move_creates_then_updates_and_removes_only_the_archived_sources()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string a = Path.Combine(root, "a"), b = Path.Combine(root, "b"), tree = Path.Combine(root, "tree");
        byte[] first = [1, 2, 3], second = [7, 8, 9], nested = "nested move bytes"u8.ToArray();
        Source(a, first); Source(b, second); Source(Path.Combine(tree, "sub", "file"), nested); Directory.CreateDirectory(Path.Combine(tree, "empty"));
        var (manager, journals) = MoveManager(root); var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        string target = Path.Combine(root, "moved.tar");
        var job = manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFile, "first", a), new(ArchiveChangeKind.AddFile, "second", b),
            new(ArchiveChangeKind.AddFolder, "moved", tree)], Profile: profile) { MoveSources = true }, null);
        await ArchiveNativeJobCases.Completed(job);
        Assert.Equal(new[] { 0, 1, 2 }, job.CompletedRootIndices); Assert.Equal(0, job.FailedRootCount);
        Assert.False(File.Exists(a)); Assert.False(File.Exists(b)); Assert.False(Directory.Exists(tree));
        Assert.Equal("Archive saved; moved 3 original files and 3 folders.", job.Summary);
        // The clean scope destroyed its sidecars; read the published archive itself.
        Assert.Equal(new Dictionary<string, string?>
        {
            ["first"] = Convert.ToHexString(first), ["second"] = Convert.ToHexString(second), ["moved"] = null, ["moved/empty"] = null,
            ["moved/sub"] = null, ["moved/sub/file"] = Convert.ToHexString(nested),
        }, Entries(target));
        Assert.Empty(Directory.GetFileSystemEntries(root, ".filecat-*"));
        // Each file's aside name was journaled before its rename, so an interrupted move says where the original is.
        var removals = JobJournal.ReadAll(Assert.Single(Directory.GetFiles(journals))).Where(record => record.Type == "intent" &&
            record.Node["op"]?.GetValue<string>() == "archive-native-move-delete").Select(record => record.Node).ToArray();
        Assert.Equal(6, removals.Length);
        foreach (var intent in removals)
        {
            string removed = intent["path"]!.GetValue<string>(); string? staged = intent["staged"]?.GetValue<string>();
            bool folder = removed == tree || removed.StartsWith(tree + "/", StringComparison.Ordinal) && !removed.EndsWith("/file", StringComparison.Ordinal);
            if (folder) Assert.Null(staged);
            else Assert.True(staged is not null && Path.GetDirectoryName(staged) == Path.GetDirectoryName(removed) &&
                Path.GetFileName(staged).StartsWith(".filecat-move-", StringComparison.Ordinal), removed + " -> " + staged);
        }

        // Move one more file into the existing archive.
        string added = Path.Combine(root, "added"); byte[] addedBytes = "another moved source"u8.ToArray(); Source(added, addedBytes);
        var assessor = new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
        var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
        var update = manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "added", added)]) with { MoveSources = true }, null);
        await ArchiveNativeJobCases.Completed(update);
        Assert.Equal(new[] { 0 }, update.CompletedRootIndices); Assert.False(File.Exists(added));
        Assert.Equal(Convert.ToHexString(addedBytes), Entries(target)["added"]);
        Assert.Equal(new[] { "journals", "moved.tar" }, Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Empty(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
    }

    // Plan §7.2 rejects hard-link aliases at capture, so nothing is archived or removed; RemoveCapturedSource keeps the
    // single-link check as defence in depth.
    [Fact]
    public async Task A_hard_linked_original_is_refused_before_anything_changes()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string a = Path.Combine(root, "a"), b = Path.Combine(root, "b"); Source(a, [1, 2, 3]); Source(b, [4, 5]);
        string link = Path.Combine(root, "elsewhere"); Directory.CreateDirectory(link);
        Assert.Equal(0, HardLink(a, Path.Combine(link, "a-link")));
        var (manager, _) = MoveManager(root);
        var job = manager.SubmitArchive(new(Path.Combine(root, "moved.tar"), null,
            [new(ArchiveChangeKind.AddFile, "a", a), new(ArchiveChangeKind.AddFile, "b", b)], Profile: new(ArchiveWriteFormat.Tar)) { MoveSources = true }, null);
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(120));
            while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
        }
        string issues = string.Join(" | ", job.Issues.Select(issue => issue.Message + " [" + issue.Details + "]"));
        Assert.True(job.State == JobState.Failed, job.State + ": " + issues);
        Assert.True(File.Exists(a), issues); Assert.True(File.Exists(b)); Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(link, "a-link")));
        Assert.False(File.Exists(Path.Combine(root, "moved.tar")));
        Assert.Contains(job.Issues, issue => issue.Message == "“a” has another hard link (a second name for the same file), which FileCat does not archive or update. Nothing was changed.");
        Assert.Empty(job.CompletedRootIndices);
        Assert.Empty(Directory.GetFileSystemEntries(root, ".filecat-*"));
    }

    // The removal primitives after a verified publication: anything that no longer matches its capture is kept.
    [Theory, InlineData("changed"), InlineData("replaced"), InlineData("new-child")]
    public void A_source_that_no_longer_matches_its_capture_is_kept(string change)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string tree = Path.Combine(root, "tree"), file = Path.Combine(tree, "file"); Source(file, [1, 2, 3]);
        using var selection = UnixArchiveSelectionTree.Capture(root, ["tree"], ct);
        var fileMember = Assert.Single(selection.Members, member => member.Type == ArchiveMemberType.File);
        var folderMember = Assert.Single(selection.Members, member => member.Type == ArchiveMemberType.Directory);
        switch (change)
        {
            case "changed": File.AppendAllText(file, "changed after the archive was written"); break;
            case "replaced":
                string replacement = Path.Combine(tree, ".replacement"); Source(replacement, [1, 2, 3]); File.Move(replacement, file, overwrite: true); break;
            default:
                File.WriteAllText(Path.Combine(tree, "new-child"), "added after capture");
                selection.RemoveCaptured(fileMember.Id, ct); // The archived file itself is still exactly its capture.
                Assert.False(File.Exists(file));
                break;
        }
        var error = Assert.ThrowsAny<IOException>(() => selection.RemoveCaptured(change == "new-child" ? folderMember.Id : fileMember.Id, ct));
        Assert.True(error is ArchiveWriteException { Reason: ArchiveWriteReason.SourceChanged }, error.ToString());
        Assert.True(Directory.Exists(tree));
        if (change == "new-child") Assert.Equal("added after capture", File.ReadAllText(Path.Combine(tree, "new-child")));
        else Assert.True(File.Exists(file));
        Assert.Empty(Directory.GetFileSystemEntries(tree, ".filecat-*"));
    }

    // Plan §11.6.1: a skipped conflicting input is never deleted; the other input moves.
    [Fact]
    public async Task A_conflict_skipped_input_is_never_deleted()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string stored = Path.Combine(root, "stored"); Source(stored, [1]);
        var (manager, _) = MoveManager(root); var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        string target = Path.Combine(root, "moved.tar");
        await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFile, "a", stored)], Profile: profile), null));
        string incoming = Path.Combine(root, "incoming"), other = Path.Combine(root, "other"); Source(incoming, [2]); Source(other, [3]);
        var assessor = new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
        var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
        var job = manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "a", incoming), new(ArchiveChangeKind.AddFile, "b", other)])
            with { MoveSources = true, AddConflictPolicy = ArchiveAddConflictPolicy.Skip }, null);
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(120));
            while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
        }
        string issues = string.Join(" | ", job.Issues.Select(issue => issue.Message));
        Assert.True(job.State is JobState.Completed or JobState.CompletedWithIssues, job.State + ": " + issues);
        Assert.True(File.Exists(incoming), issues); Assert.Equal([2], File.ReadAllBytes(incoming));
        Assert.False(File.Exists(other));
        Assert.Equal(new[] { 1 }, job.CompletedRootIndices); Assert.Equal(new[] { 0 }, job.FailedRootIndices);
        var entries = Entries(target); Assert.Equal("01", entries["a"]); Assert.Equal("03", entries["b"]);
    }

    // A source folder that refuses the removal: the archive is saved, the original stays, and the result says so.
    [Fact]
    public async Task A_source_that_cannot_be_removed_gives_an_honest_partial_result()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string locked = Path.Combine(root, "locked"); string inside = Path.Combine(locked, "kept"); Source(inside, [9, 9]);
        string free = Path.Combine(root, "free"); Source(free, [8]);
        File.SetUnixFileMode(locked, (UnixFileMode)0x140); // r-x for the owner: names inside cannot be renamed or removed.
        try
        {
            var (manager, _) = MoveManager(root); string target = Path.Combine(root, "moved.tar");
            var job = manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFile, "kept", inside), new(ArchiveChangeKind.AddFile, "free", free)],
                Profile: new(ArchiveWriteFormat.Tar)) { MoveSources = true }, null);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(120));
                while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
            }
            string issues = string.Join(" | ", job.Issues.Select(issue => issue.Message));
            Assert.True(job.State == JobState.CompletedWithIssues, job.State + ": " + issues);
            Assert.True(File.Exists(inside)); Assert.False(File.Exists(free));
            Assert.Equal(new[] { 1 }, job.CompletedRootIndices); Assert.Equal(new[] { 0 }, job.FailedRootIndices);
            Assert.Contains(job.Issues, issue => issue.Message.StartsWith("Archive saved; The original could not be set aside for removal", StringComparison.Ordinal));
            Assert.Empty(Directory.GetFileSystemEntries(locked, ".filecat-*"));
            var entries = Entries(target); Assert.Equal("0909", entries["kept"]); Assert.Equal("08", entries["free"]);
        }
        finally { File.SetUnixFileMode(locked, (UnixFileMode)0x1c0); }
    }

    private static Dictionary<string, string?> Entries(string tar)
    {
        var entries = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var reader = new System.Formats.Tar.TarReader(File.OpenRead(tar));
        while (reader.GetNextEntry() is { } entry)
        {
            using var content = new MemoryStream(); entry.DataStream?.CopyTo(content);
            entries.Add(entry.Name, entry.EntryType == System.Formats.Tar.TarEntryType.Directory ? null : Convert.ToHexString(content.ToArray()));
        }
        return entries;
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int HardLink(string existing, string created);
}
