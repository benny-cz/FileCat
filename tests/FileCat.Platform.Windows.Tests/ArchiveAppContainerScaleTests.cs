using System.Diagnostics;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    // AT18 on Windows (opt-in, FILECAT_ARCHIVE_SCALE=1, native lane): a TAR at the 100,000-member policy limit is created and
    // updated through the confined AppContainer workers and TxF publication; one more member refuses before anything is
    // written. Phase times, the managed heap's high-water mark, peak working set and open handles are reported.
    [Fact]
    public async Task At_the_member_limit_creation_and_update_finish_within_their_resource_budgets()
    {
        _ = Probe();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Explicit native transactional job lane.");
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SCALE") != "1") Assert.Skip("Set FILECAT_ARCHIVE_SCALE=1 for the AT18 scale measurement.");
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS"); Assert.True(assets is not null && Path.IsPathFullyQualified(assets));
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!); string executable = Path.Combine(assets!, "FileCat.exe");
        var ct = TestContext.Current.CancellationToken;
        // The phase each retained peak was seen in; FILECAT_ARCHIVE_SCALE_RETAINED=1 also logs the retained heap at every
        // phase change (a full collection each time, for a solo measurement run).
        string phaseNow = "start", peakPhase = "start"; bool retainedAtPhases = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SCALE_RETAINED") == "1";
        string root = Directory.CreateTempSubdirectory("filecat-native-at18-").FullName;
        try
        {
            // The tree itself, 497 folders of 200 tiny files and a last folder of 100: 99,999 members.
            string tree = Path.Combine(root, "tree"); int members = 1; var treeClock = Stopwatch.StartNew();
            for (int f = 0; members < ArchiveWritePolicy.MaxMembers - 1; f++)
            {
                string folder = Directory.CreateDirectory(Path.Combine(tree, $"f{f:000}")).FullName; members++;
                for (int i = 0; i < 200 && members < ArchiveWritePolicy.MaxMembers - 1; i++)
                {
                    string file = Path.Combine(folder, $"n{i:000}.txt"); File.WriteAllText(file, $"{f}:{i}");
                    File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch.AddSeconds(1700000000)); members++;
                }
                Directory.SetLastWriteTimeUtc(folder, DateTime.UnixEpoch.AddSeconds(1700000000));
            }
            Directory.SetLastWriteTimeUtc(tree, DateTime.UnixEpoch.AddSeconds(1700000000));
            Assert.Equal(ArchiveWritePolicy.MaxMembers - 1, members); Log($"tree of {members} members written in {treeClock.Elapsed.TotalSeconds:F1}s");
            var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(root, "journals"),
                new([WindowsArchiveJobBackend.Create(profile.Format, executable)]));
            string target = Path.Combine(root, "scale.tar");
            // A small job first, so the handles and heap the runtime opens lazily on its first job are part of the baseline.
            string warm = Path.Combine(root, "warm.txt"); File.WriteAllText(warm, "warm-up"); File.SetLastWriteTimeUtc(warm, DateTime.UnixEpoch.AddSeconds(1700000000));
            await Finish("warm-up", manager.SubmitArchive(new(Path.Combine(root, "warm.tar"), null, [new(ArchiveChangeKind.AddFile, "warm.txt", warm)], Profile: profile), null));
            File.Delete(Path.Combine(root, "warm.tar")); File.Delete(warm);
            int handles = Process.GetCurrentProcess().HandleCount;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long heapBefore = GC.GetTotalMemory(false), heapPeak = heapBefore;
            using var sampler = new Timer(_ =>
            {
                long heap = GC.GetTotalMemory(false), seen;
                while (heap > (seen = Interlocked.Read(ref heapPeak)) && Interlocked.CompareExchange(ref heapPeak, heap, seen) != seen) { }
            }, null, 0, 20);
            // What is actually retained: a full collection every 15 s; reported, not asserted (other tests share the process).
            long retainedPeak = heapBefore;
            using var retainedSampler = new Timer(_ =>
            {
                long retained = GC.GetTotalMemory(true), seen;
                while (retained > (seen = Interlocked.Read(ref retainedPeak)))
                    if (Interlocked.CompareExchange(ref retainedPeak, retained, seen) == seen) { Volatile.Write(ref peakPhase, Volatile.Read(ref phaseNow)); break; }
            }, null, 15_000, 15_000);

            var clock = Stopwatch.StartNew();
            await Finish("create", manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFolder, "tree", tree)], Profile: profile), null));
            var created = clock.Elapsed; long size = new FileInfo(target).Length;
            string extra = Path.Combine(root, "extra.txt"); File.WriteAllText(extra, "the last member"); File.SetLastWriteTimeUtc(extra, DateTime.UnixEpoch.AddSeconds(1700000001));
            clock.Restart();
            var reviewed = await manager.ReviewArchiveUpdateAsync(target, profile, null, ct);
            var assessed = clock.Elapsed; Assert.Equal(members, reviewed.Assessment.Manifest.Members.Count);
            Log($"assessed in {assessed.TotalSeconds:F1}s");
            clock.Restart();
            await Finish("update", manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "extra.txt", extra)]), null));
            var updated = clock.Elapsed;

            // One past the limit: refused before writing, the archive unchanged.
            byte[] before = File.ReadAllBytes(target);
            string over = Path.Combine(root, "over.txt"); File.WriteAllText(over, "one too many"); File.SetLastWriteTimeUtc(over, DateTime.UnixEpoch.AddSeconds(1700000002));
            reviewed = await manager.ReviewArchiveUpdateAsync(target, profile, null, ct);
            var refused = manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "over.txt", over)]), null);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(30));
                while (!refused.State.IsFinished()) await Task.Delay(10, deadline.Token);
            }
            Assert.Equal(JobState.Failed, refused.State);
            Assert.Equal(before, File.ReadAllBytes(target));
            Assert.Equal(new[] { "extra.txt", "journals", "over.txt", "scale.tar", "tree" },
                Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));

            sampler.Dispose(); retainedSampler.Dispose(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var process = Process.GetCurrentProcess(); process.Refresh();
            string report = $"AT18 members={members + 1} archiveBytes={size} create={created.TotalSeconds:F1}s assess={assessed.TotalSeconds:F1}s " +
                $"update={updated.TotalSeconds:F1}s heapBefore={heapBefore >> 20}MiB heapPeak={Interlocked.Read(ref heapPeak) >> 20}MiB " +
                $"retainedPeak={Interlocked.Read(ref retainedPeak) >> 20}MiB (in {Volatile.Read(ref peakPhase)}) " +
                $"chargedCeiling={ArchiveWritePolicy.MaxRetainedHeapBytes >> 20}MiB peakWorkingSet={process.PeakWorkingSet64 >> 20}MiB " +
                $"handles={handles}->{process.HandleCount} refusal=\"{string.Join(" | ", refused.Issues.Select(issue => issue.Message))}\"";
            Log(report);
            Assert.True(process.HandleCount <= handles + 64, report); // No handle growth beyond the runner's own churn.
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(root); }

        async Task Finish(string name, Job job)
        {
            var watch = Stopwatch.StartNew(); ArchiveJobPhase? phase = null; var nextProgress = TimeSpan.FromMinutes(1);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(45));
            while (!job.State.IsFinished())
            {
                if (job.ArchivePhase != phase)
                {
                    phase = job.ArchivePhase; Volatile.Write(ref phaseNow, $"{name}:{phase}");
                    Log($"{name} phase={phase} at {watch.Elapsed.TotalSeconds:F1}s heap={GC.GetTotalMemory(false) >> 20}MiB" +
                        (retainedAtPhases ? $" retained={GC.GetTotalMemory(true) >> 20}MiB" : ""));
                }
                if (watch.Elapsed >= nextProgress) { nextProgress += TimeSpan.FromMinutes(1); Log($"{name} still {phase} at {watch.Elapsed.TotalSeconds:F0}s items={job.ItemsDone}/{job.ItemsTotal} bytes={job.BytesDone}"); }
                await Task.Delay(10, deadline.Token);
            }
            Log($"{name} {job.State} at {watch.Elapsed.TotalSeconds:F1}s");
            Assert.True(job.State == JobState.Completed, job.State + ": " + string.Join(" | ", job.Issues.Select(issue => issue.Message)));
        }
        static void Log(string line)
        {
            line = "AT18 " + line; TestContext.Current.TestOutputHelper?.WriteLine(line); Console.WriteLine(line);
            // A test host stopped by the hang guard loses its captured output; this file, when named, keeps every phase line.
            if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SCALE_LOG") is { Length: > 0 } progress)
                File.AppendAllText(progress, DateTime.UtcNow.ToString("O") + " " + line + Environment.NewLine);
        }
    }
}
