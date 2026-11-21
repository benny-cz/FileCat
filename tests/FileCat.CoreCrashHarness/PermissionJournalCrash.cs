using FileCat.Core.Analysis;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

internal static class PermissionJournalCrash
{
    internal static async Task<int> Run(string root, string boundary)
    {
        int stopAt = boundary switch
        {
            "begin" => 2,
            "intent" => 4,
            "native-return" => 5,
            "outcome" => 6,
            _ => 0
        };
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess || stopAt == 0 ||
            !Path.IsPathFullyQualified(root) || !Directory.Exists(root) || Directory.EnumerateFileSystemEntries(root).Any())
            return 2;

        string path = Path.Join(root, "document");
        using (var file = new FileStream(path, new FileStreamOptions
        {
            Access = FileAccess.Write, Mode = FileMode.CreateNew,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        }))
            file.WriteByte(42);
        File.SetUnixFileMode(path, (UnixFileMode)0x1a0); // 0640
        var item = ItemRef.ForFileSystemPath(path, EntryKind.File);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        var service = new LinuxPermissionService(qualifyAdditionalFileSystems: true);
        using var inspection = service.Inspect(subject, PermissionSections.Ordinary, CancellationToken.None);
        using var review = LinuxPermissionService.Prepare(inspection, item, new(PermissionScope.Objects,
            [new ChangePermissionMode(0x180, 0xfff)]), false, CancellationToken.None).Review;
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), root, ArchiveJobBackends.Empty,
            new NoStorageTransforms(), createPermissionJournal: OpenJournal);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.JobFinished += _ => finished.TrySetResult();
        manager.SubmitPermissions(review);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(15));
        return 3; // The parent must terminate this process while the journal is still open.

        PermissionJournalFile OpenJournal(string leaf, CancellationToken cancellation)
        {
            var journal = UnixAnalysisPrivateDirectory.CreatePermissionJournal(root, leaf, cancellation);
            int checks = 0;
            return journal with { Verify = Verify };

            void Verify()
            {
                journal.Verify();
                // WritePermission verifies before and after each flush. Check 5 is before writing
                // the outcome, after the actual mode setter and read-back have returned.
                if (++checks != stopAt) return;
                Console.WriteLine("PERMISSION_JOURNAL_READY");
                Console.ReadLine();
            }
        }
    }
}
