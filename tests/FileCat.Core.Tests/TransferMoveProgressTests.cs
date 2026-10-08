using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class TransferMoveProgressTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(4, VerifyMode.Native)]
    [InlineData(4, VerifyMode.ReadBack)]
    [InlineData(1048576, VerifyMode.Native)]
    [InlineData(1048576, VerifyMode.ReadBack)]
    public async Task The_file_plan_includes_the_complete_check_before_move_source_deletion(int length, VerifyMode verify)
    {
        string root = Directory.CreateDirectory(Path.Join(Path.GetTempPath(), "filecat-move-progress", Guid.NewGuid().ToString("N"))).FullName;
        var data = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        string source = Path.Join(root, "source.dat"), destination = Path.Join(root, "destination"), target = Path.Join(destination, "published.dat");
        File.WriteAllBytes(source, data); var files = new Files(source, destination, target);
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, Path.Join(root, "journals")) { MaxConcurrent = 0 };
        try
        {
            var job = jobs.Submit(new JobRequest { Kind = JobKind.Move, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)], Destination = Location.FileSystem(destination), NewName = "published.dat", Options = new TransferOptions { Verify = verify } });
            files.Job = job; jobs.MaxConcurrent = 4; jobs.Schedule(); var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "Owned progress move did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            var progress = files.Progress;
            output.WriteLine("TRANSFER_MOVE_PROGRESS " + JsonSerializer.Serialize(new
            {
                length, verify, job.State, SourceInfoReadsAfterPublish = files.SourceChecks,
                BeforeDeletionFileProgress = progress is { } p ? new { p.Size, p.WorkDone, p.WorkTotal } : null,
                job.VerifyBytesDone, job.VerifyBytesTotal, job.WorkBytesDone, job.WorkBytesTotal,
                SourceExists = File.Exists(source), TargetSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))),
                OriginalSHA256 = Convert.ToHexString(SHA256.HashData(data)),
                ActualOwnedCopyPublishHashAndDelete = true, SyntheticDestinationVolumeRouting = true,
                NativeDesktopOrPhysicalSource = false, NativeFrameOrAtomicIdentityQualified = false,
            }));
            Assert.Equal(JobState.Completed, job.State); Assert.False(File.Exists(source)); Assert.Equal(data, File.ReadAllBytes(target));
            Assert.NotNull(progress); Assert.Equal(length, progress.Value.Size);
            long expected = (verify == VerifyMode.Native ? 3L : 5L) * length;
            Assert.Equal(expected, progress.Value.WorkTotal); Assert.Equal(expected, progress.Value.WorkDone);
            Assert.Equal(expected, job.WorkBytesDone); Assert.Equal(expected, job.WorkBytesTotal);
        }
        finally
        {
            foreach (var job in jobs.Jobs) job.Cancel(); SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(3)); Directory.Delete(root, true);
        }
    }

    private sealed class Files(string sourcePath, string destination, string target) : PortableFileOperations
    {
        public Job? Job;
        public (long Size, long WorkDone, long WorkTotal)? Progress;
        public int SourceChecks;
        private int _worker;
        public override string GetVolumeRoot(string path) => path.StartsWith(destination, PathUtil.SafetyComparison) ? destination : base.GetVolumeRoot(path);
        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
        {
            base.Move(source, destination, replaceExisting, writeThrough);
            if (destination == target) _worker = Environment.CurrentManagedThreadId;
        }
        public override FileSystemItemInfo? TryGetInfo(string path)
        {
            var info = base.TryGetInfo(path);
            // The second worker source check follows both complete hashes and precedes delete-source intent.
            // Discovery on another thread cannot supply this observation.
            if (_worker == Environment.CurrentManagedThreadId && path == sourcePath && ++SourceChecks == 2)
                Progress = Job!.CurrentFileProgress;
            return info;
        }
    }
}
