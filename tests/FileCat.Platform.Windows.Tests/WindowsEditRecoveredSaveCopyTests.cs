using FileCat.Platform.Windows.Archives;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

[Collection("Native edit snapshot reservations")]
public sealed class WindowsEditRecoveredSaveCopyTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditRecoveredSaveCopyCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Complete_retained_capture_can_publish_one_new_file_after_restart(string scenario)
    {
        if(!OperatingSystem.IsWindows())Assert.Skip("Native Windows edit storage required.");
        ArchiveEditRecoveredSaveCopyCases.Run(scenario,WindowsEditDirectoryLease.OpenRoot,
            new WindowsFileOperations(),WindowsEditSaveCopyDestination.Open);
    }
}
