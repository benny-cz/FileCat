using System.IO.Compression;
using System.Text;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ArchiveUpdateTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly JobManager _jobs;

    public ArchiveUpdateTests()
    {
        _providers.Register(new LocalFileSystemProvider());
        _jobs = new JobManager(new PortableFileOperations(), _providers, Path.Combine(_dir.Path, "journal"));
    }

    public void Dispose() => _dir.Dispose();

    private async Task<Job> RunAsync(ArchivePlan plan, IReadOnlyList<ItemRef>? sources = null)
    {
        var job = _jobs.Submit(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan, Sources = sources ?? [] });
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!job.State.IsFinished())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Job still {job.State}");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    private static Dictionary<string, string> Read(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        // Duplicate names are legal in ZIP; the last one wins here.
        return archive.Entries.GroupBy(e => e.FullName).Select(g => g.Last()).ToDictionary(e => e.FullName, e =>
        {
            if (e.FullName.EndsWith('/')) return "<dir>";
            using var reader = new StreamReader(e.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        });
    }

    private string MakeZip(string name, params (string Member, string Text)[] members)
    {
        string zip = Path.Combine(_dir.Path, name);
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        archive.Comment = "archive comment";
        foreach (var (member, text) in members)
        {
            var entry = archive.CreateEntry(member);
            if (member.EndsWith('/')) continue;
            entry.Comment = "about " + member;
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
        return zip;
    }

    [Fact]
    public async Task Pack_creates_an_archive_of_files_and_folders_with_empty_folders_kept()
    {
        string src = _dir.Dir("pack");
        _dir.File("pack/a.txt", "alpha");
        _dir.File("pack/sub/b.txt", "beta");
        _dir.Dir("pack/empty");
        string single = _dir.File("single.txt", "one");
        string zip = Path.Combine(_dir.Path, "new.zip");
        var job = await RunAsync(new ArchivePlan(zip, null,
        [
            new ArchiveChange(ArchiveChangeKind.AddFolder, "pack", src),
            new ArchiveChange(ArchiveChangeKind.AddFile, "single.txt", single),
        ]));
        Assert.Equal(JobState.Completed, job.State);
        var content = Read(zip);
        Assert.Equal("alpha", content["pack/a.txt"]);
        Assert.Equal("beta", content["pack/sub/b.txt"]);
        Assert.Equal("<dir>", content["pack/empty/"]);
        Assert.Equal("one", content["single.txt"]);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*"));

        var again = await RunAsync(new ArchivePlan(zip, null, [new ArchiveChange(ArchiveChangeKind.AddFile, "x.txt", single)]));
        Assert.Equal(JobState.Failed, again.State); // creating never overwrites an existing file
    }

    [Fact]
    public async Task Update_adds_replaces_deletes_renames_and_keeps_everything_else_exactly()
    {
        string zip = MakeZip("update.zip", ("keep.txt", "kept"), ("dup.txt", "first"), ("dup.txt", "second"),
            ("old/one.txt", "1"), ("old/two.txt", "2"), ("gone.txt", "bye"), ("folder/inner.txt", "in"), ("existing.txt", "old"));
        string added = _dir.File("added.txt", "new file");
        string replacement = _dir.File("replacement.txt", "replaced");
        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
        [
            new ArchiveChange(ArchiveChangeKind.AddFile, "folder/added.txt", added),
            new ArchiveChange(ArchiveChangeKind.AddFile, "existing.txt", replacement),
            new ArchiveChange(ArchiveChangeKind.Delete, "gone.txt"),
            new ArchiveChange(ArchiveChangeKind.Rename, "old", NewMemberPath: "renamed"),
            new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "made"),
        ]));
        Assert.Equal(JobState.CompletedWithIssues, job.State); // existing.txt was kept: replacing was not chosen
        using (var archive = ZipFile.OpenRead(zip))
        {
            Assert.Equal("archive comment", archive.Comment);
            Assert.Equal(["keep.txt", "dup.txt", "dup.txt", "renamed/one.txt", "renamed/two.txt", "folder/inner.txt", "existing.txt", "folder/added.txt", "made/"],
                archive.Entries.Select(e => e.FullName));
            Assert.Equal("about keep.txt", archive.Entries[0].Comment);
            string Text(int i) { using var r = new StreamReader(archive.Entries[i].Open()); return r.ReadToEnd(); }
            Assert.Equal("first", Text(1));
            Assert.Equal("second", Text(2));
            Assert.Equal("old", Text(6));
            Assert.Equal("new file", Text(7));
        }

        var replace = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), [new ArchiveChange(ArchiveChangeKind.AddFile, "existing.txt", replacement)],
            ReplaceExisting: true));
        Assert.Equal(JobState.Completed, replace.State);
        Assert.Equal("replaced", Read(zip)["existing.txt"]);
        var editCommit = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), [new ArchiveChange(ArchiveChangeKind.Replace, "keep.txt", added)]));
        Assert.Equal(JobState.Completed, editCommit.State);
        Assert.Equal("new file", Read(zip)["keep.txt"]);
    }

    [Fact]
    public async Task A_changed_archive_or_a_name_collision_is_refused_and_the_original_is_untouched()
    {
        string zip = MakeZip("guard.zip", ("a.txt", "a"), ("b.txt", "b"));
        var baseline = ArchiveBaseline.Of(zip);
        File.SetLastWriteTimeUtc(zip, DateTime.UtcNow.AddMinutes(5)); // another program touched it
        var bytes = File.ReadAllBytes(zip);
        var stale = await RunAsync(new ArchivePlan(zip, baseline, [new ArchiveChange(ArchiveChangeKind.Delete, "a.txt")]));
        Assert.Equal(JobState.Failed, stale.State);
        Assert.Contains(stale.Issues, i => i.Message.Contains("changed after it was opened", StringComparison.Ordinal));
        Assert.Equal(bytes, File.ReadAllBytes(zip));

        var collision = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), [new ArchiveChange(ArchiveChangeKind.Rename, "a.txt", NewMemberPath: "b.txt")]));
        Assert.Equal(JobState.Failed, collision.State);
        Assert.Equal(bytes, File.ReadAllBytes(zip));
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*"));

        var traversal = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), [new ArchiveChange(ArchiveChangeKind.Rename, "a.txt", NewMemberPath: "../evil.txt")]));
        Assert.Equal(JobState.Failed, traversal.State);
        Assert.Equal(bytes, File.ReadAllBytes(zip));
    }

    [Fact]
    public async Task Damaged_or_encrypted_members_stop_an_update_and_the_test_command_names_them()
    {
        string zip = MakeZip("damaged.zip", ("good.txt", "good"), ("bad.txt", new string('x', 4000)));
        // Flip bytes inside bad.txt's compressed data (after its local header and name).
        var bytes = File.ReadAllBytes(zip);
        int local = IndexOf(bytes, "bad.txt"u8.ToArray());
        bytes[local + 7 + 5] ^= 0x55;
        bytes[local + 7 + 6] ^= 0x55;
        File.WriteAllBytes(zip, bytes);
        var update = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), [new ArchiveChange(ArchiveChangeKind.Delete, "good.txt")]));
        Assert.Equal(JobState.Failed, update.State);
        Assert.Equal(bytes, File.ReadAllBytes(zip));

        var test = _jobs.Submit(new JobRequest { Kind = JobKind.ArchiveTest, Sources = [ItemRef.ForFileSystemPath(zip, EntryKind.File)] });
        while (!test.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Contains(test.Issues, i => i.Path.Contains("bad.txt", StringComparison.Ordinal) && i.Severity == IssueSeverity.Error);
        Assert.Contains(test.Issues, i => i.Message.StartsWith("1 members intact", StringComparison.Ordinal));

        string locked = MakeZip("encrypted.zip", ("secret.txt", "s"));
        var raw = File.ReadAllBytes(locked);
        int central = IndexOf(raw, [0x50, 0x4B, 0x01, 0x02]);
        raw[central + 8] |= 1; // general-purpose flag bit 0: encrypted
        File.WriteAllBytes(locked, raw);
        var refused = await RunAsync(new ArchivePlan(locked, ArchiveBaseline.Of(locked), [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "x")]));
        Assert.Equal(JobState.Failed, refused.State);
        Assert.Contains(refused.Issues, i => i.Message.Contains("encrypted", StringComparison.Ordinal));
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        throw new InvalidOperationException("Pattern not found.");
    }

    [Fact]
    public async Task Large_archives_plan_in_linear_time()
    {
        string zip = Path.Combine(_dir.Path, "large.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            for (int i = 0; i < 20_000; i++)
            {
                using var writer = new StreamWriter(archive.CreateEntry($"d{i % 100}/f{i}.txt", CompressionLevel.NoCompression).Open());
                writer.Write(i);
            }
        var changes = Enumerable.Range(50, 50).Select(d => new ArchiveChange(ArchiveChangeKind.Delete, $"d{d}"))
            .Append(new ArchiveChange(ArchiveChangeKind.Rename, "d1", NewMemberPath: "renamed")).ToList();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), changes, CompressionLevel.Fastest));
        Assert.Equal(JobState.Completed, job.State);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"took {clock.Elapsed}");
        using var result = ZipFile.OpenRead(zip);
        Assert.Equal(10_000, result.Entries.Count);
        Assert.Equal(200, result.Entries.Count(e => e.FullName.StartsWith("renamed/", StringComparison.Ordinal)));
        Assert.DoesNotContain(result.Entries, e => e.FullName.StartsWith("d1/", StringComparison.Ordinal) || e.FullName.StartsWith("d77/", StringComparison.Ordinal));
    }

    [Fact]
    public void Member_paths_are_checked()
    {
        Assert.Null(ArchivePaths.Problem("a/b.txt"));
        Assert.NotNull(ArchivePaths.Problem("/abs.txt"));
        Assert.NotNull(ArchivePaths.Problem("a/../b"));
        Assert.NotNull(ArchivePaths.Problem(@"a\b"));
        Assert.NotNull(ArchivePaths.Problem("C:/x"));
        Assert.NotNull(ArchivePaths.Problem("a//b"));
        Assert.NotNull(ArchivePaths.Problem("bad|name"));
    }
}
