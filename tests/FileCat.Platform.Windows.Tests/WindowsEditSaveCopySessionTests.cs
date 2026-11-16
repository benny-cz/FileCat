using FileCat.Platform.Windows.Archives;
using FileCat.Tests;
namespace FileCat.Platform.Windows.Tests;
[Collection("Native edit snapshot reservations")]
public sealed class WindowsEditSaveCopySessionTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditSaveCopySessionCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Save_copy_attempts_bind_the_session_and_reopen_only_for_readonly_recovery(string scenario)
    {
        if(!OperatingSystem.IsWindows())Assert.Skip("Windows native saved-copy session required.");
        ArchiveEditSaveCopySessionCases.Run(scenario,WindowsEditDirectoryLease.OpenRoot,new WindowsFileOperations(),WindowsEditSaveCopyDestination.Open);
    }
}
