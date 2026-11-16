using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Tests;
namespace FileCat.Core.Tests;
[Collection("Native edit snapshot reservations")]
public sealed class UnixEditStorageAdmissionTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditStorageAdmissionCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public async Task Native_storage_is_counted_and_admitted_across_lifetimes_and_processes(string scenario)
    {
        if(OperatingSystem.IsWindows())Assert.Skip("Unix native storage admission lane required.");
        await ArchiveEditStorageAdmissionCases.Run(scenario,UnixEditDirectoryLease.OpenRoot,new UnixFileOperations(),"-");
    }
}
