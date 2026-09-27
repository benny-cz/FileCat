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
        if (!_ops.GetVolumeInfo(f).SupportsNamedStreams) return;
        Assert.True(_ops.WriteOriginMark(f, "[ZoneTransfer]\r\nZoneId=3\r\n"));
        Assert.Contains("ZoneId=3", _ops.ReadOriginMark(f));
    }

    [Fact]
    public void Recycle_reports_outcome_and_restore_uses_the_bin_item()
    {
        if (!OperatingSystem.IsWindows() || !WindowsFileOperations.RecycleBinExists(_root)) return;
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
        while (!job.State.IsFinished()) await Task.Delay(10);
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
