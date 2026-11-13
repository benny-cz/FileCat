using FileCat.Platform.Windows.Archives;
using FileCat.Tests;
namespace FileCat.Platform.Windows.Tests;
public sealed class WindowsEditStorageTests
{
    [Fact]
    public void Legacy_and_native_roots_keep_their_own_sessions_and_revisions()
    {
        if(!OperatingSystem.IsWindows())Assert.Skip("Windows native storage required.");
        ArchiveEditDualRootCases.Run(WindowsEditDirectoryLease.OpenRoot);
    }

    [Theory,InlineData("roundtrip"),InlineData("stale-save"),InlineData("atomic-editor-save"),InlineData("future-state"),
        InlineData("collision"),InlineData("broken-link"),InlineData("working-directory-replaced"),InlineData("failed-copy"),InlineData("concurrent-save"),
        InlineData("damaged-ancestor"),InlineData("missing-ancestor"),InlineData("revision-budget"),InlineData("working-read"),InlineData("failed-copy-marker-directory"),InlineData("interrupted-marker"),InlineData("earlier-marker"),InlineData("working-missing"),InlineData("cancelled-copy"),InlineData("failed-copy-kept"),
        InlineData("in-progress-hidden"),InlineData("concurrent-copies"),InlineData("concurrent-accounting"),
        InlineData("published-record-failure"),InlineData("cancel-before-marker"),InlineData("unfinished-before-marker"),InlineData("replaced-before-removal"),InlineData("held-briefly"),InlineData("folder-not-held"),InlineData("published-meanwhile"),InlineData("abandon-without-gate"),InlineData("set-aside-leftover"),InlineData("grown-partial"),InlineData("pending-free-space"),InlineData("describe-outside-gate"),InlineData("revision-beside-pending"),InlineData("desktop-artifact"),InlineData("complete-copy-kept"),InlineData("store-artifact"),InlineData("revision-stage-kept"),InlineData("marker-only-kept"),InlineData("empty-unfinished"),InlineData("vanished-during-review"),InlineData("record-unreadable"),InlineData("discard-leftover")]
    public void Native_session_storage_keeps_private_working_bytes_and_exclusive_state_revisions(string scenario)
    {
        if(!OperatingSystem.IsWindows())Assert.Skip("Windows native storage required.");
        ArchiveEditStorageCases.Run(scenario,WindowsEditDirectoryLease.OpenRoot);
    }

    [Theory,InlineData("copy"),InlineData("folders"),InlineData("session"),InlineData("abandon"),InlineData("publish")]
    public Task A_killed_native_copy_is_listed_as_interrupted_with_its_partial_copy_kept(string point)
    {
        if(!OperatingSystem.IsWindows())Assert.Skip("Windows native storage required.");
        ArchiveEditCreationCases.SkipPrivileged("Native edit storage requires an unelevated process.");
        return ArchiveEditCreationCrashCases.RunAsync(WindowsEditDirectoryLease.OpenRoot,typeof(WindowsEditDirectoryLease).Assembly.Location,point);
    }

    [Fact]
    public void Composing_the_native_store_leaves_its_folders_exactly_as_they_were()
    {
        if(!OperatingSystem.IsWindows())Assert.Skip("Windows native storage required.");
        ArchiveEditCreationCases.SkipPrivileged("Native edit storage refuses an elevated or root process.");
        ArchiveEditCreationCases.RunComposition(WindowsEditDirectoryLease.OpenRoot);
    }

    [Theory,InlineData(false),InlineData(true)]
    public void Save_copy_of_a_native_edit_keeps_its_download_mark_and_leaves_no_capture(bool replace)
    {
        if(!OperatingSystem.IsWindows())Assert.Skip("Windows native storage required.");
        ArchiveEditCreationCases.SkipPrivileged("Native edit storage refuses an elevated or root process.");
        ArchiveEditCreationCases.RunSaveCopy(replace,WindowsEditDirectoryLease.OpenRoot,new FileCat.Platform.Windows.WindowsFileOperations());
    }

    [Fact]
    public void A_failed_creation_removes_its_marked_working_copy_exactly()
    {
        if(!OperatingSystem.IsWindows())Assert.Skip("Windows native storage required.");
        ArchiveEditCreationCases.SkipPrivileged("Native edit storage refuses an elevated or root process.");
        ArchiveEditCreationCases.RunMarkedFailure(WindowsEditDirectoryLease.OpenRoot,new FileCat.Platform.Windows.WindowsFileOperations());
    }
}
