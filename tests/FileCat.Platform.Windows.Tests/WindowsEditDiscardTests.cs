using FileCat.Platform.Windows.Archives;
using FileCat.Tests;
namespace FileCat.Platform.Windows.Tests;
[Collection("Native edit snapshot reservations")]
public sealed class WindowsEditDiscardTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditDiscardCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Explicit_discard_keeps_unreviewed_objects_and_reports_partial_cleanup(string scenario)
    {if(!OperatingSystem.IsWindows())Assert.Skip("Native Windows discard required.");ArchiveEditDiscardCases.Run(scenario,WindowsEditDirectoryLease.OpenRoot,new WindowsFileOperations());}
}
