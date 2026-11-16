using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    // AT08 destination denial on a real NTFS volume: a folder that denies the user new files refuses FileCat's first artifact,
    // so nothing exists to review. The job says so in plain words and leaves the folder and the journal clean.
    [Fact]
    public async Task A_folder_the_user_cannot_write_refuses_a_new_archive_before_anything_exists()
    {
        _ = Probe();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Explicit native transactional job lane.");
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS"); Assert.True(assets is not null && Path.IsPathFullyQualified(assets));
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!); string executable = Path.Combine(assets!, "FileCat.exe");
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-native-denied-").FullName;
        string locked = Path.Combine(root, "locked"); Directory.CreateDirectory(locked);
        var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
        var folder = new DirectoryInfo(locked);
        try
        {
            string input = Path.Combine(root, "input.txt"); File.WriteAllText(input, "stays outside the refused folder");
            File.SetLastWriteTimeUtc(input, DateTime.UnixEpoch.AddSeconds(1700000000));
            var security = folder.GetAccessControl(); security.AddAccessRule(deny); folder.SetAccessControl(security);
            string journals = Path.Combine(root, "journals");
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals,
                new([WindowsArchiveJobBackend.Create(ArchiveWriteFormat.Tar, executable)]));
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
        finally
        {
            try { var security = folder.GetAccessControl(); security.RemoveAccessRule(deny); folder.SetAccessControl(security); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            FileCat.Tests.TestCleanup.DeleteTree(root);
        }
    }
}
