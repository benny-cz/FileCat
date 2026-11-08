using FileCat.Core.Edit;
using FileCat.Tests;
namespace FileCat.Core.Tests;
public sealed class UnixEditSnapshotTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditSnapshotCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Native_capture_keeps_reviewed_bytes_and_never_reopens_a_job_source_path(string scenario)
    {if(OperatingSystem.IsWindows())Assert.Skip("Run the native Windows snapshot lane.");ArchiveEditSnapshotCases.Run(scenario,UnixEditDirectoryLease.OpenRoot);}
}
