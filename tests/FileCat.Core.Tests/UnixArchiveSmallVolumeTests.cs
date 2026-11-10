using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    // AT08 on a real small volume (for example a 160 MiB tmpfs owned by the test user): when the space a write needs is not
    // there, creation and update refuse before writing, the original stays byte-identical and no FileCat folder is left.
    [Fact]
    public async Task A_nearly_full_volume_refuses_before_writing_and_keeps_the_original_and_folder_clean()
    {
        Native();
        string? volume = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SMALL_VOLUME");
        if (string.IsNullOrEmpty(volume)) Assert.Skip("Set FILECAT_ARCHIVE_SMALL_VOLUME to an owned small volume (about 160 MiB).");
        var ct = TestContext.Current.CancellationToken;
        string folder = Path.Combine(Canonical(volume), "at08-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); File.SetUnixFileMode(folder, (UnixFileMode)0x1c0);
        using var sources = new TempDir(); string root = Canonical(sources.Path);
        try
        {
            string small = Path.Combine(root, "small.txt"); File.WriteAllText(small, "small"); File.SetLastWriteTimeUtc(small, DateTime.UnixEpoch.AddSeconds(1700000000));
            string big = Path.Combine(root, "big.bin"); var bytes = new byte[24 << 20]; new Random(808).NextBytes(bytes); File.WriteAllBytes(big, bytes);
            File.SetLastWriteTimeUtc(big, DateTime.UnixEpoch.AddSeconds(1700000001));
            var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
            var backend = UnixArchiveJobBackend.Create(profile.Format, (nonce, _) => Child.Start(nonce, "archive-assess-v2"),
                (nonce, _) => Child.Start(nonce, "archive-transform-v2"), (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(root, "journals"), new([backend]));
            string target = Path.Combine(folder, "kept.tar");
            await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFile, "small.txt", small)], Profile: profile), null));
            byte[] original = File.ReadAllBytes(target);

            // Leave less free space than the update needs (its input, evidence and the 64 MiB safety margin).
            long available = FileSystemCapacity.Query(folder)?.AvailableBytes ?? throw new IOException("The small volume reports no capacity.");
            long keep = (64L << 20) + (8L << 20);
            if (available > keep)
                using (var filler = File.Create(Path.Combine(folder, "filler.bin"))) { filler.SetLength(available - keep); filler.Write(new byte[1]); }
            foreach (string path in Directory.GetFiles(folder, "filler.bin")) using (var fill = new FileStream(path, FileMode.Open, FileAccess.Write))
            { var block = new byte[1 << 20]; for (long at = 0; at < fill.Length; at += block.Length) { fill.Position = at; fill.Write(block, 0, (int)Math.Min(block.Length, fill.Length - at)); } }

            var assessor = new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
            var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
            var update = manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "big.bin", big)]), null);
            var creation = manager.SubmitArchive(new(Path.Combine(folder, "new.tar"), null, [new(ArchiveChangeKind.AddFile, "big.bin", big)], Profile: profile), null);
            foreach (var job in new[] { update, creation })
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(120));
                while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
                Assert.Equal(JobState.Failed, job.State);
                Assert.Contains(job.Issues, issue => issue.Message.StartsWith("Saving this archive needs up to", StringComparison.Ordinal) &&
                    issue.Message.Contains("Choose another destination or free space first.", StringComparison.Ordinal));
            }
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.Equal(new[] { "filler.bin", "kept.tar" }, Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    // AT08 ENOSPC during the write itself: the space review passes, then the drive fills before the worker writes the stage
    // (as when another program writes at the same time). The job must fail with the plan's full-drive text, never publish,
    // keep the original byte-identical and leave nothing outside its own private stage folder.
    [Fact]
    public async Task A_drive_that_fills_during_the_rebuild_fails_with_the_space_message_and_keeps_the_original()
    {
        Native();
        string? volume = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SMALL_VOLUME");
        if (string.IsNullOrEmpty(volume)) Assert.Skip("Set FILECAT_ARCHIVE_SMALL_VOLUME to an owned small volume (about 160 MiB).");
        var ct = TestContext.Current.CancellationToken;
        string folder = Path.Combine(Canonical(volume), "at08-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); File.SetUnixFileMode(folder, (UnixFileMode)0x1c0);
        using var sources = new TempDir(); string root = Canonical(sources.Path);
        try
        {
            string small = Path.Combine(root, "small.txt"); File.WriteAllText(small, "small"); File.SetLastWriteTimeUtc(small, DateTime.UnixEpoch.AddSeconds(1700000000));
            string big = Path.Combine(root, "big.bin"); var bytes = new byte[16 << 20]; new Random(809).NextBytes(bytes); File.WriteAllBytes(big, bytes);
            File.SetLastWriteTimeUtc(big, DateTime.UnixEpoch.AddSeconds(1700000001));
            var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
            string filler = Path.Combine(folder, "filler.bin"); bool armed = false; int fills = 0;
            var backend = UnixArchiveJobBackend.Create(profile.Format, (nonce, _) => Child.Start(nonce, "archive-assess-v2"), (nonce, _) =>
            {
                // The transform worker starts after the space review and before the first stage byte is written.
                if (Volatile.Read(ref armed)) { FillLeaving(filler, 1L << 20); Interlocked.Increment(ref fills); }
                return Child.Start(nonce, "archive-transform-v2");
            }, (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(root, "journals"), new([backend]));
            string target = Path.Combine(folder, "kept.tar"), created = Path.Combine(folder, "new.tar");
            await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFile, "small.txt", small)], Profile: profile), null));
            byte[] original = File.ReadAllBytes(target);
            var assessor = new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
            var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
            Volatile.Write(ref armed, true);
            foreach (var submit in new Func<Job>[]
            {
                () => manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "big.bin", big)]), null),
                () => manager.SubmitArchive(new(created, null, [new(ArchiveChangeKind.AddFile, "big.bin", big)], Profile: profile), null),
            })
            {
                var job = submit();
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(120));
                    while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
                }
                string issues = string.Join(" | ", job.Issues.Select(issue => issue.Message + " [" + issue.Details + "]"));
                Assert.True(job.State == JobState.Failed, job.State + ": " + issues);
                Assert.True(job.Issues.Any(issue => issue.Message == ArchiveWriteMessages.OutOfSpace), issues);
                File.Delete(filler); // Room again for the next case; the failed transaction's own files stay recorded.
            }
            Assert.Equal(2, fills);
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.False(File.Exists(created));
            // Nothing is published or left in the archive folder itself; only the transactions' private stage folders remain.
            foreach (string entry in Directory.GetFileSystemEntries(folder).Where(entry => Path.GetFileName(entry) != "kept.tar"))
                Assert.True(Directory.Exists(entry) && Path.GetFileName(entry).StartsWith(".filecat-archive-stage-", StringComparison.Ordinal), entry);
        }
        finally { Directory.Delete(folder, recursive: true); }

        static void FillLeaving(string path, long leave)
        {
            var block = new byte[1 << 20];
            using var fill = new FileStream(path, FileMode.Append, FileAccess.Write);
            try
            {
                while (true)
                {
                    long available = FileSystemCapacity.Query(Path.GetDirectoryName(path)!)?.AvailableBytes ?? throw new IOException("No capacity.");
                    if (available <= leave) break;
                    fill.Write(block, 0, (int)Math.Min(block.Length, available - leave)); fill.Flush(true);
                }
            }
            catch (IOException error) when (ArchiveWriteMessages.IsOutOfSpace(error)) { }
        }
    }
}
