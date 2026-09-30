using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsFileOperationsTests : IDisposable
{
    private readonly string _root;
    private readonly WindowsFileOperations _ops = new();

    public WindowsFileOperationsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "filecat-wintests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void CopyFile2_reports_progress_and_refuses_to_overwrite()
    {
        if (!OperatingSystem.IsWindows()) return;
        var src = Path.Combine(_root, "src.bin");
        File.WriteAllBytes(src, new byte[3 * 1024 * 1024 + 17]);
        var dst = Path.Combine(_root, "dst.bin");
        long last = 0;
        _ops.CopyFile(src, dst, new FileCopyOptions(), (done, total) =>
        {
            last = done;
            return CopyProgressAction.Continue;
        }, CancellationToken.None);
        Assert.Equal(new FileInfo(src).Length, new FileInfo(dst).Length);
        Assert.Equal(new FileInfo(src).Length, last);
        Assert.ThrowsAny<IOException>(() => _ops.CopyFile(src, dst, new FileCopyOptions(), null, CancellationToken.None));
    }

    [Fact]
    public void CopyFile2_cancellation_throws_and_removes_partial_destination()
    {
        if (!OperatingSystem.IsWindows()) return;
        var src = Path.Combine(_root, "big.bin");
        using (var fs = new FileStream(src, FileMode.Create)) fs.SetLength(64L * 1024 * 1024);
        var dst = Path.Combine(_root, "big-copy.bin");
        Assert.Throws<OperationCanceledException>(() =>
            _ops.CopyFile(src, dst, new FileCopyOptions(), (done, _) => done > 0 ? CopyProgressAction.Cancel : CopyProgressAction.Continue, CancellationToken.None));
        Assert.False(File.Exists(dst));
    }

    [Fact]
    public void Shortcuts_made_by_windows_yield_their_raw_targets()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Directory.CreateDirectory(Path.Combine(_root, "target folder ž")).FullName;
        var file = Path.Combine(folder, "doc.txt");
        File.WriteAllText(file, "x");
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) Assert.Skip("Windows Script Host is disabled by policy here.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        foreach (var (name, target) in new[] { ("folder.lnk", folder), ("file.lnk", file) })
        {
            dynamic shortcut = shell.CreateShortcut(Path.Combine(_root, name));
            shortcut.TargetPath = target;
            shortcut.Save();
        }
        // Windows stores the target with the file system's own casing ("C:\Windows" for "C:\WINDOWS\Temp").
        Assert.True(FileCat.Core.FileSystem.ShellLinkReader.TryRead(Path.Combine(_root, "folder.lnk"), out var toFolder));
        Assert.Equal(folder, toFolder!.Path, ignoreCase: true);
        Assert.True(toFolder.IsDirectory);
        Assert.True(FileCat.Core.FileSystem.ShellLinkReader.TryRead(Path.Combine(_root, "file.lnk"), out var toFile));
        Assert.Equal(file, toFile!.Path, ignoreCase: true);
        Assert.False(toFile.IsDirectory);
    }

    [Fact]
    public void Flushed_copies_work_for_read_only_files_with_streams()
    {
        if (!OperatingSystem.IsWindows()) return;
        var src = Path.Combine(_root, "ro.bin");
        var data = new byte[2 * 1024 * 1024 + 5];
        new Random(7).NextBytes(data);
        File.WriteAllBytes(src, data);
        File.WriteAllText(src + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        File.SetAttributes(src, FileAttributes.ReadOnly);
        try
        {
            // The flush uses the copy engine's own write handle: a read-only file could not be reopened for it.
            var staged = Path.Combine(_root, "ro.staged");
            _ops.CopyFile(src, staged, new FileCopyOptions { FlushDestination = true }, null, CancellationToken.None);
            var final = Path.Combine(_root, "ro-copy.bin");
            _ops.Move(staged, final, replaceExisting: false, writeThrough: true);
            Assert.Equal(data, File.ReadAllBytes(final));
            Assert.True(File.GetAttributes(final).HasFlag(FileAttributes.ReadOnly));
            Assert.Contains("Zone.Identifier", _ops.GetAlternateStreams(final));
            File.SetAttributes(final, FileAttributes.Normal);
        }
        finally
        {
            File.SetAttributes(src, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Move_publishes_and_replaces_only_when_asked()
    {
        if (!OperatingSystem.IsWindows()) return;
        var a = Path.Combine(_root, "a.txt");
        var b = Path.Combine(_root, "b.txt");
        File.WriteAllText(a, "new");
        File.WriteAllText(b, "old");
        Assert.ThrowsAny<IOException>(() => _ops.Move(a, b, replaceExisting: false));
        _ops.Move(a, b, replaceExisting: true);
        Assert.Equal("new", File.ReadAllText(b));
        Assert.False(File.Exists(a));
    }

    [Fact]
    public void Junction_removal_never_touches_the_target()
    {
        if (!OperatingSystem.IsWindows()) return;
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        var link = Path.Combine(_root, "junction");
        Junction.Create(link, target);
        Assert.True(File.Exists(Path.Combine(link, "keep.txt")));
        var info = _ops.TryGetInfo(link);
        Assert.NotNull(info);
        Assert.True(info!.IsLink);
        _ops.DeleteDirectory(link);
        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public async Task Junctions_and_hard_links_are_created_and_undone_only_when_provably_safe()
    {
        if (!OperatingSystem.IsWindows()) return;
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var manager = new JobManager(_ops, providers, Path.Combine(_root, "journal-links"));
        var target = Directory.CreateDirectory(Path.Combine(_root, "t")).FullName;
        File.WriteAllText(Path.Combine(target, "inner.txt"), "inner");
        var original = Path.Combine(_root, "original.txt");
        File.WriteAllText(original, "data");
        var links = Directory.CreateDirectory(Path.Combine(_root, "links")).FullName;
        async Task<Job> Run(ItemRef item, LinkKind kind)
        {
            var job = manager.Submit(new JobRequest { Kind = JobKind.CreateLink, Sources = [item], Destination = Location.FileSystem(links), Link = new Core.Operations.LinkOptions(kind) });
            while (!job.State.IsFinished()) await Task.Delay(10);
            return job;
        }

        var junction = await Run(ItemRef.ForFileSystemPath(target, EntryKind.Directory), LinkKind.Junction);
        Assert.Equal(JobState.Completed, junction.State);
        Assert.Equal("inner", File.ReadAllText(Path.Combine(links, "t", "inner.txt")));
        Assert.True(_ops.TryGetInfo(Path.Combine(links, "t"))!.IsLink);
        Assert.Contains(UndoService.Undo(junction, _ops), l => l.StartsWith("Removed the created link", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(links, "t")));
        Assert.Equal("inner", File.ReadAllText(Path.Combine(target, "inner.txt")));

        var hard = await Run(ItemRef.ForFileSystemPath(original, EntryKind.File), LinkKind.Hard);
        Assert.Equal(JobState.Completed, hard.State);
        string link = Path.Combine(links, "original.txt");
        Assert.NotNull(_ops.GetFileIdentity(original));
        Assert.Equal(_ops.GetFileIdentity(original), _ops.GetFileIdentity(link));
        // While the original name is gone, the link holds the last name of the data: undo keeps it.
        File.Move(original, original + ".away");
        Assert.Contains(UndoService.Undo(hard, _ops), l => l.StartsWith("Kept", StringComparison.Ordinal));
        Assert.True(File.Exists(link));
        File.Move(original + ".away", original);
        Assert.Contains(UndoService.Undo(hard, _ops), l => l.StartsWith("Removed the created hard link", StringComparison.Ordinal));
        Assert.False(File.Exists(link));
        Assert.Equal("data", File.ReadAllText(original));

        // A junction never adopts an existing folder.
        var existing = Directory.CreateDirectory(Path.Combine(_root, "existing")).FullName;
        Assert.ThrowsAny<IOException>(() => Junction.Create(existing, target));
        Assert.False(_ops.TryGetInfo(existing)!.IsLink);
    }

    [Fact]
    public void Volume_profile_and_recycle_classification()
    {
        if (!OperatingSystem.IsWindows()) return;
        var vol = _ops.GetVolumeInfo(_root);
        Assert.False(string.IsNullOrEmpty(vol.FileSystem));
        Assert.Equal(RecycleClassification.NoRecycleBin, _ops.ClassifyRecycle(@"\\server\share\file.txt", 10));
        Assert.Equal(TimeSpan.FromSeconds(2), new WindowsFileOperations().GetVolumeInfo(@"\\nonexistent-server-xyz\share").TimestampPrecision);
    }

    [Fact]
    public void Mark_of_the_web_round_trips_on_ntfs()
    {
        if (!OperatingSystem.IsWindows()) return;
        var f = Path.Combine(_root, "download.exe");
        File.WriteAllText(f, "x");
        if (!_ops.GetVolumeInfo(f).SupportsNamedStreams) Assert.Skip("The test folder's volume has no named streams.");
        var modified = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(f, modified);
        Assert.True(_ops.WriteOriginMark(f, "[ZoneTransfer]\r\nZoneId=3\r\n"));
        Assert.Contains("ZoneId=3", _ops.ReadOriginMark(f));
        // Writing the mark's stream would stamp the file as modified now; the file keeps its own time.
        Assert.Equal(modified, File.GetLastWriteTimeUtc(f));
    }

    [Fact]
    public async Task Files_extracted_from_a_downloaded_zip_keep_their_times_and_the_mark()
    {
        if (!OperatingSystem.IsWindows()) return;
        var zip = Path.Combine(_root, "download.zip");
        // ZIP stores the local wall-clock time, as archivers do.
        var when = new DateTimeOffset(new DateTime(2019, 5, 6, 7, 8, 10, DateTimeKind.Local));
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("readme.txt");
            entry.LastWriteTime = when;
            using var writer = new StreamWriter(entry.Open());
            writer.Write("hello");
        }
        if (!_ops.GetVolumeInfo(zip).SupportsNamedStreams) Assert.Skip("The test folder's volume has no named streams.");
        File.WriteAllText(zip + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        var providers = new ProviderRegistry();
        providers.Register(new WindowsFileSystemProvider());
        var zipProvider = new Core.Archives.ZipProvider(Path.Combine(_root, "spool"));
        providers.Register(zipProvider);
        var jobs = new JobManager(_ops, providers, Path.Combine(_root, "journal"));
        var output = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        var root = Core.Archives.ZipProvider.ForFile(zip);
        var listed = new List<EntryData>();
        await zipProvider.EnumerateAsync(root, new ListSink(listed), TestContext.Current.CancellationToken);
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [zipProvider.GetItemRef(root, Assert.Single(listed))],
            Destination = Location.FileSystem(output),
        });
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(JobState.Completed, job.State);
        var extracted = Path.Combine(output, "readme.txt");
        Assert.Contains("ZoneId=3", _ops.ReadOriginMark(extracted));
        Assert.Equal(when.UtcDateTime, File.GetLastWriteTimeUtc(extracted), TimeSpan.FromSeconds(2)); // ZIP keeps 2-second times
    }

    private sealed class ListSink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries) => list.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }

    [Fact]
    public void Recycle_reports_outcome_and_restore_uses_the_bin_item()
    {
        if (!OperatingSystem.IsWindows() || !WindowsFileOperations.RecycleBinExists(_root)) Assert.Skip("The test folder's volume has no Recycle Bin.");
        var f = Path.Combine(_root, $"filecat-recycle-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(f, "recycle me");
        var result = Assert.Single(_ops.Recycle([f], null, CancellationToken.None));
        Assert.Equal(RecycleOutcome.Recycled, result.Outcome);
        Assert.False(File.Exists(f));
        Assert.NotNull(result.RecycledId);
        Assert.True(_ops.TryRestoreRecycled(result.RecycledId!, f, out var error), error);
        Assert.Equal("recycle me", File.ReadAllText(f));
        File.Delete(f);
    }

    [Fact]
    public async Task Job_engine_runs_on_native_operations()
    {
        if (!OperatingSystem.IsWindows()) return;
        var providers = new ProviderRegistry();
        providers.Register(new WindowsFileSystemProvider());
        var manager = new JobManager(_ops, providers, Path.Combine(_root, "journal"));
        var src = Directory.CreateDirectory(Path.Combine(_root, "s")).FullName;
        var dst = Directory.CreateDirectory(Path.Combine(_root, "d")).FullName;
        File.WriteAllText(Path.Combine(src, "one.txt"), "1");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "sub", "two.txt"), "2");
        var job = manager.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(src, EntryKind.Directory)],
            Destination = Location.FileSystem(dst),
            Options = new TransferOptions { Verify = VerifyMode.ReadBack },
        });
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("2", File.ReadAllText(Path.Combine(dst, "s", "sub", "two.txt")));
    }

    [Fact]
    public void Alternate_data_streams_are_listed_without_the_default_stream()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_root, "streams.txt");
        File.WriteAllText(path, "main");
        Assert.Empty(_ops.GetAlternateStreams(path));
        File.WriteAllText(path + ":extra", "x");
        File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        var streams = _ops.GetAlternateStreams(path).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Zone.Identifier", "extra"], streams);
        Assert.Empty(_ops.GetAlternateStreams(Path.Combine(_root, "missing.txt")));
    }
}
