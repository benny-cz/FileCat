using FileCat.Platform.Windows.Archives;
using FileCat.Tests;
namespace FileCat.Platform.Windows.Tests;
[Collection("Native edit snapshot reservations")]
public sealed class WindowsEditStorageAdmissionTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditStorageAdmissionCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public async Task Native_storage_is_counted_and_admitted_across_lifetimes_and_processes(string scenario)
    {
        if(!OperatingSystem.IsWindows())Assert.Skip("Windows native storage admission lane required.");
        await ArchiveEditStorageAdmissionCases.Run(scenario,WindowsEditDirectoryLease.OpenRoot,new WindowsFileOperations(),typeof(WindowsEditDirectoryLease).Assembly.Location);
    }
}
