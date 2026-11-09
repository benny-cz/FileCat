using FileCat.Core.Archives.Unix;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Tests;

namespace FileCat.Core.Tests;

[Collection("Native edit snapshot reservations")]
public sealed class UnixEditRecoveredSaveCopyTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditRecoveredSaveCopyCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Complete_retained_capture_can_publish_one_new_file_after_restart(string scenario)
    {
        if(OperatingSystem.IsWindows())Assert.Skip("Native Unix edit storage required.");
        ArchiveEditRecoveredSaveCopyCases.Run(scenario,UnixEditDirectoryLease.OpenRoot,
            new UnixFileOperations(),UnixEditSaveCopyDestination.Open);
    }
}
