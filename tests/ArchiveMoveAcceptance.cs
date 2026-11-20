using System.IO.Compression;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

/// <summary>Runs the same real-file move cases against each platform's actual file-operations adapter.</summary>
public abstract class ArchiveMoveAcceptance
{
    protected abstract IFileSystemOperations NativeFiles();

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Case_distinct_sources_are_separately_archived_removed_and_accounted(bool parents, bool differentBytes)
    {
        using var f = new Fixture(NativeFiles());
        string? template = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_CASE_ROOT") ?? (OperatingSystem.IsWindows() ? null : f.Root);
        if (template is null) Assert.Skip("Explicit case-sensitive NTFS source fixture.");
        string source = Path.Combine(template!, Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(source);
        try
        {
            // macOS volumes are case-insensitive by default: "Case" and "case" would be one file there.
            File.WriteAllText(Path.Combine(source, "CaseProbe"), "");
            bool insensitive = File.Exists(Path.Combine(source, "caseprobe")); File.Delete(Path.Combine(source, "CaseProbe"));
            if (insensitive) Assert.Skip("This case needs a case-sensitive source folder.");
            string first = Path.Combine(source, parents ? "Root/payload" : "Case");
            string second = Path.Combine(source, parents ? "root/payload" : "case");
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(first)!); System.IO.Directory.CreateDirectory(Path.GetDirectoryName(second)!);
            File.WriteAllText(first, "first"); File.WriteAllText(second, differentBytes ? "second" : "first");
            Assert.NotEqual(f.Files.GetFileIdentity(first), f.Files.GetFileIdentity(second));
            var job = await f.Run([new(ArchiveChangeKind.AddFile, "first", first), new(ArchiveChangeKind.AddFile, "second", second)]);
            Assert.Equal(JobState.Completed, job.State);
            Assert.False(File.Exists(first)); Assert.False(File.Exists(second));
            Assert.Equal("first", f.Read("first")); Assert.Equal(differentBytes ? "second" : "first", f.Read("second"));
            Assert.Equal(new[] { 0, 1 }, job.CompletedRootIndices); Assert.Empty(job.FailedRootIndices);
        }
        finally { System.IO.Directory.Delete(source, true); }
    }

