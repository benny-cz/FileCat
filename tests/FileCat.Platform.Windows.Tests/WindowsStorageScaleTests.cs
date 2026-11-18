using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

[Collection("Windows storage job evidence")]
public sealed class WindowsStorageScaleTests
{
    [Fact]
    public void Ten_thousand_NTFS_files_keep_durable_progress_and_allow_live_review_while_paused()
    {
        Assert.SkipUnless(WindowsStorageReviewEvidence.IsSupportedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_STORAGE_SCALE") == "1",
            "Requires the disposable normal-user Windows NTFS storage scale fixture.");
        string? worker = Environment.GetEnvironmentVariable("FILECAT_STORAGE_CLOUD_WORKER_EXECUTABLE");
        Assert.SkipUnless(Path.IsPathFullyQualified(worker ?? "") && File.Exists(worker), "Requires the built Windows FileCat cloud query worker.");
        using var cloud = WindowsStorageCloudQuery.UseWorker(worker!, Environment.GetEnvironmentVariable("FILECAT_STORAGE_CLOUD_WORKER_ASSEMBLY"));
        var ct = TestContext.Current.CancellationToken;
        var output = TestContext.Current.TestOutputHelper!;
        string root = Directory.CreateTempSubdirectory("filecat-storage-scale-").FullName;
        const int count = 10_000;
        var finished = new ConcurrentDictionary<Guid, byte>();
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Join(root, "journal"), ArchiveJobBackends.Empty,
            new WindowsStorageTransforms(), WindowsStorageReviewEvidence.CreateJournal, WindowsStorageReviewEvidence.Backend) { MaxConcurrent = 2 };
        manager.JobFinished += job => finished.TryAdd(job.Id, 0);
        using var process = Process.GetCurrentProcess();
        try
        {
            string input = Directory.CreateDirectory(Path.Join(root, "input")).FullName;
            Directory.CreateDirectory(Path.Join(root, "journal"));
            var volume = new DriveInfo(Path.GetPathRoot(root)!);
            Assert.Equal("NTFS", volume.DriveFormat);
            byte[] payload = Enumerable.Repeat((byte)0x55, 8192).ToArray();
            var prep = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                File.WriteAllBytes(Path.Join(input, i.ToString("D5") + ".bin"), payload);
            }
            output.WriteLine($"Native fixture: {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {volume.DriveFormat}, {volume.DriveType}, {count} files, {payload.Length} bytes each. Creation {prep.Elapsed.TotalSeconds:F2}s.");

            process.Refresh();
            int startHandles = process.HandleCount, peakHandles = startHandles;
            long startWorkingSet = process.WorkingSet64, peakWorkingSet = startWorkingSet;
            void Measure()
            {
                process.Refresh();
                peakHandles = Math.Max(peakHandles, process.HandleCount);
                peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            }
            var first = Stopwatch.StartNew();
            Job compress = Submit();
            WaitFor(() => compress.ItemsDone >= 100 || Retired(compress), TimeSpan.FromMinutes(4), "first 100 items");
            Assert.False(Retired(compress), compress.Summary);
            compress.Pause();
            WaitFor(() => compress.State == JobState.Paused || Retired(compress), TimeSpan.FromSeconds(30), "pause");
            Assert.Equal(JobState.Paused, compress.State);
            var live = Assert.IsType<StorageJournalPrefix>(compress.StorageResult?.DurablePrefix);
            var report = Stopwatch.StartNew();
            string leaf = StorageJournalPolicy.FileName(compress.CreatedUtc, compress.Id);
            var history = manager.ReadStorageHistory(leaf, live, ct);
            var calls = manager.ReadStorageCalls(leaf, live, ct: ct);
            report.Stop();
            Assert.Equal(live, history.Prefix);
            Assert.False(history.Damaged);
            Assert.False(history.Ended);
            Assert.NotEmpty(calls.Calls);
            long pausedCount = compress.ItemsDone;
            Job unrelated = manager.Submit(new JobRequest { Kind = JobKind.CreateDirectory, Destination = Location.FileSystem(root), NewName = "queued" });
            Assert.Same(compress, unrelated.WaitingFor);
            Assert.Equal(JobState.Queued, unrelated.State);
            Assert.Equal(pausedCount, compress.ItemsDone);
            compress.Resume();
            WaitFor(() => Retired(compress) && Retired(unrelated), TimeSpan.FromMinutes(8), "compression and the queued operation");
            first.Stop();
            Assert.Equal(JobState.Completed, compress.State);
            Assert.Equal(JobState.Completed, unrelated.State);
            Assert.Equal(count + 1, compress.StorageResult!.Counts.Changed);
            Assert.Equal(StorageCompleteness.Complete, compress.StorageResult.Discovery);
            Assert.Equal(payload, File.ReadAllBytes(Path.Join(input, "00000.bin")));
            Assert.Equal(payload, File.ReadAllBytes(Path.Join(input, "09999.bin")));
            var completed = manager.ReadStorageHistory(leaf, ct: ct);
            Assert.True(completed.Ended);
            Assert.False(completed.Damaged);
            long recorded = Assert.IsType<StorageHistoryCall>(completed.LastCall).Call;
            // A pause can win admission after an intent was flushed. That record is durable even though Windows was not called.
            long deniedCount = recorded - count - 1;
            Assert.InRange(deniedCount, 0, 1);
            var pauseBoundary = manager.ReadStorageCalls(leaf, completed.Prefix, afterCall: pausedCount - 1, ct: ct);
            var denied = pauseBoundary.Calls.Where(call => call.Disposition == StorageCallDisposition.NotAttempted).ToArray();
            Assert.Equal(deniedCount, denied.LongLength);
            Assert.All(denied, call => Assert.Equal(StorageNoCallReason.Paused, call.NoCallReason));
            output.WriteLine($"Initial job {first.Elapsed.TotalSeconds:F2}s including pause. Live history and call page {report.Elapsed.TotalMilliseconds:F1}ms at {pausedCount} items. Journal {completed.Prefix.ByteLength:N0} bytes, {count + 1:N0} native changes, {deniedCount} denied intents.");

