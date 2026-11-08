using FileCat.Platform.Windows.Archives;
using FileCat.Tests;
namespace FileCat.Platform.Windows.Tests;
[Collection("Native edit snapshot reservations")]
public sealed class WindowsEditSnapshotTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditSnapshotCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Native_capture_keeps_reviewed_bytes_and_never_reopens_a_job_source_path(string scenario)
    {if(!OperatingSystem.IsWindows())Assert.Skip("Windows native snapshot lane required.");ArchiveEditSnapshotCases.Run(scenario,WindowsEditDirectoryLease.OpenRoot);}
    [Fact]
    public void Cleanup_keeps_a_snapshot_with_an_unexpected_named_stream()
    {if(!OperatingSystem.IsWindows())Assert.Skip("Windows native snapshot lane required.");ArchiveEditSnapshotCases.Run("cleanup-extra-stream",WindowsEditDirectoryLease.OpenRoot);}
}
