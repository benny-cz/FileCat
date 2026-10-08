using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class TransferDiscoveryProgressTests(ITestOutputHelper output)
{
    public static TheoryData<int, JobKind, VerifyMode, string> Cases
    {
        get
        {
            var cases = new TheoryData<int, JobKind, VerifyMode, string>();
            foreach (int length in new[] { 4, 1048576 })
            foreach (var kind in new[] { JobKind.Copy, JobKind.Move })
            foreach (var verify in new[] { VerifyMode.Native, VerifyMode.ReadBack })
            foreach (string timing in new[] { "early", "late", "missing" })
                cases.Add(length, kind, verify, timing);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Admitted_file_work_is_counted_when_discovery_is_early_late_or_unavailable(int length, JobKind kind, VerifyMode verify, string timing)
    {
        string root = Directory.CreateDirectory(Path.Join(Path.GetTempPath(), "filecat-discovery-progress", Guid.NewGuid().ToString("N"))).FullName;
        byte[] data = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        string source = Path.Join(root, "source.dat"), destination = Path.Join(root, "destination"), target = Path.Join(destination, "published.dat");
        File.WriteAllBytes(source, data);
        using var files = new Files(source, destination, timing);
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, Path.Join(root, "journals")) { MaxConcurrent = 0 };
        try
        {
            var job = jobs.Submit(new JobRequest { Kind = kind, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)], Destination = Location.FileSystem(destination), NewName = "published.dat", Options = new TransferOptions { Verify = verify } });
            files.Job = job; jobs.MaxConcurrent = 4; jobs.Schedule(); var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "Owned discovery control did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            long baseWork = (verify == VerifyMode.ReadBack ? 3L : 1L) * length;
            long expected = baseWork + (kind == JobKind.Move ? 2L * length : 0);
            output.WriteLine("TRANSFER_DISCOVERY_PROGRESS " + JsonSerializer.Serialize(new
            {
                length, kind, verify, timing, job.State, files.DiscoveryCalls,
                CopyEntryWorkTotal = files.CopyEntryTotal, CopyEntryFileProgress = files.CopyEntryProgress, files.CopyEntryTotalsFinal,
                job.ItemsTotal, job.ItemsDone, job.BytesDone, job.BytesTotal, job.VerifyBytesDone, job.VerifyBytesTotal,
                job.WorkBytesDone, job.WorkBytesTotal, job.TotalsFinal,
                SourceExists = File.Exists(source), TargetSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))),
                OriginalSHA256 = Convert.ToHexString(SHA256.HashData(data)),
                ControlledDiscoveryTimingOrMissingMetadata = true, ActualOwnedCopyVerificationAndDeletion = true,
                SyntheticDestinationVolumeRouting = true, NativeHistoricalInterleavingReproduced = false,
                NativeDesktopPhysicalAtomicOrCandidateQualified = false,
            }));
            Assert.Equal(JobState.Completed, job.State); Assert.Equal(kind == JobKind.Copy, File.Exists(source));
            Assert.Equal(data, File.ReadAllBytes(target)); Assert.Equal(1, files.DiscoveryCalls);
            Assert.Equal(baseWork, files.CopyEntryTotal);
            Assert.Equal(expected, job.WorkBytesDone); Assert.Equal(expected, job.WorkBytesTotal);
            Assert.Equal(length, job.BytesDone); Assert.Equal(length, job.BytesTotal);
            Assert.Equal(expected - length, job.VerifyBytesDone); Assert.Equal(expected - length, job.VerifyBytesTotal);
            Assert.Equal(1, job.ItemsDone); Assert.Equal(1, job.ItemsTotal);
            if (timing == "missing") Assert.False(files.CopyEntryTotalsFinal);
        }
        finally
        {
            foreach (var job in jobs.Jobs) job.Cancel(); files.Release();
            Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(3)));
            Directory.Delete(root, true);
        }
    }

    private sealed class Files(string sourcePath, string destination, string timing) : PortableFileOperations, IDisposable
    {
        private readonly ManualResetEventSlim _copyEntered = new();
        private readonly ManualResetEventSlim _discoveryDone = new();
        public Job? Job;
        public int DiscoveryCalls;
        public long CopyEntryTotal;
        public bool CopyEntryTotalsFinal;
        public object? CopyEntryProgress;
        public override string GetVolumeRoot(string path) => path.StartsWith(destination, PathUtil.SafetyComparison) ? destination : base.GetVolumeRoot(path);
        public override FileSystemItemInfo? TryGetInfo(string path)
        {
            // Control only the background metadata estimate. Transfer metadata, reads, copies and deletions use
            // real owned files. This deliberately does not claim to reproduce the historical native scheduling.
            bool discovery = path == sourcePath && new StackTrace(false).GetFrames().Any(frame =>
                frame.GetMethod() is { Name: "Discover", DeclaringType.Name: "TransferExecutor" });
            if (!discovery) return base.TryGetInfo(path);
            Interlocked.Increment(ref DiscoveryCalls);
            if (timing == "late") Assert.True(_copyEntered.Wait(TimeSpan.FromSeconds(5)));
            var info = timing == "missing" ? null : base.TryGetInfo(path);
            _discoveryDone.Set(); return info;
        }
        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken token)
        {
            if (timing == "early")
            {
                Assert.True(_discoveryDone.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(SpinWait.SpinUntil(() => Job!.TotalsFinal, TimeSpan.FromSeconds(5)));
            }
            CopyEntryTotal = Job!.WorkBytesTotal;
            CopyEntryTotalsFinal = Job.TotalsFinal;
            CopyEntryProgress = Job.CurrentFileProgress is { } p ? new { p.Size, p.WorkDone, p.WorkTotal } : null;
            _copyEntered.Set(); Assert.True(_discoveryDone.Wait(TimeSpan.FromSeconds(5)));
            base.CopyFile(source, destination, options, progress, token);
        }
        public void Release() { _copyEntered.Set(); _discoveryDone.Set(); }
        public void Dispose() { _copyEntered.Dispose(); _discoveryDone.Dispose(); }
    }
}
