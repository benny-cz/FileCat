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
    // AT08 device failure on a real volume (Linux). FILECAT_ARCHIVE_FAULT_VOLUME is a folder on an ext4 file system over a
    // device-mapper target that a root controller (eng/validation/archive-writing/fault_volume.sh serve) switches to fail
    // every write with EIO, reads still working, when FILECAT_ARCHIVE_FAULT_CONTROL/request says "fail". Writes then land in
    // the page cache and the drive refuses them where FileCat makes its stage durable (fsync), or the file system turns
    // read-only and refuses the next change. The update must fail with a typed refusal that names the drive, never publish,
    // and keep the original byte-identical, also after "restore" checks and mounts the file system again; the reviewed
    // recovery then discards whatever the failing drive would not let the job remove, and the folder is as it was.
    [Fact]
    public async Task A_drive_that_fails_writes_refuses_with_the_drive_text_and_keeps_the_original()
    {
        Native();
        string? volume = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_FAULT_VOLUME");
        string? control = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_FAULT_CONTROL");
        if (string.IsNullOrEmpty(volume) || string.IsNullOrEmpty(control))
            Assert.Skip("Set FILECAT_ARCHIVE_FAULT_VOLUME and FILECAT_ARCHIVE_FAULT_CONTROL for a fault_volume.sh volume.");
        var ct = TestContext.Current.CancellationToken;
        string folder = Path.Combine(Canonical(volume), "at08d-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); File.SetUnixFileMode(folder, (UnixFileMode)0x1c0);
        using var sources = new TempDir(); string root = Canonical(sources.Path);
        bool failing = false;
        try
        {
            string small = Path.Combine(root, "small.txt"); File.WriteAllText(small, "small"); File.SetLastWriteTimeUtc(small, DateTime.UnixEpoch.AddSeconds(1700000000));
            string big = Path.Combine(root, "big.bin"); var bytes = new byte[8 << 20]; new Random(812).NextBytes(bytes); File.WriteAllBytes(big, bytes);
            File.SetLastWriteTimeUtc(big, DateTime.UnixEpoch.AddSeconds(1700000001));
            var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
            var backend = UnixArchiveJobBackend.Create(profile.Format, (nonce, _) => Child.Start(nonce, "archive-assess-v2"),
                (nonce, _) => Child.Start(nonce, "archive-transform-v2"), (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
            string journals = Path.Combine(root, "journals");
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([backend]));
            string target = Path.Combine(folder, "kept.tar");
            await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFile, "small.txt", small)], Profile: profile), null));
            byte[] original = File.ReadAllBytes(target);
            var assessor = new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
            var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);

            Switch(control, "fail", ct); failing = true;
            var update = manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "big.bin", big)]), null);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(180));
                while (!update.State.IsFinished()) await Task.Delay(10, deadline.Token);
            }
            string issues = string.Join(" | ", update.Issues.Select(issue => issue.Message + " [" + issue.Details + "]"));
            Assert.True(update.State == JobState.Failed, update.State + ": " + issues);
            // The drive's own error, or (once the file system turned read-only after it) the read-only destination text.
            Assert.True(update.Issues.Any(issue => issue.Message == ArchiveWriteMessages.DriveError ||
                issue.Message.Contains("is on a drive that is read-only now. Nothing was changed.", StringComparison.Ordinal)), issues);
            Assert.DoesNotContain(update.Issues, issue => issue.Message.Contains("storage/permission control", StringComparison.Ordinal));
            Assert.Equal(original, File.ReadAllBytes(target)); // Reads still work on the failing drive.

            Switch(control, "restore", ct); failing = false;
            Assert.Equal(original, File.ReadAllBytes(target)); // And after the file system was checked and mounted again.
            foreach (var interrupted in JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero))
            {
                var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(interrupted.JournalPath).Transactions);
                using var discard = new ArchiveTransactionDiscard(UnixArchiveDiscard.OpenRecovery(interrupted.JournalPath, facts.Nonce, ct));
                Assert.True(discard.Execute(ct).IsResolved, issues);
                discard.Finish("Recovery finished the discard once the drive worked again.");
            }
            Assert.True(new[] { "kept.tar" }.SequenceEqual(Directory.GetFileSystemEntries(folder).Select(Path.GetFileName)), issues);
            Assert.Empty(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        }
        finally
        {
            if (failing) try { Switch(control!, "restore", CancellationToken.None); } catch (IOException) { } catch (TimeoutException) { }
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        // One request at a time: the controller answers in done with the same word once the change is in effect.
        static void Switch(string control, string word, CancellationToken ct)
        {
            string done = Path.Combine(control, "done"), request = Path.Combine(control, "request");
            File.Delete(done);
            File.WriteAllText(request + ".tmp", word); File.Move(request + ".tmp", request, overwrite: true);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(120))
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(done))
                {
                    string answer = File.ReadAllText(done).Trim();
                    if (answer != word) throw new IOException($"The fault controller answered \"{answer}\" to \"{word}\".");
                    File.Delete(done); return;
                }
                Thread.Sleep(100);
            }
            throw new TimeoutException($"The fault controller did not answer \"{word}\".");
        }
    }
}
