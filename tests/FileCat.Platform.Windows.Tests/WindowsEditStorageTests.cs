using FileCat.Platform.Windows.Archives;
using FileCat.Tests;
namespace FileCat.Platform.Windows.Tests;
public sealed class WindowsEditStorageTests
{
    [Theory,InlineData("roundtrip"),InlineData("stale-save"),InlineData("atomic-editor-save"),InlineData("future-state"),
        InlineData("collision"),InlineData("broken-link"),InlineData("working-directory-replaced"),InlineData("failed-copy"),InlineData("concurrent-save"),
        InlineData("damaged-ancestor"),InlineData("missing-ancestor"),InlineData("revision-budget"),InlineData("working-read")]
    public void Native_session_storage_keeps_private_working_bytes_and_exclusive_state_revisions(string scenario)
    {
        if(!OperatingSystem.IsWindows())Assert.Skip("Windows native storage required.");
        ArchiveEditStorageCases.Run(scenario,WindowsEditDirectoryLease.OpenRoot);
    }
}
