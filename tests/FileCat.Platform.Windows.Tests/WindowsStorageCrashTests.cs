using System.Diagnostics;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// T-REC.C process-kill fixtures. A child copy of this test assembly runs a reviewed two-file Compress job and
/// terminates itself at a native call phase, without unwinding. A fresh manager over the same private evidence must
/// then publish a pending review with the exact recorded prefix, never replay the call, and leave each file as the
/// native calls actually left it.
/// </summary>
[Collection("Windows storage job evidence")]
public sealed class WindowsStorageCrashTests
{
    private const string ChildRoot = "FILECAT_STORAGE_CRASH_CHILD_ROOT", ChildPhase = "FILECAT_STORAGE_CRASH_PHASE";
    private static CancellationToken Stop => TestContext.Current.CancellationToken;

    private static JobManager Manager(string root, WindowsStorageTransforms adapter) =>
        new(new PortableFileOperations(), new ProviderRegistry(), Path.Join(root, "journals"), ArchiveJobBackends.Empty, adapter,
            WindowsStorageReviewEvidence.CreateJournal, WindowsStorageReviewEvidence.Backend);

    /// <summary>The child half: only runs inside the process the parent starts.</summary>
    [Fact]
    public void Crash_child_terminates_at_the_requested_native_call_phase()
    {
        string? root = Environment.GetEnvironmentVariable(ChildRoot), phase = Environment.GetEnvironmentVariable(ChildPhase);
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(phase)) Assert.Skip("Started only by the storage crash parent fixture.");
        int starting = 0, returned = 0;
        void Die(string marker)
        {
            File.WriteAllText(Path.Join(root, "phase-reached"), marker);
            Process.GetCurrentProcess().Kill(); // TerminateProcess: no unwinding, finalizers or flush on the way out
        }
        var adapter = new WindowsStorageTransforms(
            compressionSetStartingForQualification: () =>
            {
                int call = Interlocked.Increment(ref starting);
                if (phase == "before-first-call" && call == 1) Die(phase);
                if (phase == "before-second-call" && call == 2) Die(phase);
                if (phase == "hold-before-first-call" && call == 1)
                {
                    // Another process inspects this live job's evidence while the admitted call waits.
                    File.WriteAllText(Path.Join(root, "holding"), phase);
                    var limit = DateTime.UtcNow.AddMinutes(2);
                    while (!File.Exists(Path.Join(root, "release")) && DateTime.UtcNow < limit) Thread.Sleep(50);
                }
            },
            compressionSetReturnedForQualification: _ =>
            {
                if (Interlocked.Increment(ref returned) == 1 && phase == "after-first-call") Die(phase);
            });
        var manager = Manager(root, adapter);
        string data = Path.Join(root, "data");
        var sources = new[] { "first.bin", "second.bin" }.Select(n => ItemRef.ForFileSystemPath(Path.Join(data, n), EntryKind.File)).ToArray();
        using var review = manager.PrepareStorageReview(sources, StorageAction.Compress, Stop);
        using var finished = new ManualResetEventSlim();
        manager.JobFinished += _ => finished.Set();
        var job = manager.SubmitStorage(review, review.Approve(StorageAction.Compress, StorageScope.SelectedItems), _ => { });
        bool done = finished.Wait(TimeSpan.FromMinutes(3), Stop);
        if (phase != "hold-before-first-call") Assert.Fail("The job finished without reaching the crash phase " + phase + ".");
        Assert.True(done, "The held job did not finish after release.");
        Assert.Equal((JobState.Completed, 2L), (job.State, job.StorageResult!.Counts.Changed));
    }

    [Fact]
    public void A_job_running_in_another_process_owns_its_evidence_until_it_finishes()
    {
        Assert.SkipWhen(!WindowsStorageTransforms.IsQualifiedHost, "Qualified Windows 11 x64 client NTFS fixture.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity, "Storage jobs must run in the normal user process.");
        string exe = Path.ChangeExtension(typeof(WindowsStorageCrashTests).Assembly.Location, ".exe");
        Assert.SkipUnless(File.Exists(exe), "The fixture needs the test assembly's own executable.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-owner-").FullName;
        Process? child = null;
        try
        {
            string data = Directory.CreateDirectory(Path.Join(root, "data")).FullName;
            Directory.CreateDirectory(Path.Join(root, "journals"));
            File.WriteAllBytes(Path.Join(data, "first.bin"), new byte[65536]); File.WriteAllBytes(Path.Join(data, "second.bin"), new byte[65536]);
            child = StartChild(exe, root, "hold-before-first-call");
            var limit = DateTime.UtcNow.AddMinutes(2);
            while (!File.Exists(Path.Join(root, "holding")) && !child.HasExited && DateTime.UtcNow < limit) Thread.Sleep(50);
            Assert.True(File.Exists(Path.Join(root, "holding")), "The child did not reach its held call.");

            var manager = Manager(root, new WindowsStorageTransforms());
            var row = Assert.Single(manager.ReadStorageHistoryPage(ct: Stop).Rows);
            var candidate = manager.StorageHistoryCandidate(row.FileName);
            Assert.False(manager.IsInterrupted(candidate, Stop));
            var busy = Assert.ThrowsAny<IOException>(() => manager.ReadStorageReview(row.FileName, Stop));
            Assert.Contains(busy.HResult & 0xffff, new[] { 32, 33 });
            var prefix = manager.ReadStorageHistory(row.FileName, ct: Stop);
            Assert.False(prefix.Ended);
            Assert.True(prefix.EffectUncertain);
            Assert.Equal((1L, 0), (prefix.LastCall!.Call, prefix.LastCall.SourceIndex));
            Assert.Null(prefix.LastCall.Result);
            var census = manager.AccountStorageHistory(Stop);
            TestContext.Current.TestOutputHelper?.WriteLine($"Census during the foreign job: complete {census.Complete}");
            Assert.False((File.GetAttributes(Path.Join(data, "first.bin")) & FileAttributes.Compressed) != 0);

            File.WriteAllText(Path.Join(root, "release"), "release");
            Assert.True(child.WaitForExit(TimeSpan.FromMinutes(3)), "The child did not finish after release.");
            Assert.Equal(0, child.ExitCode);
            var finished = manager.ReadStorageReview(row.FileName, Stop);
            Assert.False(finished.ReviewPending);
            Assert.True(finished.Current.History.Ended);
            Assert.False(manager.IsInterrupted(candidate, Stop));
            Assert.True((File.GetAttributes(Path.Join(data, "first.bin")) & FileAttributes.Compressed) != 0);
            Assert.True((File.GetAttributes(Path.Join(data, "second.bin")) & FileAttributes.Compressed) != 0);
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited) { File.WriteAllText(Path.Join(root, "release"), "release"); child.WaitForExit(TimeSpan.FromMinutes(1)); }
                if (!child.HasExited) child.Kill();
                child.Dispose();
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private static Process StartChild(string exe, string root, string phase)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = false, RedirectStandardError = false };
        foreach (string argument in new[] { "-method", typeof(WindowsStorageCrashTests).FullName + "." + nameof(Crash_child_terminates_at_the_requested_native_call_phase), "-noLogo" })
            start.ArgumentList.Add(argument);
        start.Environment[ChildRoot] = root; start.Environment[ChildPhase] = phase;
        return Process.Start(start)!;
    }

    [Theory]
    [InlineData("before-first-call", false, false)]
    [InlineData("after-first-call", true, false)]
    [InlineData("before-second-call", true, false)]
    public void A_killed_process_leaves_a_pending_review_of_the_recorded_prefix_and_no_replay(string phase, bool firstCompressed, bool secondCompressed)
    {
        Assert.SkipWhen(!WindowsStorageTransforms.IsQualifiedHost, "Qualified Windows 11 x64 client NTFS fixture.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity, "Storage jobs must run in the normal user process.");
        string exe = Path.ChangeExtension(typeof(WindowsStorageCrashTests).Assembly.Location, ".exe");
        Assert.SkipUnless(File.Exists(exe), "The crash fixture needs the test assembly's own executable.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-crash-").FullName;
        try
        {
            string data = Directory.CreateDirectory(Path.Join(root, "data")).FullName;
            Directory.CreateDirectory(Path.Join(root, "journals"));
            byte[] first = Enumerable.Repeat((byte)'a', 65536).ToArray(), second = Enumerable.Repeat((byte)'b', 65536).ToArray();
            File.WriteAllBytes(Path.Join(data, "first.bin"), first); File.WriteAllBytes(Path.Join(data, "second.bin"), second);

            var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-method", typeof(WindowsStorageCrashTests).FullName + "." + nameof(Crash_child_terminates_at_the_requested_native_call_phase), "-noLogo" })
                start.ArgumentList.Add(argument);
            start.Environment[ChildRoot] = root; start.Environment[ChildPhase] = phase;
            using (var child = Process.Start(start)!)
            {
                var output = child.StandardOutput.ReadToEndAsync(Stop); var error = child.StandardError.ReadToEndAsync(Stop);
                Assert.True(child.WaitForExit(TimeSpan.FromMinutes(3)), "The crash child did not exit.");
                TestContext.Current.TestOutputHelper?.WriteLine($"Child exit {child.ExitCode}: {output.Result}{error.Result}");
                Assert.NotEqual(0, child.ExitCode);
            }
            Assert.Equal(phase, File.ReadAllText(Path.Join(root, "phase-reached")));

            // A fresh process view of the same private evidence.
            var manager = Manager(root, new WindowsStorageTransforms());
            var page = manager.ReadStorageHistoryPage(ct: Stop);
            var row = Assert.Single(page.Rows);
            TestContext.Current.TestOutputHelper?.WriteLine($"History row: {row.Status}; action {row.Action}; roots {row.RootCount}");
            Assert.Equal(StorageAction.Compress, row.Action);
            var status = manager.ReadStorageReview(row.FileName, Stop);
            var history = status.Current.History;
            Assert.True(status.ReviewPending);
            Assert.False(history.Ended);
            Assert.True(manager.IsInterrupted(manager.StorageHistoryCandidate(row.FileName), Stop));
            var call = history.LastCall;
            Assert.NotNull(call);
            switch (phase)
            {
                case "before-first-call":
                case "after-first-call":
                    Assert.Equal((1L, 0), (call.Call, call.SourceIndex));
                    Assert.Null(call.Result);
                    Assert.True(history.EffectUncertain);
                    break;
                case "before-second-call":
                    Assert.Equal((2L, 1), (call.Call, call.SourceIndex));
                    Assert.Null(call.Result);
                    Assert.True(history.EffectUncertain);
                    break;
            }
            Assert.Empty(manager.Jobs);
            Assert.Equal((firstCompressed, secondCompressed), (Compressed(Path.Join(data, "first.bin")), Compressed(Path.Join(data, "second.bin"))));
            Assert.Equal(first, File.ReadAllBytes(Path.Join(data, "first.bin")));
            Assert.Equal(second, File.ReadAllBytes(Path.Join(data, "second.bin")));
            // Reading the review again neither replays nor closes it.
            Assert.True(manager.ReadStorageReview(row.FileName, Stop).ReviewPending);
            Assert.Equal((firstCompressed, secondCompressed), (Compressed(Path.Join(data, "first.bin")), Compressed(Path.Join(data, "second.bin"))));
        }
        finally { Directory.Delete(root, recursive: true); }

        static bool Compressed(string path) => (File.GetAttributes(path) & FileAttributes.Compressed) != 0;
    }
}