            string last = Path.Join(input, "zz-new.bin");
            File.WriteAllBytes(last, payload);
            using (var handle = File.OpenHandle(last, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                Assert.True(WindowsCompression.InvokeAdmitted(handle, StorageAction.RemoveCompression).NativeSucceeded);
            Assert.False(File.GetAttributes(last).HasFlag(FileAttributes.Compressed));
            var second = Stopwatch.StartNew();
            Job retry = Submit();
            WaitFor(() => Retired(retry), TimeSpan.FromMinutes(8), "fresh pass over the already-satisfied items");
            second.Stop();
            Assert.Equal(JobState.Completed, retry.State);
            Assert.Equal(count + 1, retry.StorageResult!.Counts.AlreadySatisfied);
            Assert.Equal(1, retry.StorageResult.Counts.Changed);
            Assert.True(File.GetAttributes(last).HasFlag(FileAttributes.Compressed));
            var retried = manager.ReadStorageHistory(StorageJournalPolicy.FileName(retry.CreatedUtc, retry.Id), ct: ct);
            Assert.Equal(1, retried.LastCall?.Call);
            output.WriteLine($"Fresh pass {second.Elapsed.TotalSeconds:F2}s. Journal {retried.Prefix.ByteLength:N0} bytes, one call. Process handles {startHandles:N0} to {peakHandles:N0}; sampled working set {startWorkingSet:N0} to {peakWorkingSet:N0} bytes.");
            Assert.True(peakHandles <= startHandles + 256, $"The application process grew from {startHandles:N0} handles to {peakHandles:N0}.");

            Job Submit()
            {
                using var review = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(input, EntryKind.Directory)], StorageAction.Compress, ct);
                return manager.SubmitStorage(review, review.Approve(StorageAction.Compress, StorageScope.ExistingDescendants), _ => { });
            }
            void WaitFor(Func<bool> condition, TimeSpan limit, string phase)
            {
                var wait = Stopwatch.StartNew();
                while (!condition())
                {
                    ct.ThrowIfCancellationRequested();
                    if (wait.Elapsed > limit) throw new TimeoutException("Timed out waiting for " + phase + ".");
                    Measure();
                    ct.WaitHandle.WaitOne(25);
                }
                Measure();
            }
        }
        finally
        {
            foreach (Job job in manager.Jobs) if (!Retired(job)) job.Cancel();
            if (SpinWait.SpinUntil(() => manager.Jobs.All(Retired), TimeSpan.FromSeconds(30))) Directory.Delete(root, recursive: true);
            else output.WriteLine("An active test job still owns the disposable fixture at " + root);
        }

        bool Retired(Job job) => finished.ContainsKey(job.Id);
    }
}
