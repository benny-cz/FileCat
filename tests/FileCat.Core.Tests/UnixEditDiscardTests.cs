using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Tests;
namespace FileCat.Core.Tests;
[Collection("Native edit snapshot reservations")]
public sealed class UnixEditDiscardTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditDiscardCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Explicit_discard_keeps_unreviewed_objects_and_reports_partial_cleanup(string scenario)
    {if(OperatingSystem.IsWindows())Assert.Skip("Native Unix discard required.");ArchiveEditDiscardCases.Run(scenario,UnixEditDirectoryLease.OpenRoot,new UnixFileOperations());}
}
