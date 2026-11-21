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
        manager.SubmitStorage(review, review.Approve(StorageAction.Compress, StorageScope.SelectedItems), _ => { });
        finished.Wait(TimeSpan.FromSeconds(60), Stop);
        Assert.Fail("The job finished without reaching the crash phase " + phase + ".");
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