    [Fact]
    public async Task Verified_file_and_folder_moves_remove_originals_and_keep_complete_archive_bytes()
    {
        using var f = new Fixture(NativeFiles()); string file = f.File("single", "one");
        string folder = f.Directory("tree"); f.File("tree/sub/data", "two"); f.Directory("tree/empty");
        var job = await f.Run([new(ArchiveChangeKind.AddFile, "single", file), new(ArchiveChangeKind.AddFolder, "tree", folder)]);
        Assert.Equal(JobState.Completed, job.State); Assert.False(File.Exists(file)); Assert.False(System.IO.Directory.Exists(folder));
        Assert.Equal("one", f.Read("single")); Assert.Equal("two", f.Read("tree/sub/data")); Assert.Equal("kept", f.Read("kept"));
        using var zip = ZipFile.OpenRead(f.Archive); Assert.NotNull(zip.GetEntry("tree/empty/")); Assert.NotNull(zip.GetEntry("tree/sub/"));
        Assert.Equal(new[] { 0, 1 }, job.CompletedRootIndices); Assert.Empty(job.FailedRootIndices); Assert.Contains("Archive saved", job.Summary);
        Assert.True(f.Files.Removed > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_names_are_skipped_or_replaced_without_deleting_a_skipped_original(bool replace)
    {
        using var f = new Fixture(NativeFiles()); string source = f.File("incoming", "new");
        var job = await f.Run([new(ArchiveChangeKind.AddFile, "kept", source)], replace);
        Assert.Equal(replace ? JobState.Completed : JobState.CompletedWithIssues, job.State);
        Assert.Equal(!replace, File.Exists(source)); Assert.Equal(replace ? "new" : "kept", f.Read("kept"));
        Assert.Equal(replace ? new[] { 0 } : Array.Empty<int>(), job.CompletedRootIndices);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_content_or_same_byte_replacement_after_publication_keeps_the_current_source(bool replacement)
    {
        using var f = new Fixture(NativeFiles()); string source = f.File("incoming", "before"); DateTime time = File.GetLastWriteTimeUtc(source);
        f.Files.AfterPublication = _ =>
        {
            if (replacement)
            {
                string next = f.File("next", "before"); File.Move(source, f.Root + "/retained-original"); File.Move(next, source);
            }
            else File.WriteAllText(source, "after!");
            File.SetLastWriteTimeUtc(source, time);
        };
        var job = await f.Run([new(ArchiveChangeKind.AddFile, "incoming", source)]);
        Assert.Equal(JobState.CompletedWithIssues, job.State); Assert.True(File.Exists(source)); Assert.Equal("before", f.Read("incoming"));
        Assert.Equal(replacement ? "before" : "after!", File.ReadAllText(source)); Assert.Empty(job.CompletedRootIndices);
    }

    [Fact]
    public async Task A_new_child_after_publication_keeps_the_folder_and_moves_only_captured_files()
    {
        using var f = new Fixture(NativeFiles()); string folder = f.Directory("tree"); string source = f.File("tree/old", "captured");
        f.Files.AfterPublication = _ => f.File("tree/new", "later");
        var job = await f.Run([new(ArchiveChangeKind.AddFolder, "tree", folder)]);
        Assert.Equal(JobState.CompletedWithIssues, job.State); Assert.False(File.Exists(source)); Assert.True(System.IO.Directory.Exists(folder));
        Assert.Equal("later", File.ReadAllText(folder + "/new")); Assert.Equal("captured", f.Read("tree/old")); Assert.Empty(job.CompletedRootIndices);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_before_or_after_publication_keeps_sources_and_reports_the_actual_archive_result(bool published)
    {
        using var f = new Fixture(NativeFiles()); string source = f.File("incoming", "owned"); byte[] before = File.ReadAllBytes(f.Archive);
        if (published) f.Files.AfterPublication = j => j.Cancel(); else f.Files.BeforeEncoding = j => j.Cancel();
        var job = await f.Run([new(ArchiveChangeKind.AddFile, "incoming", source)]);
        Assert.Equal(JobState.Canceled, job.State); Assert.Equal("owned", File.ReadAllText(source)); Assert.Empty(job.CompletedRootIndices);
        if (published) { Assert.Equal("owned", f.Read("incoming")); Assert.Contains("Archive saved", job.Summary); }
        else Assert.Equal(before, File.ReadAllBytes(f.Archive));
    }

    [Fact]
    public async Task A_source_deletion_failure_is_a_partial_move_after_a_successfully_saved_archive()
    {
        using var f = new Fixture(NativeFiles()); string source = f.File("incoming", "owned"); f.Files.RefuseRemoval = source;
        var job = await f.Run([new(ArchiveChangeKind.AddFile, "incoming", source)]);
        Assert.Equal(JobState.CompletedWithIssues, job.State); Assert.Equal("owned", File.ReadAllText(source)); Assert.Equal("owned", f.Read("incoming"));
        Assert.Contains("Archive saved", job.Summary); Assert.Equal(new[] { 0 }, job.FailedRootIndices);
    }

    [Fact]
    public async Task A_published_archive_changed_before_removal_never_authorizes_source_deletion()
    {
        using var f = new Fixture(NativeFiles()); string source = f.File("incoming", "owned");
        f.Files.AfterPublication = _ => File.WriteAllText(f.Archive, "external replacement");
        var job = await f.Run([new(ArchiveChangeKind.AddFile, "incoming", source)]);
        Assert.Equal(JobState.CompletedWithIssues, job.State); Assert.Equal("owned", File.ReadAllText(source));
        Assert.Equal("external replacement", File.ReadAllText(f.Archive)); Assert.Equal(0, f.Files.Removed);
    }

    [Fact]
    public async Task An_archive_inside_a_selected_source_folder_is_refused_before_any_change()
    {
        using var f = new Fixture(NativeFiles()); byte[] before = File.ReadAllBytes(f.Archive);
        var job = await f.Run([new(ArchiveChangeKind.AddFolder, "all", f.Root)]);
        Assert.Equal(JobState.Failed, job.State); Assert.Equal(before, File.ReadAllBytes(f.Archive)); Assert.Equal(0, f.Files.Removed);
    }

    [Fact]
    public async Task Replacement_of_duplicate_members_keeps_no_later_old_copy_hiding_the_moved_data()
    {
        using var f = new Fixture(NativeFiles());
        using (var zip = ZipFile.Open(f.Archive, ZipArchiveMode.Update))
        { using var writer = new StreamWriter(zip.CreateEntry("kept").Open()); writer.Write("later old"); }
        string source = f.File("incoming", "new");
        var job = await f.Run([new(ArchiveChangeKind.AddFile, "kept", source)], replace: true);
        Assert.Equal(JobState.Completed, job.State); Assert.False(File.Exists(source));
        using var result = ZipFile.OpenRead(f.Archive);
        Assert.Equal(2, result.Entries.Count(e => e.FullName == "kept"));
        foreach (var entry in result.Entries.Where(e => e.FullName == "kept"))
        { using var reader = new StreamReader(entry.Open()); Assert.Equal("new", reader.ReadToEnd()); }
    }

    [Fact]
    public async Task A_new_file_cannot_hide_an_existing_implicit_archive_folder()
    {
        using var f = new Fixture(NativeFiles()); string source = f.File("incoming", "owned");
        using (var zip = ZipFile.Open(f.Archive, ZipArchiveMode.Update))
        { using var writer = new StreamWriter(zip.CreateEntry("tree/kept").Open()); writer.Write("nested"); }
        byte[] before = File.ReadAllBytes(f.Archive);
        var job = await f.Run([new(ArchiveChangeKind.AddFile, "tree", source)]);
        Assert.Equal(JobState.Failed, job.State); Assert.Equal(before, File.ReadAllBytes(f.Archive));
        Assert.Equal("owned", File.ReadAllText(source)); Assert.Equal(0, f.Files.Removed);
    }

    [Fact]
    public async Task Ordinary_add_is_still_a_copy_and_does_not_remove_its_source()
    {
        using var f = new Fixture(NativeFiles()); string source = f.File("incoming", "owned");
        var job = await f.Run([new(ArchiveChangeKind.AddFile, "incoming", source)], move: false);
        Assert.Equal(JobState.Completed, job.State); Assert.True(File.Exists(source)); Assert.Equal("owned", f.Read("incoming"));
        Assert.Equal(0, f.Files.Removed);
    }

    [Fact]
    public async Task A_download_mark_survives_the_move_on_the_archive_and_the_original_is_removed()
    {
        var native = NativeFiles(); using var f = new Fixture(native); string source = f.File("incoming", "download");
        const string mark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.invalid/owned-input\r\n";
        Assert.True(native.WriteOriginMark(source, mark));
        var job = await f.Run([new(ArchiveChangeKind.AddFile, "incoming", source)]);
        Assert.Equal(JobState.Completed, job.State); Assert.False(File.Exists(source));
        Assert.NotNull(native.ReadOriginMark(f.Archive)); Assert.Contains("ZoneId=3", native.ReadOriginMark(f.Archive));
    }

    [Fact]
    public async Task Windows_extra_streams_are_kept_with_the_source_instead_of_silently_lost()
    {
        var native = NativeFiles(); if (!OperatingSystem.IsWindows()) Assert.Skip("Actual Windows alternate stream case.");
        using var f = new Fixture(native); string source = f.File("incoming", "primary");
        File.WriteAllText(source + ":owned-notes", "extra bytes");
        var job = await f.Run([new(ArchiveChangeKind.AddFile, "incoming", source)]);
        Assert.Equal(JobState.CompletedWithIssues, job.State); Assert.Equal("primary", f.Read("incoming"));
        Assert.Equal("extra bytes", File.ReadAllText(source + ":owned-notes")); Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task Unix_file_and_directory_modes_are_represented_in_the_moved_entries()
    {
        var native = NativeFiles(); if (OperatingSystem.IsWindows()) Assert.Skip("Actual Unix permission bits.");
        using var f = new Fixture(native); string folder = f.Directory("tree"); string source = f.File("tree/run", "owned");
        File.SetUnixFileMode(source, (UnixFileMode)0x1e8); File.SetUnixFileMode(folder, (UnixFileMode)0x1e8);
        var job = await f.Run([new(ArchiveChangeKind.AddFolder, "tree", folder)]);
        Assert.Equal(JobState.Completed, job.State);
        using var zip = ZipFile.OpenRead(f.Archive);
        Assert.Equal(0x81e8, (zip.GetEntry("tree/run")!.ExternalAttributes >> 16) & 0xffff);
        Assert.Equal(0x41e8, (zip.GetEntry("tree/")!.ExternalAttributes >> 16) & 0xffff);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_archive_folders_merge_and_the_completed_source_tree_is_removed(bool replace)
    {
        using var f = new Fixture(NativeFiles()); string folder = f.Directory("tree"); f.File("tree/new", "new");
        using (var zip = ZipFile.Open(f.Archive, ZipArchiveMode.Update)) zip.CreateEntry("tree/");
        var job = await f.Run([new(ArchiveChangeKind.AddFolder, "tree", folder)], replace);
        Assert.Equal(JobState.Completed, job.State); Assert.False(System.IO.Directory.Exists(folder));
        Assert.Equal("new", f.Read("tree/new")); Assert.Equal(new[] { 0 }, job.CompletedRootIndices);
    }

    [Fact]
    public async Task A_failed_child_removal_cannot_complete_its_parent_even_if_an_external_actor_removes_the_child()
    {
        using var f = new Fixture(NativeFiles()); string folder = f.Directory("tree"); string source = f.File("tree/owned", "owned");
        f.Files.RefuseRemoval = source; f.Files.DisappearOnRefusal = true;
        var job = await f.Run([new(ArchiveChangeKind.AddFolder, "tree", folder)]);
        Assert.Equal(JobState.CompletedWithIssues, job.State); Assert.True(System.IO.Directory.Exists(folder));
        Assert.Empty(job.CompletedRootIndices); Assert.Equal(new[] { 0 }, job.FailedRootIndices); Assert.Equal("owned", f.Read("tree/owned"));
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string Root, Archive;
        internal readonly Hooks Files;
        private readonly ProviderRegistry providers = new();
        private readonly JobManager jobs;
        internal Fixture(IFileSystemOperations native)
        {
            Root = Path.Combine(Path.GetTempPath(), "filecat-archive-move-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Root); Archive = Path.Combine(Root, "owned.zip");
            using (var zip = ZipFile.Open(Archive, ZipArchiveMode.Create))
            { using var writer = new StreamWriter(zip.CreateEntry("kept").Open()); writer.Write("kept"); }
            Files = new Hooks(native, Archive); providers.Register(new LocalFileSystemProvider());
            jobs = new JobManager(Files, providers, Path.Combine(Root, "journal"));
        }
        internal string File(string name, string text)
        { string path = Path.Combine(Root, name); System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!); System.IO.File.WriteAllText(path, text); return path; }
        internal string Directory(string name) => System.IO.Directory.CreateDirectory(Path.Combine(Root, name)).FullName;
        internal string Read(string member)
        { using var zip = ZipFile.OpenRead(Archive); using var reader = new StreamReader(zip.GetEntry(member)!.Open()); return reader.ReadToEnd(); }
        internal async Task<Job> Run(ArchiveChange[] changes, bool replace = false, bool move = true)
        {
            Files.Ready.Reset();
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.ArchiveUpdate,
                Archive = new(Archive, ArchiveBaseline.Of(Archive), changes, ReplaceExisting: replace) { MoveSources = move },
                Sources = changes.Select(c => ItemRef.ForFileSystemPath(c.SourcePath!, c.Kind == ArchiveChangeKind.AddFolder ? EntryKind.Directory : EntryKind.File)).ToArray(),
            });
            Files.Job = job; Files.Ready.Set();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!job.State.IsFinished() && clock.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.True(job.State.IsFinished(), "Owned move did not finish.");
            TestContext.Current.TestOutputHelper?.WriteLine(job.State + ": " + job.Summary + " " + JobIssue.Summarize(job.Issues));
            return job;
        }
        public void Dispose()
        {
            foreach (var job in jobs.Jobs.Where(j => !j.State.IsFinished())) job.Cancel();
            Files.Ready.Set();
            // This is the exact unique fixture root, never a source selected from outside the fixture.
            string full = Path.GetFullPath(Root);
            if (Path.GetDirectoryName(full) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) ||
                !Path.GetFileName(full).StartsWith("filecat-archive-move-", StringComparison.Ordinal)) throw new IOException("Unexpected fixture cleanup root.");
            System.IO.Directory.Delete(full, recursive: true); Files.Ready.Dispose();
        }
    }

    private sealed class Hooks(IFileSystemOperations inner, string archive) : PortableFileOperations
    {
        internal readonly ManualResetEventSlim Ready = new();
        internal Job? Job;
        internal Action<Job>? AfterPublication, BeforeEncoding;
        internal string? RefuseRemoval;
        internal bool DisappearOnRefusal;
        internal int Removed;
        public override FileSystemItemInfo? TryGetInfo(string path) => inner.TryGetInfo(path);
        public override string? GetFileIdentity(string path) => inner.GetFileIdentity(path);
        public override string? GetOpenedFileIdentity(SafeFileHandle file) => inner.GetOpenedFileIdentity(file);
        public override string? GetFinalPath(string path) => inner.GetFinalPath(path);
        public override string? ReadOriginMark(string path) => inner.ReadOriginMark(path);
        public override bool WriteOriginMark(string path, string mark) => inner.WriteOriginMark(path, mark);
        public override IReadOnlyList<string> GetAlternateStreams(string path) => inner.GetAlternateStreams(path);
        public override IDisposable HoldRegularFile(string path, string? root = null)
        {
            Ready.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            if (PathUtil.SafetyComparer.Equals(path, archive)) { var action = AfterPublication; AfterPublication = null; action?.Invoke(Job!); }
            else { var action = BeforeEncoding; BeforeEncoding = null; action?.Invoke(Job!); }
            return inner.HoldRegularFile(path, root);
        }
        public override IDisposable HoldDirectory(string path, string? root = null) => inner.HoldDirectory(path, root);
        public override BoundDeletion? DeleteReviewedFile(string path, string? identity, string hash, long length, CancellationToken ct)
        {
            if (RefuseRemoval is { } refused && PathUtil.SafetyComparer.Equals(Path.GetFullPath(path), Path.GetFullPath(refused)))
            { if (DisappearOnRefusal) File.Delete(path); return new(false, false, "Owned source deletion refusal."); }
            var result = inner.DeleteReviewedFile(path, identity, hash, length, ct); if (result?.Deleted == true) Removed++; return result;
        }
        public override BoundTreeDeletion? DeleteReviewedTree(string path, IReadOnlyList<BoundDeletionEntry> reviewed, Action checkpoint, CancellationToken ct)
        { var result = inner.DeleteReviewedTree(path, reviewed, checkpoint, ct); if (result is not null) Removed += result.Done; return result; }
        public override void DeleteFile(string path) { inner.DeleteFile(path); Removed++; }
        public override void DeleteDirectory(string path) { inner.DeleteDirectory(path); Removed++; }
    }
}
