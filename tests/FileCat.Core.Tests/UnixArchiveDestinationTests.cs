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
    // AT08 destination denial on a real volume: a folder the user may not write refuses FileCat's private stage folder, so
    // nothing exists to review. The job says so in plain words and leaves the folder and the journal clean.
    [Fact]
    public async Task A_folder_the_user_cannot_write_refuses_a_new_archive_before_anything_exists()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string input = Path.Combine(root, "input.txt"); File.WriteAllText(input, "stays outside the refused folder");
        File.SetLastWriteTimeUtc(input, DateTime.UnixEpoch.AddSeconds(1700000000));
        string locked = Path.Combine(root, "locked"); Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, (UnixFileMode)0x140); // r-x for the owner only
        try
        {
            var backend = UnixArchiveJobBackend.Create(ArchiveWriteFormat.Tar, (nonce, _) => Child.Start(nonce, "archive-assess-v2"),
                (nonce, _) => Child.Start(nonce, "archive-transform-v2"), (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
            string journals = Path.Combine(root, "journals");
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([backend]));
            var job = manager.SubmitArchive(new(Path.Combine(locked, "new.tar"), null, [new(ArchiveChangeKind.AddFile, "input.txt", input)],
                Profile: new(ArchiveWriteFormat.Tar)), null);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(120));
                while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
            }
            string issues = string.Join(" | ", job.Issues.Select(issue => issue.Message));
            Assert.True(job.State == JobState.Failed, job.State + ": " + issues);
            Assert.Contains(job.Issues, issue => issue.Message.StartsWith(
                "FileCat is not allowed to create files in “locked”. Nothing was changed.", StringComparison.Ordinal));
            Assert.Empty(Directory.GetFileSystemEntries(locked));
            Assert.Empty(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
            Assert.Equal("stays outside the refused folder", File.ReadAllText(input));
        }
        finally { File.SetUnixFileMode(locked, (UnixFileMode)0x1c0); }
    }
}
