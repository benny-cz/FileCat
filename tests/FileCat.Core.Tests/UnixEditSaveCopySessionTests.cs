using FileCat.Core.Archives.Unix;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Tests;
namespace FileCat.Core.Tests;
[Collection("Native edit snapshot reservations")]
public sealed class UnixEditSaveCopySessionTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditSaveCopySessionCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Save_copy_attempts_bind_the_session_and_reopen_only_for_readonly_recovery(string scenario)
    {
        if(OperatingSystem.IsWindows())Assert.Skip("Windows native saved-copy session lane required.");
        ArchiveEditSaveCopySessionCases.Run(scenario,UnixEditDirectoryLease.OpenRoot,new UnixFileOperations(),UnixEditSaveCopyDestination.Open);
    }
}
