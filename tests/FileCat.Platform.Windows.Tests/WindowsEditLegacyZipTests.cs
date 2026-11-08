using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Archives;
using FileCat.Tests;
namespace FileCat.Platform.Windows.Tests;
[Collection("Native edit snapshot reservations")]
public sealed class WindowsEditLegacyZipTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditLegacyZipCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public async Task Legacy_ZIP_jobs_read_and_verify_the_retained_edit_without_reopening_its_path(string scenario)
    {if(!OperatingSystem.IsWindows())Assert.Skip("Windows native ZIP edit integration required.");await ArchiveEditLegacyZipCases.Run(scenario,WindowsEditDirectoryLease.OpenRoot,new WindowsFileOperations());}
}
