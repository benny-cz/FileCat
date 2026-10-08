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

    private Task<(Job Job, List<DecisionRequest> Asked)> CopyAsync(IFileSystemOperations fs, IEnumerable<string> files, string dest, DecisionAction answer) =>
        RunAsync(JobKind.Copy, fs, files, dest, answer);

    private async Task<(Job Job, List<DecisionRequest> Asked)> RunAsync(JobKind kind, IFileSystemOperations fs, IEnumerable<string> files, string dest, DecisionAction answer)
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
            Kind = kind,
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
        if (!OperatingSystem.IsWindows()) Assert.Skip("Only Windows keeps an open file from being shared; sharing modes are advisory elsewhere.");
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

    /// <summary>
    /// A destination volume without named streams (FAT/exFAT, or a network share, which names a file system of its
    /// choosing: Samba says NTFS), and a source file with two streams.
    /// </summary>
    private sealed class NoStreamsDestination(string destination, bool share = false) : PortableFileOperations
    {
        public override VolumeInfo GetVolumeInfo(string path) =>
            path.StartsWith(destination, StringComparison.OrdinalIgnoreCase)
                ? base.GetVolumeInfo(path) with { SupportsNamedStreams = false, FileSystem = share ? "NTFS" : "exFAT", IsRemote = share }
                : base.GetVolumeInfo(path);

        public override IReadOnlyList<string> GetAlternateStreams(string path) => ["Zone.Identifier", "thumbnail", "author"];

        // The destination folder acts as its own volume (like a FAT stick mounted into a folder).
        public override string GetVolumeRoot(string path) =>
            path.StartsWith(destination, StringComparison.OrdinalIgnoreCase) ? destination : base.GetVolumeRoot(path);

        public override string? ReadOriginMark(string path) => "[ZoneTransfer]\r\nZoneId=3\r\n";
    }

    [Fact]
    public async Task Streams_a_destination_cannot_store_are_named_as_lost()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Named streams are an NTFS concept.");
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

    [Theory]
    [InlineData(DecisionAction.KeepSource, true, true)]
    [InlineData(DecisionAction.Proceed, true, false)]
    [InlineData(DecisionAction.Skip, false, true)]
    public async Task A_move_that_would_lose_metadata_asks_before_the_original_is_deleted(DecisionAction answer, bool copied, bool sourceKept)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The metadata a move would lose here is NTFS's streams.");
        var src = _dir.Dir("src");
        var dst = _dir.Dir("dst");
        var file = Path.Combine(src, "download.zip");
        File.WriteAllText(file, "zip");
        var (job, asked) = await RunAsync(JobKind.Move, new NoStreamsDestination(dst), [file], dst, answer);
        var question = Assert.IsType<ConfirmRequest>(Assert.Single(asked));
        Assert.Equal("confirm-metadata-loss", question.ClassKey);
        Assert.Contains("Mark of the Web", question.Message);
        Assert.Contains(DecisionAction.KeepSource, question.Actions);
        Assert.Equal(copied, File.Exists(Path.Combine(dst, "download.zip")));
        Assert.Equal(sourceKept, File.Exists(file));
        Assert.True(job.State.IsFinished());
    }

    [Fact]
    public async Task A_share_is_named_as_what_cannot_store_the_metadata_not_the_file_system_it_claims()
    {
        // Release issue I34: the lab's Samba share reported NTFS, and FileCat said "NTFS cannot store" the mark.
        if (!OperatingSystem.IsWindows()) Assert.Skip("The metadata a move would lose here is NTFS's streams.");
        var src = _dir.Dir("src");
        var dst = _dir.Dir("dst");
        var file = Path.Combine(src, "download.zip");
        File.WriteAllText(file, "zip");
        var (move, asked) = await RunAsync(JobKind.Move, new NoStreamsDestination(dst, share: true), [file], dst, DecisionAction.KeepSource);
        var question = Assert.IsType<ConfirmRequest>(Assert.Single(asked));
        Assert.Contains("The network share cannot store its download origin (Mark of the Web)", question.Message);
        Assert.DoesNotContain("NTFS", question.Message);
        var warnings = move.Issues.Where(i => i.Severity == IssueSeverity.Warning).Select(i => i.Message).ToList();
        Assert.Contains(warnings, m => m.Contains("cannot be stored on the network share", StringComparison.Ordinal));
        Assert.Contains(warnings, m => m.Contains("(thumbnail, author); the network share cannot store them", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, m => m.Contains("NTFS", StringComparison.Ordinal));
    }

    /// <summary>
    /// The destination acts as its own volume (a move copies, then deletes the source), and the copy is taken away just
    /// after it was published — as an antivirus quarantine or a sync client can take a new file.
    /// </summary>
    private sealed class CopyTakenAway(string source, string destination) : PortableFileOperations
    {
        public override string GetVolumeRoot(string path) =>
            path.StartsWith(destination, StringComparison.OrdinalIgnoreCase) ? destination : base.GetVolumeRoot(path);

        // Just before the source goes, the move asks whether the copy is the source itself: by then the copy has been taken
        // away. (Not on reading the source's information: the job also counts its sources on another thread, which may
        // come late and would take the copy away while it is written.)
        public override string? GetFileIdentity(string path)
        {
            string copy = Path.Combine(destination, Path.GetFileName(path));
            if (string.Equals(path, source, StringComparison.OrdinalIgnoreCase) && File.Exists(copy)) File.Delete(copy);
            return base.GetFileIdentity(path);
        }
    }

    [Fact]
    public async Task A_move_keeps_its_source_when_the_copy_is_gone_before_the_source_would_go()
    {
        // Release plan DPI P01: the source of a move was deleted without checking that its copy was still there.
        var src = _dir.Dir("src");
        var dst = _dir.Dir("dst");
        var file = Path.Combine(src, "report.txt");
        File.WriteAllText(file, "the only copy");
        var (job, _) = await RunAsync(JobKind.Move, new CopyTakenAway(file, dst), [file], dst, DecisionAction.Skip);
        Assert.True(job.State.IsFinished());
        Assert.False(File.Exists(Path.Combine(dst, "report.txt")));
        Assert.Equal("the only copy", File.ReadAllText(file));
        Assert.Contains(job.Issues, i => i.Message.Contains("is no longer at the destination", StringComparison.Ordinal));
    }

    /// <summary>An EFS-encrypted source: the native engine refuses a non-encrypting destination until allowed.</summary>
    private sealed class EncryptedSource : PortableFileOperations
    {
        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
        {
            if (!options.AllowDecryptedDestination) throw new IOException("ERROR_ENCRYPTION_FAILED", unchecked((int)0x80071770));
            base.CopyFile(source, destination, options, progress, ct);
        }
    }

    [Fact]
    public async Task Encrypted_files_are_copied_decrypted_only_with_consent()
    {
        var src = _dir.Dir("src");
        var dst = _dir.Dir("dst");
        var file = Path.Combine(src, "secret.txt");
        File.WriteAllText(file, "s");
        var (job, asked) = await CopyAsync(new EncryptedSource(), [file], dst, DecisionAction.Proceed);
        Assert.Equal("confirm-decrypt", Assert.Single(asked).ClassKey);
        Assert.True(File.Exists(Path.Combine(dst, "secret.txt")));
        Assert.Contains(job.Issues, i => i.Message.Contains("Copied decrypted", StringComparison.Ordinal));
        var dst2 = _dir.Dir("dst2");
        var (skipped, _) = await CopyAsync(new EncryptedSource(), [file], dst2, DecisionAction.Skip);
        Assert.False(File.Exists(Path.Combine(dst2, "secret.txt")));
        Assert.Equal([0], skipped.FailedRootIndices);
    }

    [Fact]
    public void A_failed_copy_never_deletes_a_destination_it_did_not_create()
    {
        var src = _dir.File("a.txt");
        File.WriteAllText(src, "new");
        var existing = Path.Combine(_dir.Dir("dst"), "a.txt");
        File.WriteAllText(existing, "precious");
        Assert.ThrowsAny<IOException>(() => new PortableFileOperations().CopyFile(src, existing, new FileCopyOptions(), null, TestContext.Current.CancellationToken));
        Assert.Equal("precious", File.ReadAllText(existing));
    }

    /// <summary>Records the durability each step asks for; the destination folder acts as another volume.</summary>
    private sealed class DurabilityRecorder(string destination) : PortableFileOperations
    {
        public readonly List<string> Events = [];

        public override string GetVolumeRoot(string path) =>
            path.StartsWith(destination, StringComparison.OrdinalIgnoreCase) ? destination : base.GetVolumeRoot(path);

        public override void CopyFile(string source, string target, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
        {
            lock (Events) Events.Add($"copy {Path.GetFileName(source)} flush={options.FlushDestination}");
            base.CopyFile(source, target, options, progress, ct);
        }

        public override void Move(string source, string target, bool replaceExisting, bool writeThrough = false)
        {
            lock (Events) Events.Add($"publish {Path.GetFileName(target)} writeThrough={writeThrough}");
            base.Move(source, target, replaceExisting, writeThrough);
        }

        public override void DeleteFile(string path)
        {
            lock (Events) Events.Add($"delete {Path.GetFileName(path)}");
            base.DeleteFile(path);
        }
    }

    [Fact]
    public async Task A_move_to_another_volume_makes_the_copy_durable_before_deleting_the_source()
    {
        var src = _dir.Dir("src");
        var dst = _dir.Dir("dst");
        var small = Path.Combine(src, "small.txt");
        File.WriteAllText(small, "s");
        var large = Path.Combine(src, "large.bin");
        using (var fs = new FileStream(large, FileMode.Create)) fs.SetLength(TransferExecutor.DirectCopyLimit + 1);

        var moving = new DurabilityRecorder(dst);
        var (moved, _) = await RunAsync(JobKind.Move, moving, [small, large], dst, DecisionAction.CancelJob);
        Assert.Equal(JobState.Completed, moved.State);
        // The small file is written straight to its name and flushed; the large one is flushed under its staged name
        // and published with write-through. Only then is each source deleted.
        Assert.Equal(["copy small.txt flush=True", "delete small.txt", "copy large.bin flush=True", "publish large.bin writeThrough=True", "delete large.bin"], moving.Events);

        // A copy deletes nothing and keeps the cheaper path.
        var copyDst = _dir.Dir("copy");
        var copying = new DurabilityRecorder(copyDst);
        var (copied, _) = await RunAsync(JobKind.Copy, copying, [Path.Combine(dst, "small.txt"), Path.Combine(dst, "large.bin")], copyDst, DecisionAction.CancelJob);
        Assert.Equal(JobState.Completed, copied.State);
        Assert.Equal(["copy small.txt flush=False", "copy large.bin flush=False", "publish large.bin writeThrough=False"], copying.Events);
    }

    [Fact]
    public async Task Small_new_files_are_copied_directly_and_recovery_finds_incomplete_ones()
    {
        var src = _dir.Dir("src");
        var dst = _dir.Dir("dst");
        File.WriteAllText(Path.Combine(src, "one.txt"), "1111");
        File.WriteAllText(Path.Combine(src, "two.txt"), "2222");
        var (job, _) = await CopyAsync(new PortableFileOperations(), [Path.Combine(src, "one.txt"), Path.Combine(src, "two.txt")], dst, DecisionAction.Skip);
        Assert.Equal(JobState.Completed, job.State);
        Assert.Empty(Directory.GetFiles(dst, JournalRecovery.StagedPrefix + "*"));

        // A crash mid-copy leaves a short file under the real name: the fill record lets recovery point at it.
        var journalDir = _dir.Dir("crash-journal");
        var crashed = new InterruptedJob(Path.Combine(journalDir, "job-x.fcj"), "Copy", "Copy", DateTime.UtcNow.AddMinutes(-1),
            [Path.Combine(src, "one.txt"), Path.Combine(src, "two.txt")], dst, [], [], 0, 2)
        {
            FillDirectories = [new FillDirectory(src, dst)],
        };
        File.WriteAllText(Path.Combine(dst, "two.txt"), "2"); // truncated by the "crash"
        var incomplete = JournalRecovery.FindIncompleteCopies(crashed);
        Assert.Equal([Path.Combine(dst, "two.txt")], incomplete);
    }
}
