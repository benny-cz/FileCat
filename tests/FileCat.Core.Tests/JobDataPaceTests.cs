using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class JobDataPaceTests
{
    [Fact]
    public async Task Copy_counts_processed_data_but_same_volume_move_and_skipped_conflict_do_not()
    {
        string root = Directory.CreateTempSubdirectory("filecat-data-pace-").FullName;
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            string moved = Directory.CreateDirectory(Path.Combine(root, "moved")).FullName;
            string copied = Directory.CreateDirectory(Path.Combine(root, "copied")).FullName;
            string path = Path.Combine(source, "pace.bin");
            File.WriteAllBytes(path, new byte[1 << 20]);
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var manager = new JobManager(new PortableFileOperations(), providers, Path.Combine(root, "journal"));

            var move = manager.Submit(new JobRequest
            {
                Kind = JobKind.Move,
                Sources = [ItemRef.ForFileSystemPath(path, EntryKind.File)],
                Destination = Location.FileSystem(moved),
            });
            await Finished(move);
            Assert.Equal(JobState.Completed, move.State);
            Assert.Equal(1 << 20, move.BytesDone); // Logical progress still reaches completion.
            Assert.Equal(0, move.DataBytesProcessed); // A rename did not transfer 1 MiB.

            string movedPath = Path.Combine(moved, "pace.bin");
            var copy = manager.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(movedPath, EntryKind.File)],
                Destination = Location.FileSystem(copied),
            });
            await Finished(copy);
            Assert.Equal(JobState.Completed, copy.State);
            Assert.True(copy.DataBytesProcessed >= 1 << 20);

            int asked = 0;
            manager.DecisionRequested += decision =>
            {
                Interlocked.Increment(ref asked);
                decision.Resolve(new Decision(DecisionAction.Skip));
            };
            var skipped = manager.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(movedPath, EntryKind.File)],
                Destination = Location.FileSystem(copied),
            });
            await Finished(skipped);
            Assert.Equal(1, asked);
            Assert.Equal(1, skipped.ItemsSkipped);
            Assert.Equal(1 << 20, skipped.BytesDone); // Existing logical completion rule is preserved.
            Assert.Equal(0, skipped.DataBytesProcessed); // Skip must not look like a transfer-speed spike.
        }
        finally { Directory.Delete(root, recursive: true); }

        static async Task Finished(Job job)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            while (!job.State.IsFinished()) await Task.Delay(10, timeout.Token);
        }
    }
}
