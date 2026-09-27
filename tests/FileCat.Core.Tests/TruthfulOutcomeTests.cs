using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// P3 exit: full, disconnected, locked, blocked, racing, and unreadable resources end with truthful outcomes.
/// Failures are injected at the file-operation boundary with the Win32 codes Windows reports.
/// </summary>
public sealed class TruthfulOutcomeTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>Fails copies of files whose name contains <paramref name="marker"/> with a Win32 error code.</summary>
    private sealed class FaultyOperations(string marker, int win32Code) : PortableFileOperations
    {
        public int Attempts;

        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
        {
            if (Path.GetFileName(source).Contains(marker, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref Attempts);
                throw new IOException("injected failure", unchecked((int)0x80070000) | win32Code);
            }
            base.CopyFile(source, destination, options, progress, ct);
        }
    }

    private async Task<(Job Job, List<DecisionRequest> Asked)> CopyAsync(IFileSystemOperations fs, IEnumerable<string> files, string dest, DecisionAction answer)
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(fs, providers, Path.Combine(_dir.Path, "journal-" + Guid.NewGuid().ToString("N")));
        var asked = new List<DecisionRequest>();
        jobs.DecisionRequested += d =>
        {
            lock (asked) asked.Add(d.Request);
            d.Resolve(new Decision(answer));
        };
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = files.Select(f => ItemRef.ForFileSystemPath(f, EntryKind.File)).ToList(),
            Destination = Location.FileSystem(dest),
        });
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!job.State.IsFinished())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(job.State.ToString());
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return (job, asked);
    }

    [Theory]
    [InlineData(112, "diskfull", "not enough free space")]
    [InlineData(64, "offline", "no longer reachable")]
    [InlineData(225, "blocked", "Windows Security blocked")]
    [InlineData(21, "device", "not ready or was disconnected")]
    [InlineData(5, "access", "Access is denied")]
    [InlineData(19, "writeprotect", "write-protected")]
    [InlineData(389, "cloud", "cloud storage provider")]
    public async Task Failures_are_asked_classified_and_reported_while_other_files_complete(int code, string errorClass, string text)
    {
        var src = _dir.Dir("src");
        var dst = _dir.Dir("dst");
        var good = Path.Combine(src, "good.txt");
        var bad = Path.Combine(src, "bad-FAIL.txt");
        File.WriteAllText(good, "g");
        File.WriteAllText(bad, "b");
        var (job, asked) = await CopyAsync(new FaultyOperations("FAIL", code), [good, bad], dst, DecisionAction.Skip);

        var request = Assert.IsType<ErrorRequest>(Assert.Single(asked));
        Assert.Equal("error-" + errorClass, request.ClassKey);
        Assert.Contains(text, request.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Equal([0], job.CompletedRootIndices);
        Assert.Equal([1], job.FailedRootIndices);
        Assert.True(File.Exists(Path.Combine(dst, "good.txt")));
        Assert.False(File.Exists(Path.Combine(dst, "bad-FAIL.txt")));
        // Nothing half-written is left behind under a staged name.
        Assert.Empty(Directory.GetFiles(dst, JournalRecovery.StagedPrefix + "*"));
        var issue = Assert.Single(job.Issues, i => i.Severity == IssueSeverity.Error);
        Assert.Contains(text, issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_file_in_use_is_retried_automatically_before_asking()
    {
        var src = _dir.Dir("src");
        var dst = _dir.Dir("dst");
        var locked = Path.Combine(src, "locked-FAIL.txt");
        File.WriteAllText(locked, "x");
        var fs = new FaultyOperations("FAIL", 32);
        var (job, asked) = await CopyAsync(fs, [locked], dst, DecisionAction.Skip);
        Assert.Equal(4, fs.Attempts); // three quiet retries (antivirus scans are brief), then the question
        Assert.Equal("error-sharing", Assert.Single(asked).ClassKey);
        Assert.Equal(JobState.Failed, job.State); // its only item failed
    }

    [Fact]
    public async Task Canceling_at_a_failure_is_canceled_not_failed_and_keeps_completed_work()
    {
        var src = _dir.Dir("src");
        var dst = _dir.Dir("dst");
        var first = Path.Combine(src, "a.txt");
        var bad = Path.Combine(src, "b-FAIL.txt");
        File.WriteAllText(first, "a");
        File.WriteAllText(bad, "b");
        var (job, _) = await CopyAsync(new FaultyOperations("FAIL", 64), [first, bad], dst, DecisionAction.CancelJob);
        Assert.Equal(JobState.Canceled, job.State);
        Assert.True(File.Exists(Path.Combine(dst, "a.txt")));
        Assert.Equal([0], job.CompletedRootIndices);
    }

    [Fact]
    public async Task A_really_locked_file_is_reported_as_in_use()
    {
        if (!OperatingSystem.IsWindows()) return; // sharing modes are advisory elsewhere
        var src = _dir.Dir("src");
        var dst = _dir.Dir("dst");
        var path = Path.Combine(src, "open.txt");
        File.WriteAllText(path, "x");
        using var hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var (job, asked) = await CopyAsync(new PortableFileOperations(), [path], dst, DecisionAction.Skip);
        Assert.Equal("error-sharing", Assert.Single(asked).ClassKey);
        Assert.Contains("in use by another program", job.Issues.Single(i => i.Severity == IssueSeverity.Error).Message);
        Assert.False(File.Exists(Path.Combine(dst, "open.txt")));
    }

    [Fact]
    public async Task A_source_deleted_before_its_turn_is_reported_not_invented()
    {
        var src = _dir.Dir("src");
        var dst = _dir.Dir("dst");
        var present = Path.Combine(src, "present.txt");
        File.WriteAllText(present, "p");
        var (job, asked) = await CopyAsync(new PortableFileOperations(), [Path.Combine(src, "vanished.txt"), present], dst, DecisionAction.Skip);
        Assert.Empty(asked);
        Assert.Equal([1], job.CompletedRootIndices);
        Assert.Equal([0], job.FailedRootIndices);
        Assert.Contains("no longer exists", job.Issues.Single(i => i.Severity == IssueSeverity.Error).Message);
    }

    /// <summary>A destination volume without named streams (FAT/exFAT), and a source file with two streams.</summary>
    private sealed class NoStreamsDestination(string destination) : PortableFileOperations
    {
        public override VolumeInfo GetVolumeInfo(string path) =>
            path.StartsWith(destination, StringComparison.OrdinalIgnoreCase)
                ? base.GetVolumeInfo(path) with { SupportsNamedStreams = false, FileSystem = "exFAT" }
                : base.GetVolumeInfo(path);

        public override IReadOnlyList<string> GetAlternateStreams(string path) => ["Zone.Identifier", "thumbnail", "author"];

        public override string? ReadOriginMark(string path) => "[ZoneTransfer]\r\nZoneId=3\r\n";
    }

    [Fact]
    public async Task Streams_a_destination_cannot_store_are_named_as_lost()
    {
        if (!OperatingSystem.IsWindows()) return; // named streams are an NTFS concept
        var src = _dir.Dir("src");
        var dst = _dir.Dir("dst");
        var file = Path.Combine(src, "photo.jpg");
        File.WriteAllText(file, "jpeg");
        var (job, _) = await CopyAsync(new NoStreamsDestination(dst), [file], dst, DecisionAction.Skip);
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.True(File.Exists(Path.Combine(dst, "photo.jpg")));
        var warnings = job.Issues.Where(i => i.Severity == IssueSeverity.Warning).Select(i => i.Message).ToList();
        Assert.Contains(warnings, m => m.Contains("2 alternate data streams (thumbnail, author)", StringComparison.Ordinal) && m.Contains("exFAT", StringComparison.Ordinal));
        Assert.Contains(warnings, m => m.Contains("Mark of the Web", StringComparison.Ordinal));
    }
}
