using FileCat.Core.Edit;
using FileCat.Tests;
namespace FileCat.Core.Tests;
public sealed class UnixEditStorageTests
{
    [Theory,InlineData("roundtrip"),InlineData("stale-save"),InlineData("atomic-editor-save"),InlineData("future-state"),
        InlineData("collision"),InlineData("broken-link"),InlineData("working-directory-replaced"),InlineData("failed-copy"),InlineData("concurrent-save"),
        InlineData("damaged-ancestor"),InlineData("missing-ancestor"),InlineData("revision-budget"),InlineData("working-read"),InlineData("working-symlink")]
    public void Native_session_storage_keeps_private_working_bytes_and_exclusive_state_revisions(string scenario)
    {
        if(OperatingSystem.IsWindows())Assert.Skip("Run the Windows native storage lane.");
        ArchiveEditStorageCases.Run(scenario,UnixEditDirectoryLease.OpenRoot);
    }
}
