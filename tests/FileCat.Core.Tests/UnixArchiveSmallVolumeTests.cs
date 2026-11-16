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
            string filler = Path.Combine(folder, "filler.bin"); bool armed = false; int fills = 0; var outcomes = new List<string>();
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
                // The exact discard runs while the drive is still full. A copy-on-write volume (APFS) may refuse even the
                // removal; then the files are kept and said so, with a location, and the journal stays open for recovery.
                bool kept = job.Issues.Any(issue => issue.Message.Contains("kept them for review", StringComparison.Ordinal));
                if (kept) Assert.Contains(job.Issues, issue => issue.TargetPath is not null && issue.TargetPath.StartsWith(folder, StringComparison.Ordinal));
                outcomes.Add(issues);
                File.Delete(filler); // Room again for the next case.
            }
            // With space back, the reviewed recovery finishes any discard the full drive refused.
            foreach (var interrupted in JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero))
            {
                var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(interrupted.JournalPath).Transactions);
                using var discard = new ArchiveTransactionDiscard(UnixArchiveDiscard.OpenRecovery(interrupted.JournalPath, facts.Nonce, ct));
                Assert.True(discard.Execute(ct).IsResolved, string.Join(" || ", outcomes));
                discard.Finish("Recovery finished the discard once space was available.");
            }
            Assert.Equal(2, fills);
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.False(File.Exists(created));
            // Nothing was published, and each unpublished transaction discarded its own partial stage, sidecars and private
            // folder exactly (plan §7.1), so the folder is as it was and no journal is left for review.
            Assert.True(new[] { "kept.tar" }.SequenceEqual(Directory.GetFileSystemEntries(folder).Select(Path.GetFileName)), string.Join(" || ", outcomes));
            Assert.Empty(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
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

    // AT08 quota on a real volume: free space (statvfs) does not count the user's disk quota, so the space review passes and
    // the stage write itself meets EDQUOT. FILECAT_ARCHIVE_QUOTA_VOLUME names a folder on a volume with ample free space
    // where the test user's hard block quota is about 40 MiB (an ext4 loop image mounted with usrquota on Ubuntu). The jobs
    // must fail with the full-drive text, never publish, keep the original byte-identical and discard their own files, which
    // also returns the quota.
    [Fact]
    public async Task A_disk_quota_reached_during_the_rebuild_fails_with_the_space_message_and_keeps_the_original()
    {
        Native();
        string? volume = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_QUOTA_VOLUME");
        if (string.IsNullOrEmpty(volume)) Assert.Skip("Set FILECAT_ARCHIVE_QUOTA_VOLUME to a folder where the user's disk quota is about 40 MiB.");
        var ct = TestContext.Current.CancellationToken;
        string folder = Path.Combine(Canonical(volume), "at08q-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); File.SetUnixFileMode(folder, (UnixFileMode)0x1c0);
        using var sources = new TempDir(); string root = Canonical(sources.Path);
        try
        {
            string small = Path.Combine(root, "small.txt"); File.WriteAllText(small, "small"); File.SetLastWriteTimeUtc(small, DateTime.UnixEpoch.AddSeconds(1700000000));
            string big = Path.Combine(root, "big.bin"); var bytes = new byte[48 << 20]; new Random(811).NextBytes(bytes); File.WriteAllBytes(big, bytes);
            File.SetLastWriteTimeUtc(big, DateTime.UnixEpoch.AddSeconds(1700000001));
            // The review must see room for the whole job, so only the quota can stop the write.
            long available = FileSystemCapacity.Query(folder)?.AvailableBytes ?? throw new IOException("The quota volume reports no capacity.");
            Assert.True(available > (256L << 20), $"The quota volume needs more than 256 MiB free; it has {available >> 20} MiB.");
            var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
            var backend = UnixArchiveJobBackend.Create(profile.Format, (nonce, _) => Child.Start(nonce, "archive-assess-v2"),
                (nonce, _) => Child.Start(nonce, "archive-transform-v2"), (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
            string journals = Path.Combine(root, "journals");
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([backend]));
            string target = Path.Combine(folder, "kept.tar"), created = Path.Combine(folder, "new.tar");
            await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFile, "small.txt", small)], Profile: profile), null));
            byte[] original = File.ReadAllBytes(target);
            var assessor = new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
            var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
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
                // Removing its own files frees the quota, so the live discard completes and nothing waits for review.
                Assert.DoesNotContain(job.Issues, issue => issue.Message.Contains("kept them for review", StringComparison.Ordinal));
            }
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.False(File.Exists(created));
            Assert.Equal(new[] { "kept.tar" }, Directory.GetFileSystemEntries(folder).Select(Path.GetFileName));
            Assert.Empty(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
