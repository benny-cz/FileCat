using FileCat.Core.Analysis;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

internal static class PermissionJournalCrash
{
    /// <summary>
    /// Runs one bound mode change through the protected journal and stops at a journal boundary until the parent kills
    /// this process. The route is "linux" (the pinned Linux writer) or "darwin-parent"/"darwin-descriptor" (the macOS
    /// parent-relative and retained-descriptor writers).
    /// </summary>
    internal static async Task<int> Run(string root, string boundary, string route = "linux")
    {
        int stopAt = boundary switch
        {
            "begin" => 2,
            "intent" => 4,
            "native-return" => 5,
            "outcome" => 6,
            _ => 0
        };
        bool darwin = route is "darwin-parent" or "darwin-descriptor";
        if (!(darwin ? OperatingSystem.IsMacOS() : route == "linux" && OperatingSystem.IsLinux()) || Environment.IsPrivilegedProcess || stopAt == 0 ||
            !Path.IsPathFullyQualified(root) || !Directory.Exists(root) || Directory.EnumerateFileSystemEntries(root).Any())
            return 2;

        string path = Path.Join(root, "document");
        using (var file = new FileStream(path, new FileStreamOptions
        {
            Access = FileAccess.Write, Mode = FileMode.CreateNew,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        }))
            file.WriteByte(42);
        // The descriptor writer opens a regular file for execution only, so its document is executable: 0750 to 0700.
        bool executable = route == "darwin-descriptor";
        File.SetUnixFileMode(path, (UnixFileMode)(executable ? 0x1e8 : 0x1a0)); // 0750 or 0640
        var item = ItemRef.ForFileSystemPath(path, EntryKind.File);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        IPermissionService service = darwin
            ? new DarwinPermissionService(qualifyParentWrites: route == "darwin-parent", qualifyDescriptorWrites: route == "darwin-descriptor")
            : new LinuxPermissionService(qualifyAdditionalFileSystems: true);
        var sections = darwin ? PermissionSections.Ordinary | PermissionSections.Times | PermissionSections.Flags : PermissionSections.Ordinary;
        using var inspection = service.Inspect(subject, sections, CancellationToken.None);
        if (darwin && inspection.Snapshot.Binding?.DarwinRoute != (route == "darwin-parent" ? DarwinBindingRoute.ParentRelative : DarwinBindingRoute.Descriptor))
            return 4;
        PermissionDraft draft = new(PermissionScope.Objects, [new ChangePermissionMode((ushort)(executable ? 0x1c0 : 0x180), 0xfff)]);
        using var review = (darwin
            ? DarwinPermissionService.Prepare(inspection, item, draft, false, CancellationToken.None)
            : LinuxPermissionService.Prepare(inspection, item, draft, false, CancellationToken.None)).Review;
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
