using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Tests;
namespace FileCat.Core.Tests;
[Collection("Native edit snapshot reservations")]
public sealed class UnixEditLegacyZipTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditLegacyZipCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public async Task Legacy_ZIP_jobs_read_and_verify_the_retained_edit_without_reopening_its_path(string scenario)
    {if(OperatingSystem.IsWindows())Assert.Skip("Run native Windows ZIP edit integration.");await ArchiveEditLegacyZipCases.Run(scenario,UnixEditDirectoryLease.OpenRoot,new UnixFileOperations());}
}
